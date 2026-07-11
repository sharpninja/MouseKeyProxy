using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MouseKeyProxy.Common;

namespace MouseKeyProxy.Service.Device;

/// <summary>
/// FR-MKP-013: emits <see cref="DeviceEventKind.BootComplete"/> and initial Connect edges after the host starts.
/// </summary>
public sealed class DeviceBootCompleteHostedService : IHostedService
{
    private readonly DeviceFunctionCoordinator _coordinator;
    private readonly ILogger<DeviceBootCompleteHostedService> _logger;

    /// <summary>Creates the boot-complete publisher.</summary>
    public DeviceBootCompleteHostedService(
        DeviceFunctionCoordinator coordinator,
        ILogger<DeviceBootCompleteHostedService> logger)
    {
        _coordinator = coordinator;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var events = await _coordinator.NotifyBootCompleteAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Device boot complete published {Count} events", events.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Device boot complete notification failed");
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
