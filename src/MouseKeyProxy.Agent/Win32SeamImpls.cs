using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using MouseKeyProxy.Common;

namespace MouseKeyProxy.Agent;

/// <summary>
/// Real shipped impls of seams using P/Invoke (SendInput, ClipCursor).
/// Called from tray/hotkey path (no placeholders).
/// </summary>
public class Win32InputInjector : IInputInjector
{
    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT { public uint type; public InputUnion u; }
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }
    [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public UIntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public UIntPtr dwExtraInfo; }

    public readonly record struct InputDescriptor(
        uint Type,
        ushort Vk,
        ushort Scan,
        uint Flags,
        int Dx,
        int Dy,
        uint MouseData,
        uint MouseFlags);

    private const uint INPUT_KEYBOARD = 1;
    private const uint INPUT_MOUSE = 0;
    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    private const uint MOUSEEVENTF_XDOWN = 0x0080;
    private const uint MOUSEEVENTF_XUP = 0x0100;
    private const uint MOUSEEVENTF_WHEEL = 0x0800;
    private const uint MOUSEEVENTF_HWHEEL = 0x01000;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;
    private const uint KEYEVENTF_SCANCODE = 0x0008;
    private const uint LLKHF_EXTENDED = 0x0001;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    public void Send(InputEvent evt)
    {
        if (!TryInjectBatch(new[] { evt }, out var error))
        {
            throw new InvalidOperationException(error ?? "Input injection failed.");
        }
    }

    public bool TryInjectBatch(IEnumerable<InputEvent> events, out string? error)
    {
        error = null;
        try
        {
            var inputs = BuildInputs(events).ToArray();
            if (inputs.Length == 0)
            {
                return true;
            }

            var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
            if (sent != inputs.Length)
            {
                error = $"SendInput sent {sent}/{inputs.Length} events win32={Marshal.GetLastWin32Error()}";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static IReadOnlyList<InputDescriptor> BuildInputDescriptors(IEnumerable<InputEvent> events)
    {
        return BuildInputs(events).Select(ToDescriptor).ToArray();
    }

    private static IEnumerable<INPUT> BuildInputs(IEnumerable<InputEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        foreach (var evt in events)
        {
            if (!InputSupportMatrix.IsSupported(evt.Kind, evt.Vk))
            {
                throw new InvalidOperationException("Unsupported input per matrix: " + InputSupportMatrix.GetFailureReason(evt.Kind, evt.Vk));
            }

            foreach (var input in BuildInputs(evt))
            {
                yield return input;
            }
        }
    }

    private static IEnumerable<INPUT> BuildInputs(InputEvent evt)
    {
        if (evt.Kind == InputKind.TEXT_INPUT)
        {
            foreach (var ch in evt.Text ?? string.Empty)
            {
                yield return KeyboardInput(0, ch, KEYEVENTF_UNICODE);
                yield return KeyboardInput(0, ch, KEYEVENTF_UNICODE | KEYEVENTF_KEYUP);
            }
            yield break;
        }

        if (evt.Kind == InputKind.KEY_DOWN || evt.Kind == InputKind.KEY_UP)
        {
            var keyFlags = evt.Kind == InputKind.KEY_UP ? KEYEVENTF_KEYUP : 0;
            var extended = (evt.Flags & LLKHF_EXTENDED) != 0 || IsExtendedKey(evt.Vk);
            if (extended)
            {
                keyFlags |= KEYEVENTF_EXTENDEDKEY;
            }

            if (evt.Scan != 0)
            {
                keyFlags |= KEYEVENTF_SCANCODE;
                yield return KeyboardInput(0, (ushort)evt.Scan, keyFlags);
            }
            else
            {
                yield return KeyboardInput((ushort)evt.Vk, 0, keyFlags);
            }
            yield break;
        }

        if (evt.Kind == InputKind.MOUSE_MOVE)
        {
            yield return MouseInput(evt.Dx, evt.Dy, 0, MOUSEEVENTF_MOVE);
            yield break;
        }

        if (evt.Kind == InputKind.MOUSE_DOWN || evt.Kind == InputKind.MOUSE_UP || evt.Kind == InputKind.MOUSE_XBUTTON)
        {
            var flags = evt.Flags != 0
                ? evt.Flags
                : evt.Kind == InputKind.MOUSE_DOWN ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP;
            yield return MouseInput(0, 0, evt.XButton, flags);
            yield break;
        }

        if (evt.Kind == InputKind.MOUSE_WHEEL || evt.Kind == InputKind.MOUSE_HWHEEL)
        {
            var flags = evt.Kind == InputKind.MOUSE_WHEEL ? MOUSEEVENTF_WHEEL : MOUSEEVENTF_HWHEEL;
            yield return MouseInput(0, 0, unchecked((uint)evt.WheelDelta), flags);
        }
    }

    private static INPUT KeyboardInput(ushort vk, ushort scan, uint flags)
    {
        return new INPUT
        {
            type = INPUT_KEYBOARD,
            u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, wScan = scan, dwFlags = flags } }
        };
    }

    private static INPUT MouseInput(int dx, int dy, uint mouseData, uint flags)
    {
        return new INPUT
        {
            type = INPUT_MOUSE,
            u = new InputUnion { mi = new MOUSEINPUT { dx = dx, dy = dy, mouseData = mouseData, dwFlags = flags } }
        };
    }

    private static InputDescriptor ToDescriptor(INPUT input)
    {
        return input.type == INPUT_KEYBOARD
            ? new InputDescriptor(input.type, input.u.ki.wVk, input.u.ki.wScan, input.u.ki.dwFlags, 0, 0, 0, 0)
            : new InputDescriptor(input.type, 0, 0, 0, input.u.mi.dx, input.u.mi.dy, input.u.mi.mouseData, input.u.mi.dwFlags);
    }

    private static bool IsExtendedKey(uint vk)
    {
        return vk is 0x21 or 0x22 or 0x23 or 0x24 or 0x25 or 0x26 or 0x27 or 0x28
            or 0x2D or 0x2E or 0x5B or 0x5C or 0x6F
            or ModifierReleasePolicy.VK_RCONTROL or ModifierReleasePolicy.VK_RMENU;
    }
}

public class Win32DesktopController : IRemoteDesktopController
{
    private const int SW_RESTORE = 9;

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    public RemoteControlResult SetMousePosition(string displayId, int x, int y)
    {
        if (SetCursorPos(x, y))
        {
            return RemoteControlResult.Success($"cursor moved display={displayId} x={x} y={y}");
        }

        return RemoteControlResult.Failure("SET_CURSOR_POS_FAILED", $"SetCursorPos failed win32={Marshal.GetLastWin32Error()}");
    }

    public IReadOnlyList<RemoteWindowNode> LocateProcess(string processName, uint pid)
    {
        var nodes = new List<RemoteWindowNode>();
        EnumWindows((hWnd, _) =>
        {
            GetWindowThreadProcessId(hWnd, out var windowPid);
            if (MatchesProcess(windowPid, processName, pid))
            {
                nodes.Add(ToNode(hWnd, windowPid));
            }

            return true;
        }, IntPtr.Zero);

        return nodes;
    }

    public RemoteControlResult SetFocusByHwnd(ulong hwnd, bool bringToFront)
    {
        var handle = new IntPtr(unchecked((long)hwnd));
        if (bringToFront)
        {
            ShowWindow(handle, SW_RESTORE);
        }

        return SetForegroundWindow(handle)
            ? RemoteControlResult.Success($"focused hwnd=0x{hwnd:x}")
            : RemoteControlResult.Failure("SET_FOREGROUND_FAILED", $"SetForegroundWindow failed hwnd=0x{hwnd:x}");
    }

    private static RemoteWindowNode ToNode(IntPtr hWnd, uint processId)
    {
        var children = new List<RemoteWindowNode>();
        EnumChildWindows(hWnd, (child, _) =>
        {
            GetWindowThreadProcessId(child, out var childPid);
            children.Add(new RemoteWindowNode(
                (ulong)child.ToInt64(),
                GetText(child),
                GetClass(child),
                childPid,
                Array.Empty<RemoteWindowNode>()));
            return true;
        }, IntPtr.Zero);

        return new RemoteWindowNode(
            (ulong)hWnd.ToInt64(),
            GetText(hWnd),
            GetClass(hWnd),
            processId,
            children);
    }

    private static bool MatchesProcess(uint windowPid, string processName, uint pid)
    {
        if (pid != 0 && windowPid != pid)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(processName))
        {
            return pid == 0 || windowPid == pid;
        }

        try
        {
            using var process = Process.GetProcessById((int)windowPid);
            var expected = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? processName[..^4]
                : processName;
            return string.Equals(process.ProcessName, expected, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string GetText(IntPtr hWnd)
    {
        var builder = new System.Text.StringBuilder(512);
        GetWindowText(hWnd, builder, builder.Capacity);
        return builder.ToString();
    }

    private static string GetClass(IntPtr hWnd)
    {
        var builder = new System.Text.StringBuilder(256);
        GetClassName(hWnd, builder, builder.Capacity);
        return builder.ToString();
    }
}

public sealed class Win32ScreenshotCapture : IScreenshotCapture
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    public ScreenshotCaptureResult Capture(ScreenshotCaptureRequest request)
    {
        var correlationId = string.IsNullOrWhiteSpace(request.CorrelationId)
            ? Guid.NewGuid().ToString("N")
            : request.CorrelationId;
        var capturedAt = DateTimeOffset.UtcNow;
        var bounds = ResolveBounds(request, out var hwnd);

        using var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
            if (request.IncludeCursor)
            {
                DrawCursorIfInside(graphics, bounds);
            }
        }

        byte[] png;
        using (var ms = new MemoryStream())
        {
            bitmap.Save(ms, ImageFormat.Png);
            png = ms.ToArray();
        }

        var target = request.Target == ScreenshotTarget.Hwnd && hwnd == IntPtr.Zero
            ? ScreenshotTarget.Foreground
            : request.Target;
        var metadataText = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["MouseKeyProxy.CapturedAtUtc"] = capturedAt.ToString("O"),
            ["MouseKeyProxy.SourceHost"] = Environment.MachineName,
            ["MouseKeyProxy.CorrelationId"] = correlationId,
            ["MouseKeyProxy.Target"] = target.ToString(),
            ["MouseKeyProxy.Hwnd"] = $"0x{unchecked((ulong)hwnd.ToInt64()):x}",
            ["MouseKeyProxy.Width"] = bounds.Width.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["MouseKeyProxy.Height"] = bounds.Height.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        png = PngTextMetadata.AddTextChunks(png, metadataText);
        var sha256 = Convert.ToHexString(SHA256.HashData(png)).ToLowerInvariant();

        return new ScreenshotCaptureResult(
            new ScreenshotMetadata(
                capturedAt,
                Environment.MachineName,
                correlationId,
                target,
                unchecked((ulong)hwnd.ToInt64()),
                bounds.Width,
                bounds.Height,
                sha256),
            png);
    }

    private static Rectangle ResolveBounds(ScreenshotCaptureRequest request, out IntPtr hwnd)
    {
        hwnd = IntPtr.Zero;
        if (request.Target == ScreenshotTarget.Desktop)
        {
            return SystemInformation.VirtualScreen;
        }

        hwnd = request.Target == ScreenshotTarget.Hwnd && request.Hwnd != 0
            ? new IntPtr(unchecked((long)request.Hwnd))
            : GetForegroundWindow();
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var rect))
        {
            return SystemInformation.VirtualScreen;
        }

        var width = Math.Max(1, rect.Right - rect.Left);
        var height = Math.Max(1, rect.Bottom - rect.Top);
        return new Rectangle(rect.Left, rect.Top, width, height);
    }

    private static void DrawCursorIfInside(Graphics graphics, Rectangle bounds)
    {
        var cursorPosition = Cursor.Position;
        if (!bounds.Contains(cursorPosition))
        {
            return;
        }

        var cursorBounds = new Rectangle(cursorPosition.X - bounds.Left, cursorPosition.Y - bounds.Top, 32, 32);
        Cursors.Default.Draw(graphics, cursorBounds);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}

internal static class PngTextMetadata
{
    private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };

    public static byte[] AddTextChunks(byte[] png, IReadOnlyDictionary<string, string> textChunks)
    {
        if (png.Length < Signature.Length || !png.AsSpan(0, Signature.Length).SequenceEqual(Signature) || textChunks.Count == 0)
        {
            return png;
        }

        using var output = new MemoryStream(png.Length + textChunks.Count * 128);
        output.Write(png, 0, Signature.Length);
        var offset = Signature.Length;
        var inserted = false;
        while (offset < png.Length)
        {
            if (offset + 8 > png.Length)
            {
                return png;
            }

            var length = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset, 4));
            var type = Encoding.ASCII.GetString(png, offset + 4, 4);
            var chunkLength = checked((int)length + 12);
            if (offset + chunkLength > png.Length)
            {
                return png;
            }

            output.Write(png, offset, chunkLength);
            offset += chunkLength;

            if (!inserted && type == "IHDR")
            {
                foreach (var (keyword, value) in textChunks)
                {
                    WriteTextChunk(output, keyword, value);
                }
                inserted = true;
            }
        }

        return inserted ? output.ToArray() : png;
    }

    private static void WriteTextChunk(Stream output, string keyword, string value)
    {
        var keywordBytes = Encoding.ASCII.GetBytes(keyword);
        var valueBytes = Encoding.ASCII.GetBytes(value);
        var data = new byte[keywordBytes.Length + 1 + valueBytes.Length];
        Buffer.BlockCopy(keywordBytes, 0, data, 0, keywordBytes.Length);
        Buffer.BlockCopy(valueBytes, 0, data, keywordBytes.Length + 1, valueBytes.Length);

        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)data.Length);
        output.Write(length);
        var type = Encoding.ASCII.GetBytes("tEXt");
        output.Write(type);
        output.Write(data);

        Span<byte> crcInput = stackalloc byte[type.Length + data.Length];
        type.CopyTo(crcInput);
        data.CopyTo(crcInput[type.Length..]);
        Span<byte> crc = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32.Compute(crcInput));
        output.Write(crc);
    }
}

internal static class Crc32
{
    private const uint Polynomial = 0xEDB88320u;
    private static readonly uint[] Table = BuildTable();

    public static uint Compute(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        return crc ^ 0xFFFFFFFFu;
    }

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < table.Length; i++)
        {
            var value = i;
            for (var bit = 0; bit < 8; bit++)
            {
                value = (value & 1) != 0 ? Polynomial ^ (value >> 1) : value >> 1;
            }

            table[i] = value;
        }

        return table;
    }
}

public class Win32CursorClip : ICursorClip
{
    [DllImport("user32.dll")]
    private static extern bool ClipCursor(ref RECT lpRect);
    [DllImport("user32.dll")]
    private static extern bool ClipCursor(IntPtr lpRect);
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }

    private bool _clipped;

    public void ClipToPoint(int x, int y)
    {
        var r = new RECT { Left = x, Top = y, Right = x + 1, Bottom = y + 1 };
        ClipCursor(ref r);
        _clipped = true;
    }

    public void Release()
    {
        ClipCursor(IntPtr.Zero);
        _clipped = false;
    }

    public bool IsClipped => _clipped;
}

// Real hotkey using RegisterHotKey (shipped, no 'demo'/'sim')
public class Win32HotkeyMonitor : IHotkeyMonitor
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const uint VK_F1 = 0x70;
    private const uint VK_F2 = 0x71;
    private const uint VK_F3 = 0x72;
    private const uint VK_CONTROL = 0x11;
    private const uint VK_MENU = 0x12;
    private static readonly TimeSpan ToggleDebounceWindow = TimeSpan.FromMilliseconds(300);

    public event EventHandler<ToggleEventArgs>? ToggleRequested;

    /// <inheritdoc />
    public event EventHandler<ToggleEventArgs>? EmergencyReleaseRequested;

    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int idHook, KeyboardHookProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)] private static extern IntPtr GetModuleHandle(string? lpModuleName);

    private IntPtr _hwnd;
    private IntPtr _keyboardHook;
    private int _id = 1;
    private readonly KeyboardHookProc _keyboardProc;
    private long _lastToggleTimestamp;
    private long _lastEmergencyTimestamp;
    private readonly HotkeyConfig _config;

    public Win32HotkeyMonitor() : this(new HotkeyConfig())
    {
    }

    /// <summary>Creates the monitor with a specific hotkey configuration.</summary>
    /// <param name="config">The toggle/emergency-release hotkey bindings.</param>
    public Win32HotkeyMonitor(HotkeyConfig config)
    {
        _config = config ?? new HotkeyConfig();
        _keyboardProc = KeyboardHookCallback;
    }

    /// <summary>The active hotkey configuration.</summary>
    public HotkeyConfig Config => _config;

    public void StartMonitoring()
    {
        if (_keyboardHook != IntPtr.Zero)
        {
            return;
        }

        using var currentProcess = Process.GetCurrentProcess();
        using var currentModule = currentProcess.MainModule;
        var moduleHandle = currentModule?.ModuleName is { Length: > 0 } moduleName
            ? GetModuleHandle(moduleName)
            : IntPtr.Zero;
        _keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, moduleHandle, 0);
        if (_keyboardHook == IntPtr.Zero)
        {
            throw new InvalidOperationException($"SetWindowsHookEx failed for Ctrl-Alt-F1/Ctrl-Alt-F2 hotkey monitor win32={Marshal.GetLastWin32Error()}");
        }
    }

    public void StopMonitoring()
    {
        if (_hwnd != IntPtr.Zero) UnregisterHotKey(_hwnd, _id);
        _hwnd = IntPtr.Zero;
        if (_keyboardHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = IntPtr.Zero;
        }
    }

    public void RegisterForWindow(IntPtr hwnd, uint modifiers, uint vk)
    {
        _hwnd = hwnd;
        if (!RegisterHotKey(hwnd, _id, modifiers, vk))
        {
            throw new InvalidOperationException($"RegisterHotKey failed for modifiers=0x{modifiers:x} vk=0x{vk:x} win32={Marshal.GetLastWin32Error()}");
        }
    }

    // Called from real WM_HOTKEY handler or test to drive shipped state
    public void RaiseToggle(string chord, bool remote)
    {
        if (!ShouldDispatch(ref _lastToggleTimestamp))
        {
            return;
        }

        ToggleRequested?.Invoke(this, new ToggleEventArgs(chord, remote));
    }

    /// <summary>
    /// FR-MKP-001 / TR-MKP-RELI-001: raises the dedicated emergency-release hotkey (distinct from
    /// toggle), debounced independently from the toggle. Called from the WM_HOTKEY handler or tests.
    /// </summary>
    public void RaiseEmergencyRelease(string chord, bool remote)
    {
        if (!ShouldDispatch(ref _lastEmergencyTimestamp))
        {
            return;
        }

        EmergencyReleaseRequested?.Invoke(this, new ToggleEventArgs(chord, remote));
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && IsKeyDownMessage(wParam.ToInt32()))
        {
            var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);

            // Dedicated emergency-release hotkey takes precedence over toggle.
            if (MatchesChord(data.vkCode, _config.EmergencyReleaseVk, _config.EmergencyReleaseMods) || IsCtrlAltF3(data.vkCode))
            {
                RaiseEmergencyRelease("Ctrl-Alt-F3", false);
                return new IntPtr(1);
            }

            // Toggle: configured binding, plus the legacy Ctrl-Alt-F1/F2 chords for back-compat.
            if (MatchesChord(data.vkCode, _config.ToggleVk, _config.ToggleMods) || IsCtrlAltF1(data.vkCode) || IsCtrlAltF2(data.vkCode))
            {
                RaiseToggle(data.vkCode == VK_F2 ? "Ctrl-Alt-F2" : "Ctrl-Alt-F1", false);
                return new IntPtr(1);
            }
        }

        return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    private static bool ShouldDispatch(ref long lastTimestamp)
    {
        var now = Stopwatch.GetTimestamp();
        var previous = Interlocked.Read(ref lastTimestamp);
        if (previous != 0 && Stopwatch.GetElapsedTime(previous, now) < ToggleDebounceWindow)
        {
            return false;
        }

        Interlocked.Exchange(ref lastTimestamp, now);
        return true;
    }

    private static bool MatchesChord(uint vk, uint targetVk, uint mods)
    {
        if (vk != targetVk)
        {
            return false;
        }

        const uint modAlt = 0x0001, modControl = 0x0002, modShift = 0x0004, modWin = 0x0008;
        if ((mods & modControl) != 0 && !IsKeyDown(VK_CONTROL)) return false;
        if ((mods & modAlt) != 0 && !IsKeyDown(VK_MENU)) return false;
        if ((mods & modShift) != 0 && !IsKeyDown(0x10)) return false;   // VK_SHIFT
        if ((mods & modWin) != 0 && !IsKeyDown(0x5B) && !IsKeyDown(0x5C)) return false; // L/R Win
        return true;
    }

    private static bool IsKeyDownMessage(int message)
    {
        return message is WM_KEYDOWN or WM_SYSKEYDOWN;
    }

    private static bool IsCtrlAltF1(uint vk)
    {
        return vk == VK_F1 && IsKeyDown(VK_CONTROL) && IsKeyDown(VK_MENU);
    }

    private static bool IsCtrlAltF2(uint vk)
    {
        return vk == VK_F2 && IsKeyDown(VK_CONTROL) && IsKeyDown(VK_MENU);
    }

    private static bool IsCtrlAltF3(uint vk)
    {
        return vk == VK_F3 && IsKeyDown(VK_CONTROL) && IsKeyDown(VK_MENU);
    }

    private static bool IsKeyDown(uint vk)
    {
        return (GetAsyncKeyState((int)vk) & 0x8000) != 0;
    }

    private delegate IntPtr KeyboardHookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }
}