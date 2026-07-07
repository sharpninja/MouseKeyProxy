using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms;

namespace MouseKeyProxy.WindowProbe;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var statePath = GetOption(args, "--state", Path.Combine(Path.GetTempPath(), "mousekeyproxy-windowprobe-state.json"));
        var readyPath = GetOption(args, "--ready", string.Empty);
        Application.Run(new ProbeForm(statePath, readyPath));
    }

    private static string GetOption(string[] args, string name, string fallback)
    {
        var index = Array.FindIndex(args, arg => arg.Equals(name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
    }
}

internal sealed class ProbeForm : Form
{
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const int WM_SYSCOMMAND = 0x0112;
    private const int WM_MOVE = 0x0003;
    private const int WM_SIZE = 0x0005;
    private const int WM_MOVING = 0x0216;
    private const int WM_SIZING = 0x0214;
    private const int WM_WINDOWPOSCHANGED = 0x0047;
    private const int SC_KEYMENU = 0xF100;
    private readonly string _statePath;
    private readonly string _readyPath;
    private readonly List<ProbeMessage> _messages = new();
    private int _sequence;

    public ProbeForm(string statePath, string readyPath)
    {
        _statePath = statePath;
        _readyPath = readyPath;
        Text = "MouseKeyProxy WindowProbe";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(640, 420);
        KeyPreview = true;
        Shown += (_, _) =>
        {
            WriteState("shown", IntPtr.Zero, IntPtr.Zero);
            if (!string.IsNullOrWhiteSpace(_readyPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_readyPath)) ?? Directory.GetCurrentDirectory());
                File.WriteAllText(_readyPath, Handle.ToInt64().ToString("x"));
            }
        };
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);

        if (ShouldRecord(m.Msg))
        {
            WriteState(MessageName(m.Msg), m.WParam, m.LParam);
        }
    }

    private bool ShouldRecord(int msg)
    {
        return msg is WM_KEYDOWN or WM_KEYUP or WM_SYSKEYDOWN or WM_SYSKEYUP or WM_SYSCOMMAND or WM_MOVE or WM_SIZE or WM_MOVING or WM_SIZING or WM_WINDOWPOSCHANGED;
    }

    private void WriteState(string messageName, IntPtr wParam, IntPtr lParam)
    {
        var now = DateTimeOffset.UtcNow;
        var bounds = Bounds;
        var monitor = Screen.FromHandle(Handle);
        var message = new ProbeMessage(
            ++_sequence,
            now,
            messageName,
            wParam.ToInt64(),
            lParam.ToInt64(),
            messageName == "WM_SYSCOMMAND" && ((int)wParam & 0xFFF0) == SC_KEYMENU);
        _messages.Add(message);
        if (_messages.Count > 200)
        {
            _messages.RemoveAt(0);
        }

        var state = new ProbeState(
            now,
            Environment.MachineName,
            Process.GetCurrentProcess().Id,
            unchecked((ulong)Handle.ToInt64()),
            Text,
            new ProbeBounds(bounds.Left, bounds.Top, bounds.Width, bounds.Height),
            monitor.DeviceName,
            WindowState.ToString(),
            message,
            _messages.ToArray());

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_statePath)) ?? Directory.GetCurrentDirectory());
        File.WriteAllText(_statePath, JsonSerializer.Serialize(state, ProbeJson.Default.Options));
    }

    private static string MessageName(int msg)
    {
        return msg switch
        {
            WM_KEYDOWN => "WM_KEYDOWN",
            WM_KEYUP => "WM_KEYUP",
            WM_SYSKEYDOWN => "WM_SYSKEYDOWN",
            WM_SYSKEYUP => "WM_SYSKEYUP",
            WM_SYSCOMMAND => "WM_SYSCOMMAND",
            WM_MOVE => "WM_MOVE",
            WM_SIZE => "WM_SIZE",
            WM_MOVING => "WM_MOVING",
            WM_SIZING => "WM_SIZING",
            WM_WINDOWPOSCHANGED => "WM_WINDOWPOSCHANGED",
            _ => $"0x{msg:x}"
        };
    }
}

internal sealed record ProbeState(
    DateTimeOffset CapturedAtUtc,
    string SourceHost,
    int ProcessId,
    ulong Hwnd,
    string Title,
    ProbeBounds Bounds,
    string Monitor,
    string WindowState,
    ProbeMessage LastMessage,
    IReadOnlyList<ProbeMessage> Messages);

internal sealed record ProbeBounds(int Left, int Top, int Width, int Height);

internal sealed record ProbeMessage(
    int Sequence,
    DateTimeOffset CapturedAtUtc,
    string Name,
    long WParam,
    long LParam,
    bool IsSystemKeyMenu);

[JsonSerializable(typeof(ProbeState))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class ProbeJson : JsonSerializerContext;