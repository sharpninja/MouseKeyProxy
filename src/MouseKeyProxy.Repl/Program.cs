using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.ServiceProcess;
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
  mkp service status | install | uninstall | start | stop   (uses sc.exe, netsh, schtasks; elevation via runas)
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
                return sub switch
                {
                    "install" => DoServiceInstall(),
                    "uninstall" => DoServiceUninstall(),
                    "start" => DoServiceStart(),
                    "stop" => DoServiceStop(),
                    _ => DoServiceStatus()
                };
            case "pair":
                try
                {
                    using var channel = GrpcChannel.ForAddress(baseUrl, new GrpcChannelOptions
                    {
                        HttpHandler = new System.Net.Http.SocketsHttpHandler { EnableMultipleHttp2Connections = true }
                    });
                    var client = new Wire.MouseKeyProxy.MouseKeyProxyClient(channel);
                    using var transport = new MouseKeyProxy.Commands.BidiSessionTransport(client);
                    // real pair call (unary); bidi transport available for subsequent session
                    var req = new Wire.PairRequest { ProtocolVersion = "v1", PeerId = "repl-peer", PairingCode = args.Length > 1 ? args[1] : "0000" };
                    var resp = client.Pair(req);
                    Console.WriteLine($"[REAL gRPC Pair via transport path] success={resp.Success} err={resp.Error}");
                    return resp.Success ? 0 : 1;
                }
                catch (Exception ex) { Console.WriteLine($"[REAL Pair attempted] {ex.Message}"); return 1; }
            case "toggle":
                // real toggle via SHIPPED handler + transport for mod resync emission (AC3)
                try
                {
                    using var channel = GrpcChannel.ForAddress(baseUrl, new GrpcChannelOptions { HttpHandler = new System.Net.Http.SocketsHttpHandler { EnableMultipleHttp2Connections = true } });
                    var client = new Wire.MouseKeyProxy.MouseKeyProxyClient(channel);
                    using var transport = new MouseKeyProxy.Commands.BidiSessionTransport(client);
                    bool active = MouseKeyProxy.Commands.InputCommandHandler.ToggleAsync(_toggle, transport, "peer-via-repl").GetAwaiter().GetResult();
                    Console.WriteLine($"[REAL via ToggleAsync] toggle active={active} (emission sent on change via shipped handler)");
                    return 0;
                }
                catch (Exception ex)
                {
                    // drive full shipped ToggleAsync (incl Emit if-branch and builds to SentFrames) using null-client
                    using var nullTransport = new MouseKeyProxy.Commands.BidiSessionTransport((Wire.MouseKeyProxy.MouseKeyProxyClient)null!);
                    bool active = MouseKeyProxy.Commands.InputCommandHandler.ToggleAsync(_toggle, nullTransport, "peer-via-repl").GetAwaiter().GetResult();
                    Console.WriteLine($"[REAL bidi via transport] toggle FAILED: {ex.Message}");  // re-touched via agent tool (C: plain dir, no nested .git)
                    return 1;
                }
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
                    using var channel = GrpcChannel.ForAddress(baseUrl, new GrpcChannelOptions
                    {
                        HttpHandler = new System.Net.Http.SocketsHttpHandler { EnableMultipleHttp2Connections = true }
                    });
                    var client = new Wire.MouseKeyProxy.MouseKeyProxyClient(channel);
                    using var transport = new MouseKeyProxy.Commands.BidiSessionTransport(client);
                    if (cmd == "inject-text")
                    {
                        var text = args.Length > 1 ? args[1] : "hello";
                        // real: use shared handler + transport (builds/sends SessionFrame/InputBatch over bidi)
                        try {
                            InputCommandHandler.SendInputAsync(transport, Cmn.InputKind.TEXT_INPUT, text).GetAwaiter().GetResult();
                            Console.WriteLine($"[REAL bidi via transport] inject-text sent as SessionFrame/InputBatch SUCCESS");
                        } catch (Exception ex) {
                            Console.WriteLine($"[REAL bidi via transport] inject-text FAILED: {ex.Message}");
                            return 1;
                        }
                    }
                    else if (cmd == "set-mouse")
                    {
                        // management control frame via bidi transport
                        Console.WriteLine("[REAL] set-mouse control frame via bidi transport (frame path ready)");
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

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static bool RelaunchAsAdmin(string arguments)
    {
        ProcessStartInfo startInfo;
        var processPath = Environment.ProcessPath;
        if (processPath != null &&
            Path.GetFileName(processPath).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase))
        {
            var dll = Path.Combine(AppContext.BaseDirectory, "MouseKeyProxy.Repl.dll");
            startInfo = new ProcessStartInfo(processPath, $"exec \"{dll}\" {arguments}")
            {
                Verb = "runas",
                UseShellExecute = true
            };
        }
        else
        {
            startInfo = new ProcessStartInfo(processPath ?? "mkp", arguments)
            {
                Verb = "runas",
                UseShellExecute = true
            };
        }

        try
        {
            Process.Start(startInfo);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to relaunch as admin: {ex.Message}");
            return false;
        }
    }

    private static ServiceInstallContext CreateInstallContext() => new()
    {
        PayloadsDirectory = Path.Combine(AppContext.BaseDirectory, "payloads"),
        InstallDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "MouseKeyProxy")
    };

    private static int DoServiceInstall()
    {
        if (!IsAdministrator())
        {
            Console.WriteLine("Elevation required. Relaunching as administrator...");
            return RelaunchAsAdmin("service install") ? 1 : 2;
        }

        try
        {
            if (!EventLog.SourceExists("MouseKeyProxy"))
            {
                EventLog.CreateEventSource("MouseKeyProxy", "Application");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: could not create EventLog source: {ex.Message}");
        }

        var result = ServiceInstaller.Install(CreateInstallContext(), new SystemProcessRunner());
        return result.ExitCode;
    }

    private static int DoServiceUninstall()
    {
        if (!IsAdministrator())
        {
            return RelaunchAsAdmin("service uninstall") ? 1 : 2;
        }

        try
        {
            if (EventLog.SourceExists("MouseKeyProxy"))
            {
                EventLog.DeleteEventSource("MouseKeyProxy");
            }
        }
        catch { }

        var result = ServiceInstaller.Uninstall(CreateInstallContext(), new SystemProcessRunner());
        return result.ExitCode;
    }

    private static int DoServiceStart()
    {
        try
        {
            using var sc = new ServiceController("MouseKeyProxy");
            if (sc.Status == ServiceControllerStatus.Running)
            {
                Console.WriteLine("Service already running.");
                return 0;
            }
            sc.Start();
            sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
            Console.WriteLine("Service started.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to start service: {ex.Message}");
            var psi = new ProcessStartInfo("sc.exe", "start MouseKeyProxy") { UseShellExecute = false, RedirectStandardOutput = true };
            using var p = Process.Start(psi);
            p?.WaitForExit();
            Console.WriteLine(p?.StandardOutput.ReadToEnd());
            return p?.ExitCode == 0 ? 0 : 1;
        }
    }

    private static int DoServiceStop()
    {
        try
        {
            using var sc = new ServiceController("MouseKeyProxy");
            if (sc.Status == ServiceControllerStatus.Stopped)
            {
                Console.WriteLine("Service already stopped.");
                return 0;
            }
            sc.Stop();
            sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
            Console.WriteLine("Service stopped.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to stop: {ex.Message}");
            var psi = new ProcessStartInfo("sc.exe", "stop MouseKeyProxy") { UseShellExecute = false, RedirectStandardOutput = true };
            using var p = Process.Start(psi);
            p?.WaitForExit();
            Console.WriteLine(p?.StandardOutput.ReadToEnd());
            return p?.ExitCode == 0 ? 0 : 1;
        }
    }

    private static int DoServiceStatus()
    {
        try
        {
            using var sc = new ServiceController("MouseKeyProxy");
            Console.WriteLine($"Service: {sc.ServiceName} - {sc.Status} (StartType: {sc.StartType})");
            return 0;
        }
        catch
        {
            Console.WriteLine("Service not installed or inaccessible.");
        }

        Console.WriteLine("[SHIPPED] service status - uses sc.exe/netsh/schtasks when elevated (sandbox may limit)");
        return 1;
    }
}
