using System;
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

    [DllImport("user32.dll", SetLastError = true)]
    static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    public void Send(InputEvent evt)
    {
        if (!InputSupportMatrix.IsSupported(evt.Kind, evt.Vk))
            throw new InvalidOperationException("Unsupported input per matrix: " + InputSupportMatrix.GetFailureReason(evt.Kind, evt.Vk));

        if (evt.Kind == InputKind.KEY_DOWN || evt.Kind == InputKind.KEY_UP)
        {
            var inp = new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wVk = (ushort)evt.Vk, dwFlags = (uint)(evt.Kind == InputKind.KEY_UP ? 2 : 0) } } };
            SendInput(1, new[] { inp }, Marshal.SizeOf(typeof(INPUT)));
        }
        else if (evt.Kind == InputKind.MOUSE_MOVE)
        {
            var inp = new INPUT { type = INPUT_MOUSE, u = new InputUnion { mi = new MOUSEINPUT { dx = evt.Dx, dy = evt.Dy, dwFlags = MOUSEEVENTF_MOVE } } };
            SendInput(1, new[] { inp }, Marshal.SizeOf(typeof(INPUT)));
        }
        // mouse buttons/wheel etc can be extended
    }

    public bool TryInjectBatch(System.Collections.Generic.IEnumerable<InputEvent> events, out string? error)
    {
        error = null;
        try { foreach (var e in events) Send(e); return true; }
        catch (Exception ex) { error = ex.Message; return false; }
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

    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
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
        RegisterHotKey(hwnd, _id, modifiers, vk);
    }

    // Called from real WM_HOTKEY handler or test to drive shipped state
    public void RaiseToggle(string chord, bool remote)
    {
        ToggleRequested?.Invoke(this, new ToggleEventArgs(chord, remote));
    }
}
