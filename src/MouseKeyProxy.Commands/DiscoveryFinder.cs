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
/// TR-MKP-SEC-001 / FR-MKP-014: peer/REPL/agent discovery. Listens for UDP beacons advertising
/// pairing availability and/or folder shares.
/// </summary>
public static class DiscoveryFinder
{
    /// <summary>Filters which beacons are returned from a listen window.</summary>
    public enum DiscoveryFilter
    {
        /// <summary>Only unpaired ToFU pairing advertisers.</summary>
        PairingAvailable = 0,

        /// <summary>Only devices advertising a folder share.</summary>
        FolderShareAvailable = 1,

        /// <summary>Any beacon (pairing and/or share).</summary>
        Any = 2,
    }

    /// <summary>
    /// Listens for discovery beacons for up to <paramref name="timeout"/> and returns distinct
    /// devices (deduplicated by peer id).
    /// </summary>
    /// <param name="timeout">How long to listen.</param>
    /// <param name="port">The UDP discovery port (defaults to the well-known port).</param>
    /// <param name="filter">Which advertisers to keep.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The discovered beacons.</returns>
    public static async Task<IReadOnlyList<DiscoveryBeacon>> ListenAsync(
        TimeSpan timeout,
        int port = DiscoveryBeacon.DiscoveryPort,
        DiscoveryFilter filter = DiscoveryFilter.PairingAvailable,
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
                if (!DiscoveryBeacon.TryParse(result.Buffer, out var beacon) || beacon is null)
                {
                    continue;
                }

                if (!Matches(beacon, filter))
                {
                    continue;
                }

                found[beacon.PeerId] = beacon;
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

    private static bool Matches(DiscoveryBeacon beacon, DiscoveryFilter filter)
        => filter switch
        {
            DiscoveryFilter.PairingAvailable => beacon.PairingAvailable,
            DiscoveryFilter.FolderShareAvailable => beacon.FolderShareAvailable,
            _ => beacon.PairingAvailable || beacon.FolderShareAvailable,
        };
}
