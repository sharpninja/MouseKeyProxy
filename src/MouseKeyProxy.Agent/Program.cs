using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Grpc.Net.Client;
using MouseKeyProxy.Common;
using MouseKeyProxy.Commands;
using MouseKeyProxy.Network;
using Wire = MouseKeyProxy.Network.V1;

namespace MouseKeyProxy.Agent;

/// <summary>
/// User-session tray agent. Menu items match docs/wireframes/01-tray-icon-menu.svg.
/// Uses custom logo from assets/logo.png.
/// </summary>
internal static class Program
{
    private static Win32InputInjector? _injector;
    private static Win32CursorClip? _clip;
    private static ToggleStateMachine? _state;
    private static Win32HotkeyMonitor? _hotkey;

    [STAThread]
    public static void Main()
    {
        ApplicationConfiguration.Initialize();

        _injector = new Win32InputInjector();
        _clip = new Win32CursorClip();
        _state = new ToggleStateMachine();
        _hotkey = new Win32HotkeyMonitor();

        var tray = new NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Visible = true,
            Text = "MouseKeyProxy"
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("Toggle Active (Ctrl-Alt-F1)", null, (_, _) => DoRealToggle());
        menu.Items.Add("Start Mirror Mode", null, (_, _) => ShowMirrorForm());
        menu.Items.Add("Inject Text to Remote...", null, (_, _) => ShowInjectForm());
        menu.Items.Add("Start/Stop Service", null, (_, _) => Console.WriteLine("[TRAY] service toggle via shared REPL lib"));
        menu.Items.Add("Pair/Discover (REPL)", null, (_, _) => Console.WriteLine("[TRAY] pair via shared REPL lib"));
        menu.Items.Add("Settings", null, (_, _) => ShowStatusForm());
        menu.Items.Add("Exit", null, (_, _) => { tray.Visible = false; Application.Exit(); });
        tray.ContextMenuStrip = menu;

        _hotkey.ToggleRequested += (_, _) => DoRealToggle();
        _hotkey.StartMonitoring();

        var hiddenForm = new Form { Visible = false, ShowInTaskbar = false };
        hiddenForm.Load += (_, _) => _hotkey.RegisterForWindow(hiddenForm.Handle, 0x0003, (uint)Keys.F1);
        hiddenForm.Show();

        Application.Run();
    }

    private static Icon LoadTrayIcon()
    {
        var logoPath = Path.Combine(AppContext.BaseDirectory, "logo.png");
        if (!File.Exists(logoPath))
        {
            logoPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "assets", "logo.png"));
        }
        if (!File.Exists(logoPath))
        {
            logoPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "assets", "logo.png"));
        }
        if (File.Exists(logoPath))
        {
            using var bmp = new Bitmap(logoPath);
            return Icon.FromHandle(bmp.GetHicon());
        }
        var fallback = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(fallback))
        {
            g.Clear(Color.FromArgb(74, 144, 217));
        }
        return Icon.FromHandle(fallback.GetHicon());
    }

    private static void ShowInjectForm()
    {
        using var form = new Form { Text = "Inject to Remote", Width = 360, Height = 240, StartPosition = FormStartPosition.CenterScreen };
        form.Controls.Add(new Label { Text = "Remote:", Left = 16, Top = 16, AutoSize = true });
        form.Controls.Add(new ComboBox { Left = 80, Top = 12, Width = 240, Items = { "peer-via-repl" } });
        form.Controls.Add(new Label { Text = "Text:", Left = 16, Top = 48, AutoSize = true });
        var text = new TextBox { Left = 16, Top = 72, Width = 320, Height = 80, Multiline = true };
        form.Controls.Add(text);
        var send = new Button { Text = "Send", Left = 200, Top = 168, Width = 64 };
        send.Click += (_, _) => { DoRealInject(text.Text); form.Close(); };
        form.Controls.Add(send);
        form.Controls.Add(new Button { Text = "Cancel", Left = 272, Top = 168, Width = 64, DialogResult = DialogResult.Cancel });
        form.ShowDialog();
    }

    private static void ShowMirrorForm()
    {
        using var form = new Form { Text = "Mirror Mode", Width = 320, Height = 200, StartPosition = FormStartPosition.CenterScreen };
        form.Controls.Add(new Label { Text = "Active", Left = 16, Top = 16, AutoSize = true });
        form.Controls.Add(new CheckBox { Text = "Remote A", Left = 16, Top = 48, Checked = true });
        form.Controls.Add(new Button { Text = "Stop", Left = 200, Top = 120, Width = 80 });
        form.ShowDialog();
    }

    private static void ShowStatusForm()
    {
        using var form = new Form { Text = "Status", Width = 300, Height = 160, StartPosition = FormStartPosition.CenterScreen };
        form.Controls.Add(new Label { Text = "Role: Host", Left = 16, Top = 16, AutoSize = true });
        form.Controls.Add(new Label { Text = "Connected: (pair via REPL)", Left = 16, Top = 40, AutoSize = true });
        form.Controls.Add(new Label { Text = "Last clip: (none)", Left = 16, Top = 64, AutoSize = true });
        form.ShowDialog();
    }

    private static void ApplyClipForActive(bool active)
    {
        if (active)
        {
            _clip!.ClipToPoint(100, 100);
        }
        else
        {
            _clip!.Release();
        }
    }

    private static void DoRealToggle()
    {
        string baseUrl = Environment.GetEnvironmentVariable("MKP_GRPC") ?? "http://localhost:50051";
        try
        {
            using var channel = GrpcChannel.ForAddress(baseUrl, new GrpcChannelOptions
            {
                HttpHandler = new System.Net.Http.SocketsHttpHandler { EnableMultipleHttp2Connections = true }
            });
            var client = new Wire.MouseKeyProxy.MouseKeyProxyClient(channel);
            using var transport = new BidiSessionTransport(client);
            bool active = InputCommandHandler.ToggleAsync(_state!, transport, "peer").GetAwaiter().GetResult();
            ApplyClipForActive(active);
        }
        catch (Exception ex)
        {
            using var nullTransport = new BidiSessionTransport((Wire.MouseKeyProxy.MouseKeyProxyClient)null!);
            bool active = InputCommandHandler.ToggleAsync(_state!, nullTransport, "peer").GetAwaiter().GetResult();
            ApplyClipForActive(active);
            Console.WriteLine($"toggle FAILED: {ex.Message}");
        }
    }

    private static void DoRealInject(string? text = null)
    {
        string payload = text ?? "real-injected";
        string baseUrl = Environment.GetEnvironmentVariable("MKP_GRPC") ?? "http://localhost:50051";
        try
        {
            using var channel = GrpcChannel.ForAddress(baseUrl, new GrpcChannelOptions
            {
                HttpHandler = new System.Net.Http.SocketsHttpHandler { EnableMultipleHttp2Connections = true }
            });
            var client = new Wire.MouseKeyProxy.MouseKeyProxyClient(channel);
            using var transport = new BidiSessionTransport(client);
            InputCommandHandler.SendInputAsync(transport, InputKind.TEXT_INPUT, payload).GetAwaiter().GetResult();
            Console.WriteLine("[REAL bidi via transport] inject-text sent as SessionFrame/InputBatch SUCCESS");
        }
        catch (Exception ex)
        {
            try
            {
                _injector!.Send(new InputEvent(InputKind.TEXT_INPUT, Text: payload));
                Console.WriteLine($"[LOCAL fallback inject] after remote FAILED: {ex.Message}");
            }
            catch (Exception localEx)
            {
                Console.WriteLine($"[SHIPPED observable fail] inject FAILED: {ex.Message}; local: {localEx.Message}");
            }
        }
    }
}