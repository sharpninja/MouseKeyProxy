using MouseKeyProxy.Common;
using Xunit;

namespace MouseKeyProxy.Common.Tests;

/// <summary>
/// TEST-MKP-038 / TEST-MKP-045 / FR-MKP-014 / FR-MKP-016:
/// only UsbConnectedPc and PairedHost IPs may access folder share / SMB.
/// </summary>
public class ShareAccessAllowlistTests
{
    /// <summary>Allowlist contains only the two role IPs; third IP is denied.</summary>
    [Fact]
    public void OnlyRoleIpsAreAllowed()
    {
        var list = new ShareAccessAllowlist();
        list.SetPeer("desktop", PeerShareRole.UsbConnectedPc, "192.168.1.10");
        list.SetPeer("legion", PeerShareRole.PairedHost, "192.168.1.20");

        Assert.True(list.IsIpAllowed("192.168.1.10"));
        Assert.True(list.IsIpAllowed("192.168.1.20"));
        Assert.False(list.IsIpAllowed("192.168.1.99"));
        Assert.False(list.IsIpAllowed("10.0.0.1"));

        var ips = list.GetAllowedIps();
        Assert.Equal(2, ips.Count);
        Assert.Contains("192.168.1.10", ips);
        Assert.Contains("192.168.1.20", ips);
    }

    /// <summary>Revoking one peer removes its IP; the other remains.</summary>
    [Fact]
    public void RemovePeer_DropsOnlyThatIp()
    {
        var list = new ShareAccessAllowlist();
        list.SetPeer("desktop", PeerShareRole.UsbConnectedPc, "192.168.1.10");
        list.SetPeer("legion", PeerShareRole.PairedHost, "192.168.1.20");
        list.RemovePeer("legion");

        Assert.True(list.IsIpAllowed("192.168.1.10"));
        Assert.False(list.IsIpAllowed("192.168.1.20"));
    }

    /// <summary>Updating peer IP replaces previous allow entry for that peer.</summary>
    [Fact]
    public void SetPeer_UpdatesIpForSamePeer()
    {
        var list = new ShareAccessAllowlist();
        list.SetPeer("desktop", PeerShareRole.UsbConnectedPc, "192.168.1.10");
        list.SetPeer("desktop", PeerShareRole.UsbConnectedPc, "192.168.1.11");

        Assert.False(list.IsIpAllowed("192.168.1.10"));
        Assert.True(list.IsIpAllowed("192.168.1.11"));
    }

    /// <summary>Null or empty IP is ignored for allow checks (peer registered but no IP yet).</summary>
    [Fact]
    public void EmptyIp_DoesNotAllowWildcard()
    {
        var list = new ShareAccessAllowlist();
        list.SetPeer("desktop", PeerShareRole.UsbConnectedPc, null);
        Assert.False(list.IsIpAllowed("192.168.1.10"));
        Assert.Empty(list.GetAllowedIps());
    }

    /// <summary>
    /// TR-MKP-XFER-004: IPv4-mapped IPv6 forms from gRPC dual-stack match the learned IPv4 allow entry.
    /// Operators never enter IPs; ObservePeerIp records last-seen address of a paired peer.
    /// </summary>
    [Fact]
    public void ObservePeerIp_AndMappedIpv6_MatchPairedHost()
    {
        var list = new ShareAccessAllowlist();
        list.ObservePeerIp("control-host", "::ffff:192.168.1.127");

        Assert.True(list.IsIpAllowed("192.168.1.127"));
        Assert.True(list.IsIpAllowed("::ffff:192.168.1.127"));
        Assert.False(list.IsIpAllowed("192.168.1.99"));
        Assert.Contains("192.168.1.127", list.GetAllowedIps());
    }

    /// <summary>ObservePeerIp preserves role when peer was already registered at Pair time.</summary>
    [Fact]
    public void ObservePeerIp_PreservesExistingRole()
    {
        var list = new ShareAccessAllowlist();
        list.SetPeer("usb-pc", PeerShareRole.UsbConnectedPc, "10.0.0.1");
        list.ObservePeerIp("usb-pc", "10.0.0.2");

        Assert.False(list.IsIpAllowed("10.0.0.1"));
        Assert.True(list.IsIpAllowed("10.0.0.2"));
    }
}
