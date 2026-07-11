using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;

namespace MouseKeyProxy.Service.Pairing;

/// <summary>TR-MKP-SEC-001: a peer that has completed pairing, keyed by its client-cert thumbprint.</summary>
/// <param name="PeerId">The peer's logical id.</param>
/// <param name="CertThumbprint">The peer's issued client-certificate thumbprint (authorization key).</param>
/// <param name="PairedUtc">When the peer paired.</param>
/// <param name="LastSeenUtc">When the peer was last seen on an authorized call.</param>
/// <param name="Revoked">Whether the pairing has been revoked.</param>
public sealed record PairedPeer(
    string PeerId,
    string CertThumbprint,
    DateTimeOffset PairedUtc,
    DateTimeOffset LastSeenUtc,
    bool Revoked);

/// <summary>
/// TR-MKP-SEC-001: service-owned pairing authority. Issues time-bound, single-use pairing codes,
/// registers paired peers by client-cert thumbprint, and answers authorization/revocation queries
/// for the gRPC authorization interceptor.
/// </summary>
public interface IPairedPeerStore
{
    /// <summary>Mints a time-bound, single-use pairing code.</summary>
    /// <param name="ttl">How long the code is valid.</param>
    /// <returns>The pairing code to present to the peer operator.</returns>
    string IssuePairingCode(TimeSpan ttl);

    /// <summary>Validates and consumes a pairing code (single use). Returns false if unknown or expired.</summary>
    /// <param name="code">The code presented during pairing.</param>
    /// <returns>True if the code was valid and is now consumed.</returns>
    bool TryConsumePairingCode(string code);

    /// <summary>Registers (or re-registers) a paired peer by its issued client-cert thumbprint.</summary>
    /// <param name="peerId">The peer's logical id.</param>
    /// <param name="certThumbprint">The issued client-certificate thumbprint.</param>
    /// <returns>The stored peer record.</returns>
    PairedPeer RegisterPeer(string peerId, string certThumbprint);

    /// <summary>Finds a paired peer by client-cert thumbprint, or null.</summary>
    /// <param name="certThumbprint">The presented client-cert thumbprint.</param>
    /// <returns>The peer, or null when unknown.</returns>
    PairedPeer? FindByThumbprint(string certThumbprint);

    /// <summary>Finds a paired peer by peer id, or null.</summary>
    /// <param name="peerId">The peer id.</param>
    /// <returns>The peer, or null when unknown.</returns>
    PairedPeer? FindByPeerId(string peerId);

    /// <summary>Revokes a peer's pairing.</summary>
    /// <param name="peerId">The peer id to revoke.</param>
    /// <returns>True if a peer was revoked.</returns>
    bool Revoke(string peerId);

    /// <summary>Revokes every registered peer (device pairing reset / re-open ToFU).</summary>
    /// <returns>Number of peers newly marked revoked.</returns>
    int RevokeAll();

    /// <summary>True when a peer with the given cert thumbprint is paired and not revoked.</summary>
    /// <param name="certThumbprint">The presented client-cert thumbprint.</param>
    /// <returns>Whether the caller is authorized.</returns>
    bool IsAuthorized(string certThumbprint);

    /// <summary>True when at least one non-revoked peer is paired (drives the trust-on-first-use gate).</summary>
    /// <returns>Whether any peer is currently paired.</returns>
    bool HasPairedPeer();

    /// <summary>Snapshot of all registered peers (including revoked), for host/intro selection.</summary>
    /// <returns>Peer records.</returns>
    IReadOnlyList<PairedPeer> ExportPeers();
}

/// <summary>
/// TR-MKP-SEC-001: in-memory <see cref="IPairedPeerStore"/> with a controllable clock. Persistence
/// of paired peers (DPAPI on Windows, restricted-permission file on Linux) is layered on separately.
/// </summary>
public sealed class PairedPeerStore : IPairedPeerStore
{
    // Unambiguous alphabet (no 0/O/1/I) for operator-entered codes.
    private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int CodeLength = 8;

    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private readonly Dictionary<string, DateTimeOffset> _codes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PairedPeer> _byThumbprint = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PairedPeer> _byPeerId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Action<IReadOnlyList<PairedPeer>>? _onChanged;

    /// <summary>Creates the store with the given time source (defaults to the system clock).</summary>
    /// <param name="timeProvider">Clock used for code expiry and timestamps.</param>
    public PairedPeerStore(TimeProvider? timeProvider = null) : this(timeProvider, null, null)
    {
    }

    /// <summary>
    /// TR-MKP-SEC-001: creates the store seeded with persisted peers and a change callback so pairings
    /// survive restarts. The callback is invoked with the current peer snapshot after any registration
    /// or revocation.
    /// </summary>
    /// <param name="timeProvider">Clock used for code expiry and timestamps.</param>
    /// <param name="initialPeers">Peers loaded from persistence (seeds the store).</param>
    /// <param name="onChanged">Invoked with the peer snapshot after register/revoke so the caller can persist.</param>
    public PairedPeerStore(TimeProvider? timeProvider, IEnumerable<PairedPeer>? initialPeers, Action<IReadOnlyList<PairedPeer>>? onChanged)
    {
        _time = timeProvider ?? TimeProvider.System;
        _onChanged = onChanged;
        if (initialPeers is not null)
        {
            foreach (var peer in initialPeers)
            {
                _byThumbprint[peer.CertThumbprint] = peer;
                _byPeerId[peer.PeerId] = peer;
            }
        }
    }

    /// <summary>Snapshot of all paired peers (for persistence).</summary>
    /// <returns>The current peer records.</returns>
    public IReadOnlyList<PairedPeer> ExportPeers()
    {
        lock (_gate)
        {
            return _byPeerId.Values.ToList();
        }
    }

    /// <summary>True when at least one non-revoked peer is paired (used by the boot-time pairing check).</summary>
    public bool HasPairedPeer()
    {
        lock (_gate)
        {
            return _byPeerId.Values.Any(p => !p.Revoked);
        }
    }

    /// <inheritdoc />
    public string IssuePairingCode(TimeSpan ttl)
    {
        var code = RandomNumberGenerator.GetString(CodeAlphabet, CodeLength);
        lock (_gate)
        {
            _codes[code] = _time.GetUtcNow() + ttl;
        }

        return code;
    }

    /// <inheritdoc />
    public bool TryConsumePairingCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        lock (_gate)
        {
            if (!_codes.TryGetValue(code, out var expiry))
            {
                return false;
            }

            _codes.Remove(code); // single-use: consume regardless of expiry outcome
            return expiry >= _time.GetUtcNow();
        }
    }

    /// <inheritdoc />
    public PairedPeer RegisterPeer(string peerId, string certThumbprint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(peerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(certThumbprint);

        var now = _time.GetUtcNow();
        var peer = new PairedPeer(peerId, certThumbprint, now, now, Revoked: false);
        lock (_gate)
        {
            _byThumbprint[certThumbprint] = peer;
            _byPeerId[peerId] = peer;
        }

        NotifyChanged();
        return peer;
    }

    private void NotifyChanged() => _onChanged?.Invoke(ExportPeers());

    /// <inheritdoc />
    public PairedPeer? FindByThumbprint(string certThumbprint)
    {
        lock (_gate)
        {
            return _byThumbprint.TryGetValue(certThumbprint, out var peer) ? peer : null;
        }
    }

    /// <inheritdoc />
    public PairedPeer? FindByPeerId(string peerId)
    {
        lock (_gate)
        {
            return _byPeerId.TryGetValue(peerId, out var peer) ? peer : null;
        }
    }

    /// <inheritdoc />
    public bool Revoke(string peerId)
    {
        lock (_gate)
        {
            if (!_byPeerId.TryGetValue(peerId, out var peer))
            {
                return false;
            }

            if (peer.Revoked)
            {
                return false;
            }

            var revoked = peer with { Revoked = true };
            _byPeerId[peerId] = revoked;
            _byThumbprint[peer.CertThumbprint] = revoked;
        }

        NotifyChanged();
        return true;
    }

    /// <inheritdoc />
    public int RevokeAll()
    {
        var count = 0;
        lock (_gate)
        {
            foreach (var peerId in _byPeerId.Keys.ToList())
            {
                var peer = _byPeerId[peerId];
                if (peer.Revoked)
                {
                    continue;
                }

                var revoked = peer with { Revoked = true };
                _byPeerId[peerId] = revoked;
                _byThumbprint[peer.CertThumbprint] = revoked;
                count++;
            }
        }

        if (count > 0)
        {
            NotifyChanged();
        }

        return count;
    }

    /// <inheritdoc />
    public bool IsAuthorized(string certThumbprint)
    {
        if (string.IsNullOrWhiteSpace(certThumbprint))
        {
            return false;
        }

        lock (_gate)
        {
            return _byThumbprint.TryGetValue(certThumbprint, out var peer) && !peer.Revoked;
        }
    }
}
