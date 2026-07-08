using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MouseKeyProxy.Service;

/// <summary>
/// SHIPPED Service host with full ILogger support. Logs to the Windows Event Log on Windows and
/// to journald (systemd console) on Linux/Raspberry Pi. The mTLS + pairing pipeline is built by
/// <see cref="ServiceHost.Build(string[], int?, MouseKeyProxy.Service.Pairing.IPairingCertificateAuthority, MouseKeyProxy.Service.Pairing.IPairedPeerStore, System.Action{IServiceCollection}, bool)"/>.
/// </summary>
internal static class Program
{
    public static void Main(string[] args)
    {
        var app = ServiceHost.Build(args);

        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("MouseKeyProxy.Service");
        logger.LogInformation("MouseKeyProxy service starting (logging via {Sink})",
            OperatingSystem.IsWindows() ? "Windows Event Log" : "journald");

        app.Run();
    }
}
