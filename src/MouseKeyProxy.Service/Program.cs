using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MouseKeyProxy.Common;

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

        builder.WebHost.ConfigureKestrel(serverOptions =>
        {
            serverOptions.ListenAnyIP(LabTopology.GrpcPort, listenOptions =>
            {
                listenOptions.Protocols = HttpProtocols.Http2;
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

        builder.Services.AddGrpc();

        var app = builder.Build();

        app.MapGrpcService<MouseKeyProxyImpl>();

        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("MouseKeyProxy.Service");
        logger.LogInformation("MouseKeyProxy service starting (logging via {Sink})",
            OperatingSystem.IsWindows() ? "Windows Event Log" : "journald");

        app.Run();
    }
}