using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MouseKeyProxy.Common;

namespace MouseKeyProxy.Service.Device;

/// <summary>
/// FR-MKP-013: mirrors device events to the paired host. v1 logs and retains the latest
/// events for GetDeviceConfiguration / diagnostics; a session-stream push can subscribe later.
/// </summary>
public interface IDeviceEventHostMirror
{
    /// <summary>Mirrors a single event to the host side.</summary>
    /// <param name="deviceEvent">Event already published on the local bus.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    Task MirrorAsync(DeviceEvent deviceEvent, CancellationToken cancellationToken = default);
}

/// <summary>Default host mirror: structured log (session push can wrap this later).</summary>
public sealed class LoggingDeviceEventHostMirror : IDeviceEventHostMirror
{
    private readonly ILogger<LoggingDeviceEventHostMirror> _logger;

    /// <summary>Creates the logging mirror.</summary>
    /// <param name="logger">Logger.</param>
    public LoggingDeviceEventHostMirror(ILogger<LoggingDeviceEventHostMirror> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task MirrorAsync(DeviceEvent deviceEvent, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "DeviceEvent mirror kind={Kind} correlationId={CorrelationId} detail={Detail}",
            deviceEvent.Kind,
            deviceEvent.CorrelationId,
            deviceEvent.Detail ?? string.Empty);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Background subscriber: local bus → host mirror for every device event.
/// </summary>
public sealed class DeviceEventMirrorHostedService : Microsoft.Extensions.Hosting.BackgroundService
{
    private readonly IDeviceEventBus _bus;
    private readonly IDeviceEventHostMirror _mirror;
    private readonly ILogger<DeviceEventMirrorHostedService> _logger;

    /// <summary>Creates the hosted mirror pump.</summary>
    public DeviceEventMirrorHostedService(
        IDeviceEventBus bus,
        IDeviceEventHostMirror mirror,
        ILogger<DeviceEventMirrorHostedService> logger)
    {
        _bus = bus;
        _mirror = mirror;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var e in _bus.SubscribeAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    await _mirror.MirrorAsync(e, stoppingToken).ConfigureAwait(false);
                }
                catch (System.Exception ex) when (ex is not System.OperationCanceledException)
                {
                    _logger.LogWarning(ex, "DeviceEvent host mirror failed for {Kind}", e.Kind);
                }
            }
        }
        catch (System.OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutdown
        }
    }
}
