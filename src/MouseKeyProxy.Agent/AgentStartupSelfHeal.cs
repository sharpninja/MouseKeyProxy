using System;
using System.Collections.Generic;
using System.Linq;

namespace MouseKeyProxy.Agent;

/// <summary>
/// Pure planning for Agent startup self-heal (reload credential, prefer settings endpoint,
/// probe reachability/mTLS, optionally try alternate live gRPC hosts). Side effects stay in Program.
/// </summary>
public static class AgentStartupSelfHeal
{
    /// <summary>Outcome of a self-heal plan.</summary>
    public sealed class Plan
    {
        /// <summary>Remote gRPC URL to use after heal (may differ from the stored pairing URL).</summary>
        public string? RemoteGrpcUrl { get; init; }

        /// <summary>Peer display name/host.</summary>
        public string? RemotePeer { get; init; }

        /// <summary>Whether the Agent should report Connected after heal.</summary>
        public bool MarkConnected { get; init; }

        /// <summary>User-visible summary (tray / status).</summary>
        public string Summary { get; init; } = string.Empty;

        /// <summary>Ordered diagnostic steps taken (for logs/tests).</summary>
        public IReadOnlyList<string> Steps { get; init; } = Array.Empty<string>();

        /// <summary>When true, operator must run <c>mkp pair</c> again.</summary>
        public bool RequiresRePair { get; init; }
    }

    /// <summary>
    /// Builds a heal plan from persisted pairing + settings and probe results.
    /// </summary>
    /// <param name="pairingUrl">URL from agent-pairing.json.</param>
    /// <param name="pairingPeer">Peer from agent-pairing.json.</param>
    /// <param name="settingsUrl">URL from settings.json (preferred when set).</param>
    /// <param name="settingsPeer">Peer from settings.json.</param>
    /// <param name="credentialPresent">Whether peer-credential.bin loaded.</param>
    /// <param name="probe">
    /// Probes a URL: returns (tcpOk, mtlsOk). mtlsOk implies tcpOk.
    /// </param>
    /// <param name="alternateLiveUrls">Optional live gRPC endpoints from LAN probe.</param>
    public static Plan BuildPlan(
        string? pairingUrl,
        string? pairingPeer,
        string? settingsUrl,
        string? settingsPeer,
        bool credentialPresent,
        Func<string, (bool TcpOk, bool MtlsOk)> probe,
        IReadOnlyList<string>? alternateLiveUrls = null)
    {
        ArgumentNullException.ThrowIfNull(probe);
        var steps = new List<string>();

        var preferredUrl = FirstNonEmpty(settingsUrl, pairingUrl);
        var preferredPeer = FirstNonEmpty(settingsPeer, pairingPeer, HostFromUrl(preferredUrl));

        if (string.IsNullOrWhiteSpace(preferredUrl))
        {
            steps.Add("no configured remote URL");
            return new Plan
            {
                MarkConnected = false,
                Summary = "Self-heal: no remote endpoint configured.",
                Steps = steps,
            };
        }

        if (!string.IsNullOrWhiteSpace(settingsUrl) &&
            !string.Equals(NormalizeUrl(settingsUrl), NormalizeUrl(pairingUrl), StringComparison.OrdinalIgnoreCase))
        {
            steps.Add($"prefer settings URL over pairing ({settingsUrl})");
        }

        if (!credentialPresent)
        {
            steps.Add("peer-credential.bin missing");
            return new Plan
            {
                RemoteGrpcUrl = preferredUrl,
                RemotePeer = preferredPeer,
                MarkConnected = false,
                RequiresRePair = true,
                Summary = "Self-heal: no peer credential on disk; run mkp pair.",
                Steps = steps,
            };
        }

        steps.Add($"probe preferred {preferredUrl}");
        var (tcpOk, mtlsOk) = probe(preferredUrl!);
        if (mtlsOk)
        {
            steps.Add("mTLS OK on preferred endpoint");
            return new Plan
            {
                RemoteGrpcUrl = preferredUrl,
                RemotePeer = preferredPeer,
                MarkConnected = true,
                Summary = $"Self-heal: device channel OK ({HostFromUrl(preferredUrl)}).",
                Steps = steps,
            };
        }

        if (tcpOk && !mtlsOk)
        {
            steps.Add("TCP open but mTLS failed on preferred (stale cert?)");
            // Try alternates with same credential before demanding re-pair.
        }
        else
        {
            steps.Add("preferred endpoint not reachable");
        }

        if (alternateLiveUrls is { Count: > 0 })
        {
            foreach (var alt in alternateLiveUrls
                         .Where(u => !string.IsNullOrWhiteSpace(u))
                         .Select(u => u.Trim())
                         .Where(u => !string.Equals(NormalizeUrl(u), NormalizeUrl(preferredUrl), StringComparison.OrdinalIgnoreCase))
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                steps.Add($"probe alternate {alt}");
                var (altTcp, altMtls) = probe(alt);
                if (altMtls)
                {
                    steps.Add("mTLS OK on alternate endpoint");
                    return new Plan
                    {
                        RemoteGrpcUrl = alt,
                        RemotePeer = HostFromUrl(alt),
                        MarkConnected = true,
                        Summary = $"Self-heal: recovered via alternate endpoint ({HostFromUrl(alt)}).",
                        Steps = steps,
                    };
                }

                if (altTcp)
                {
                    steps.Add($"alternate TCP open but mTLS failed: {alt}");
                }
            }
        }

        if (tcpOk)
        {
            return new Plan
            {
                RemoteGrpcUrl = preferredUrl,
                RemotePeer = preferredPeer,
                MarkConnected = false,
                RequiresRePair = true,
                Summary = "Self-heal: device reachable but credential rejected; run mkp pair.",
                Steps = steps,
            };
        }

        return new Plan
        {
            RemoteGrpcUrl = preferredUrl,
            RemotePeer = preferredPeer,
            MarkConnected = false,
            RequiresRePair = false,
            Summary = "Self-heal: device offline or filtered; will retry when network is up.",
            Steps = steps,
        };
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();

    private static string? HostFromUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        return Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host)
            ? uri.Host
            : url.Trim();
    }

    private static string? NormalizeUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var t = url.Trim().TrimEnd('/');
        return t;
    }
}
