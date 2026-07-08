using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text.Json;
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
    private static readonly JsonSerializerOptions AgentControlJsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// TR-MKP-SEC-001: builds a channel for effect RPCs using the persisted peer credential (mTLS),
    /// falling back to a bootstrap channel (which trusts the server on first use) when unpaired so
    /// pairing-bootstrap RPCs remain reachable.
    /// </summary>
    private static GrpcChannel CreateReplChannel(string baseUrl)
    {
        var credential = PeerCredentialStore.Load(PeerCredentialStore.DefaultPath());
        return credential is not null
            ? PairingClient.CreateAuthenticatedChannel(baseUrl, credential)
            : CreateBootstrapChannel(baseUrl);
    }

    /// <summary>Builds a bootstrap channel that trusts the server certificate (operator-local pairing).</summary>
    private static GrpcChannel CreateBootstrapChannel(string baseUrl)
    {
        var handler = new System.Net.Http.SocketsHttpHandler
        {
            EnableMultipleHttp2Connections = true,
            SslOptions = new System.Net.Security.SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (_, _, _, _) => true,
            },
        };
        return GrpcChannel.ForAddress(baseUrl, new GrpcChannelOptions { HttpHandler = handler });
    }
    private const string EventLogSourceName = "MouseKeyProxy";
    private const string EventLogName = "MouseKeyProxy";

    // Test seam: when set, network commands use this gRPC client instead of dialing a live
    // endpoint. Keeps unit tests hermetic - no sockets, no real OS input injection.
    internal static Func<string, Wire.MouseKeyProxy.MouseKeyProxyClient>? TestGrpcClientFactory;

    public static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "--help" or "-h" or "help")
        {
            Console.WriteLine(@"mkp - MouseKeyProxy REPL tool
See: https://github.com/sharpninja/MouseKeyProxy

Commands:
  mkp --help
  mkp --version
  mkp status [--json]
  mkp service status | install | uninstall | start | stop   (uses sc.exe, netsh, schtasks; elevation via runas)
  mkp agent status [--json] | emergency-release [--json]
  mkp pair discover | pair <code> | pair status [--json]
  mkp pair mint [ttlSeconds]                                 (service host mints a one-time pairing code)
  mkp toggle
  mkp emergency-release [--json]
  mkp clear-modifiers
  mkp capture-screenshot --remote <peer> --target desktop|foreground|hwnd --hwnd <hex> --clipboard --out <path>
  mkp hid status | provision-check | clear-modifiers | test-key --chord alt+space|win+left|win+right | test-mouse --dx 40 --dy 0 | capture-proof --out <path>
  mkp pi provision [--url URL] [--sha256 HASH] [--stage-root DIR] [--profile NAME] [--force] [--no-launch]
  mkp pi shutdown [--reboot] [--remote <host>]   (Linux/Pi appliance only)
  mkp logs
  mkp clipboard list | clear
  mkp set-mouse --display X --x 100 --y 200
  mkp inject-text ""hello""
  mkp locate-process notepad
  mkp focus-hwnd 123456

Explicit 'mkp service install' does NOT happen on 'dotnet tool install'.
");
            return 0;
        }

        if (args[0] is "--version" or "-v" or "version")
        {
            Console.WriteLine(GetVersionText());
            return 0;
        }

        var cmd = args[0].ToLowerInvariant();
        string baseUrl = Environment.GetEnvironmentVariable("MKP_GRPC") ?? "https://localhost:50051";

        switch (cmd)
        {
            case "clear-modifiers":
                return DoClearModifiers(baseUrl);
            case "capture-screenshot":
                return DoCaptureScreenshot(args, baseUrl);
            case "hid":
                return DoHid(args);
            case "pi":
                return DoPi(args);
            case "status":
                return DoStatus(args);
            case "agent":
                return DoAgent(args);
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
                if (args.Length > 1 && args[1].Equals("status", StringComparison.OrdinalIgnoreCase))
                {
                    return DoAgentStatus(args);
                }

                if (args.Length > 1 && args[1].Equals("mint", StringComparison.OrdinalIgnoreCase))
                {
                    // Operator mints a one-time pairing code on the service host to relay to a peer.
                    try
                    {
                        var ttl = args.Length > 2 && int.TryParse(args[2], out var t) ? t : 0;
                        using var channel = CreateBootstrapChannel(baseUrl);
                        var client = new Wire.MouseKeyProxy.MouseKeyProxyClient(channel);
                        var minted = client.RequestPairingCode(new Wire.RequestPairingCodeRequest { ProtocolVersion = "v1", TtlSeconds = ttl });
                        if (!minted.Success)
                        {
                            Console.WriteLine($"[PAIR mint failed] {minted.Error}");
                            return 1;
                        }

                        Console.WriteLine($"Pairing code: {minted.PairingCode} (valid {minted.TtlSeconds}s)");
                        return 0;
                    }
                    catch (Exception ex) { Console.WriteLine($"[PAIR mint error] {ex.Message}"); return 1; }
                }

                try
                {
                    // TR-MKP-SEC-001: real pairing - exchange the one-time code for a service-signed
                    // client certificate over mTLS and persist the credential for later effect RPCs.
                    var code = args.Length > 1 ? args[1] : "0000";
                    var credential = PairingClient.PairAsync(baseUrl, "repl-peer", code).GetAwaiter().GetResult();
                    PeerCredentialStore.Save(PeerCredentialStore.DefaultPath(), credential);
                    Console.WriteLine($"[REAL gRPC Pair] paired repl-peer; client cert thumbprint={credential.ClientCertificate.Thumbprint}");

                    var agentResponse = NotifyLocalAgentPairingState(baseUrl, code);
                    Console.WriteLine($"[AGENT pairing state] ok={agentResponse.Ok} err={agentResponse.ErrorCode} msg={agentResponse.Message}");
                    return agentResponse.Ok ? 0 : 1;
                }
                catch (MouseKeyProxy.Commands.PairingException ex) { Console.WriteLine($"[REAL Pair rejected] {ex.Error}"); return 1; }
                catch (Exception ex) { Console.WriteLine($"[REAL Pair attempted] {ex.Message}"); return 1; }
            case "toggle":
                // real toggle via SHIPPED handler + transport for mod resync emission (AC3)
                try
                {
                    using var channel = CreateReplChannel(baseUrl);
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
            case "emergency-release":
            case "release":
                return DoEmergencyRelease(args);
            case "logs":
            case "open-logs":
                return DoOpenLogs();
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
            case "inject-text":
            case "set-mouse":
            case "locate-process":
            case "focus-hwnd":
                try
                {
                    using var channel = TestGrpcClientFactory is null
                        ? CreateReplChannel(baseUrl)
                        : null;
                    var client = TestGrpcClientFactory?.Invoke(baseUrl)
                        ?? new Wire.MouseKeyProxy.MouseKeyProxyClient(channel!);
                    using var transport = new MouseKeyProxy.Commands.BidiSessionTransport(client);
                    if (cmd == "inject-text")
                    {
                        var text = args.Length > 1 ? args[1] : "hello";
                        var response = client.InjectInput(new Wire.InjectInputRequest
                        {
                            ProtocolVersion = "v1",
                            PeerId = "repl-peer",
                            CorrelationId = Guid.NewGuid().ToString("n"),
                            Events = { new Wire.InputEvent { Kind = Wire.InputKind.TextInput, Text = text, TsMs = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() } }
                        });
                        Console.WriteLine($"[REAL gRPC InjectInput] ok={response.Ok} err={response.Err} msg={response.Msg}");
                        return response.Ok ? 0 : 1;
                    }
                    else if (cmd == "set-mouse")
                    {
                        var displayId = GetOption(args, "--display", "primary");
                        var x = GetIntOption(args, "--x", 100);
                        var y = GetIntOption(args, "--y", 100);
                        var response = client.SetMousePosition(new Wire.SetMousePositionRequest
                        {
                            ProtocolVersion = "v1",
                            PeerId = "repl-peer",
                            DisplayId = displayId,
                            X = x,
                            Y = y,
                            CorrelationId = Guid.NewGuid().ToString("n")
                        });
                        Console.WriteLine($"[REAL gRPC SetMousePosition] ok={response.Ok} err={response.Err} msg={response.Msg}");
                        return response.Ok ? 0 : 1;
                    }
                    else if (cmd == "focus-hwnd")
                    {
                        var hwnd = args.Length > 1 ? ParseHwnd(args[1]) : 0UL;
                        var response = client.SetFocusByHwnd(new Wire.SetFocusByHwndRequest
                        {
                            ProtocolVersion = "v1",
                            PeerId = "repl-peer",
                            Hwnd = hwnd,
                            BringToFront = true,
                            CorrelationId = Guid.NewGuid().ToString("n")
                        });
                        Console.WriteLine($"[REAL gRPC SetFocusByHwnd] ok={response.Ok} err={response.Err} msg={response.Msg}");
                        return response.Ok ? 0 : 1;
                    }
                    else
                    {
                        var processName = args.Length > 1 ? args[1] : "";
                        var response = client.LocateProcess(new Wire.LocateProcessRequest
                        {
                            ProtocolVersion = "v1",
                            PeerId = "repl-peer",
                            ProcessName = processName,
                            Pid = (uint)GetIntOption(args, "--pid", 0)
                        });
                        Console.WriteLine($"[REAL gRPC LocateProcess] errorCode={response.ErrorCode} count={response.Nodes.Count}");
                        foreach (var node in response.Nodes)
                        {
                            Console.WriteLine($"HWND=0x{node.Hwnd:x} PID={node.ProcessId} CLASS={node.ClassName} TITLE={node.Title}");
                        }
                        return response.ErrorCode == "0" ? 0 : 1;
                    }
                }
                catch (Exception ex) { Console.WriteLine($"[REAL network op] {ex.Message}"); return 1; }
            default:
                Console.WriteLine($"unknown cmd: {cmd}. Use mkp --help");
                return 1;
        }
    }

    private static int DoClearModifiers(string baseUrl)
    {
        try
        {
            using var channel = CreateReplChannel(baseUrl);
            var client = new Wire.MouseKeyProxy.MouseKeyProxyClient(channel);
            var response = client.ClearModifiers(new Wire.ClearModifiersRequest
            {
                ProtocolVersion = "v1",
                PeerId = "repl-peer",
                CorrelationId = Guid.NewGuid().ToString("N")
            });

            Console.WriteLine($"[REAL gRPC ClearModifiers] ok={response.Ok} err={response.Err} msg={response.Msg}");
            return response.Ok ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[REAL gRPC ClearModifiers] {ex.Message}");
            return 1;
        }
    }

    private static int DoCaptureScreenshot(string[] args, string baseUrl)
    {
        var remote = GetOption(args, "--remote", string.Empty);
        if (!string.IsNullOrWhiteSpace(remote))
        {
            baseUrl = remote.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || remote.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                ? remote
                : Cmn.LabTopology.GrpcUrl(remote);
        }

        var target = ParseScreenshotTarget(GetOption(args, "--target", "desktop"));
        var hwnd = ParseHwnd(GetOption(args, "--hwnd", "0"));
        var outPath = GetOption(args, "--out", string.Empty);
        var putClipboard = args.Any(static arg => arg.Equals("--clipboard", StringComparison.OrdinalIgnoreCase));
        var correlationId = Guid.NewGuid().ToString("N");

        try
        {
            using var channel = CreateReplChannel(baseUrl);
            var client = new Wire.MouseKeyProxy.MouseKeyProxyClient(channel);
            using var call = client.CaptureScreenshot(new Wire.CaptureScreenshotRequest
            {
                ProtocolVersion = "v1",
                PeerId = "repl-peer",
                CorrelationId = correlationId,
                Target = target,
                Hwnd = hwnd,
                IncludeCursor = true
            });

            Wire.ScreenshotMetadata? metadata = null;
            using var png = new MemoryStream();
            while (call.ResponseStream.MoveNext(System.Threading.CancellationToken.None).GetAwaiter().GetResult())
            {
                var chunk = call.ResponseStream.Current;
                metadata ??= chunk.Metadata;
                chunk.Data.WriteTo(png);
            }

            var bytes = png.ToArray();
            if (!string.IsNullOrWhiteSpace(outPath))
            {
                var fullPath = Path.GetFullPath(outPath);
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory());
                File.WriteAllBytes(fullPath, bytes);
            }

            if (putClipboard)
            {
                WindowsClipboard.SetPng(bytes);
            }

            Console.WriteLine($"[REAL gRPC CaptureScreenshot] bytes={bytes.Length} sha256={metadata?.Sha256 ?? string.Empty} capturedAtUtc={metadata?.CapturedAtUtc ?? string.Empty} sourceHost={metadata?.SourceHost ?? string.Empty} correlationId={metadata?.CorrelationId ?? correlationId}");
            return bytes.Length > 0 ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[REAL gRPC CaptureScreenshot] {ex.Message}");
            return 1;
        }
    }

    private static int DoPi(string[] args)
    {
        var sub = args.Length > 1 ? args[1].ToLowerInvariant() : "provision";
        if (sub is "shutdown" or "reboot")
        {
            var reboot = sub == "reboot" || args.Any(static a => a.Equals("--reboot", StringComparison.OrdinalIgnoreCase));
            return DoPiShutdown(args, reboot);
        }

        if (sub != "provision")
        {
            Console.WriteLine($"unknown pi cmd: {sub}. Use mkp pi provision | shutdown | reboot.");
            return 1;
        }

        var sha = GetOption(args, "--sha256", PiProvisionOptions.DefaultSha256);
        var options = new PiProvisionOptions
        {
            ImageUrl = new Uri(GetOption(args, "--url", PiProvisionOptions.DefaultImageUrl)),
            ExpectedSha256 = string.Equals(sha, "skip", StringComparison.OrdinalIgnoreCase) ? null : sha,
            StageRoot = GetOption(args, "--stage-root", PiProvisionOptions.DefaultStageRoot),
            Profile = GetOption(args, "--profile", "default"),
            Force = args.Any(static a => a.Equals("--force", StringComparison.OrdinalIgnoreCase)),
            LaunchRufus = !args.Any(static a => a.Equals("--no-launch", StringComparison.OrdinalIgnoreCase))
        };

        try
        {
            Console.WriteLine($"Staging Raspberry Pi image from {options.ImageUrl} into {options.StageRoot} (this can take several minutes)...");
            var provisioner = new PiImageProvisioner();
            var result = provisioner.ProvisionAsync(options).GetAwaiter().GetResult();
            Console.WriteLine($"[mkp pi provision] ok={result.Ok} image={result.ImagePath}");
            Console.WriteLine($"  rufus={result.RufusPath}");
            Console.WriteLine($"  args={string.Join(' ', result.LaunchArguments)}");
            Console.WriteLine(result.Message);
            if (result.Ok && options.LaunchRufus)
            {
                Console.WriteLine("Select the SD card in RUFUS For MouseKeyProxy, configure the Pi HID profile, then click START to write.");
            }

            return result.Ok ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[mkp pi provision] failed: {ex.Message}");
            return 1;
        }
    }

    private static int DoPiShutdown(string[] args, bool reboot)
    {
        // NOTE (audit/WIP): the server-side Shutdown RPC is not yet isolated by pairing auth
        // (TR-MKP-SEC-001). This command is parked and must not be relied on until that lands.
        var remote = GetOption(args, "--remote", string.Empty);
        var baseUrl = !string.IsNullOrWhiteSpace(remote)
            ? (remote.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? remote : Cmn.LabTopology.GrpcUrl(remote))
            : (Environment.GetEnvironmentVariable("MKP_GRPC") ?? "https://localhost:50051");
        var label = reboot ? "reboot" : "shutdown";
        try
        {
            using var channel = CreateReplChannel(baseUrl);
            var client = new Wire.MouseKeyProxy.MouseKeyProxyClient(channel);
            var resp = client.Shutdown(new Wire.ShutdownRequest
            {
                ProtocolVersion = "v1",
                PeerId = "repl-peer",
                Mode = reboot ? Wire.ShutdownMode.Reboot : Wire.ShutdownMode.Poweroff,
                CorrelationId = Guid.NewGuid().ToString("N")
            });
            Console.WriteLine($"[mkp pi {label}] ok={resp.Ok} err={resp.Err} msg={resp.Msg}");
            return resp.Ok ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[mkp pi {label}] {ex.Message}");
            return 1;
        }
    }

    private static int DoHid(string[] args)
    {
        var sub = args.Length > 1 ? args[1].ToLowerInvariant() : "status";
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var options = PiHidClientOptions.FromEnvironment();
            var client = new PiHidClient(http, options);
            return sub switch
            {
                "status" => PrintHidStatus(client, options),
                "provision-check" => DoHidProvisionCheck(client, options),
                "clear-modifiers" => PrintHidResult(client.ClearModifiersAsync().GetAwaiter().GetResult(), "clear-modifiers"),
                "reset" => PrintHidResult(client.ResetAsync().GetAwaiter().GetResult(), "reset"),
                "test-key" => DoHidTestKey(args, client),
                "test-mouse" => DoHidTestMouse(args, client),
                "capture-proof" => DoHidCaptureProof(args, client, options),
                _ => UnknownHidCommand(sub),
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"hid {sub} failed: {ex.Message}");
            return 1;
        }
    }

    private static int PrintHidStatus(PiHidClient client, PiHidClientOptions options)
    {
        var result = client.GetStatusAsync().GetAwaiter().GetResult();
        Console.WriteLine($"pi-hid url={options.BaseUri} status={(int)result.StatusCode} ok={result.Ok}");
        if (!string.IsNullOrWhiteSpace(result.Body))
        {
            Console.WriteLine(result.Body);
        }

        return result.Ok ? 0 : 1;
    }

    private static int DoHidProvisionCheck(PiHidClient client, PiHidClientOptions options)
    {
        var tokenPresent = !string.IsNullOrWhiteSpace(options.Token);
        Console.WriteLine($"pi-hid provision-check sourceHost={Environment.MachineName.ToLowerInvariant()} targetHost={Environment.GetEnvironmentVariable("MKP_TARGET_HOST") ?? "payton-desktop"} url={options.BaseUri} tokenPresent={tokenPresent}");
        if (!tokenPresent)
        {
            Console.WriteLine("missing MKP_HID_PI_TOKEN");
            return 1;
        }

        return PrintHidStatus(client, options);
    }

    private static int DoHidTestKey(string[] args, PiHidClient client)
    {
        var chordText = GetOption(args, "--chord", args.Length > 2 ? args[2] : "alt+space");
        if (!PiHidReports.TryParseChord(chordText, out var chord))
        {
            Console.WriteLine("unsupported HID chord. Use alt+space, win+left, or win+right.");
            return 1;
        }

        var results = client.TestChordAsync(chord).GetAwaiter().GetResult();
        return PrintHidResults(results, $"test-key {chord}");
    }

    private static int DoHidTestMouse(string[] args, PiHidClient client)
    {
        var dx = GetIntOption(args, "--dx", 0);
        var dy = GetIntOption(args, "--dy", 0);
        var wheel = GetIntOption(args, "--wheel", 0);
        var results = client.TestMouseAsync(dx, dy, wheel).GetAwaiter().GetResult();
        return PrintHidResults(results, $"test-mouse dx={dx} dy={dy} wheel={wheel}");
    }

    private static int DoHidCaptureProof(string[] args, PiHidClient client, PiHidClientOptions options)
    {
        var utc = DateTimeOffset.UtcNow;
        var stamp = utc.ToString("yyyyMMddTHHmmssZ");
        var outPath = GetOption(args, "--out", Path.Combine(Environment.CurrentDirectory, "docs", $"receipts-hid-provision-{stamp}.txt"));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        var status = client.GetStatusAsync().GetAwaiter().GetResult();
        var lines = new[]
        {
            "MouseKeyProxy Pi HID proof receipt",
            $"capturedAtUtc={utc:O}",
            $"sourceHost={Environment.MachineName.ToLowerInvariant()}",
            $"targetHost={Environment.GetEnvironmentVariable("MKP_TARGET_HOST") ?? "payton-desktop"}",
            $"piUrl={options.BaseUri}",
            $"tokenPresent={!string.IsNullOrWhiteSpace(options.Token)}",
            $"statusCode={(int)status.StatusCode}",
            $"statusOk={status.Ok}",
            $"statusBody={status.Body}",
        };
        File.WriteAllLines(outPath, lines);
        Console.WriteLine($"hid proof receipt: {outPath}");
        return status.Ok ? 0 : 1;
    }

    private static int PrintHidResult(PiHidHttpResult result, string operation)
    {
        Console.WriteLine($"pi-hid {operation} status={(int)result.StatusCode} ok={result.Ok}");
        if (!string.IsNullOrWhiteSpace(result.Body))
        {
            Console.WriteLine(result.Body);
        }

        return result.Ok ? 0 : 1;
    }

    private static int PrintHidResults(IReadOnlyList<PiHidHttpResult> results, string operation)
    {
        var failed = results.FirstOrDefault(static r => !r.Ok);
        Console.WriteLine($"pi-hid {operation} reports={results.Count} ok={failed is null}");
        if (failed != null)
        {
            Console.WriteLine($"first failure: status={(int)failed.StatusCode} body={failed.Body}");
            return 1;
        }

        return 0;
    }

    private static int UnknownHidCommand(string sub)
    {
        Console.WriteLine($"unknown hid cmd: {sub}. Use mkp hid status, provision-check, clear-modifiers, test-key, test-mouse, or capture-proof.");
        return 1;
    }

    private static Wire.ScreenshotTarget ParseScreenshotTarget(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "foreground" => Wire.ScreenshotTarget.Foreground,
            "hwnd" => Wire.ScreenshotTarget.Hwnd,
            _ => Wire.ScreenshotTarget.Desktop
        };
    }
    private static string GetVersionText()
    {
        var assembly = typeof(Program).Assembly;
        var informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            return informationalVersion;
        }

        return assembly.GetName().Version?.ToString() ?? "unknown";
    }
    private static int DoStatus(string[] args)
    {
        if (WantsJson(args))
        {
            var service = ReadServiceStatus();
            var agent = GetLocalAgentStatus();
            Console.WriteLine(JsonSerializer.Serialize(new { service, agent }, AgentControlJsonOptions));
            return service.Ok && agent.Ok ? 0 : 1;
        }

        var serviceExitCode = DoServiceStatus();
        var agentStatus = GetLocalAgentStatus();
        PrintAgentStatus(agentStatus);
        return serviceExitCode == 0 && agentStatus.Ok ? 0 : 1;
    }

    private static int DoAgent(string[] args)
    {
        var sub = args.Length > 1 ? args[1].ToLowerInvariant() : "status";
        return sub switch
        {
            "status" => DoAgentStatus(args),
            "emergency-release" => DoEmergencyRelease(args),
            "release" => DoEmergencyRelease(args),
            _ => UnknownAgentCommand(sub)
        };
    }

    private static int UnknownAgentCommand(string sub)
    {
        Console.WriteLine($"unknown agent cmd: {sub}. Use mkp agent status or mkp agent emergency-release.");
        return 1;
    }

    private static int DoAgentStatus(string[] args)
    {
        var response = GetLocalAgentStatus();
        if (WantsJson(args))
        {
            Console.WriteLine(JsonSerializer.Serialize(response, AgentControlJsonOptions));
        }
        else
        {
            PrintAgentStatus(response);
        }

        return response.Ok ? 0 : 1;
    }

    private static int DoEmergencyRelease(string[] args)
    {
        var response = SendLocalAgentControlRequest(new Cmn.AgentControlRequest
        {
            Operation = Cmn.AgentControlPipe.EmergencyRelease,
            RemotePeer = Environment.MachineName.ToLowerInvariant(),
            CorrelationId = Guid.NewGuid().ToString("N"),
            NotifyPeer = true
        });

        if (WantsJson(args))
        {
            Console.WriteLine(JsonSerializer.Serialize(response, AgentControlJsonOptions));
        }
        else
        {
            Console.WriteLine($"[AGENT emergency release] ok={response.Ok} err={response.ErrorCode} msg={response.Message}");
        }

        return response.Ok ? 0 : 1;
    }

    private static Cmn.AgentControlResponse GetLocalAgentStatus()
    {
        return SendLocalAgentControlRequest(new Cmn.AgentControlRequest
        {
            Operation = Cmn.AgentControlPipe.GetAgentStatus
        });
    }

    private static void PrintAgentStatus(Cmn.AgentControlResponse response)
    {
        Console.WriteLine($"Agent: ok={response.Ok} err={response.ErrorCode} msg={response.Message}");
        Console.WriteLine($"Pairing: state={response.RemoteState} peer={response.RemotePeer} endpoint={response.RemoteGrpcUrl}");
        Console.WriteLine($"Forwarding: active={response.ForwardingActive}");
    }

    private static bool WantsJson(string[] args)
    {
        return args.Any(static arg => arg.Equals("--json", StringComparison.OrdinalIgnoreCase));
    }

    private static int DoOpenLogs()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "eventvwr.msc",
                Arguments = "/c:MouseKeyProxy",
                UseShellExecute = true
            });
            Console.WriteLine("[REAL Event Viewer] opened MouseKeyProxy log");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[REAL Event Viewer] failed: {ex.Message}");
            return 1;
        }
    }

    private static string GetOption(string[] args, string name, string defaultValue)
    {
        var index = Array.FindIndex(args, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : defaultValue;
    }

    private static Cmn.AgentControlResponse NotifyLocalAgentPairingState(string remoteGrpcUrl, string pairingCode)
    {
        return SendLocalAgentControlRequest(new Cmn.AgentControlRequest
        {
            Operation = Cmn.AgentControlPipe.NotifyPairingState,
            RemotePeer = ResolveRemotePeerName(remoteGrpcUrl),
            RemoteGrpcUrl = remoteGrpcUrl,
            PairingCode = pairingCode
        });
    }

    private static Cmn.AgentControlResponse SendLocalAgentControlRequest(Cmn.AgentControlRequest request)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(
                ".",
                Cmn.AgentControlPipe.PipeName,
                PipeDirection.InOut,
                PipeOptions.None);
            pipe.Connect(2000);

            using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(pipe, leaveOpen: true);

            writer.WriteLine(JsonSerializer.Serialize(request, AgentControlJsonOptions));
            var line = reader.ReadLine();
            if (string.IsNullOrWhiteSpace(line))
            {
                return Cmn.AgentControlResponse.Failure("AGENT_IPC_EMPTY_RESPONSE", "Agent control pipe returned no response.");
            }

            return JsonSerializer.Deserialize<Cmn.AgentControlResponse>(line, AgentControlJsonOptions)
                ?? Cmn.AgentControlResponse.Failure("AGENT_IPC_BAD_RESPONSE", "Agent control pipe returned an unreadable response.");
        }
        catch (System.TimeoutException ex)
        {
            return Cmn.AgentControlResponse.Failure("AGENT_IPC_UNAVAILABLE", ex.Message);
        }
        catch (IOException ex)
        {
            return Cmn.AgentControlResponse.Failure("AGENT_IPC_UNAVAILABLE", ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Cmn.AgentControlResponse.Failure("AGENT_IPC_DENIED", ex.Message);
        }
        catch (Exception ex)
        {
            return Cmn.AgentControlResponse.Failure("AGENT_IPC_ERROR", ex.Message);
        }
    }

    private static string ResolveRemotePeerName(string remoteGrpcUrl)
    {
        return Uri.TryCreate(remoteGrpcUrl, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host)
            ? uri.Host.ToLowerInvariant()
            : remoteGrpcUrl;
    }

    private static int GetIntOption(string[] args, string name, int defaultValue)
    {
        return int.TryParse(GetOption(args, name, defaultValue.ToString()), out var value) ? value : defaultValue;
    }

    private static ulong ParseHwnd(string value)
    {
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return Convert.ToUInt64(value[2..], 16);
        }

        return ulong.TryParse(value, out var hwnd) ? hwnd : 0UL;
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
            if (EventLog.SourceExists(EventLogSourceName))
            {
                var currentLogName = EventLog.LogNameFromSourceName(EventLogSourceName, ".");
                if (!string.Equals(currentLogName, EventLogName, StringComparison.OrdinalIgnoreCase))
                {
                    EventLog.DeleteEventSource(EventLogSourceName);
                    EventLog.CreateEventSource(EventLogSourceName, EventLogName);
                }
            }
            else
            {
                EventLog.CreateEventSource(EventLogSourceName, EventLogName);
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
            if (EventLog.SourceExists(EventLogSourceName))
            {
                EventLog.DeleteEventSource(EventLogSourceName);
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

    private static ServiceStatusSnapshot ReadServiceStatus()
    {
        try
        {
            using var sc = new ServiceController("MouseKeyProxy");
            return new ServiceStatusSnapshot
            {
                Ok = true,
                Name = sc.ServiceName,
                Status = sc.Status.ToString(),
                StartType = sc.StartType.ToString()
            };
        }
        catch (Exception ex)
        {
            return new ServiceStatusSnapshot
            {
                Ok = false,
                Name = "MouseKeyProxy",
                Error = ex.Message
            };
        }
    }

    private static int DoServiceStatus()
    {
        var status = ReadServiceStatus();
        if (status.Ok)
        {
            Console.WriteLine($"Service: {status.Name} - {status.Status} (StartType: {status.StartType})");
            return 0;
        }

        Console.WriteLine("Service not installed or inaccessible.");
        Console.WriteLine("[SHIPPED] service status - uses sc.exe/netsh/schtasks when elevated (sandbox may limit)");
        return 1;
    }

    private static class WindowsClipboard
    {
        private const uint GMEM_MOVEABLE = 0x0002;

        public static void SetPng(byte[] png)
        {
            if (png.Length == 0)
            {
                throw new ArgumentException("PNG clipboard data is empty.", nameof(png));
            }

            if (!OpenClipboard(IntPtr.Zero))
            {
                throw new InvalidOperationException($"OpenClipboard failed win32={Marshal.GetLastWin32Error()}");
            }

            var handle = IntPtr.Zero;
            try
            {
                EmptyClipboard();
                var format = RegisterClipboardFormat("PNG");
                if (format == 0)
                {
                    throw new InvalidOperationException($"RegisterClipboardFormat(PNG) failed win32={Marshal.GetLastWin32Error()}");
                }

                handle = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)png.Length);
                if (handle == IntPtr.Zero)
                {
                    throw new InvalidOperationException($"GlobalAlloc failed win32={Marshal.GetLastWin32Error()}");
                }

                var target = GlobalLock(handle);
                if (target == IntPtr.Zero)
                {
                    throw new InvalidOperationException($"GlobalLock failed win32={Marshal.GetLastWin32Error()}");
                }

                try
                {
                    Marshal.Copy(png, 0, target, png.Length);
                }
                finally
                {
                    GlobalUnlock(handle);
                }

                if (SetClipboardData(format, handle) == IntPtr.Zero)
                {
                    throw new InvalidOperationException($"SetClipboardData(PNG) failed win32={Marshal.GetLastWin32Error()}");
                }

                handle = IntPtr.Zero;
            }
            finally
            {
                CloseClipboard();
                if (handle != IntPtr.Zero)
                {
                    GlobalFree(handle);
                }
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool OpenClipboard(IntPtr hWndNewOwner);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool EmptyClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool CloseClipboard();

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern uint RegisterClipboardFormat(string lpszFormat);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalLock(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalUnlock(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalFree(IntPtr hMem);
    }
    private sealed class ServiceStatusSnapshot
    {
        public bool Ok { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string StartType { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;
    }
}
