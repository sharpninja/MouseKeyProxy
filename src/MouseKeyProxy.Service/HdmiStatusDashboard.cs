using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MouseKeyProxy.Service.Pairing;

namespace MouseKeyProxy.Service;

public interface IDeviceDashboardEventSink
{
    ValueTask RecordAsync(string direction, string kind, string summary, CancellationToken cancellationToken = default);
}

public sealed class NullDeviceDashboardEventSink : IDeviceDashboardEventSink
{
    public ValueTask RecordAsync(string direction, string kind, string summary, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
}

/// <summary>
/// Linux/Pi appliance HDMI dashboard owned by the systemd-launched gRPC service.
/// It writes directly to tty1, so no interactive user login is required.
/// </summary>
public sealed class HdmiStatusDashboard : BackgroundService, IDeviceDashboardEventSink
{
    private const int MaxRecentEvents = 40;
    private readonly ILogger<HdmiStatusDashboard> _logger;
    private readonly IPairedPeerStore _pairedPeers;
    private readonly ConcurrentQueue<string> _recentEvents = new();
    private readonly SemaphoreSlim _eventFileGate = new(1, 1);
    private readonly string _ttyPath;
    private readonly string _eventLogPath;
    private readonly string _pairingStatePath;
    private readonly string _keyboardDevice;
    private readonly string _mouseDevice;
    private readonly string _wifiSsid;

    public HdmiStatusDashboard(ILogger<HdmiStatusDashboard> logger, IPairedPeerStore pairedPeers)
    {
        _logger = logger;
        _pairedPeers = pairedPeers;
        _ttyPath = Environment.GetEnvironmentVariable("MKP_DASHBOARD_TTY") ?? "/dev/tty1";
        _eventLogPath = Environment.GetEnvironmentVariable("MKP_DASHBOARD_EVENT_LOG") ?? "/var/log/mousekeyproxy/events.log";
        _pairingStatePath = Environment.GetEnvironmentVariable("MKP_DASHBOARD_PAIRING_FILE") ?? "/var/lib/mousekeyproxy/pairing.env";
        _keyboardDevice = Environment.GetEnvironmentVariable("MKP_HID_KEYBOARD_DEVICE") ?? "/dev/hidg0";
        _mouseDevice = Environment.GetEnvironmentVariable("MKP_HID_MOUSE_DEVICE") ?? "/dev/hidg1";
        _wifiSsid = Environment.GetEnvironmentVariable("MKP_WIFI_SSID") ?? string.Empty;
    }

    public async ValueTask RecordAsync(string direction, string kind, string summary, CancellationToken cancellationToken = default)
    {
        var line = $"{DateTimeOffset.UtcNow:O} {Clean(direction)} {Clean(kind)} {Clean(summary)}";
        _recentEvents.Enqueue(line);
        while (_recentEvents.Count > MaxRecentEvents && _recentEvents.TryDequeue(out _))
        {
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_eventLogPath) ?? "/var/log/mousekeyproxy");
            await _eventFileGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await File.AppendAllTextAsync(_eventLogPath, line + Environment.NewLine, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _eventFileGate.Release();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            _logger.LogDebug(ex, "Could not append dashboard event log");
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RecordAsync("service", "start", "grpc-server-started dashboard=tty1", stoppingToken).ConfigureAwait(false);

        while (!stoppingToken.IsCancellationRequested)
        {
            await RenderOnceAsync(stoppingToken).ConfigureAwait(false);
            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task RenderOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            var text = BuildDashboardText();
            await using var stream = new FileStream(_ttyPath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite, 4096, useAsync: true);
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(text.AsMemory(), cancellationToken).ConfigureAwait(false);
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Could not render HDMI dashboard to {TtyPath}", _ttyPath);
        }
    }

    private string BuildDashboardText()
    {
        var now = DateTimeOffset.UtcNow;
        var peers = _pairedPeers.ExportPeers().Where(p => !p.Revoked).OrderBy(p => p.PeerId, StringComparer.OrdinalIgnoreCase).ToArray();
        var host = Environment.GetEnvironmentVariable("MKP_PAIR_HOST") ?? peers.FirstOrDefault()?.PeerId ?? "unpaired";
        var remote = Environment.GetEnvironmentVariable("MKP_PAIR_REMOTE") ?? Dns.GetHostName();
        var updated = Environment.GetEnvironmentVariable("MKP_PAIR_UPDATED_UTC") ?? peers.OrderByDescending(p => p.LastSeenUtc).FirstOrDefault()?.LastSeenUtc.ToString("O") ?? "never";
        var events = _recentEvents.ToArray();

        if (events.Length == 0 && File.Exists(_eventLogPath))
        {
            events = File.ReadLines(_eventLogPath).TakeLast(12).ToArray();
        }
        else
        {
            events = events.TakeLast(12).ToArray();
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_pairingStatePath) ?? "/var/lib/mousekeyproxy");
        File.WriteAllLines(_pairingStatePath, new[]
        {
            $"MKP_PAIR_HOST='{host}'",
            $"MKP_PAIR_REMOTE='{remote}'",
            $"MKP_PAIR_UPDATED_UTC='{updated}'",
        });

        var hidDevices = string.Join(' ', new[] { _keyboardDevice, _mouseDevice }.Where(File.Exists));
        if (string.IsNullOrWhiteSpace(hidDevices))
        {
            hidDevices = "none";
        }

        return "\u001b[2J\u001b[H" +
            "MouseKeyProxy Pi Service Status\n" +
            $"Updated: {now:O} UTC\n\n" +
            "Identity\n" +
            $"  Hostname: {Dns.GetHostName()}\n" +
            $"  IP: {GetPrimaryIp()}\n" +
            "  Owner: MouseKeyProxy.Service systemd process\n\n" +
            "Features\n" +
            $"  gRPC service: active\n" +
            $"  Wi-Fi SSID: {(string.IsNullOrWhiteSpace(_wifiSsid) ? "unknown" : _wifiSsid)}\n" +
            $"  HID keyboard: {(File.Exists(_keyboardDevice) ? "enabled" : "waiting")} ({_keyboardDevice})\n" +
            $"  HID mouse: {(File.Exists(_mouseDevice) ? "enabled" : "waiting")} ({_mouseDevice})\n" +
            $"  /dev/hidg*: {hidDevices}\n\n" +
            "Pairing\n" +
            $"  Host: {host}\n" +
            $"  Remote: {remote}\n" +
            $"  Updated: {updated}\n" +
            $"  Active peers: {(peers.Length == 0 ? "none" : string.Join(", ", peers.Select(p => p.PeerId)))}\n\n" +
            "Traffic\n" +
            (events.Length == 0 ? "  No host/remote traffic recorded yet.\n" : string.Join('\n', events.Select(e => "  " + e)) + "\n") +
            "\nEvent format: UTC direction kind summary\n";
    }

    private static string Clean(string value) => value.Replace('\r', ' ').Replace('\n', ' ').Trim();

    private static string GetPrimaryIp()
    {
        foreach (var address in NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Select(a => a.Address)
            .Where(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !IPAddress.IsLoopback(a)))
        {
            return address.ToString();
        }

        return "no-ip-yet";
    }
}
