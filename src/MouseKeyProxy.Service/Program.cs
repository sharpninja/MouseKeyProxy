using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MouseKeyProxy.Service;

/// <summary>
/// SHIPPED Service host with full ILogger support writing to Windows Event Viewer (source "MouseKeyProxy").
/// Uses WebApplication + UseWindowsService for real gRPC + EventLog.
/// </summary>
internal static class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Host.UseWindowsService();

        builder.Logging.ClearProviders();
        builder.Logging.AddEventLog(options =>
        {
            options.SourceName = "MouseKeyProxy";
            options.LogName = "Application";
        });
        builder.Logging.AddConsole();

        builder.Services.AddGrpc();

        var app = builder.Build();

        app.MapGrpcService<MouseKeyProxyImpl>();

        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("MouseKeyProxy.Service");
        logger.LogInformation("MouseKeyProxy service starting (EventLog source=MouseKeyProxy)");

        app.Run();

        logger.LogInformation("MouseKeyProxy service stopped");
    }
}