using System.Text;
using MouseKeyProxy.Common;
using Xunit;

namespace MouseKeyProxy.Common.Tests;

/// <summary>
/// TR-MKP-SEC-001: verifies the LAN discovery beacon a boot-unpaired Pi broadcasts so a peer can find
/// it and ToFU-pair. Round-trips its fields and rejects non-beacon / garbage datagrams.
/// </summary>
public class DiscoveryBeaconTests
{
    /// <summary>A beacon serializes and parses back with all fields intact.</summary>
    [Fact]
    [Trait("Category", "Discovery")]
    public void Beacon_RoundTrips()
    {
        var beacon = new DiscoveryBeacon("mkp-hid-pi", "192.168.1.50", 50051, PairingAvailable: true);

        var bytes = beacon.ToBytes();
        Assert.True(DiscoveryBeacon.TryParse(bytes, out var parsed));
        Assert.NotNull(parsed);
        Assert.Equal("mkp-hid-pi", parsed!.PeerId);
        Assert.Equal("192.168.1.50", parsed.Host);
        Assert.Equal(50051, parsed.GrpcPort);
        Assert.True(parsed.PairingAvailable);
    }

    /// <summary>A datagram without the beacon magic header is rejected.</summary>
    [Fact]
    [Trait("Category", "Discovery")]
    public void NonBeaconDatagram_IsRejected()
    {
        var noise = Encoding.UTF8.GetBytes("hello world not a beacon");
        Assert.False(DiscoveryBeacon.TryParse(noise, out var parsed));
        Assert.Null(parsed);
    }

    /// <summary>The magic header with garbage payload is rejected rather than throwing.</summary>
    [Fact]
    [Trait("Category", "Discovery")]
    public void MagicWithGarbagePayload_IsRejected()
    {
        var bytes = Encoding.UTF8.GetBytes("MKPDISCO1{not valid json");
        Assert.False(DiscoveryBeacon.TryParse(bytes, out var parsed));
        Assert.Null(parsed);
    }

    /// <summary>An empty datagram is rejected.</summary>
    [Fact]
    [Trait("Category", "Discovery")]
    public void EmptyDatagram_IsRejected()
    {
        Assert.False(DiscoveryBeacon.TryParse(System.Array.Empty<byte>(), out var parsed));
        Assert.Null(parsed);
    }
}
