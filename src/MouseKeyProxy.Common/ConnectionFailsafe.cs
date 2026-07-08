using System;

namespace MouseKeyProxy.Common;

/// <summary>
/// TR-MKP-RELI-001 / FR-MKP-001: connection safety failsafe. Tracks remote-forwarding liveness so
/// the agent can force-release the cursor clip and held modifiers when the peer goes silent, and give
/// up reconnecting (falling back to local input) after a bounded window. Uses an injectable clock so
/// the deadlines are deterministically testable.
/// </summary>
public sealed class ConnectionFailsafe
{
    /// <summary>Force clip/modifier release when the peer has been silent this long while active.</summary>
    public static readonly TimeSpan ClipReleaseDeadline = TimeSpan.FromSeconds(2);

    /// <summary>Give up reconnecting (fall back to local input) after being disconnected this long.</summary>
    public static readonly TimeSpan ReconnectGiveUp = TimeSpan.FromSeconds(5);

    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private bool _active;
    private DateTimeOffset? _lastHeartbeat;
    private DateTimeOffset? _disconnectedAt;

    /// <summary>Creates the failsafe with the given clock (defaults to the system clock).</summary>
    /// <param name="timeProvider">Clock used for deadline evaluation.</param>
    public ConnectionFailsafe(TimeProvider? timeProvider = null) => _time = timeProvider ?? TimeProvider.System;

    /// <summary>Whether remote forwarding is currently active (a hold is in effect).</summary>
    public bool IsActive
    {
        get { lock (_gate) { return _active; } }
    }

    /// <summary>Marks remote forwarding active and records an initial heartbeat.</summary>
    public void OnActivated()
    {
        lock (_gate)
        {
            _active = true;
            _lastHeartbeat = _time.GetUtcNow();
            _disconnectedAt = null;
        }
    }

    /// <summary>Records a heartbeat / ack from the peer (proof of liveness).</summary>
    public void OnHeartbeat()
    {
        lock (_gate)
        {
            _lastHeartbeat = _time.GetUtcNow();
            _disconnectedAt = null;
        }
    }

    /// <summary>Records that the channel disconnected; starts the reconnect give-up window.</summary>
    public void OnDisconnected()
    {
        lock (_gate)
        {
            _disconnectedAt = _time.GetUtcNow();
        }
    }

    /// <summary>Clears all hold state (an explicit release completed).</summary>
    public void OnReleased()
    {
        lock (_gate)
        {
            _active = false;
            _lastHeartbeat = null;
            _disconnectedAt = null;
        }
    }

    /// <summary>
    /// True when a hold is active and the peer has been silent for at least
    /// <see cref="ClipReleaseDeadline"/> - the clip and held modifiers must be force-released.
    /// </summary>
    public bool ShouldForceRelease()
    {
        lock (_gate)
        {
            return _active
                && _lastHeartbeat is { } heartbeat
                && (_time.GetUtcNow() - heartbeat) >= ClipReleaseDeadline;
        }
    }

    /// <summary>
    /// True when the channel has been disconnected for at least <see cref="ReconnectGiveUp"/> -
    /// reconnect attempts must stop and input falls back to local.
    /// </summary>
    public bool ShouldGiveUpReconnect()
    {
        lock (_gate)
        {
            return _disconnectedAt is { } disconnected
                && (_time.GetUtcNow() - disconnected) >= ReconnectGiveUp;
        }
    }
}
