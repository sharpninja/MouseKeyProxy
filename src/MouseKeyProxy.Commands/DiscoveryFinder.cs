using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using MouseKeyProxy.Common;

namespace MouseKeyProxy.Commands;

/// <summary>
/// TR-MKP-SEC-001: the peer/REPL side of plug-n-play discovery. Listens for UDP discovery beacons from
/// unpaired devices for a bounded window and returns the distinct devices advertising availability.
/// </summary>
public static class DiscoveryFinder
{
    /// <summary>
    /// Listens for discovery beacons for up to <paramref name="timeout"/> and returns the distinct
    /// pairing-available devices seen (deduplicated by peer id).
    /// </summary>
    /// <param name="timeout">How long to listen.</param>
    /// <param name="port">The UDP discovery port (defaults to the well-known port).</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The discovered beacons.</returns>
    public static async Task<IReadOnlyList<DiscoveryBeacon>> ListenAsync(
        TimeSpan timeout,
        int port = DiscoveryBeacon.DiscoveryPort,
        CancellationToken cancellationToken = default)
    {
        var found = new Dictionary<string, DiscoveryBeacon>(StringComparer.OrdinalIgnoreCase);

        using var udp = new UdpClient();
        udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, port));

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(timeout);

        try
        {
            while (!linked.IsCancellationRequested)
            {
                var result = await udp.ReceiveAsync(linked.Token).ConfigureAwait(false);
                if (DiscoveryBeacon.TryParse(result.Buffer, out var beacon) && beacon!.PairingAvailable)
                {
                    found[beacon.PeerId] = beacon;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Listening window elapsed.
        }
        catch (SocketException)
        {
            // Socket closed on cancellation.
        }

        return found.Values.ToList();
    }
}
