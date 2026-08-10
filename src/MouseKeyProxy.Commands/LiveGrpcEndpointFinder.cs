using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using MouseKeyProxy.Common;

namespace MouseKeyProxy.Commands;

/// <summary>
/// TR-MKP-SEC-001: when plug-n-play ToFU discovery finds no unpaired advertisers, probe likely
/// LAN hosts on the gRPC port and produce operator guidance for code-based re-pair
/// (<c>mkp pair mint</c> then <c>mkp pair &lt;code&gt;</c>).
/// </summary>
public static class LiveGrpcEndpointFinder
{
    /// <summary>Default TCP connect timeout per candidate host.</summary>
    public static readonly TimeSpan DefaultConnectTimeout = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Builds a deduplicated list of host names/IPs to probe: optional extras first, then each
    /// local unicast IPv4 expanded to its /24 (private ranges only; no link-local explosion).
    /// </summary>
    /// <param name="localUnicast">Local interface addresses with prefix lengths.</param>
    /// <param name="extraHosts">Optional configured hosts (settings, env, topology).</param>
    /// <param name="maxHostsPerSubnet">Cap on hosts expanded from a single /24 (default 254 usable).</param>
    /// <returns>Candidate host strings suitable for TCP connect.</returns>
    public static IReadOnlyList<string> BuildCandidates(
        IEnumerable<(IPAddress Address, int PrefixLength)> localUnicast,
        IEnumerable<string>? extraHosts = null,
        int maxHostsPerSubnet = 254)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (extraHosts is not null)
        {
            foreach (var raw in extraHosts)
            {
                var host = NormalizeHost(raw);
                if (host is not null)
                {
                    set.Add(host);
                }
            }
        }

        // Always include loopback so a co-located service is visible.
        set.Add(IPAddress.Loopback.ToString());

        foreach (var (address, prefix) in localUnicast)
        {
            if (address.AddressFamily != AddressFamily.InterNetwork)
            {
                continue;
            }

            if (IPAddress.IsLoopback(address))
            {
                set.Add(IPAddress.Loopback.ToString());
                continue;
            }

            // Skip link-local (APIPA) subnet expansion; those ranges are large and rarely useful.
            if (IsLinkLocal(address))
            {
                continue;
            }

            set.Add(address.ToString());

            // Expand only prefix == 24 private networks (lab Wi-Fi / Ethernet). Tighter prefixes
            // still add the host itself; looser prefixes are not fully enumerated.
            if (prefix == 24 && IsPrivateIpv4(address) && maxHostsPerSubnet > 0)
            {
                var bytes = address.GetAddressBytes();
                var limit = Math.Min(maxHostsPerSubnet, 254);
                for (var hostPart = 1; hostPart <= limit; hostPart++)
                {
                    set.Add($"{bytes[0]}.{bytes[1]}.{bytes[2]}.{hostPart}");
                }
            }
        }

        return set.OrderBy(static h => h, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Enumerates up IPv4 unicast addresses from the machine's network interfaces.
    /// </summary>
    public static IReadOnlyList<(IPAddress Address, int PrefixLength)> GetLocalUnicastAddresses()
    {
        var list = new List<(IPAddress, int)>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up)
            {
                continue;
            }

            var props = nic.GetIPProperties();
            foreach (var uni in props.UnicastAddresses)
            {
                if (uni.Address.AddressFamily != AddressFamily.InterNetwork)
                {
                    continue;
                }

                list.Add((uni.Address, uni.PrefixLength));
            }
        }

        return list;
    }

    /// <summary>
    /// Probes candidate hosts for an open TCP port and returns <c>https://host:port</c> URLs for
    /// those that accept a connection within the timeout.
    /// </summary>
    /// <param name="hosts">Candidate host names or IPv4 strings.</param>
    /// <param name="port">TCP port (default <see cref="LabTopology.GrpcPort"/>).</param>
    /// <param name="connectTimeout">Per-host connect timeout.</param>
    /// <param name="probe">Optional injectable probe (tests); default is a real TCP connect.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Open endpoint URLs, sorted.</returns>
    public static async Task<IReadOnlyList<string>> FindOpenEndpointsAsync(
        IEnumerable<string> hosts,
        int port = LabTopology.GrpcPort,
        TimeSpan? connectTimeout = null,
        Func<string, int, TimeSpan, CancellationToken, Task<bool>>? probe = null,
        CancellationToken cancellationToken = default)
    {
        var timeout = connectTimeout ?? DefaultConnectTimeout;
        probe ??= TcpConnectProbeAsync;
        var hostList = hosts
            .Select(NormalizeHost)
            .Where(static h => h is not null)
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var open = new System.Collections.Concurrent.ConcurrentBag<string>();
        await Parallel.ForEachAsync(
            hostList,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = 64,
                CancellationToken = cancellationToken,
            },
            async (host, ct) =>
            {
                try
                {
                    if (await probe(host, port, timeout, ct).ConfigureAwait(false))
                    {
                        open.Add($"https://{host}:{port}");
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    // Unreachable / refused / DNS failure: treat as closed.
                }
            }).ConfigureAwait(false);

        return open.OrderBy(static u => u, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Operator-facing lines when ToFU discovery returned no unpaired devices.
    /// </summary>
    /// <param name="liveEndpoints">Open <c>https://host:port</c> URLs from a LAN probe.</param>
    /// <returns>Lines to print to the console.</returns>
    public static IReadOnlyList<string> FormatEmptyDiscoverGuidance(IReadOnlyList<string> liveEndpoints)
    {
        var lines = new List<string>
        {
            "No unpaired devices found (ToFU only advertises when the device has zero peers).",
        };

        if (liveEndpoints is { Count: > 0 })
        {
            lines.Add($"Live gRPC endpoints on the LAN (port {LabTopology.GrpcPort}):");
            foreach (var url in liveEndpoints)
            {
                lines.Add($"  {url}");
            }
        }
        else
        {
            lines.Add("No live gRPC endpoints detected on local subnets (device offline, wrong VLAN, or port filtered).");
        }

        lines.Add("To pair when ToFU is closed: set MKP_GRPC=https://HOST:50051, then mkp pair mint 300 && mkp pair <code>");
        lines.Add("If you still hold a valid paired cert for that device: mkp pair reset-device  (re-opens ToFU) then mkp pair discover");
        return lines;
    }

    /// <summary>Default TCP connect probe used by <see cref="FindOpenEndpointsAsync"/>.</summary>
    public static async Task<bool> TcpConnectProbeAsync(
        string host,
        int port,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var client = new TcpClient();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(timeout);
        try
        {
            await client.ConnectAsync(host, port, linked.Token).ConfigureAwait(false);
            return client.Connected;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (SocketException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static string? NormalizeHost(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var s = raw.Trim();
        // Accept full URLs from settings / MKP_GRPC.
        if (Uri.TryCreate(s, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
            !string.IsNullOrWhiteSpace(uri.Host))
        {
            return uri.Host;
        }

        // Strip optional trailing :port for bare host:port forms.
        if (s.Contains(':', StringComparison.Ordinal) &&
            !IPAddress.TryParse(s, out _) &&
            Uri.TryCreate("https://" + s, UriKind.Absolute, out var bare) &&
            !string.IsNullOrWhiteSpace(bare.Host))
        {
            return bare.Host;
        }

        return s;
    }

    private static bool IsLinkLocal(IPAddress address)
    {
        var b = address.GetAddressBytes();
        return b[0] == 169 && b[1] == 254;
    }

    private static bool IsPrivateIpv4(IPAddress address)
    {
        var b = address.GetAddressBytes();
        if (b[0] == 10)
        {
            return true;
        }

        if (b[0] == 172 && b[1] is >= 16 and <= 31)
        {
            return true;
        }

        if (b[0] == 192 && b[1] == 168)
        {
            return true;
        }

        return false;
    }
}
