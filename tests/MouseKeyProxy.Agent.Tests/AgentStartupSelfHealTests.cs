using System;
using System.Collections.Generic;
using MouseKeyProxy.Agent;
using Xunit;

namespace MouseKeyProxy.Agent.Tests;

/// <summary>
/// Startup self-heal planning: settings URL preference, credential/mTLS recovery, alternate hosts.
/// </summary>
public class AgentStartupSelfHealTests
{
    /// <summary>Settings remote URL wins over stale pairing URL when both are set.</summary>
    [Fact]
    public void BuildPlan_PrefersSettingsUrl_WhenMtlsOk()
    {
        var plan = AgentStartupSelfHeal.BuildPlan(
            pairingUrl: "https://192.168.1.77:50051",
            pairingPeer: "192.168.1.77",
            settingsUrl: "https://192.168.1.133:50051",
            settingsPeer: "192.168.1.133",
            credentialPresent: true,
            probe: url => url.Contains("133") ? (true, true) : (true, false));

        Assert.True(plan.MarkConnected);
        Assert.Equal("https://192.168.1.133:50051", plan.RemoteGrpcUrl);
        Assert.Contains(plan.Steps, s => s.Contains("prefer settings", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Missing credential forces re-pair even if a URL is configured.</summary>
    [Fact]
    public void BuildPlan_MissingCredential_RequiresRePair()
    {
        var plan = AgentStartupSelfHeal.BuildPlan(
            pairingUrl: "https://192.168.1.133:50051",
            pairingPeer: "pi",
            settingsUrl: null,
            settingsPeer: null,
            credentialPresent: false,
            probe: _ => throw new InvalidOperationException("must not probe without credential"));

        Assert.False(plan.MarkConnected);
        Assert.True(plan.RequiresRePair);
        Assert.Contains("mkp pair", plan.Summary, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>TCP up but mTLS fail → re-pair; alternates tried first.</summary>
    [Fact]
    public void BuildPlan_MtlsFail_ThenAlternateSucceeds()
    {
        var plan = AgentStartupSelfHeal.BuildPlan(
            pairingUrl: "https://192.168.1.77:50051",
            pairingPeer: "desktop",
            settingsUrl: "https://192.168.1.77:50051",
            settingsPeer: "desktop",
            credentialPresent: true,
            probe: url => url.Contains("133") ? (true, true) : (true, false),
            alternateLiveUrls: new[] { "https://192.168.1.133:50051" });

        Assert.True(plan.MarkConnected);
        Assert.Equal("https://192.168.1.133:50051", plan.RemoteGrpcUrl);
        Assert.Contains(plan.Steps, s => s.Contains("alternate", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>TCP open + mTLS fail on all candidates → requires re-pair.</summary>
    [Fact]
    public void BuildPlan_AllMtlsFail_RequiresRePair()
    {
        var plan = AgentStartupSelfHeal.BuildPlan(
            pairingUrl: "https://192.168.1.133:50051",
            pairingPeer: "pi",
            settingsUrl: null,
            settingsPeer: null,
            credentialPresent: true,
            probe: _ => (true, false),
            alternateLiveUrls: new List<string> { "https://192.168.1.10:50051" });

        Assert.False(plan.MarkConnected);
        Assert.True(plan.RequiresRePair);
        Assert.Contains("credential rejected", plan.Summary, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Unreachable preferred endpoint without alternates → offline, not re-pair.</summary>
    [Fact]
    public void BuildPlan_Offline_DoesNotRequireRePair()
    {
        var plan = AgentStartupSelfHeal.BuildPlan(
            pairingUrl: "https://192.168.1.133:50051",
            pairingPeer: "pi",
            settingsUrl: null,
            settingsPeer: null,
            credentialPresent: true,
            probe: _ => (false, false));

        Assert.False(plan.MarkConnected);
        Assert.False(plan.RequiresRePair);
        Assert.Contains("offline", plan.Summary, StringComparison.OrdinalIgnoreCase);
    }
}
