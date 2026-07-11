using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Principal;
using Grpc.Net.Client;
using MouseKeyProxy.Commands;
using MouseKeyProxy.Common;
using MouseKeyProxy.Repl;
using Wire = MouseKeyProxy.Network.V1;

namespace MouseKeyProxy.Bootstrap;

/// <summary>
/// FR-MKP-024 / FR-MKP-025 / FR-MKP-026: elevated client bootstrap used by MSI custom action
/// and by <c>Install-MouseKeyProxy.ps1</c> on MKP-DEPLOY media.
/// </summary>
/// <remarks>
/// Usage:
/// <code>
/// MouseKeyProxy.Bootstrap.exe [--payloads DIR] [--bootstrap-json PATH] [--skip-install] [--skip-pair]
/// </code>
/// </remarks>
public static class Program
{
    /// <summary>Entry point.</summary>
    /// <param name="args">CLI args.</param>
    /// <returns>Process exit code (0 success).</returns>
    public static int Main(string[] args)
    {
        try
        {
            return Run(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[bootstrap] FATAL: {ex.Message}");
            return 99;
        }
    }

    private static int Run(string[] args)
    {
        if (!IsAdministrator())
        {
            Console.Error.WriteLine("[bootstrap] Elevation required. Relaunch as administrator.");
            return 2;
        }

        var payloads = GetOption(args, "--payloads")
            ?? Path.Combine(AppContext.BaseDirectory, "payloads");
        var bootstrapJson = GetOption(args, "--bootstrap-json")
            ?? FindBootstrapJson(AppContext.BaseDirectory);
        var skipInstall = HasFlag(args, "--skip-install");
        var skipPair = HasFlag(args, "--skip-pair");

        Console.WriteLine($"[bootstrap] payloads={payloads}");
        Console.WriteLine($"[bootstrap] bootstrapJson={bootstrapJson ?? "(none)"}");

        if (!skipInstall)
        {
            var install = ServiceInstaller.Install(
                new ServiceInstallContext
                {
                    PayloadsDirectory = payloads,
                    Log = msg => Console.WriteLine($"[install] {msg}"),
                },
                new SystemProcessRunner());
            if (!install.Success)
            {
                Console.Error.WriteLine($"[bootstrap] install failed exit={install.ExitCode}");
                return install.ExitCode == 0 ? 3 : install.ExitCode;
            }

            Console.WriteLine("[bootstrap] install OK");
        }

        if (skipPair)
        {
            return 0;
        }

        if (string.IsNullOrWhiteSpace(bootstrapJson) || !File.Exists(bootstrapJson))
        {
            Console.Error.WriteLine("[bootstrap] device-bootstrap.json not found; skip pair (install only).");
            return 0;
        }

        var cfg = DeviceBootstrapConfig.LoadFile(bootstrapJson);
        File.Copy(bootstrapJson, Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "MouseKeyProxy",
            "device-bootstrap.json"), overwrite: true);

        var deviceUrl = ResolveDeviceUrl(cfg);
        Console.WriteLine($"[bootstrap] pairing to device {deviceUrl}");

        var peerId = Environment.MachineName.ToLowerInvariant();
        var code = cfg.InstallTicket ?? string.Empty;
        var credential = PairingClient.PairAsync(deviceUrl, peerId, code).GetAwaiter().GetResult();
        PeerCredentialStore.Save(PeerCredentialStore.DefaultPath(), credential);
        Console.WriteLine($"[bootstrap] paired peerId={credential.PeerId} thumb={credential.ClientCertificate.Thumbprint}");

        // Notify local agent (best effort).
        try
        {
            var agentResp = NotifyAgent(deviceUrl, code);
            Console.WriteLine($"[bootstrap] agent notify ok={agentResp.Ok} err={agentResp.ErrorCode} msg={agentResp.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[bootstrap] agent notify skipped: {ex.Message}");
        }

        // Mint clipboard intro code on local Service (bootstrap channel trusts server).
        var localServiceUrl = "https://127.0.0.1:50051";
        string introCode;
        try
        {
            using var localCh = CreateBootstrapChannel(localServiceUrl);
            var localClient = new Wire.MouseKeyProxy.MouseKeyProxyClient(localCh);
            var minted = localClient.RequestPairingCode(new Wire.RequestPairingCodeRequest
            {
                ProtocolVersion = "v1",
                TtlSeconds = 300,
            });
            if (!minted.Success || string.IsNullOrWhiteSpace(minted.PairingCode))
            {
                Console.WriteLine($"[bootstrap] local mint failed: {minted.Error}; generating random intro code");
                introCode = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
            }
            else
            {
                introCode = minted.PairingCode;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[bootstrap] local mint error: {ex.Message}; random intro code");
            introCode = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        }

        var clipboardEndpoint = $"https://{GetLanIPv4()}:50051";
        Console.WriteLine($"[bootstrap] CompleteClientInstall endpoint={clipboardEndpoint}");

        using (var ch = PairingClient.CreateAuthenticatedChannel(deviceUrl, credential))
        {
            var client = new Wire.MouseKeyProxy.MouseKeyProxyClient(ch);
            var complete = client.CompleteClientInstall(new Wire.CompleteClientInstallRequest
            {
                ProtocolVersion = "v1",
                PeerId = peerId,
                ClipboardEndpoint = clipboardEndpoint,
                ClipboardIntroCode = introCode,
                CorrelationId = Guid.NewGuid().ToString("n"),
                IntroTtlSeconds = 300,
            });
            Console.WriteLine(
                $"[bootstrap] CompleteClientInstall ok={complete.Ok} err={complete.Err} msg={complete.Msg} host={complete.HostPeerId} queued={complete.ClipboardIntroQueued}");
            if (!complete.Ok)
            {
                return 4;
            }
        }

        // Persist intro code for local service pair-in by host (same OTP minted above).
        var introPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MouseKeyProxy",
            "clipboard-intro.json");
        Directory.CreateDirectory(Path.GetDirectoryName(introPath)!);
        File.WriteAllText(introPath, System.Text.Json.JsonSerializer.Serialize(new
        {
            clipboardEndpoint,
            introCode,
            deviceUrl,
            savedAtUtc = DateTimeOffset.UtcNow,
        }));

        Console.WriteLine("[bootstrap] DONE");
        return 0;
    }

    private static string ResolveDeviceUrl(DeviceBootstrapConfig cfg)
    {
        if (cfg.PreferDiscovery)
        {
            try
            {
                var beacons = DiscoveryFinder.ListenAsync(
                    TimeSpan.FromSeconds(4),
                    cfg.DiscoveryPort <= 0 ? DiscoveryBeacon.DiscoveryPort : cfg.DiscoveryPort,
                    DiscoveryFinder.DiscoveryFilter.Any).GetAwaiter().GetResult();
                // Prefer pairing-available, else any beacon matching peer id.
                var hit = beacons.FirstOrDefault(b => b.PairingAvailable)
                    ?? beacons.FirstOrDefault(b =>
                        string.IsNullOrWhiteSpace(cfg.DevicePeerId) ||
                        string.Equals(b.PeerId, cfg.DevicePeerId, StringComparison.OrdinalIgnoreCase))
                    ?? beacons.FirstOrDefault();
                if (hit is not null)
                {
                    return $"https://{hit.Host}:{hit.GrpcPort}";
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[bootstrap] discovery failed: {ex.Message}");
            }
        }

        if (string.IsNullOrWhiteSpace(cfg.DeviceGrpcUrl))
        {
            throw new InvalidOperationException("No device URL from discovery or bootstrap JSON.");
        }

        return cfg.DeviceGrpcUrl.Trim();
    }

    private static string GetLanIPv4()
    {
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up)
                {
                    continue;
                }

                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !IPAddress.IsLoopback(ua.Address))
                    {
                        return ua.Address.ToString();
                    }
                }
            }
        }
        catch
        {
            /* fall through */
        }

        return Dns.GetHostName();
    }

    private static AgentControlResponse NotifyAgent(string deviceUrl, string pairingCode)
    {
        // Reuse REPL helper via reflection-free duplication of pipe protocol.
        return SendAgentControl(new AgentControlRequest
        {
            Operation = AgentControlPipe.NotifyPairingState,
            RemotePeer = Uri.TryCreate(deviceUrl, UriKind.Absolute, out var u) ? u.Host : Environment.MachineName,
            RemoteGrpcUrl = deviceUrl,
            PairingCode = pairingCode ?? string.Empty,
        });
    }

    private static AgentControlResponse SendAgentControl(AgentControlRequest request)
    {
        request.AuthToken = AgentControlTokenStore.Read(AgentControlTokenStore.DefaultPath()) ?? string.Empty;
        // Inline minimal pipe client (same as Repl) to avoid private API.
        var pipeName = AgentControlPipe.PipeName;
        using var pipe = new System.IO.Pipes.NamedPipeClientStream(
            ".", pipeName, System.IO.Pipes.PipeDirection.InOut,
            System.IO.Pipes.PipeOptions.None);
        pipe.Connect(timeout: 3000);
        var json = System.Text.Json.JsonSerializer.Serialize(request);
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        var len = BitConverter.GetBytes(bytes.Length);
        pipe.Write(len, 0, 4);
        pipe.Write(bytes, 0, bytes.Length);
        pipe.Flush();
        var lenBuf = new byte[4];
        _ = pipe.Read(lenBuf, 0, 4);
        var n = BitConverter.ToInt32(lenBuf, 0);
        var respBuf = new byte[n];
        var read = 0;
        while (read < n)
        {
            var r = pipe.Read(respBuf, read, n - read);
            if (r <= 0)
            {
                break;
            }

            read += r;
        }

        var respJson = System.Text.Encoding.UTF8.GetString(respBuf, 0, read);
        return System.Text.Json.JsonSerializer.Deserialize<AgentControlResponse>(respJson)
            ?? AgentControlResponse.Failure("BAD_RESPONSE", "Empty agent response.");
    }

    private static GrpcChannel CreateBootstrapChannel(string baseUrl)
    {
        var handler = new SocketsHttpHandler
        {
            EnableMultipleHttp2Connections = true,
            SslOptions = new System.Net.Security.SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (_, _, _, _) => true,
            },
        };
        return GrpcChannel.ForAddress(baseUrl, new GrpcChannelOptions { HttpHandler = handler });
    }

    private static string? FindBootstrapJson(string startDir)
    {
        var candidates = new[]
        {
            Path.Combine(startDir, "device-bootstrap.json"),
            Path.Combine(startDir, "payloads", "device-bootstrap.json"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MouseKeyProxy", "device-bootstrap.json"),
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static string? GetOption(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static bool HasFlag(string[] args, string name) =>
        args.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));

    private static bool IsAdministrator()
    {
        if (!OperatingSystem.IsWindows())
        {
            return true;
        }

        using var id = WindowsIdentity.GetCurrent();
        var p = new WindowsPrincipal(id);
        return p.IsInRole(WindowsBuiltInRole.Administrator);
    }
}
