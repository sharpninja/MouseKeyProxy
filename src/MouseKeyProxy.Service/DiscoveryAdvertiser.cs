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
/// TR-MKP-SEC-001 / FR-MKP-014: broadcasts LAN discovery beacons for:
/// <list type="bullet">
/// <item>Unpaired ToFU pairing (plug-n-play), and/or</item>
/// <item>Folder share availability so agents on either machine can discover and connect.</item>
/// </list>
/// </summary>
public sealed class DiscoveryAdvertiser : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(3);

    private readonly IPairedPeerStore _store;
    private readonly ServicePairingOptions _options;
    private readonly FolderShareOptions _folderShare;
    private readonly ILogger<DiscoveryAdvertiser> _logger;
    private readonly int _grpcPort;

    /// <summary>Creates the advertiser.</summary>
    public DiscoveryAdvertiser(
        IPairedPeerStore store,
        ServicePairingOptions options,
        FolderShareOptions folderShare,
        ILogger<DiscoveryAdvertiser> logger)
    {
        _store = store;
        _options = options;
        _folderShare = folderShare;
        _logger = logger;
        _grpcPort = LabTopology.GrpcPort;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var advertisePairing = _options.TrustOnFirstUse;
        var advertiseShare = _folderShare.Enabled;
        if (!advertisePairing && !advertiseShare)
        {
            return;
        }

        using var udp = new UdpClient { EnableBroadcast = true };
        var endpoint = new IPEndPoint(IPAddress.Broadcast, DiscoveryBeacon.DiscoveryPort);
        var peerId = Dns.GetHostName();
        _logger.LogInformation(
            "Discovery advertiser started (pairing={Pairing}, folderShare={Share}).",
            advertisePairing,
            advertiseShare);

        while (!stoppingToken.IsCancellationRequested)
        {
            var pairingAvailable = advertisePairing && !_store.HasPairedPeer();
            var shareAvailable = advertiseShare;
            if (pairingAvailable || shareAvailable)
            {
                var beacon = new DiscoveryBeacon(
                    peerId,
                    LocalIPv4(),
                    _grpcPort,
                    PairingAvailable: pairingAvailable,
                    FolderShareAvailable: shareAvailable,
                    FolderShareName: shareAvailable ? _folderShare.ShareName : string.Empty);
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
            socket.Connect("8.8.8.8", 65530);
            return ((IPEndPoint)socket.LocalEndPoint!).Address.ToString();
        }
        catch (SocketException)
        {
            return Dns.GetHostName();
        }
    }
}
