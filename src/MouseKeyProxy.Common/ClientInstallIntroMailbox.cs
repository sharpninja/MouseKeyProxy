using System;
using System.Collections.Generic;
using System.Linq;

namespace MouseKeyProxy.Common;

/// <summary>
/// FR-MKP-026: pending clipboard-client introduction left by MSI bootstrap for the control host.
/// </summary>
/// <param name="ClientPeerId">USB client machine id.</param>
/// <param name="ClipboardEndpoint">Host-reachable endpoint for clipboard channel (e.g. https://desktop:50051).</param>
/// <param name="ClipboardIntroCode">One-time code the host uses to authenticate the clipboard channel.</param>
/// <param name="CreatedUtc">When the intro was queued.</param>
/// <param name="ExpiresUtc">When the intro expires.</param>
public sealed record ClientInstallIntro(
    string ClientPeerId,
    string ClipboardEndpoint,
    string ClipboardIntroCode,
    DateTimeOffset CreatedUtc,
    DateTimeOffset ExpiresUtc);

/// <summary>Device-side mailbox for USB client clipboard intros (host claim once).</summary>
public interface IClientInstallIntroMailbox
{
    /// <summary>Queues or replaces the pending intro for a client peer.</summary>
    void Queue(ClientInstallIntro intro);

    /// <summary>
    /// Returns non-expired pending intros for the control host without consuming them.
    /// </summary>
    IReadOnlyList<ClientInstallIntro> PeekPending();

    /// <summary>
    /// Claims (removes) a pending intro by client peer id. Returns null if missing/expired.
    /// </summary>
    /// <param name="clientPeerId">Client peer id to claim.</param>
    ClientInstallIntro? Claim(string clientPeerId);
}

/// <summary>Thread-safe in-memory <see cref="IClientInstallIntroMailbox"/>.</summary>
public sealed class ClientInstallIntroMailbox : IClientInstallIntroMailbox
{
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private readonly Dictionary<string, ClientInstallIntro> _byClient =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates a mailbox using the system clock.</summary>
    public ClientInstallIntroMailbox()
        : this(TimeProvider.System)
    {
    }

    /// <summary>Creates a mailbox with an injectable clock (tests).</summary>
    /// <param name="time">Time provider.</param>
    public ClientInstallIntroMailbox(TimeProvider time)
    {
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    /// <inheritdoc />
    public void Queue(ClientInstallIntro intro)
    {
        ArgumentNullException.ThrowIfNull(intro);
        if (string.IsNullOrWhiteSpace(intro.ClientPeerId))
        {
            throw new ArgumentException("Client peer id is required.", nameof(intro));
        }

        if (string.IsNullOrWhiteSpace(intro.ClipboardEndpoint))
        {
            throw new ArgumentException("Clipboard endpoint is required.", nameof(intro));
        }

        if (string.IsNullOrWhiteSpace(intro.ClipboardIntroCode))
        {
            throw new ArgumentException("Clipboard intro code is required.", nameof(intro));
        }

        lock (_gate)
        {
            PurgeExpired_NoLock();
            _byClient[intro.ClientPeerId.Trim()] = intro;
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<ClientInstallIntro> PeekPending()
    {
        lock (_gate)
        {
            PurgeExpired_NoLock();
            return _byClient.Values.OrderBy(i => i.CreatedUtc).ToArray();
        }
    }

    /// <inheritdoc />
    public ClientInstallIntro? Claim(string clientPeerId)
    {
        if (string.IsNullOrWhiteSpace(clientPeerId))
        {
            return null;
        }

        lock (_gate)
        {
            PurgeExpired_NoLock();
            var key = clientPeerId.Trim();
            if (!_byClient.Remove(key, out var intro))
            {
                return null;
            }

            return intro;
        }
    }

    private void PurgeExpired_NoLock()
    {
        var now = _time.GetUtcNow();
        var dead = _byClient.Where(kv => now >= kv.Value.ExpiresUtc).Select(kv => kv.Key).ToArray();
        foreach (var k in dead)
        {
            _byClient.Remove(k);
        }
    }
}
