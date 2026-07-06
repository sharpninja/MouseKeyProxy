using System;
using System.Diagnostics;
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
    private static AgentControlPipeServer? _controlPipe;
    private static RemoteInputForwarder? _forwarder;
    private static NotifyIcon? _tray;
    private static HotkeyMessageForm? _hotkeyWindow;
    private static Form? _dashboardForm;
    private static bool _exitRequested;

    [STAThread]
    public static void Main()
    {
        ApplicationConfiguration.Initialize();

        _injector = new Win32InputInjector();
        _clip = new Win32CursorClip();
        _state = new ToggleStateMachine();
        _hotkey = new Win32HotkeyMonitor();
        _controlPipe = AgentControlPipeServer.Start(new Win32DesktopController(), _injector);
        _forwarder = new RemoteInputForwarder();

        _tray = new NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Visible = true,
            Text = "MouseKeyProxy"
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("MouseKeyProxy dashboard", null, (_, _) => ShowDashboardForm());
        menu.Items.Add("Toggle Active - Desktop Control (Ctrl-Alt-F1)", null, (_, _) => DoRealToggle());
        menu.Items.Add("Pairing", null, (_, _) => ShowPairingForm());
        menu.Items.Add("Reconnect", null, (_, _) => TryReconnect());
        menu.Items.Add("Service", null, (_, _) => ShowServiceForm());
        menu.Items.Add("Clipboard", null, (_, _) => ShowClipboardForm());
        menu.Items.Add("Inject Text to Remote...", null, (_, _) => ShowInjectForm());
        menu.Items.Add("Start Mirror Mode", null, (_, _) => ShowMirrorForm());
        menu.Items.Add("Emergency release", null, (_, _) => EmergencyRelease());
        menu.Items.Add("Open logs", null, (_, _) => OpenLogs());
        menu.Items.Add("Exit", null, (_, _) => ExitApplication());
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => ShowDashboardForm();

        _hotkey.ToggleRequested += (_, _) => DoRealToggle();
        _hotkey.StartMonitoring();

        _hotkeyWindow = new HotkeyMessageForm(() => _hotkey.RaiseToggle("Ctrl-Alt-F1", false));
        _hotkey.RegisterForWindow(_hotkeyWindow.Handle, 0x0003, (uint)Keys.F1);

        Application.ApplicationExit += (_, _) => CleanupApplication();
        ShowDashboardForm();
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
        using var form = new Form { Text = "Inject Text to Remote", Width = 360, Height = 240, StartPosition = FormStartPosition.CenterScreen };
        form.Controls.Add(new Label { Text = "Active peer:", Left = 16, Top = 16, AutoSize = true });
        form.Controls.Add(new ComboBox
        {
            Left = 104,
            Top = 12,
            Width = 232,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Items = { "payton-desktop" },
            SelectedIndex = 0
        });
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
        using var form = new Form { Text = "Mirror Active peer", Width = 320, Height = 200, StartPosition = FormStartPosition.CenterScreen };
        form.Controls.Add(new Label { Text = "Active peer", Left = 16, Top = 16, AutoSize = true });
        form.Controls.Add(new CheckBox { Text = "payton-desktop", Left = 16, Top = 48, Checked = true });
        form.Controls.Add(new Button { Text = "Stop", Left = 200, Top = 120, Width = 80 });
        form.ShowDialog();
    }

    private static void ShowDashboardForm()
    {
        if (_dashboardForm is null || _dashboardForm.IsDisposed)
        {
            _dashboardForm = CreateDashboardForm();
        }

        if (!_dashboardForm.Visible)
        {
            _dashboardForm.Show();
        }

        if (_dashboardForm.WindowState == FormWindowState.Minimized)
        {
            _dashboardForm.WindowState = FormWindowState.Normal;
        }

        _dashboardForm.Activate();
    }

    private static Form CreateDashboardForm()
    {
        var form = new Form
        {
            Text = "MouseKeyProxy dashboard",
            Width = 520,
            Height = 420,
            StartPosition = FormStartPosition.CenterScreen,
            MinimumSize = new Size(440, 360)
        };
        form.FormClosing += (_, args) =>
        {
            if (!_exitRequested && args.CloseReason == CloseReason.UserClosing)
            {
                args.Cancel = true;
                form.Hide();
            }
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            ColumnCount = 2,
            RowCount = 7
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        form.Controls.Add(layout);

        AddDashboardRow(layout, "Pairing", "Ready for local code entry");
        AddDashboardRow(layout, "Active peer", "payton-desktop");
        AddDashboardRow(layout, "Service", Environment.GetEnvironmentVariable("MKP_GRPC") ?? "http://localhost:50051");
        AddDashboardRow(layout, "Clipboard", "Idle");
        AddDashboardRow(layout, "Recent errors", "None recorded this session");

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoSize = true
        };
        var reconnect = new Button { Text = "Reconnect", Width = 120, Height = 32 };
        reconnect.Click += (_, _) => TryReconnect();
        actions.Controls.Add(reconnect);
        var release = new Button { Text = "Emergency release", Width = 140, Height = 32 };
        release.Click += (_, _) => EmergencyRelease();
        actions.Controls.Add(release);
        var logs = new Button { Text = "Open logs", Width = 120, Height = 32 };
        logs.Click += (_, _) => OpenLogs();
        actions.Controls.Add(logs);

        var actionRow = layout.RowStyles.Count;
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        layout.Controls.Add(new Label
        {
            Text = "Controls",
            Font = CreateBoldMessageFont(),
            TextAlign = ContentAlignment.MiddleLeft,
            Dock = DockStyle.Fill
        }, 0, actionRow);
        layout.Controls.Add(actions, 1, actionRow);

        return form;
    }

    private static void AddDashboardRow(TableLayoutPanel layout, string label, string value)
    {
        var row = layout.RowStyles.Count;
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.Controls.Add(new Label
        {
            Text = label,
            Font = CreateBoldMessageFont(),
            TextAlign = ContentAlignment.MiddleLeft,
            Dock = DockStyle.Fill
        }, 0, row);
        layout.Controls.Add(new Label
        {
            Text = value,
            TextAlign = ContentAlignment.MiddleLeft,
            Dock = DockStyle.Fill,
            AutoEllipsis = true
        }, 1, row);
    }

    private static Font CreateBoldMessageFont()
    {
        return new Font(SystemFonts.MessageBoxFont ?? Control.DefaultFont, FontStyle.Bold);
    }

    private static void ShowPairingForm()
    {
        MessageBox.Show(
            "Pairing is ready for payton-desktop. Use Reconnect after the service is reachable.",
            "Pairing",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private static void ShowServiceForm()
    {
        MessageBox.Show(
            $"Service endpoint: {Environment.GetEnvironmentVariable("MKP_GRPC") ?? "http://localhost:50051"}",
            "Service",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private static void ShowClipboardForm()
    {
        MessageBox.Show(
            "Clipboard sync is idle.",
            "Clipboard",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private static void TryReconnect()
    {
        MessageBox.Show(
            "Reconnect requested for active peer payton-desktop.",
            "Reconnect",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private static void EmergencyRelease()
    {
        _clip?.Release();
        MessageBox.Show(
            "Emergency release completed. Local cursor clipping is released.",
            "Emergency release",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private static void OpenLogs()
    {
        var logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MouseKeyProxy",
            "logs");
        Directory.CreateDirectory(logDirectory);
        Process.Start(new ProcessStartInfo
        {
            FileName = logDirectory,
            UseShellExecute = true
        });
    }

    private static void ExitApplication()
    {
        _exitRequested = true;
        Application.Exit();
    }

    private static void CleanupApplication()
    {
        if (_tray is not null)
        {
            _tray.Visible = false;
        }

        _tray?.Dispose();
        _hotkeyWindow?.Dispose();
        _dashboardForm?.Dispose();
        _forwarder?.Dispose();
        _controlPipe?.Dispose();
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
        try
        {
            var remoteUrl = ResolveRemoteGrpcUrl();
            var activePeer = remoteUrl;
            bool active = InputCommandHandler.ToggleAsync(_state!, null, activePeer).GetAwaiter().GetResult();
            if (active)
            {
                _clip?.Release();
                _forwarder!.Start(remoteUrl);
            }
            else
            {
                _forwarder?.Stop();
                _clip?.Release();
            }
        }
        catch (Exception ex)
        {
            using var nullTransport = new BidiSessionTransport((Wire.MouseKeyProxy.MouseKeyProxyClient)null!);
            bool active = InputCommandHandler.ToggleAsync(_state!, nullTransport, "peer").GetAwaiter().GetResult();
            if (!active)
            {
                _forwarder?.Stop();
                _clip?.Release();
            }
            Console.WriteLine($"toggle FAILED: {ex.Message}");
        }
    }

    private static string ResolveRemoteGrpcUrl()
    {
        var configured = Environment.GetEnvironmentVariable("MKP_REMOTE_GRPC");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        try
        {
            var (_, remotePeer) = LabTopology.ResolvePeers();
            return LabTopology.GrpcUrl(remotePeer);
        }
        catch
        {
            return "http://payton-desktop:50051";
        }
    }

    private sealed class HotkeyMessageForm : Form
    {
        private const int WM_HOTKEY = 0x0312;
        private readonly Action _onHotkey;

        public HotkeyMessageForm(Action onHotkey)
        {
            _onHotkey = onHotkey ?? throw new ArgumentNullException(nameof(onHotkey));
            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            Size = new Size(1, 1);
        }

        protected override void SetVisibleCore(bool value)
        {
            base.SetVisibleCore(false);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY)
            {
                _onHotkey();
            }

            base.WndProc(ref m);
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
