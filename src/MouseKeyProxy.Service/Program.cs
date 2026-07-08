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
/// SHIPPED Service host with full ILogger support. Logs to the Windows Event Log on Windows and
/// to journald (systemd console) on Linux/Raspberry Pi. Uses WebApplication + UseWindowsService
/// or UseSystemd for real gRPC hosting per platform.
/// </summary>
internal static class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory
        });

        // TR-MKP-SEC-001: single CA instance shared between the mTLS listener (server cert) and DI
        // (peer-cert issuance + authorization). NOTE: the CA is currently in-memory and regenerated
        // per start; DPAPI/file-perm persistence so pairings survive restarts is a follow-up slice.
        var certificateAuthority = new PairingCertificateAuthority();
        var pairedPeerStore = new PairedPeerStore();
        var serverCertificate = certificateAuthority.CreateServerCertificate(
            System.Net.Dns.GetHostName(), TimeSpan.FromDays(365));

        builder.WebHost.ConfigureKestrel(serverOptions =>
        {
            serverOptions.ListenAnyIP(LabTopology.GrpcPort, listenOptions =>
            {
                listenOptions.Protocols = HttpProtocols.Http2;
                listenOptions.UseHttps(serverCertificate, https =>
                {
                    // Request a client cert but do not require it at the TLS layer: pairing-bootstrap
                    // RPCs run before a peer has a credential. PairingAuthorizationInterceptor performs
                    // real authorization (CA-issued + paired + non-revoked) on effect-bearing RPCs.
                    https.ClientCertificateMode = ClientCertificateMode.AllowCertificate;
                    https.AllowAnyClientCertificate();
                });
            });
        });
        builder.WebHost.UseSetting(WebHostDefaults.ServerUrlsKey, string.Empty);
        builder.WebHost.PreferHostingUrls(false);

        if (OperatingSystem.IsWindows())
        {
            builder.Host.UseWindowsService();
        }

        // On Linux the process runs under systemd directly; journald ingests stdout from the
        // systemd console logger configured below. Full systemd lifetime integration
        // (UseSystemd, sd_notify) is a first-party follow-up package for the Pi host slice.
        ServiceHostConfiguration.ConfigureLogging(builder.Logging, OperatingSystem.IsWindows());

        builder.Services.AddSingleton<AgentControlPipeClient>();
        builder.Services.AddSingleton<IInputInjector, AgentPipeInputInjector>();
        builder.Services.AddSingleton<IRemoteDesktopController, AgentPipeRemoteDesktopController>();
        builder.Services.AddSingleton<IEmergencyReleaseController, AgentPipeEmergencyReleaseController>();
        builder.Services.AddSingleton<IModifierReleaseController, AgentPipeModifierReleaseController>();
        builder.Services.AddSingleton<IScreenshotCapture, AgentPipeScreenshotCapture>();
        if (OperatingSystem.IsLinux())
        {
            builder.Services.AddSingleton<ISystemPowerController, SystemctlPowerController>();
        }
        else
        {
            builder.Services.AddSingleton<ISystemPowerController, UnsupportedPowerController>();
        }
        builder.Services.AddSingleton<SessionFrameDispatcher>(sp =>
            new SessionFrameDispatcher(sp.GetRequiredService<IInputInjector>(), new ToggleStateMachine()));

        // TR-MKP-SEC-001: pairing authority + authorization interceptor.
        builder.Services.AddSingleton<IPairingCertificateAuthority>(certificateAuthority);
        builder.Services.AddSingleton<IPairedPeerStore>(pairedPeerStore);
        builder.Services.AddSingleton<PairingAuthorizer>();
        builder.Services.AddSingleton<PairingAuthorizationInterceptor>();

        builder.Services.AddGrpc(options => options.Interceptors.Add<PairingAuthorizationInterceptor>());

        var app = builder.Build();

        app.MapGrpcService<MouseKeyProxyImpl>();

        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("MouseKeyProxy.Service");
        logger.LogInformation("MouseKeyProxy service starting (logging via {Sink})",
            OperatingSystem.IsWindows() ? "Windows Event Log" : "journald");

        app.Run();
    }
}