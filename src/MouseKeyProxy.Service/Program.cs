using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MouseKeyProxy.Common;

namespace MouseKeyProxy.Service;

/// <summary>
/// SHIPPED Service host with full ILogger support writing to Windows Event Viewer (source "MouseKeyProxy").
/// Uses WebApplication + UseWindowsService for real gRPC + EventLog.
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

        builder.Host.UseWindowsService();

        builder.Logging.ClearProviders();
        builder.Logging.AddEventLog(options =>
        {
            options.SourceName = "MouseKeyProxy";
            options.LogName = "Application";
        });
        builder.Logging.AddConsole();

        builder.Services.AddSingleton<SessionFrameDispatcher>(_ => new SessionFrameDispatcher(null, new ToggleStateMachine()));

        builder.Services.AddGrpc();

        var app = builder.Build();

        app.MapGrpcService<MouseKeyProxyImpl>();

        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("MouseKeyProxy.Service");
        logger.LogInformation("MouseKeyProxy service starting (EventLog source=MouseKeyProxy)");

        app.Run();

        logger.LogInformation("MouseKeyProxy service stopped");
    }
}