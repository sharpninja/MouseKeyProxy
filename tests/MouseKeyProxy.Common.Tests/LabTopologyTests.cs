using System;
using MouseKeyProxy.Common;
using Xunit;

namespace MouseKeyProxy.Common.Tests;

/// <summary>
/// TR-MKP-ORCH-001 / audit CI-truth: verifies LabTopology resolves peers from explicit
/// configuration/overrides, still recognizes the named lab machines, and runs standalone
/// (no throw) on any other machine, so the product is usable off the two developer boxes.
/// Uses the pure overload with injected machine name + overrides (no environment mutation).
/// </summary>
public class LabTopologyTests
{
    /// <summary>Explicit local/remote overrides win regardless of the machine name.</summary>
    [Fact]
    [Trait("Category", "Topology")]
    public void ResolvePeers_Overrides_UseConfiguredPeers()
    {
        var (local, remote) = LabTopology.ResolvePeers("some-random-host", "host-a", "host-b");

        Assert.Equal("host-a", local);
        Assert.Equal("host-b", remote);
    }

    /// <summary>The named lab machines still resolve to the canonical lab pair.</summary>
    [Fact]
    [Trait("Category", "Topology")]
    public void ResolvePeers_LabMachines_ReturnLabPair()
    {
        Assert.Equal((LabTopology.Legion2, LabTopology.Desktop), LabTopology.ResolvePeers("PAYTON-LEGION2", null, null));
        Assert.Equal((LabTopology.Desktop, LabTopology.Legion2), LabTopology.ResolvePeers("payton-desktop", null, null));
    }

    /// <summary>An unknown machine with no overrides runs standalone (self local, empty remote) and does not throw.</summary>
    [Fact]
    [Trait("Category", "Topology")]
    public void ResolvePeers_UnknownMachine_RunsStandalone_NoThrow()
    {
        var (local, remote) = LabTopology.ResolvePeers("random-box-123", null, null);

        Assert.Equal("random-box-123", local);
        Assert.Equal(string.Empty, remote);
    }
}
