using System;
using System.Collections.Generic;
using System.Linq;

namespace MouseKeyProxy.Common;

/// <summary>
/// FR-MKP-014 / FR-MKP-016 / TR-MKP-XFER-004: role of a paired peer for share/SMB IP allowlisting.
/// Only these roles may access the folder share and SMB endpoints.
/// </summary>
public enum PeerShareRole
{
    /// <summary>PC that completed pairing as the USB-connected operator machine.</summary>
    UsbConnectedPc = 1,

    /// <summary>Additional host that completed code-gated client pairing.</summary>
    PairedHost = 2,
}

/// <summary>
/// Tracks which peer IPs may use folder share gRPC and SMB.
/// Allowlist is derived from pairing records (roles), not open LAN discovery.
/// </summary>
public interface IShareAccessAllowlist
{
    /// <summary>Registers or updates a peer's role and last-known remote IP.</summary>
    /// <param name="peerId">Stable peer identifier from pairing.</param>
    /// <param name="role">UsbConnectedPc or PairedHost.</param>
    /// <param name="ip">Last observed remote IP (may be null until first connect).</param>
    void SetPeer(string peerId, PeerShareRole role, string? ip);

    /// <summary>Removes a peer (revoke/unpair).</summary>
    /// <param name="peerId">Peer to remove.</param>
    void RemovePeer(string peerId);

    /// <summary>Returns true when <paramref name="ip"/> is currently allowlisted.</summary>
    /// <param name="ip">Client IP (IPv4/IPv6 string as observed by the service).</param>
    bool IsIpAllowed(string? ip);

    /// <summary>Snapshot of allowed IP addresses (non-empty only).</summary>
    IReadOnlyList<string> GetAllowedIps();
}

/// <summary>In-memory <see cref="IShareAccessAllowlist"/> for Service DI and unit tests.</summary>
public sealed class ShareAccessAllowlist : IShareAccessAllowlist
{
    private readonly object _gate = new();
    private readonly Dictionary<string, (PeerShareRole Role, string? Ip)> _peers =
        new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public void SetPeer(string peerId, PeerShareRole role, string? ip)
    {
        if (string.IsNullOrWhiteSpace(peerId))
        {
            throw new ArgumentException("Peer id is required.", nameof(peerId));
        }

        lock (_gate)
        {
            _peers[peerId.Trim()] = (role, NormalizeIp(ip));
        }
    }

    /// <inheritdoc />
    public void RemovePeer(string peerId)
    {
        if (string.IsNullOrWhiteSpace(peerId))
        {
            return;
        }

        lock (_gate)
        {
            _peers.Remove(peerId.Trim());
        }
    }

    /// <inheritdoc />
    public bool IsIpAllowed(string? ip)
    {
        var normalized = NormalizeIp(ip);
        if (normalized is null)
        {
            return false;
        }

        lock (_gate)
        {
            return _peers.Values.Any(p =>
                p.Ip is not null &&
                string.Equals(p.Ip, normalized, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetAllowedIps()
    {
        lock (_gate)
        {
            return _peers.Values
                .Select(p => p.Ip)
                .Where(ip => ip is not null)
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(ip => ip, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    private static string? NormalizeIp(string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip))
        {
            return null;
        }

        var t = ip.Trim();
        // Strip IPv6 zone / brackets if present: "[::1]" or "::1%eth0"
        if (t.StartsWith('[') && t.Contains(']', StringComparison.Ordinal))
        {
            var end = t.IndexOf(']', StringComparison.Ordinal);
            t = t[1..end];
        }

        var zone = t.IndexOf('%', StringComparison.Ordinal);
        if (zone >= 0)
        {
            t = t[..zone];
        }

        return t.Length == 0 ? null : t;
    }
}
