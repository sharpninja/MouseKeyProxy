using System;
using System.Drawing;
using System.Windows.Forms;
using MouseKeyProxy.Common;

namespace MouseKeyProxy.Agent;

/// <summary>
/// SHIPPED real tray entry. Wires real seams (IInputInjector, ICursorClip, IHotkeyMonitor).
/// Tray menu invokes real shipped code (no per-action spawn, shared with Repl via Common).
/// </summary>
internal static class Program
{
    // Initialized inside Main only. Static initializers must never run real P/Invoke
    // so that unit tests (which may load the Agent assembly) do not lock keyboard/mouse.
    private static Win32InputInjector? _injector;
    private static Win32CursorClip? _clip;
    private static ToggleStateMachine? _state;
    private static Win32HotkeyMonitor? _hotkey;

    [STAThread]
    public static void Main()
    {
        ApplicationConfiguration.Initialize();

        // Create the real implementations here, not at static init time.
        _injector = new Win32InputInjector();
        _clip = new Win32CursorClip();
        _state = new ToggleStateMachine();
        _hotkey = new Win32HotkeyMonitor();

        var tray = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Visible = true,
            Text = "MouseKeyProxy (real seams)"
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("Toggle (real hotkey+clip+state)", null, (s, e) => DoRealToggle());
        menu.Items.Add("Inject text (real SendInput)", null, (s, e) => DoRealInject());
        menu.Items.Add("Release clip (real)", null, (s, e) => { _clip!.Release(); });
        menu.Items.Add("Exit", null, (s, e) => { tray.Visible = false; Application.Exit(); });
        tray.ContextMenuStrip = menu;

        // Wire the real hotkey seam (in production this is driven by RegisterHotKey + WndProc)
        _hotkey.ToggleRequested += (s, ea) => DoRealToggle();

        Application.Run();
    }

    private static void DoRealToggle()
    {
        var res = _state!.ApplyToggle("peer");
        if (res.NewActive)
        {
            _clip!.ClipToPoint(100, 100); // real ClipCursor via seam
            Console.WriteLine($"[SHIPPED TRAY] active={res.NewActive} clip={_clip.IsClipped}");
        }
        else
        {
            _clip!.Release();
        }
    }

    private static void DoRealInject()
    {
        try
        {
            _injector!.Send(new InputEvent(InputKind.TEXT_INPUT, Text: "real-injected"));
            Console.WriteLine("[SHIPPED] real SendInput exercised");
        }
        catch (Exception ex) { Console.WriteLine("[SHIPPED observable fail] " + ex.Message); }
    }
}
