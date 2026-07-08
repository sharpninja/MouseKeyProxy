using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
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
    private const uint VK_CONTROL = 0x11;
    private const uint VK_MENU = 0x12;

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
    private readonly ConnectionFailsafe _failsafe = new();
    private Task? _watchdog;

    /// <summary>Creates the forwarder.</summary>
    /// <param name="channelFactory">
    /// TR-MKP-SEC-001: optional factory that builds a mutually-authenticated channel for a remote URL
    /// (returns null when unpaired). When omitted, a plain channel is used (test/local paths).
    /// </param>
    public RemoteInputForwarder(Func<string, GrpcChannel?>? channelFactory = null)
    {
        _channelFactory = channelFactory;
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

            _rawMouseWindow = new RawMouseInputWindow(OnRawMouseDelta);
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
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            StopCore(sendModifierRelease: true);
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

            StopCore(sendModifierRelease: true);
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
            catch (InvalidOperationException)
            {
                break;
            }
            catch (Exception ex)
            {
                // Observe (do not swallow) send faults: mark the channel disconnected so the watchdog
                // can enforce the reconnect give-up / force-release deadlines.
                _failsafe.OnDisconnected();
                Debug.WriteLine($"MouseKeyProxy remote input send failed: {ex.Message}");
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

        var response = await _client.InjectInputAsync(request, cancellationToken: ct).ResponseAsync.ConfigureAwait(false);
        if (response.Ok)
        {
            // Successful ack is proof of peer liveness - resets the force-release deadline.
            _failsafe.OnHeartbeat();
        }
        else
        {
            Debug.WriteLine($"MouseKeyProxy remote input rejected: {response.Err} {response.Msg}");
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
                TrySendModifierRelease();
                _failsafe.OnReleased();
            }
            else if (_failsafe.ShouldGiveUpReconnect())
            {
                Debug.WriteLine("MouseKeyProxy failsafe: reconnect window exceeded; falling back to local input.");
                _failsafe.OnReleased();
            }
        }
    }

    private void StopCore(bool sendModifierRelease)
    {
        if (!IsActive && _queue is null && _channel is null && _rawMouseWindow is null)
        {
            return;
        }

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

        _rawMouseWindow?.Dispose();
        _rawMouseWindow = null;

        if (sendModifierRelease)
        {
            TrySendModifierRelease();
        }

        _failsafe.OnReleased();
        _stop?.Cancel();
        _queue?.CompleteAdding();
        try { _sender?.Wait(TimeSpan.FromSeconds(1)); } catch { }
        _sender = null;
        try { _watchdog?.Wait(TimeSpan.FromSeconds(1)); } catch { }
        _watchdog = null;
        _stop?.Dispose();
        _stop = null;
        _queue?.Dispose();
        _queue = null;
        _channel?.Dispose();
        _channel = null;
        _client = null;
        RemoteUrl = null;
    }

    private void TrySendModifierRelease()
    {
        if (_client is null)
        {
            return;
        }

        try
        {
            SendBatchAsync(ModifierReleasePolicy.CreateKeyUpEvents(), CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"MouseKeyProxy remote modifier release failed: {ex.Message}");
        }
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && IsActive && DateTimeOffset.UtcNow >= _passThroughUntilUtc)
        {
            var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            if (!IsToggleChord(data.vkCode))
            {
                var input = TranslateKeyboardMessage(wParam.ToInt32(), data.vkCode, data.scanCode, data.flags);
                if (input != null && TryEnqueue(input))
                {
                    return new IntPtr(1);
                }
            }
        }

        return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && IsActive && DateTimeOffset.UtcNow >= _passThroughUntilUtc)
        {
            if (wParam.ToInt32() == WM_MOUSEMOVE)
            {
                return new IntPtr(1);
            }

            var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            var input = TranslateMouseMessage(wParam.ToInt32(), data.mouseData);
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

    private static bool IsToggleChord(uint vk)
    {
        return (vk == VK_F1 || vk == VK_F2) && IsKeyDown(VK_CONTROL) && IsKeyDown(VK_MENU);
    }

    private static bool IsKeyDown(uint vk)
    {
        return (GetAsyncKeyState((int)vk) & 0x8000) != 0;
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

                dx = input.mouse.lLastX;
                dy = input.mouse.lLastY;
                return true;
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
}