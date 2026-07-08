using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using MouseKeyProxy.Commands;
using MouseKeyProxy.Common;
using Xunit;

namespace MouseKeyProxy.Commands.Tests;

/// <summary>
/// TR-MKP-SEC-001: verifies DiscoveryFinder receives and parses a real UDP discovery beacon over
/// loopback and returns the advertising device.
/// </summary>
public class DiscoveryFinderTests
{
    private static int FreeUdpPort()
    {
        using var probe = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)probe.Client.LocalEndPoint!).Port;
    }

    /// <summary>A beacon sent to the discovery port is received, parsed, and returned.</summary>
    [Fact]
    [Trait("Category", "Discovery")]
    public async Task ListenAsync_ReceivesAdvertisedBeacon()
    {
        var port = FreeUdpPort();
        var beacon = new DiscoveryBeacon("mkp-hid-pi", "127.0.0.1", 50051, PairingAvailable: true);

        var listen = DiscoveryFinder.ListenAsync(TimeSpan.FromSeconds(3), port);

        // Give the listener a moment to bind, then send the beacon a few times over loopback.
        await Task.Delay(250);
        using (var sender = new UdpClient())
        {
            var bytes = beacon.ToBytes();
            var target = new IPEndPoint(IPAddress.Loopback, port);
            for (var i = 0; i < 3; i++)
            {
                await sender.SendAsync(bytes, bytes.Length, target);
                await Task.Delay(100);
            }
        }

        var found = await listen;

        var one = Assert.Single(found);
        Assert.Equal("mkp-hid-pi", one.PeerId);
        Assert.Equal(50051, one.GrpcPort);
        Assert.True(one.PairingAvailable);
    }

    /// <summary>With no beacons, the listen window returns an empty list rather than hanging.</summary>
    [Fact]
    [Trait("Category", "Discovery")]
    public async Task ListenAsync_NoBeacons_ReturnsEmpty()
    {
        var port = FreeUdpPort();
        var found = await DiscoveryFinder.ListenAsync(TimeSpan.FromMilliseconds(400), port);
        Assert.Empty(found);
    }
}
