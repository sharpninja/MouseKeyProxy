using System;
using MouseKeyProxy.Common;
using Xunit;

namespace MouseKeyProxy.Common.Tests;

/// <summary>
/// FR-MKP-027 / TEST: host traffic policy — input only to device; clipboard only to client.
/// </summary>
public class PeerTrafficPolicyTests
{
    /// <summary>Device appliance accepts input and rejects clipboard.</summary>
    [Fact]
    public void DeviceAppliance_InputOnly()
    {
        Assert.True(PeerTrafficPolicy.Allows(PeerEffectRole.DeviceAppliance, PeerTrafficPolicy.EffectKind.Input));
        Assert.False(PeerTrafficPolicy.Allows(PeerEffectRole.DeviceAppliance, PeerTrafficPolicy.EffectKind.Clipboard));
    }

    /// <summary>Clipboard client accepts clipboard and rejects input.</summary>
    [Fact]
    public void ClipboardClient_ClipboardOnly()
    {
        Assert.True(PeerTrafficPolicy.Allows(PeerEffectRole.ClipboardClient, PeerTrafficPolicy.EffectKind.Clipboard));
        Assert.False(PeerTrafficPolicy.Allows(PeerEffectRole.ClipboardClient, PeerTrafficPolicy.EffectKind.Input));
    }

    /// <summary>EnsureAllowed throws for inject to clipboard peer.</summary>
    [Fact]
    public void EnsureAllowed_RejectsInjectToClipboardClient()
    {
        Assert.Throws<InvalidOperationException>(() =>
            PeerTrafficPolicy.EnsureAllowed(
                PeerEffectRole.ClipboardClient,
                PeerTrafficPolicy.EffectKind.Input,
                "payton-desktop"));
    }

    /// <summary>Unknown role allows nothing.</summary>
    [Fact]
    public void Unknown_AllowsNothing()
    {
        Assert.False(PeerTrafficPolicy.Allows(PeerEffectRole.Unknown, PeerTrafficPolicy.EffectKind.Input));
        Assert.False(PeerTrafficPolicy.Allows(PeerEffectRole.Unknown, PeerTrafficPolicy.EffectKind.Clipboard));
    }
}
