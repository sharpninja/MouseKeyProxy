using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using MouseKeyProxy.Commands;
using MouseKeyProxy.Common;
using Xunit;

namespace MouseKeyProxy.Commands.Tests;

/// <summary>
/// Live gRPC host probing used when <c>mkp pair discover</c> finds no ToFU advertisers:
/// candidate collection, injectable probe, and empty-discover operator guidance text.
/// </summary>
public class LiveGrpcEndpointFinderTests
{
    /// <summary>
    /// BuildCandidates expands a /24 unicast address into subnet hosts and merges extra hosts.
    /// </summary>
    [Fact]
    [Trait("Category", "Discovery")]
    public void BuildCandidates_ExpandsSlash24_AndMergesExtras()
    {
        var local = new[] { (IPAddress.Parse("192.168.1.127"), 24) };
        var candidates = LiveGrpcEndpointFinder.BuildCandidates(
            local,
            extraHosts: new[] { "device.local", "192.168.1.77", "  ", "192.168.1.127" });

        Assert.Contains("192.168.1.127", candidates);
        Assert.Contains("192.168.1.77", candidates);
        Assert.Contains("device.local", candidates);
        Assert.Contains("192.168.1.1", candidates);
        Assert.Contains("192.168.1.254", candidates);
        Assert.DoesNotContain("192.168.1.0", candidates);
        Assert.DoesNotContain("192.168.1.255", candidates);
        // Deduped: local IP appears once even if also listed as extra.
        Assert.Equal(1, candidates.Count(h => h.Equals("192.168.1.127", StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Link-local and loopback unicast entries do not explode into a full /16 scan; loopback is kept as-is.
    /// </summary>
    [Fact]
    [Trait("Category", "Discovery")]
    public void BuildCandidates_SkipsLinkLocalSubnetExpansion_KeepsLoopback()
    {
        var local = new[]
        {
            (IPAddress.Parse("169.254.1.2"), 16),
            (IPAddress.Parse("127.0.0.1"), 8),
        };
        var candidates = LiveGrpcEndpointFinder.BuildCandidates(local);

        Assert.Contains("127.0.0.1", candidates);
        Assert.DoesNotContain("169.254.1.2", candidates);
        Assert.DoesNotContain(candidates, h => h.StartsWith("169.254.", StringComparison.Ordinal) && h != "169.254.1.2");
        // Full 127.0.0.0/8 must not be enumerated.
        Assert.DoesNotContain("127.0.0.2", candidates);
    }

    /// <summary>
    /// FindOpenEndpointsAsync returns only hosts the probe reports open, as https URLs on the gRPC port.
    /// </summary>
    [Fact]
    [Trait("Category", "Discovery")]
    public async Task FindOpenEndpointsAsync_UsesProbe_AndFormatsUrls()
    {
        var open = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "192.168.1.77", "127.0.0.1" };
        Task<bool> Probe(string host, int port, TimeSpan timeout, CancellationToken ct)
        {
            Assert.Equal(LabTopology.GrpcPort, port);
            return Task.FromResult(open.Contains(host));
        }

        var found = await LiveGrpcEndpointFinder.FindOpenEndpointsAsync(
            new[] { "192.168.1.77", "192.168.1.10", "127.0.0.1" },
            port: LabTopology.GrpcPort,
            connectTimeout: TimeSpan.FromMilliseconds(50),
            probe: Probe);

        Assert.Equal(2, found.Count);
        Assert.Contains("https://192.168.1.77:50051", found);
        Assert.Contains("https://127.0.0.1:50051", found);
    }

    /// <summary>
    /// Empty-discover guidance lists live endpoints and the mint + pair one-liner path when ToFU is closed.
    /// </summary>
    [Fact]
    [Trait("Category", "Discovery")]
    public void FormatEmptyDiscoverGuidance_WithLiveHosts_IncludesMintPairHint()
    {
        var lines = LiveGrpcEndpointFinder.FormatEmptyDiscoverGuidance(
            new[] { "https://192.168.1.77:50051", "https://127.0.0.1:50051" });

        var text = string.Join(Environment.NewLine, lines);
        Assert.Contains("No unpaired devices found", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("https://192.168.1.77:50051", text);
        Assert.Contains("https://127.0.0.1:50051", text);
        Assert.Contains("mkp pair mint", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("mkp pair <code>", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MKP_GRPC", text);
    }

    /// <summary>
    /// When no live gRPC endpoints are found, guidance still explains ToFU and code-based repair.
    /// </summary>
    [Fact]
    [Trait("Category", "Discovery")]
    public void FormatEmptyDiscoverGuidance_NoLiveHosts_StillHintsCodePair()
    {
        var lines = LiveGrpcEndpointFinder.FormatEmptyDiscoverGuidance(Array.Empty<string>());
        var text = string.Join(Environment.NewLine, lines);
        Assert.Contains("No unpaired devices found", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("No live gRPC endpoints", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("mkp pair mint", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("mkp pair <code>", text, StringComparison.OrdinalIgnoreCase);
    }
}
