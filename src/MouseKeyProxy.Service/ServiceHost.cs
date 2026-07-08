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
        }
        else
        {
            builder.Services.AddSingleton<IInputInjector, AgentPipeInputInjector>();
            builder.Services.AddSingleton<ISystemPowerController, UnsupportedPowerController>();
        }
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
        return app;
    }
}
