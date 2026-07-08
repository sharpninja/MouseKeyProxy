using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MouseKeyProxy.Common;
using MouseKeyProxy.Service.Pairing;

namespace MouseKeyProxy.Service;

/// <summary>
/// TR-MKP-SEC-001: broadcasts a LAN discovery beacon while the device is unpaired and trust-on-first-use
/// is enabled, so a peer can find it and ToFU-pair (plug-n-play). Broadcasting stops once any peer is
/// paired. Only active when ToFU is on (i.e. the Pi appliance), a no-op otherwise.
/// </summary>
public sealed class DiscoveryAdvertiser : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(3);

    private readonly IPairedPeerStore _store;
    private readonly ServicePairingOptions _options;
    private readonly ILogger<DiscoveryAdvertiser> _logger;
    private readonly int _grpcPort;

    /// <summary>Creates the advertiser.</summary>
    /// <param name="store">The paired-peer store (drives the unpaired check).</param>
    /// <param name="options">Pairing behavior (advertises only when ToFU is enabled).</param>
    /// <param name="logger">Diagnostics logger.</param>
    public DiscoveryAdvertiser(IPairedPeerStore store, ServicePairingOptions options, ILogger<DiscoveryAdvertiser> logger)
    {
        _store = store;
        _options = options;
        _logger = logger;
        _grpcPort = LabTopology.GrpcPort;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.TrustOnFirstUse)
        {
            return; // discovery advertisement is only for the plug-n-play (ToFU) appliance
        }

        using var udp = new UdpClient { EnableBroadcast = true };
        var endpoint = new IPEndPoint(IPAddress.Broadcast, DiscoveryBeacon.DiscoveryPort);
        var peerId = Dns.GetHostName();
        _logger.LogInformation("Discovery advertiser started (unpaired plug-n-play mode).");

        while (!stoppingToken.IsCancellationRequested)
        {
            if (!_store.HasPairedPeer())
            {
                var beacon = new DiscoveryBeacon(peerId, LocalIPv4(), _grpcPort, PairingAvailable: true);
                var bytes = beacon.ToBytes();
                try
                {
                    await udp.SendAsync(bytes, bytes.Length, endpoint).ConfigureAwait(false);
                }
                catch (SocketException ex)
                {
                    _logger.LogDebug(ex, "Discovery beacon broadcast failed.");
                }
            }

            try
            {
                await Task.Delay(Interval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private static string LocalIPv4()
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Connect("8.8.8.8", 65530); // no packets sent; picks the route's local endpoint
            return ((IPEndPoint)socket.LocalEndPoint!).Address.ToString();
        }
        catch (SocketException)
        {
            return Dns.GetHostName();
        }
    }
}
