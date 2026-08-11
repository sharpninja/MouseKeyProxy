using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Grpc.Core;
using Grpc.Net.Client;
using MouseKeyProxy.Common;
using Wire = MouseKeyProxy.Network.V1;

namespace MouseKeyProxy.Agent;

public sealed class RemoteInputForwarder : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WH_MOUSE_LL = 14;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const int WM_MOUSEMOVE = 0x0200;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_MBUTTONDOWN = 0x0207;
    private const int WM_MBUTTONUP = 0x0208;
    private const int WM_MOUSEWHEEL = 0x020A;
    private const int WM_XBUTTONDOWN = 0x020B;
    private const int WM_XBUTTONUP = 0x020C;
    private const int WM_MOUSEHWHEEL = 0x020E;

    private const uint VK_F1 = 0x70;
    private const uint VK_F2 = 0x71;
    private const uint VK_F3 = 0x72;
    private const uint VK_CONTROL = 0x11;
    private const uint VK_MENU = 0x12;
    private const uint VK_SHIFT = 0x10;
    private const uint VK_LWIN = 0x5B;
    private const uint VK_RWIN = 0x5C;
    private const uint VK_LCONTROL = 0xA2;
    private const uint VK_RCONTROL = 0xA3;
    private const uint VK_LMENU = 0xA4;
    private const uint VK_RMENU = 0xA5;

    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    private const uint MOUSEEVENTF_WHEEL = 0x0800;
    private const uint MOUSEEVENTF_XDOWN = 0x0080;
    private const uint MOUSEEVENTF_XUP = 0x0100;
    private const uint MOUSEEVENTF_HWHEEL = 0x01000;

    private readonly object _gate = new();
    private readonly KeyboardHookProc _keyboardProc;
    private readonly MouseHookProc _mouseProc;
    private IntPtr _keyboardHook;
    private IntPtr _mouseHook;
    private BlockingCollection<InputEvent>? _queue;
    private CancellationTokenSource? _stop;
    private Task? _sender;
    private GrpcChannel? _channel;
    private Wire.MouseKeyProxy.MouseKeyProxyClient? _client;
    private RawMouseInputWindow? _rawMouseWindow;
    private DateTimeOffset _passThroughUntilUtc;
    private bool _disposed;
    private readonly Func<string, GrpcChannel?>? _channelFactory;
    private readonly IHostCursorIndicator? _hostCursorIndicator;
    private readonly ConnectionFailsafe _failsafe = new();
    private Task? _watchdog;
    private int _lastMouseX = int.MinValue;
    private int _lastMouseY = int.MinValue;
    private int _captureCenterX;
    private int _captureCenterY;
    private readonly HotkeyConfig _hotkeys;
    private bool _ctrlDown;
    private bool _altDown;
    private bool _shiftDown;
    private bool _winDown;
    /// <summary>Raised when failsafe gives up and falls back to local input.</summary>
    public event EventHandler? FallbackToLocal;
    /// <summary>
    /// Raised when the forwarder's own hook sees toggle/emergency while active.
    /// Must stop capture here: the hotkey monitor often never sees the chord because this hook runs first and would otherwise eat it.
    /// </summary>
    public event EventHandler<ForwarderEscapeEventArgs>? EscapeRequested;

    /// <summary>Creates the forwarder.</summary>
    /// <param name="channelFactory">
    /// TR-MKP-SEC-001: optional factory that builds a mutually-authenticated channel for a remote URL
    /// (returns null when unpaired). When omitted, a plain channel is used (test/local paths).
    /// </param>
    /// <param name="hotkeys">Toggle/emergency chords (same config as the tray hotkey monitor).</param>
    /// <param name="hostCursorIndicator">Host pointer state to update while remote control is active.</param>
    public RemoteInputForwarder(
        Func<string, GrpcChannel?>? channelFactory = null,
        HotkeyConfig? hotkeys = null,
        IHostCursorIndicator? hostCursorIndicator = null)
    {
        _channelFactory = channelFactory;
        _hotkeys = hotkeys ?? new HotkeyConfig();
        _hostCursorIndicator = hostCursorIndicator;
        _keyboardProc = KeyboardHookCallback;
        _mouseProc = MouseHookCallback;
    }

    public bool IsActive { get; private set; }

    public string? RemoteUrl { get; private set; }

    public void Start(string remoteUrl)
    {
        if (string.IsNullOrWhiteSpace(remoteUrl))
        {
            throw new ArgumentException("Remote URL is required.", nameof(remoteUrl));
        }

        lock (_gate)
        {
            ThrowIfDisposed();
            if (IsActive)
            {
                return;
            }

            RemoteUrl = remoteUrl;
            _queue = new BlockingCollection<InputEvent>(boundedCapacity: 4096);
            _stop = new CancellationTokenSource();
            var bounds = System.Windows.Forms.Screen.PrimaryScreen?.Bounds
                ?? new System.Drawing.Rectangle(0, 0, 800, 600);
            _captureCenterX = bounds.Left + bounds.Width / 2;
            _captureCenterY = bounds.Top + bounds.Height / 2;
            _lastMouseX = _captureCenterX;
            _lastMouseY = _captureCenterY;
            _ctrlDown = _altDown = _shiftDown = _winDown = false;
            SetCursorPos(_captureCenterX, _captureCenterY);
            _channel = _channelFactory is not null
                ? _channelFactory(remoteUrl) ?? throw new InvalidOperationException("No paired credential for remote input forwarding.")
                : GrpcChannel.ForAddress(remoteUrl, new GrpcChannelOptions
                {
                    HttpHandler = new SocketsHttpHandler { EnableMultipleHttp2Connections = true }
                });
            _client = new Wire.MouseKeyProxy.MouseKeyProxyClient(_channel);
            _failsafe.OnActivated();
            _sender = Task.Run(() => SendLoopAsync(_stop.Token));
            _watchdog = Task.Run(() => WatchdogAsync(_stop.Token));

            try
            {
                _rawMouseWindow = new RawMouseInputWindow(OnRawMouseDelta);
            }
            catch (Exception ex)
            {
                // LL-hook relative deltas still work if raw input registration fails (e.g. some RDP sessions).
                Debug.WriteLine($"MouseKeyProxy raw mouse registration failed: {ex.Message}");
                _rawMouseWindow = null;
            }

            _passThroughUntilUtc = DateTimeOffset.UtcNow.AddMilliseconds(300);
            _keyboardHook = SetHook(WH_KEYBOARD_LL, _keyboardProc);
            _mouseHook = SetHook(WH_MOUSE_LL, _mouseProc);
            if (_keyboardHook == IntPtr.Zero || _mouseHook == IntPtr.Zero)
            {
                var error = Marshal.GetLastWin32Error();
                StopCore(sendModifierRelease: false);
                throw new InvalidOperationException($"Unable to install remote input hooks. win32={error}");
            }

            IsActive = true;
            SetHostCursorIndicator(remoteControlActive: true);
        }
    }

    /// <summary>
    /// Stops capture and restores host keyboard/mouse immediately.
    /// </summary>
    /// <param name="releaseRemoteModifiers">
    /// When true, attempts a best-effort remote modifier clear on a background thread.
    /// Never blocks the caller on peer RPC (LL-hook and UI escape paths must stay non-blocking).
    /// </param>
    public void Stop(bool releaseRemoteModifiers = true)
    {
        lock (_gate)
        {
            StopCore(sendModifierRelease: releaseRemoteModifiers);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            StopCore(sendModifierRelease: false);
            _disposed = true;
        }
    }

    public static InputEvent? TranslateKeyboardMessage(int message, uint vk, uint scan, uint flags)
    {
        var kind = message switch
        {
            WM_KEYDOWN or WM_SYSKEYDOWN => InputKind.KEY_DOWN,
            WM_KEYUP or WM_SYSKEYUP => InputKind.KEY_UP,
            _ => (InputKind?)null
        };

        if (kind is null || !InputSupportMatrix.IsSupported(kind.Value, vk))
        {
            return null;
        }

        return new InputEvent(kind.Value, Vk: vk, Scan: scan, Flags: flags, TsMs: NowMs());
    }

    public static InputEvent? TranslateRawMouseDelta(int dx, int dy)
    {
        return dx == 0 && dy == 0
            ? null
            : new InputEvent(InputKind.MOUSE_MOVE, Dx: dx, Dy: dy, TsMs: NowMs());
    }

    public static InputEvent? TranslateMouseMessage(int message, uint mouseData)
    {
        if (message == WM_MOUSEMOVE)
        {
            return null;
        }

        var flags = message switch
        {
            WM_LBUTTONDOWN => MOUSEEVENTF_LEFTDOWN,
            WM_LBUTTONUP => MOUSEEVENTF_LEFTUP,
            WM_RBUTTONDOWN => MOUSEEVENTF_RIGHTDOWN,
            WM_RBUTTONUP => MOUSEEVENTF_RIGHTUP,
            WM_MBUTTONDOWN => MOUSEEVENTF_MIDDLEDOWN,
            WM_MBUTTONUP => MOUSEEVENTF_MIDDLEUP,
            WM_XBUTTONDOWN => MOUSEEVENTF_XDOWN,
            WM_XBUTTONUP => MOUSEEVENTF_XUP,
            WM_MOUSEWHEEL => MOUSEEVENTF_WHEEL,
            WM_MOUSEHWHEEL => MOUSEEVENTF_HWHEEL,
            _ => 0u
        };

        if (flags == 0)
        {
            return null;
        }

        if (message == WM_MOUSEWHEEL || message == WM_MOUSEHWHEEL)
        {
            var delta = unchecked((short)((mouseData >> 16) & 0xffff));
            return new InputEvent(
                message == WM_MOUSEWHEEL ? InputKind.MOUSE_WHEEL : InputKind.MOUSE_HWHEEL,
                Flags: flags,
                WheelDelta: delta,
                TsMs: NowMs());
        }

        if (message == WM_XBUTTONDOWN || message == WM_XBUTTONUP)
        {
            var xButton = (uint)((mouseData >> 16) & 0xffff);
            return new InputEvent(message == WM_XBUTTONDOWN ? InputKind.MOUSE_DOWN : InputKind.MOUSE_UP, Flags: flags, XButton: xButton, TsMs: NowMs());
        }

        var buttonKind = message is WM_LBUTTONDOWN or WM_RBUTTONDOWN or WM_MBUTTONDOWN
            ? InputKind.MOUSE_DOWN
            : InputKind.MOUSE_UP;
        return new InputEvent(buttonKind, Flags: flags, TsMs: NowMs());
    }

    private async Task SendLoopAsync(CancellationToken ct)
    {
        var batch = new List<InputEvent>(32);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var next = _queue!.Take(ct);
                batch.Add(next);
                while (batch.Count < 32 && _queue.TryTake(out next))
                {
                    batch.Add(next);
                }

                await SendBatchAsync(batch, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MouseKeyProxy remote input send failed: {ex.Message}");
                if (IsDeviceHidLost(ex.Message, err: null))
                {
                    RequestHidLostFallback(ex.Message);
                    break;
                }

                // Soft fault: mark disconnected; watchdog may give up later.
                _failsafe.OnDisconnected();
            }
            finally
            {
                batch.Clear();
            }
        }
    }

    private async Task SendBatchAsync(IReadOnlyList<InputEvent> events, CancellationToken ct)
    {
        if (events.Count == 0 || _client is null)
        {
            return;
        }

        var request = new Wire.InjectInputRequest
        {
            ProtocolVersion = "v1",
            PeerId = Environment.MachineName,
            CorrelationId = Guid.NewGuid().ToString("N")
        };
        foreach (var input in events)
        {
            request.Events.Add(ToWire(input));
        }

        // Hard deadline so a dead Pi / stuck HID never freezes the send loop (or later Stop).
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(800));
        var deadline = DateTime.UtcNow.AddMilliseconds(800);
        Wire.CommandResult response;
        try
        {
            response = await _client
                .InjectInputAsync(request, deadline: deadline, cancellationToken: timeoutCts.Token)
                .ResponseAsync
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (IsDeviceHidLost(ex.Message, err: null) || IsTransportTimeout(ex) || IsAuthOrTlsFailure(ex))
        {
            if (IsDeviceHidLost(ex.Message, err: null))
            {
                RequestHidLostFallback(ex.Message);
            }
            else if (IsAuthOrTlsFailure(ex))
            {
                // Stale peer-credential after re-pair: surface clearly and drop to local.
                TryAppendForwardLog($"inject TLS/auth failed: {ex.Message}");
                RequestHidLostFallback($"TLS/credential failure (re-pair may be required): {ex.Message}");
            }
            else
            {
                Debug.WriteLine($"MouseKeyProxy remote input timed out: {ex.Message}");
                TryAppendForwardLog($"inject timeout: {ex.Message}");
                _failsafe.OnDisconnected();
            }

            return;
        }

        if (response.Ok)
        {
            // Successful ack is proof of peer liveness - resets the force-release deadline.
            _failsafe.OnHeartbeat();
            return;
        }

        Debug.WriteLine($"MouseKeyProxy remote input rejected: {response.Err} {response.Msg}");
        TryAppendForwardLog($"inject rejected err={response.Err} msg={response.Msg} events={events.Count}");
        if (IsDeviceHidLost(response.Msg, response.Err))
        {
            RequestHidLostFallback($"{response.Err}: {response.Msg}");
            return;
        }

        _failsafe.OnDisconnected();
    }

    private static void TryAppendForwardLog(string line)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MouseKeyProxy",
                "logs");
            Directory.CreateDirectory(dir);
            File.AppendAllText(
                Path.Combine(dir, "forwarder.log"),
                $"{DateTimeOffset.Now:o} {line}{Environment.NewLine}");
        }
        catch
        {
            // diagnostics only
        }
    }

    private static bool IsTransportTimeout(Exception ex)
    {
        return ex is OperationCanceledException
            || ex is TimeoutException
            || (ex is RpcException rpc && (rpc.StatusCode == StatusCode.DeadlineExceeded || rpc.StatusCode == StatusCode.Cancelled))
            || ex.Message.Contains("DeadlineExceeded", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("canceled", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAuthOrTlsFailure(Exception ex)
    {
        if (ex is RpcException rpc &&
            (rpc.StatusCode == StatusCode.Unauthenticated ||
             rpc.StatusCode == StatusCode.PermissionDenied ||
             rpc.StatusCode == StatusCode.Internal))
        {
            // Internal often wraps AuthenticationException for mTLS failures.
            var detail = rpc.Status.Detail ?? string.Empty;
            if (detail.Contains("SSL", StringComparison.OrdinalIgnoreCase) ||
                detail.Contains("certificate", StringComparison.OrdinalIgnoreCase) ||
                detail.Contains("Authentication", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        for (var cur = ex; cur is not null; cur = cur.InnerException)
        {
            if (cur is System.Security.Authentication.AuthenticationException)
            {
                return true;
            }

            if (cur.Message.Contains("certificate", StringComparison.OrdinalIgnoreCase) ||
                cur.Message.Contains("SSL", StringComparison.OrdinalIgnoreCase) ||
                cur.Message.Contains("RemoteCertificate", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// TR-MKP-RELI-001: when the Pi loses USB HID to the host PC, restore local control immediately.
    /// </summary>
    private static bool IsDeviceHidLost(string? message, string? err)
    {
        if (!string.IsNullOrWhiteSpace(err) &&
            err.Contains("DEVICE_HID_DISCONNECTED", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        return message.Contains("DEVICE_HID_DISCONNECTED", StringComparison.OrdinalIgnoreCase)
            || message.Contains("ESHUTDOWN", StringComparison.OrdinalIgnoreCase)
            || message.Contains("not attached", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Broken pipe", StringComparison.OrdinalIgnoreCase)
            || message.Contains("No such device", StringComparison.OrdinalIgnoreCase);
    }

    private void RequestHidLostFallback(string detail)
    {
        Debug.WriteLine($"MouseKeyProxy HID link lost: {detail}");
        try
        {
            lock (_gate)
            {
                if (IsActive)
                {
                    StopCore(sendModifierRelease: false);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"MouseKeyProxy HID-loss StopCore failed: {ex.Message}");
        }

        try
        {
            FallbackToLocal?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"MouseKeyProxy HID-loss FallbackToLocal failed: {ex.Message}");
        }
    }

    /// <summary>
    /// TR-MKP-RELI-001: watchdog that force-releases held modifiers when the peer goes silent past
    /// the failsafe deadline, so keys cannot remain stuck if the remote stops acking.
    /// </summary>
    private async Task WatchdogAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(250, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (_failsafe.ShouldForceRelease())
            {
                Debug.WriteLine("MouseKeyProxy failsafe: peer silent past deadline; force-releasing modifiers.");
                var client = _client;
                if (client is not null)
                {
                    try
                    {
                        await SendModifierReleaseAsync(client).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"MouseKeyProxy failsafe modifier release failed: {ex.Message}");
                    }
                }

                // Keep forwarding active; only release stuck modifiers until reconnect or give-up.
                _failsafe.OnHeartbeat(); // avoid hammering release every 250ms while still active
            }
            else if (_failsafe.ShouldGiveUpReconnect())
            {
                Debug.WriteLine("MouseKeyProxy failsafe: reconnect window exceeded; falling back to local input.");
                lock (_gate)
                {
                    StopCore(sendModifierRelease: true);
                }

                try
                {
                    FallbackToLocal?.Invoke(this, EventArgs.Empty);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"MouseKeyProxy FallbackToLocal handler failed: {ex.Message}");
                }
            }
        }
    }

    private void StopCore(bool sendModifierRelease)
    {
        if (!IsActive && _queue is null && _channel is null && _rawMouseWindow is null)
        {
            SetHostCursorIndicator(remoteControlActive: false);
            return;
        }

        // Host input must return before any peer RPC. Unhook first so Ctrl-Win-F1 / emergency
        // never waits on a hung Pi or dead HID link.
        IsActive = false;

        if (_keyboardHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = IntPtr.Zero;
        }

        if (_mouseHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }

        SetHostCursorIndicator(remoteControlActive: false);

        _rawMouseWindow?.Dispose();
        _rawMouseWindow = null;

        _failsafe.OnReleased();
        _stop?.Cancel();
        _queue?.CompleteAdding();

        // Brief join only: never stall escape for network completion.
        try { _sender?.Wait(TimeSpan.FromMilliseconds(250)); } catch { }
        _sender = null;
        try { _watchdog?.Wait(TimeSpan.FromMilliseconds(250)); } catch { }
        _watchdog = null;
        _stop?.Dispose();
        _stop = null;
        _queue?.Dispose();
        _queue = null;

        var client = _client;
        var channel = _channel;
        _client = null;
        _channel = null;
        RemoteUrl = null;

        if (sendModifierRelease && client is not null)
        {
            // Background best-effort clear; never block hook/UI threads.
            _ = Task.Run(async () =>
            {
                try
                {
                    await SendModifierReleaseAsync(client).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"MouseKeyProxy remote modifier release failed: {ex.Message}");
                }
                finally
                {
                    try { channel?.Dispose(); } catch { /* ignore */ }
                }
            });
        }
        else
        {
            try { channel?.Dispose(); } catch { /* ignore */ }
        }
    }

    private void SetHostCursorIndicator(bool remoteControlActive)
    {
        try
        {
            _hostCursorIndicator?.SetRemoteControlActive(remoteControlActive);
        }
        catch (Exception ex)
        {
            // Cursor state is an operator aid; it must never prevent capture or emergency release.
            Debug.WriteLine($"MouseKeyProxy host cursor indicator failed: {ex.Message}");
        }
    }

    private static async Task SendModifierReleaseAsync(Wire.MouseKeyProxy.MouseKeyProxyClient client)
    {
        var request = new Wire.InjectInputRequest
        {
            ProtocolVersion = "v1",
            PeerId = Environment.MachineName,
            CorrelationId = Guid.NewGuid().ToString("N")
        };
        foreach (var input in ModifierReleasePolicy.CreateKeyUpEvents())
        {
            request.Events.Add(ToWire(input));
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(750));
        var call = client.InjectInputAsync(request, deadline: DateTime.UtcNow.AddMilliseconds(750), cancellationToken: cts.Token);
        await call.ResponseAsync.ConfigureAwait(false);
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0)
        {
            return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
        }

        var message = wParam.ToInt32();
        var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
        var isDown = message is WM_KEYDOWN or WM_SYSKEYDOWN;
        UpdateTrackedModifiers(data.vkCode, isDown);

        // Never swallow SendInput / service→agent inject events (Windows peer path on this machine).
        if (!LowLevelInputFlags.ShouldConsumeForRemoteForward(
                IsActive && DateTimeOffset.UtcNow >= _passThroughUntilUtc,
                data.flags,
                isMouse: false))
        {
            return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
        }

        // Escape chords must never be forwarded to the Pi; they restore host control.
        if (isDown && IsEmergencyChord(data.vkCode))
        {
            RequestEscape(emergency: true);
            return new IntPtr(1); // consume so Pi does not see F3
        }

        if (isDown && IsToggleChord(data.vkCode))
        {
            RequestEscape(emergency: false);
            return new IntPtr(1); // consume; we already restored local
        }

        var input = TranslateKeyboardMessage(message, data.vkCode, data.scanCode, data.flags);
        if (input != null && TryEnqueue(input))
        {
            return new IntPtr(1);
        }

        return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    private void RequestEscape(bool emergency)
    {
        // Stop hooks first with no peer wait. Host must regain input even if the Pi is dead.
        try
        {
            lock (_gate)
            {
                if (IsActive)
                {
                    StopCore(sendModifierRelease: true);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"MouseKeyProxy escape StopCore failed: {ex.Message}");
        }

        try
        {
            EscapeRequested?.Invoke(this, new ForwarderEscapeEventArgs(emergency));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"MouseKeyProxy EscapeRequested handler failed: {ex.Message}");
        }
    }

    private void UpdateTrackedModifiers(uint vk, bool isDown)
    {
        switch (vk)
        {
            case VK_CONTROL:
            case VK_LCONTROL:
            case VK_RCONTROL:
                _ctrlDown = isDown;
                break;
            case VK_MENU:
            case VK_LMENU:
            case VK_RMENU:
                _altDown = isDown;
                break;
            case VK_SHIFT:
            case 0xA0:
            case 0xA1:
                _shiftDown = isDown;
                break;
            case VK_LWIN:
            case VK_RWIN:
                _winDown = isDown;
                break;
        }
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            // Pass through SendInput / agent-pipe inject; do not re-center or eat synthetic mouse.
            if (!LowLevelInputFlags.ShouldConsumeForRemoteForward(
                    IsActive && DateTimeOffset.UtcNow >= _passThroughUntilUtc,
                    data.flags,
                    isMouse: true))
            {
                return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
            }

            var message = wParam.ToInt32();

            // Relative deltas from LL hook, then re-center host cursor (1px ClipCursor yields dx=0).
            if (message == WM_MOUSEMOVE)
            {
                var dx = data.pt.x - _lastMouseX;
                var dy = data.pt.y - _lastMouseY;
                var move = TranslateRawMouseDelta(dx, dy);
                if (move != null)
                {
                    TryEnqueue(move);
                }

                // Keep host pointer parked so further moves produce fresh relative deltas.
                SetCursorPos(_captureCenterX, _captureCenterY);
                _lastMouseX = _captureCenterX;
                _lastMouseY = _captureCenterY;
                return new IntPtr(1);
            }

            var input = TranslateMouseMessage(message, data.mouseData);
            if (input != null && TryEnqueue(input))
            {
                return new IntPtr(1);
            }
        }

        return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private void OnRawMouseDelta(int dx, int dy)
    {
        if (!IsActive || DateTimeOffset.UtcNow < _passThroughUntilUtc)
        {
            return;
        }

        var input = TranslateRawMouseDelta(dx, dy);
        if (input != null)
        {
            TryEnqueue(input);
        }
    }

    private bool TryEnqueue(InputEvent input)
    {
        var queue = _queue;
        return queue != null && !queue.IsAddingCompleted && queue.TryAdd(input);
    }

    private bool IsToggleChord(uint vk)
    {
        // Configured chord (default Ctrl-Win-F1) using hook-tracked modifiers (Win is unreliable via GetAsyncKeyState).
        if (MatchesConfigured(_hotkeys.ToggleVk, _hotkeys.ToggleMods, vk))
        {
            return true;
        }

        // Fixed remote-activation fallback: F1 with any two of Control, Alt, and Windows.
        if (RemoteActivationChord.Matches(vk, IsControlDown(), IsAltDown(), IsWinDown()))
        {
            return true;
        }

        // Legacy remote chord.
        return vk == VK_F2 && IsControlDown() && IsAltDown();
    }

    private bool IsEmergencyChord(uint vk)
    {
        if (MatchesConfigured(_hotkeys.EmergencyReleaseVk, _hotkeys.EmergencyReleaseMods, vk))
        {
            return true;
        }

        // Fixed safety fallback: F3 with any two of Control, Alt, and Windows.
        return EmergencyReleaseChord.Matches(vk, IsControlDown(), IsAltDown(), IsWinDown());
    }

    private bool MatchesConfigured(uint targetVk, uint mods, uint vk)
    {
        if (vk != targetVk)
        {
            return false;
        }

        const uint modAlt = 0x0001, modControl = 0x0002, modShift = 0x0004, modWin = 0x0008;
        if ((mods & modControl) != 0 && !IsControlDown()) return false;
        if ((mods & modAlt) != 0 && !IsAltDown()) return false;
        if ((mods & modShift) != 0 && !IsShiftDown()) return false;
        if ((mods & modWin) != 0 && !IsWinDown()) return false;
        return true;
    }

    private bool IsControlDown() => _ctrlDown || IsKeyDown(VK_CONTROL) || IsKeyDown(VK_LCONTROL) || IsKeyDown(VK_RCONTROL);

    private bool IsAltDown() => _altDown || IsKeyDown(VK_MENU) || IsKeyDown(VK_LMENU) || IsKeyDown(VK_RMENU);

    private bool IsShiftDown() => _shiftDown || IsKeyDown(VK_SHIFT);

    private bool IsWinDown() => _winDown || IsKeyDown(VK_LWIN) || IsKeyDown(VK_RWIN);

    private static bool IsKeyDown(uint vk)
    {
        return (GetAsyncKeyState((int)vk) & 0x8000) != 0 || (GetKeyState((int)vk) & 0x8000) != 0;
    }

    private static Wire.InputEvent ToWire(InputEvent input)
    {
        return new Wire.InputEvent
        {
            Kind = (Wire.InputKind)input.Kind,
            Vk = input.Vk,
            Scan = input.Scan,
            Flags = input.Flags,
            Dx = input.Dx,
            Dy = input.Dy,
            WheelDelta = input.WheelDelta,
            Xbutton = input.XButton,
            Text = input.Text ?? string.Empty,
            TsMs = input.TsMs == 0 ? NowMs() : input.TsMs
        };
    }

    private static ulong NowMs()
    {
        return (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    private static IntPtr SetHook(int hookId, Delegate proc)
    {
        using var currentProcess = Process.GetCurrentProcess();
        using var currentModule = currentProcess.MainModule;
        var moduleHandle = currentModule?.ModuleName is { Length: > 0 } moduleName
            ? GetModuleHandle(moduleName)
            : IntPtr.Zero;
        return hookId == WH_KEYBOARD_LL
            ? SetWindowsHookEx(hookId, (KeyboardHookProc)proc, moduleHandle, 0)
            : SetWindowsHookEx(hookId, (MouseHookProc)proc, moduleHandle, 0);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(RemoteInputForwarder));
        }
    }

    private sealed class RawMouseInputWindow : NativeWindow, IDisposable
    {
        private const int WM_INPUT = 0x00FF;
        private const uint RIDEV_INPUTSINK = 0x00000100;
        private const uint RIDEV_REMOVE = 0x00000001;
        private const uint RID_INPUT = 0x10000003;
        private const uint RIM_TYPEMOUSE = 0;
        private readonly Action<int, int> _onDelta;
        private bool _disposed;

        public RawMouseInputWindow(Action<int, int> onDelta)
        {
            _onDelta = onDelta;
            CreateHandle(new CreateParams { Caption = "MouseKeyProxy.RawInput", Width = 1, Height = 1 });
            RegisterMouse(Handle, RIDEV_INPUTSINK);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_INPUT && TryReadMouse(m.LParam, out var dx, out var dy))
            {
                _onDelta(dx, dy);
            }

            base.WndProc(ref m);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            TryUnregisterMouse();
            DestroyHandle();
            _disposed = true;
        }

        private static void RegisterMouse(IntPtr hwnd, uint flags)
        {
            var device = new RAWINPUTDEVICE
            {
                usUsagePage = 0x01,
                usUsage = 0x02,
                dwFlags = flags,
                hwndTarget = hwnd
            };

            if (!RegisterRawInputDevices(new[] { device }, 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>()))
            {
                throw new InvalidOperationException($"RegisterRawInputDevices failed win32={Marshal.GetLastWin32Error()}");
            }
        }

        private static bool TryReadMouse(IntPtr lParam, out int dx, out int dy)
        {
            dx = 0;
            dy = 0;
            var size = 0u;
            var headerSize = (uint)Marshal.SizeOf<RAWINPUTHEADER>();
            if (GetRawInputData(lParam, RID_INPUT, IntPtr.Zero, ref size, headerSize) == uint.MaxValue || size == 0)
            {
                return false;
            }

            var buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                if (GetRawInputData(lParam, RID_INPUT, buffer, ref size, headerSize) == uint.MaxValue)
                {
                    return false;
                }

                var input = Marshal.PtrToStructure<RAWINPUT>(buffer);
                if (input.header.dwType != RIM_TYPEMOUSE)
                {
                    return false;
                }

                // Absolute devices (some touchpads/pens) report 0..65535 coordinates, not deltas.
                // Forwarding absolute values as relative HID would appear as no useful mouse motion.
                const ushort mouseMoveAbsolute = 0x01;
                if ((input.mouse.usFlags & mouseMoveAbsolute) != 0)
                {
                    return false;
                }

                dx = input.mouse.lLastX;
                dy = input.mouse.lLastY;
                return dx != 0 || dy != 0;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] pRawInputDevices, uint uiNumDevices, uint cbSize);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetRawInputData(IntPtr hRawInput, uint uiCommand, IntPtr pData, ref uint pcbSize, uint cbSizeHeader);

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWINPUTDEVICE
        {
            public ushort usUsagePage;
            public ushort usUsage;
            public uint dwFlags;
            public IntPtr hwndTarget;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWINPUTHEADER
        {
            public uint dwType;
            public uint dwSize;
            public IntPtr hDevice;
            public IntPtr wParam;
        }

        private static void TryUnregisterMouse()
        {
            try
            {
                RegisterMouse(IntPtr.Zero, RIDEV_REMOVE);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MouseKeyProxy raw input unregister failed: {ex.Message}");
            }
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct RAWMOUSE
        {
            [FieldOffset(0)] public ushort usFlags;
            [FieldOffset(4)] public uint ulButtons;
            [FieldOffset(4)] public ushort usButtonFlags;
            [FieldOffset(6)] public ushort usButtonData;
            [FieldOffset(8)] public uint ulRawButtons;
            [FieldOffset(12)] public int lLastX;
            [FieldOffset(16)] public int lLastY;
            [FieldOffset(20)] public uint ulExtraInformation;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWINPUT
        {
            public RAWINPUTHEADER header;
            public RAWMOUSE mouse;
        }
    }

    private delegate IntPtr KeyboardHookProc(int nCode, IntPtr wParam, IntPtr lParam);
    private delegate IntPtr MouseHookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, KeyboardHookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, MouseHookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);
}

/// <summary>Why the forwarder requested escape back to local control.</summary>
public sealed class ForwarderEscapeEventArgs : EventArgs
{
    /// <summary>Creates the args.</summary>
    /// <param name="emergency">True when any-two-modifiers plus F3 (or configured emergency) was used.</param>
    public ForwarderEscapeEventArgs(bool emergency) => Emergency = emergency;

    /// <summary>True for emergency-release chord; false for normal toggle-local chord.</summary>
    public bool Emergency { get; }
}
