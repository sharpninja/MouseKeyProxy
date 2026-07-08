using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.EventLog;

namespace MouseKeyProxy.Service;

/// <summary>
/// FR-MKP-012: Platform-aware host/logging configuration so the MKP gRPC service can run on
/// Windows (Event Log) and on Linux/Raspberry Pi (journald via the systemd console formatter),
/// keeping all service code on the provider-agnostic <see cref="ILogger"/> abstraction.
/// </summary>
public static class ServiceHostConfiguration
{
    /// <summary>
    /// Configures logging providers for the current platform: on Windows the Windows Event Log
    /// provider (source and log "MouseKeyProxy"); on non-Windows the systemd console provider,
    /// whose priority-prefixed stdout is ingested by journald when hosted under systemd.
    /// </summary>
    /// <param name="logging">The logging builder to configure.</param>
    /// <param name="isWindows">True to configure the Windows Event Log path; false for the journald path.</param>
    public static void ConfigureLogging(ILoggingBuilder logging, bool isWindows)
    {
        ArgumentNullException.ThrowIfNull(logging);
        logging.ClearProviders();

        if (isWindows)
        {
            logging.AddConsole();
            logging.AddEventLog(options =>
            {
                options.SourceName = "MouseKeyProxy";
                options.LogName = "MouseKeyProxy";
                options.Filter = (_, level) => level >= LogLevel.Information;
            });
            logging.AddFilter<EventLogLoggerProvider>(level => level >= LogLevel.Information);
        }
        else
        {
            // systemd console formatter emits "<priority>message" lines that journald ingests
            // (the systemd unit runs with StandardOutput=journal). No EventLog on this path.
            logging.AddSystemdConsole();
        }
    }
}
