using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MouseKeyProxy.Common;
using MouseKeyProxy.Service.Pairing;

namespace MouseKeyProxy.Service;

/// <summary>
/// TR-MKP-SEC-001: builds the mTLS gRPC host. Factored out of <see cref="Program"/> so integration
/// tests can boot the real pairing + authorization pipeline on a chosen port with test overrides.
/// </summary>
public static class ServiceHost
{
    /// <summary>
    /// Builds the configured (but not started) service host. The listener presents a CA-signed
    /// server certificate and requests (but does not require) a client certificate; the
    /// <see cref="PairingAuthorizationInterceptor"/> performs real authorization on effect RPCs.
    /// </summary>
    /// <param name="args">Process args.</param>
    /// <param name="port">Listen port (defaults to <see cref="LabTopology.GrpcPort"/>).</param>
    /// <param name="certificateAuthority">Shared pairing CA (created if null).</param>
    /// <param name="pairedPeerStore">Shared paired-peer store (created if null).</param>
    /// <param name="configureServices">Optional last-wins service overrides (e.g. a fake injector for tests).</param>
    /// <param name="useWindowsServiceLifetime">Whether to attach the Windows Service lifetime (off for tests).</param>
    /// <returns>The built <see cref="WebApplication"/>.</returns>
    public static WebApplication Build(
        string[] args,
        int? port = null,
        IPairingCertificateAuthority? certificateAuthority = null,
        IPairedPeerStore? pairedPeerStore = null,
        Action<IServiceCollection>? configureServices = null,
        bool useWindowsServiceLifetime = true)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory
        });

        // TR-MKP-SEC-001: load persisted pairing state (CA + paired peers) so a paired device stays
        // paired across a Service restart / Pi reboot. Tests inject explicit ca/store and bypass this.
        var statePath = PairingStateStore.DefaultPath();
        PairingState? loadedState = null;
        if (certificateAuthority is null || pairedPeerStore is null)
        {
            try
            {
                loadedState = PairingStateStore.Load(statePath);
            }
            catch (Exception)
            {
                loadedState = null; // corrupt/undecryptable state -> start fresh rather than crash
            }
        }

        var ca = certificateAuthority ?? new PairingCertificateAuthority(loadedState?.CaCertificate);
        var store = pairedPeerStore ?? new PairedPeerStore(
            timeProvider: null,
            initialPeers: loadedState?.Peers,
            onChanged: peers =>
            {
                try { PairingStateStore.Save(statePath, ca.CaCertificate, peers); }
                catch { /* persistence is best-effort; a failed save must not break pairing at runtime */ }
            });

        // Persist a freshly-generated CA immediately so it is stable across restarts even before the
        // first pairing (otherwise a restart would regenerate the CA and invalidate any issued cert).
        if (pairedPeerStore is null && loadedState is null)
        {
            try { PairingStateStore.Save(statePath, ca.CaCertificate, Array.Empty<PairedPeer>()); }
            catch { /* best effort */ }
        }

        var serverCertificate = ca.CreateServerCertificate(System.Net.Dns.GetHostName(), TimeSpan.FromDays(365));
        var listenPort = port ?? LabTopology.GrpcPort;

        builder.WebHost.ConfigureKestrel(serverOptions =>
        {
            serverOptions.ListenAnyIP(listenPort, listenOptions =>
            {
                listenOptions.Protocols = HttpProtocols.Http2;
                listenOptions.UseHttps(serverCertificate, https =>
                {
                    https.ClientCertificateMode = ClientCertificateMode.AllowCertificate;
                    https.AllowAnyClientCertificate();
                });
            });
        });
        builder.WebHost.UseSetting(WebHostDefaults.ServerUrlsKey, string.Empty);
        builder.WebHost.PreferHostingUrls(false);

        if (useWindowsServiceLifetime && OperatingSystem.IsWindows())
        {
            builder.Host.UseWindowsService();
        }

        ServiceHostConfiguration.ConfigureLogging(builder.Logging, OperatingSystem.IsWindows());

        builder.Services.AddSingleton<AgentControlPipeClient>();
        builder.Services.AddSingleton<IRemoteDesktopController, AgentPipeRemoteDesktopController>();
        builder.Services.AddSingleton<IEmergencyReleaseController, AgentPipeEmergencyReleaseController>();
        builder.Services.AddSingleton<IModifierReleaseController, AgentPipeModifierReleaseController>();
        builder.Services.AddSingleton<IScreenshotCapture, AgentPipeScreenshotCapture>();
        if (OperatingSystem.IsLinux())
        {
            // FR-MKP-012 / TR-MKP-HID-001: on the Pi the service injects via the USB HID gadget rather
            // than the Windows agent pipe. Device paths are overridable via environment.
            var keyboardDevice = Environment.GetEnvironmentVariable("MKP_HID_KEYBOARD_DEVICE") ?? "/dev/hidg0";
            var mouseDevice = Environment.GetEnvironmentVariable("MKP_HID_MOUSE_DEVICE") ?? "/dev/hidg1";
            builder.Services.AddSingleton<MouseKeyProxy.PiHid.IHidReportWriter>(
                new MouseKeyProxy.PiHid.FileHidReportWriter(keyboardDevice, mouseDevice));
            builder.Services.AddSingleton<IInputInjector, MouseKeyProxy.PiHid.HidGadgetInputInjector>();
            builder.Services.AddSingleton<ISystemPowerController, SystemctlPowerController>();
            // FR-MKP-013: configfs-backed enable/disable of keyboard, mouse, mass-storage FS (+ RO/RW).
            builder.Services.AddSingleton<IDeviceFunctionController, Device.ConfigfsDeviceFunctionController>();
        }
        else
        {
            builder.Services.AddSingleton<IInputInjector, AgentPipeInputInjector>();
            builder.Services.AddSingleton<ISystemPowerController, UnsupportedPowerController>();
            // Windows service does not own a USB gadget; in-memory controller keeps the API queryable in tests.
            builder.Services.AddSingleton<IDeviceFunctionController, InMemoryDeviceFunctionController>();
        }

        builder.Services.AddSingleton<IDeviceEventBus, DeviceEventBus>();

        // FR-MKP-022: LiteDB appliance config under /etc/mkp (or MKP_CONFIG_DB / ProgramData).
        // Must register before DeviceFunctionCoordinator so Rufus seed.json becomes initial gadget state.
        builder.Services.AddSingleton<IApplianceConfigStore>(_ =>
        {
            var storePath = LiteDbApplianceConfigStore.ResolveDefaultDatabasePath();
            var cfgStore = new LiteDbApplianceConfigStore(storePath);
            var seed = Environment.GetEnvironmentVariable("MKP_CONFIG_SEED")
                ?? Path.Combine(Path.GetDirectoryName(storePath) ?? "/etc/mkp", "seed.json");
            cfgStore.TryImportSeed(seed);
            return cfgStore;
        });

        // FR-MKP-013 / FR-MKP-019: seed coordinator from LiteDB (Rufus first-boot defaults).
        builder.Services.AddSingleton(sp =>
        {
            var controller = sp.GetRequiredService<IDeviceFunctionController>();
            var bus = sp.GetRequiredService<IDeviceEventBus>();
            var cfg = sp.GetRequiredService<IApplianceConfigStore>().Get();
            StorageMediaSpec? Cd(string? path, DeviceMediaSource src) =>
                string.IsNullOrWhiteSpace(path) ? null : new StorageMediaSpec(src, path);
            var initial = new DeviceFunctionState(
                KeyboardEnabled: cfg.KeyboardEnabled,
                MouseEnabled: cfg.MouseEnabled,
                FsEnabled: cfg.FsEnabled,
                FsAccess: cfg.FsAccess,
                CdromEnabled: cfg.CdromEnabled,
                CdromMedia: Cd(cfg.CdromMediaPath, cfg.CdromMediaSource),
                FloppyEnabled: cfg.FloppyEnabled,
                FloppyMedia: Cd(cfg.FloppyMediaPath, cfg.FloppyMediaSource));
            return new DeviceFunctionCoordinator(controller, bus, initial);
        });

        // FR-MKP-014/016: share/SMB IP allowlist (UsbConnectedPc + PairedHost only).
        builder.Services.AddSingleton<IShareAccessAllowlist, ShareAccessAllowlist>();
        // FR-MKP-023: one-time codes for Agent client pairing (typed on connecting machine).
        builder.Services.AddSingleton<IClientPairingCodeIssuer, ClientPairingCodeIssuer>();
        // FR-MKP-025/026: MSI install ticket + USB-client clipboard intro mailbox.
        builder.Services.AddSingleton<IInstallTicketStore, InstallTicketStore>();
        builder.Services.AddSingleton<IClientInstallIntroMailbox, ClientInstallIntroMailbox>();
        // FR-MKP-016: Samba config writer (hosts allow = allowlist only).
        builder.Services.AddSingleton<Device.ISmbShareController, Device.SmbShareController>();

        var folderShareOptions = FolderShareOptions.FromEnvironment();
        // Align FS content watcher with the folder share root when share is enabled.
        var fsWatchPath = folderShareOptions.Enabled
            ? folderShareOptions.RootPath
            : FsShareWatchOptions.DefaultWatchPath();
        builder.Services.AddSingleton(folderShareOptions);
        builder.Services.AddSingleton<IFolderShareStore>(sp =>
            new LocalFolderShareStore(sp.GetRequiredService<FolderShareOptions>()));
        builder.Services.AddSingleton(new FsShareWatchOptions { WatchPath = fsWatchPath });
        builder.Services.AddSingleton<Device.IDeviceEventHostMirror, Device.LoggingDeviceEventHostMirror>();
        builder.Services.AddHostedService<Device.DeviceEventMirrorHostedService>();
        builder.Services.AddHostedService<Device.DeviceBootCompleteHostedService>();
        // FR-MKP-013: FileSystemWatcher on shared install folder/image; FS updates → device events.
        builder.Services.AddHostedService<Device.FsShareWatchHostedService>();

        builder.Services.AddSingleton<SessionFrameDispatcher>(sp =>
            new SessionFrameDispatcher(sp.GetRequiredService<IInputInjector>(), new ToggleStateMachine()));

        // TR-MKP-SEC-001: pairing authority + authorization interceptor.
        builder.Services.AddSingleton<IPairingCertificateAuthority>(ca);
        builder.Services.AddSingleton<IPairedPeerStore>(store);
        builder.Services.AddSingleton<PairingAuthorizer>();
        builder.Services.AddSingleton<PairingAuthorizationInterceptor>();

        // TR-MKP-SEC-001: plug-n-play. When MKP_TOFU=1 the device accepts the first (codeless) pairing
        // and advertises itself on the LAN while unpaired, so a peer can discover + ToFU-pair it.
        var pairingOptions = new ServicePairingOptions
        {
            TrustOnFirstUse = string.Equals(Environment.GetEnvironmentVariable("MKP_TOFU"), "1", StringComparison.Ordinal),
        };
        builder.Services.AddSingleton(pairingOptions);
        builder.Services.AddHostedService<DiscoveryAdvertiser>();

        builder.Services.AddGrpc(options => options.Interceptors.Add<PairingAuthorizationInterceptor>());

        // Last-wins test overrides (e.g. a recording IInputInjector) go after the defaults.
        configureServices?.Invoke(builder.Services);

        var app = builder.Build();
        app.MapGrpcService<MouseKeyProxyImpl>();

        // FR-MKP-025: optional pre-seeded install ticket for MSI USB clients (from env or file).
        try
        {
            var tickets = app.Services.GetService<IInstallTicketStore>();
            if (tickets is not null)
            {
                var envTicket = Environment.GetEnvironmentVariable("MKP_INSTALL_TICKET");
                if (string.IsNullOrWhiteSpace(envTicket))
                {
                    var ticketFile = Environment.GetEnvironmentVariable("MKP_INSTALL_TICKET_FILE")
                        ?? "/mnt/mkp-deploy/install/install-ticket.txt";
                    if (File.Exists(ticketFile))
                    {
                        envTicket = File.ReadAllText(ticketFile).Trim();
                    }
                }

                if (!string.IsNullOrWhiteSpace(envTicket))
                {
                    tickets.Seed(envTicket, TimeSpan.FromDays(7));
                }
            }
        }
        catch
        {
            /* best effort */
        }

        return app;
    }
}
