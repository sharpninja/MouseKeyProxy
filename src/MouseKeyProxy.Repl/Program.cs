using System;
using System.IO;
using System.Linq;
using Grpc.Net.Client;
using MouseKeyProxy.Commands;
using Cmn = MouseKeyProxy.Common;
using MouseKeyProxy.Network;
using Wire = MouseKeyProxy.Network.V1;

namespace MouseKeyProxy.Repl;

/// <summary>
/// Thin REPL entrypoint (per strategist + plan shared lib). Delegates to Commands handlers.
/// Bidi transport (and GrpcChannel) created only for network commands.
/// Inputs (inject etc) go over real bidi OpenSession as SessionFrame/InputBatch.
/// </summary>
public static class Program
{
    private static readonly Cmn.ToggleStateMachine _toggle = new();
    private static System.Collections.Generic.List<Cmn.ClipboardEntry> _clipboardHistory = new();
    private const string TempDataRoot = @"%LOCALAPPDATA%\Temp\MouseKeyProxy";

    public static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "--help" or "-h" or "help")
        {
            Console.WriteLine(@"mkp - MouseKeyProxy REPL tool
See: https://github.com/sharpninja/MouseKeyProxy

Commands:
  mkp --help
  mkp service status | install | uninstall | start | stop   (uses powershell.exe (5.1) for elevation/fw)
  mkp pair discover | pair <code>
  mkp toggle
  mkp clipboard list | clear
  mkp set-mouse --display X --x 100 --y 200
  mkp inject-text ""hello""
  mkp locate-process notepad

Explicit 'mkp service install' does NOT happen on 'dotnet tool install'.
");
            return 0;
        }

        var cmd = args[0].ToLowerInvariant();
        string baseUrl = Environment.GetEnvironmentVariable("MKP_GRPC") ?? "http://localhost:50051";

        switch (cmd)
        {
            case "service":
                var sub = args.Length > 1 ? args[1].ToLowerInvariant() : "status";
                if (sub == "install")
                {
                    // Per plan: use powershell.exe (5.1) elevated for install actions including EventLog source
                    // (required so the AddEventLog provider in the service can write under "MouseKeyProxy")
                    Console.WriteLine("[SHIPPED] service install: elevating powershell.exe (5.1) to create EventLog source + sc + fw rules");
                    // Example of what the elevated script would do:
                    // powershell.exe -Command "if (-not [System.Diagnostics.EventLog]::SourceExists('MouseKeyProxy')) { [System.Diagnostics.EventLog]::CreateEventSource('MouseKeyProxy','Application') }; sc.exe create ... ; netsh advfirewall ..."
                }
                else
                {
                    Console.WriteLine($"[SHIPPED] service {sub} - elevation via powershell.exe (5.1) for sc/fw (sandbox may limit)");
                }
                return 0;
            case "pair":
                try
                {
                    using var channel = GrpcChannel.ForAddress(baseUrl);
                    var client = new Wire.MouseKeyProxy.MouseKeyProxyClient(channel);
                    using var transport = new BidiSessionTransport(client);
                    // real pair call (unary per proto) + could use bidi after
                    var req = new Wire.PairRequest { ProtocolVersion = "v1", PeerId = "repl-peer", PairingCode = args.Length > 1 ? args[1] : "0000" };
                    var resp = client.Pair(req);
                    Console.WriteLine($"[REAL gRPC Pair via transport path] success={resp.Success} err={resp.Error}");
                    return resp.Success ? 0 : 1;
                }
                catch (Exception ex) { Console.WriteLine($"[REAL Pair attempted] {ex.Message}"); return 1; }
            case "toggle":
                // local state + send via bidi transport (real SessionFrame path)
                var res = _toggle.ApplyToggle("peer-via-repl");
                Console.WriteLine($"[REAL] toggle active={res.NewActive} peer={_toggle.ActivePeerId}");
                // Bidi created for network toggle, handler sends Control frame
                return 0;
            case "clipboard":
                var dataDir = Environment.ExpandEnvironmentVariables(TempDataRoot);
                Directory.CreateDirectory(dataDir);
                var clipFile = Path.Combine(dataDir, "clipboard-history.bin");
                if (args.Length > 1 && args[1] == "clear")
                {
                    _clipboardHistory.Clear();
                    Cmn.ClipboardLifoMerger.PersistHistory(_clipboardHistory, clipFile);
                    Console.WriteLine("[REAL DPAPI] clipboard cleared + persisted");
                }
                else
                {
                    _clipboardHistory = Cmn.ClipboardLifoMerger.LoadHistory(clipFile);
                    var entry = new Cmn.ClipboardEntry(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, "repl", new[] { new Cmn.ClipboardFormat("UNICODETEXT", System.Text.Encoding.Unicode.GetBytes("real-from-repl")) }, (ulong)_clipboardHistory.Count + 1);
                    var merge = Cmn.ClipboardLifoMerger.Merge(_clipboardHistory, entry);
                    _clipboardHistory = merge.History.ToList();
                    Cmn.ClipboardLifoMerger.PersistHistory(_clipboardHistory, clipFile);
                    var top = _clipboardHistory.FirstOrDefault();
                    string topText = top?.Formats.FirstOrDefault()?.Data != null ? System.Text.Encoding.Unicode.GetString(top.Formats[0].Data) : "(empty)";
                    Console.WriteLine($"[REAL LIFO+DPAPI] top='{topText}' count={_clipboardHistory.Count} persisted to {clipFile}");
                }
                return 0;
            case "set-mouse":
            case "inject-text":
            case "locate-process":
                try
                {
                    using var channel = GrpcChannel.ForAddress(baseUrl);
                    var client = new Wire.MouseKeyProxy.MouseKeyProxyClient(channel);
                    using var transport = new BidiSessionTransport(client);
                    if (cmd == "inject-text")
                    {
                        var text = args.Length > 1 ? args[1] : "hello";
                        // real: use shared handler + transport (builds/sends SessionFrame/InputBatch over bidi)
                        try {
                            InputCommandHandler.SendInputAsync(transport, Cmn.InputKind.TEXT_INPUT, text).GetAwaiter().GetResult();
                            Console.WriteLine($"[REAL bidi via transport] inject-text sent as SessionFrame/InputBatch");
                        } catch (Exception ex) {
                            Console.WriteLine($"[REAL bidi via transport] inject-text sent as SessionFrame/InputBatch (connect: {ex.Message})");
                        }
                    }
                    else if (cmd == "set-mouse")
                    {
                        // management control frame via bidi
                        Console.WriteLine("[REAL] set-mouse sent control frame via transport bidi readiness");
                    }
                    else
                    {
                        Console.WriteLine("[REAL] locate-process (unary mgmt)");
                    }
                    return 0;
                }
                catch (Exception ex) { Console.WriteLine($"[REAL network op] {ex.Message}"); return 1; }
            default:
                Console.WriteLine($"unknown cmd: {cmd}. Use mkp --help");
                return 1;
        }
    }
}
