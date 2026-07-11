using System;
using System.Text;
using System.Text.Json;

namespace MouseKeyProxy.Common;

/// <summary>
/// TR-MKP-SEC-001: the LAN discovery beacon an unpaired device broadcasts (UDP) so a peer can find it
/// and complete a trust-on-first-use pairing without a manually relayed code. The magic header lets
/// receivers reject unrelated datagrams cheaply.
/// </summary>
/// <param name="PeerId">The advertising device's peer id / hostname.</param>
/// <param name="Host">The device's reachable host/IP for the gRPC endpoint.</param>
/// <param name="GrpcPort">The device's gRPC (mTLS) port.</param>
/// <param name="PairingAvailable">True while the device is unpaired and accepting a ToFU pairing.</param>
/// <param name="FolderShareAvailable">True when the device is serving a folder share over gRPC.</param>
/// <param name="FolderShareName">Display name of the folder share when available.</param>
public sealed record DiscoveryBeacon(
    string PeerId,
    string Host,
    int GrpcPort,
    bool PairingAvailable,
    bool FolderShareAvailable = false,
    string FolderShareName = "")
{
    /// <summary>The well-known UDP port devices broadcast discovery beacons on.</summary>
    public const int DiscoveryPort = 50052;

    private const string Magic = "MKPDISCO1";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Serializes the beacon to a datagram (magic header + JSON).</summary>
    /// <returns>The datagram bytes.</returns>
    public byte[] ToBytes() => Encoding.UTF8.GetBytes(Magic + JsonSerializer.Serialize(this, JsonOptions));

    /// <summary>Parses a datagram into a beacon, returning false for non-beacon or malformed data.</summary>
    /// <param name="data">The received datagram.</param>
    /// <param name="beacon">The parsed beacon when successful.</param>
    /// <returns>True when the datagram is a valid beacon.</returns>
    public static bool TryParse(byte[]? data, out DiscoveryBeacon? beacon)
    {
        beacon = null;
        if (data is null || data.Length <= Magic.Length)
        {
            return false;
        }

        string text;
        try
        {
            text = Encoding.UTF8.GetString(data);
        }
        catch (ArgumentException)
        {
            return false;
        }

        if (!text.StartsWith(Magic, StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            beacon = JsonSerializer.Deserialize<DiscoveryBeacon>(text[Magic.Length..], JsonOptions);
        }
        catch (JsonException)
        {
            return false;
        }

        return beacon is not null;
    }
}
