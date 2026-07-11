using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MouseKeyProxy.Common;

namespace MouseKeyProxy.Service.Device;

/// <summary>
/// FR-MKP-013: starts <see cref="FsShareWatcher"/> when FS is connected and stops it when
/// disconnected. FS content changes are published on the device bus (local + host mirror).
/// </summary>
public sealed class FsShareWatchHostedService : BackgroundService
{
    private readonly IDeviceEventBus _bus;
    private readonly DeviceFunctionCoordinator _coordinator;
    private readonly FsShareWatchOptions _options;
    private readonly ILogger<FsShareWatchHostedService> _logger;
    private FsShareWatcher? _watcher;

    /// <summary>Creates the FS share watch hosted service.</summary>
    public FsShareWatchHostedService(
        IDeviceEventBus bus,
        DeviceFunctionCoordinator coordinator,
        FsShareWatchOptions options,
        ILogger<FsShareWatchHostedService> logger)
    {
        _bus = bus;
        _coordinator = coordinator;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Align watcher with current FS enablement (e.g. after boot).
        if (_coordinator.State.FsEnabled)
        {
            StartWatcher();
        }

        try
        {
            await foreach (var e in _bus.SubscribeAsync(stoppingToken).ConfigureAwait(false))
            {
                if (e.Kind == DeviceEventKind.FsConnected)
                {
                    StartWatcher();
                }
                else if (e.Kind == DeviceEventKind.FsDisconnected)
                {
                    StopWatcher();
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutdown
        }
        finally
        {
            StopWatcher();
        }
    }

    private void StartWatcher()
    {
        try
        {
            EnsureWatchTargetExists();
            _watcher?.Dispose();
            _watcher = new FsShareWatcher(_bus, _options.WatchPath);
            if (_watcher.TryStart())
            {
                _logger.LogInformation("FS share watcher started at {Path}", _options.WatchPath);
            }
            else
            {
                _logger.LogWarning(
                    "FS share watch path not available yet: {Path}",
                    _options.WatchPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to start FS share watcher at {Path}", _options.WatchPath);
        }
    }

    private void StopWatcher()
    {
        try
        {
            _watcher?.Dispose();
            _watcher = null;
            _logger.LogInformation("FS share watcher stopped");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error stopping FS share watcher");
        }
    }

    private void EnsureWatchTargetExists()
    {
        var path = _options.WatchPath;
        // Prefer a directory share root; create if missing so watcher can attach.
        // If the path ends with a known image extension, only ensure the parent exists.
        if (path.EndsWith(".img", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".iso", StringComparison.OrdinalIgnoreCase))
        {
            var parent = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }

            return;
        }

        Directory.CreateDirectory(path);
    }
}
