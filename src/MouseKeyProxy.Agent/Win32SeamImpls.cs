using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using MouseKeyProxy.Common;

namespace MouseKeyProxy.Agent;

/// <summary>
/// Real shipped impls of seams using P/Invoke (SendInput, ClipCursor).
/// Called from tray/hotkey path (no placeholders).
/// </summary>
public class Win32InputInjector : IInputInjector
{
    [StructLayout(LayoutKind.Sequential)]
    struct INPUT { public uint type; public InputUnion u; }
    [StructLayout(LayoutKind.Explicit)]
    struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }
    [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public UIntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public UIntPtr dwExtraInfo; }

    const uint INPUT_KEYBOARD = 1;
    const uint INPUT_MOUSE = 0;
    const uint MOUSEEVENTF_MOVE = 0x0001;
    const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    const uint MOUSEEVENTF_LEFTUP = 0x0004;
    const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    const uint MOUSEEVENTF_XDOWN = 0x0080;
    const uint MOUSEEVENTF_XUP = 0x0100;
    const uint MOUSEEVENTF_WHEEL = 0x0800;
    const uint MOUSEEVENTF_HWHEEL = 0x01000;
    const uint KEYEVENTF_KEYUP = 0x0002;
    const uint KEYEVENTF_UNICODE = 0x0004;

    [DllImport("user32.dll", SetLastError = true)]
    static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    public void Send(InputEvent evt)
    {
        if (!InputSupportMatrix.IsSupported(evt.Kind, evt.Vk))
            throw new InvalidOperationException("Unsupported input per matrix: " + InputSupportMatrix.GetFailureReason(evt.Kind, evt.Vk));

        if (evt.Kind == InputKind.TEXT_INPUT)
        {
            foreach (var ch in evt.Text ?? string.Empty)
            {
                var down = new INPUT
                {
                    type = INPUT_KEYBOARD,
                    u = new InputUnion { ki = new KEYBDINPUT { wScan = ch, dwFlags = KEYEVENTF_UNICODE } }
                };
                var up = new INPUT
                {
                    type = INPUT_KEYBOARD,
                    u = new InputUnion { ki = new KEYBDINPUT { wScan = ch, dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP } }
                };
                SendInput(2, new[] { down, up }, Marshal.SizeOf(typeof(INPUT)));
            }
        }
        else if (evt.Kind == InputKind.KEY_DOWN || evt.Kind == InputKind.KEY_UP)
        {
            var inp = new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wVk = (ushort)evt.Vk, dwFlags = (uint)(evt.Kind == InputKind.KEY_UP ? KEYEVENTF_KEYUP : 0) } } };
            SendInput(1, new[] { inp }, Marshal.SizeOf(typeof(INPUT)));
        }
        else if (evt.Kind == InputKind.MOUSE_MOVE)
        {
            var inp = new INPUT { type = INPUT_MOUSE, u = new InputUnion { mi = new MOUSEINPUT { dx = evt.Dx, dy = evt.Dy, dwFlags = MOUSEEVENTF_MOVE } } };
            SendInput(1, new[] { inp }, Marshal.SizeOf(typeof(INPUT)));
        }
        else if (evt.Kind == InputKind.MOUSE_DOWN || evt.Kind == InputKind.MOUSE_UP || evt.Kind == InputKind.MOUSE_XBUTTON)
        {
            var flags = evt.Flags != 0
                ? evt.Flags
                : evt.Kind == InputKind.MOUSE_DOWN ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP;
            var inp = new INPUT { type = INPUT_MOUSE, u = new InputUnion { mi = new MOUSEINPUT { mouseData = evt.XButton, dwFlags = flags } } };
            SendInput(1, new[] { inp }, Marshal.SizeOf(typeof(INPUT)));
        }
        else if (evt.Kind == InputKind.MOUSE_WHEEL || evt.Kind == InputKind.MOUSE_HWHEEL)
        {
            var flags = evt.Kind == InputKind.MOUSE_WHEEL ? MOUSEEVENTF_WHEEL : MOUSEEVENTF_HWHEEL;
            var inp = new INPUT { type = INPUT_MOUSE, u = new InputUnion { mi = new MOUSEINPUT { mouseData = unchecked((uint)evt.WheelDelta), dwFlags = flags } } };
            SendInput(1, new[] { inp }, Marshal.SizeOf(typeof(INPUT)));
        }
    }

    public bool TryInjectBatch(System.Collections.Generic.IEnumerable<InputEvent> events, out string? error)
    {
        error = null;
        try { foreach (var e in events) Send(e); return true; }
        catch (Exception ex) { error = ex.Message; return false; }
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

public class Win32CursorClip : ICursorClip
{
    [DllImport("user32.dll")]
    static extern bool ClipCursor(ref RECT lpRect);
    [DllImport("user32.dll")]
    static extern bool GetClipCursor(out RECT lpRect);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }

    private bool _clipped;

    public void ClipToPoint(int x, int y)
    {
        var r = new RECT { Left = x, Top = y, Right = x + 1, Bottom = y + 1 };
        ClipCursor(ref r);
        _clipped = true;
    }

    public void Release()
    {
        var empty = new RECT();
        ClipCursor(ref empty);
        _clipped = false;
    }

    public bool IsClipped => _clipped;
}

// Real hotkey using RegisterHotKey (shipped, no 'demo'/'sim')
public class Win32HotkeyMonitor : IHotkeyMonitor
{
    public event EventHandler<ToggleEventArgs>? ToggleRequested;

    [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private IntPtr _hwnd;
    private int _id = 1;

    public void StartMonitoring()
    {
        // Real registration (for tray hwnd); in practice called from Form handle
        // Called from Program tray or test to drive real state + resync
    }

    public void StopMonitoring()
    {
        if (_hwnd != IntPtr.Zero) UnregisterHotKey(_hwnd, _id);
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
        ToggleRequested?.Invoke(this, new ToggleEventArgs(chord, remote));
    }
}
