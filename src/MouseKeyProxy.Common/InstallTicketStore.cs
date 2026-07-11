using System;
using System.Security.Cryptography;

namespace MouseKeyProxy.Common;

/// <summary>
/// FR-MKP-025 / TR-MKP-SEC-PAIR-001: long-lived install ticket for USB-client MSI pairing
/// when ToFU is already closed by a control host.
/// </summary>
public interface IInstallTicketStore
{
    /// <summary>Issues a new ticket with the given lifetime (clamped).</summary>
    /// <param name="ttl">Desired lifetime; minimum 1 minute, maximum 30 days.</param>
    /// <returns>The ticket string (alphanumeric).</returns>
    string Issue(TimeSpan ttl);

    /// <summary>Returns the active ticket if present and not expired; otherwise null.</summary>
    string? PeekActive();

    /// <summary>
    /// Validates a ticket without consuming multi-use tickets before expiry.
    /// Returns false when missing, wrong, or expired.
    /// </summary>
    /// <param name="ticket">Candidate ticket.</param>
    /// <param name="error">Machine-readable error code when false.</param>
    bool TryValidate(string? ticket, out string error);

    /// <summary>Revokes the active ticket immediately.</summary>
    void Revoke();

    /// <summary>
    /// Seeds a known ticket (e.g. from <c>MKP_INSTALL_TICKET</c> or staged bootstrap JSON).
    /// </summary>
    /// <param name="ticket">Ticket string.</param>
    /// <param name="ttl">Lifetime.</param>
    void Seed(string ticket, TimeSpan ttl);
}

/// <summary>Thread-safe in-memory <see cref="IInstallTicketStore"/>.</summary>
public sealed class InstallTicketStore : IInstallTicketStore
{
    private static readonly TimeSpan MinTtl = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan MaxTtl = TimeSpan.FromDays(30);

    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private string? _ticket;
    private DateTimeOffset _expiresUtc;

    /// <summary>Creates a store using the system clock.</summary>
    public InstallTicketStore()
        : this(TimeProvider.System)
    {
    }

    /// <summary>Creates a store with an injectable clock (tests).</summary>
    /// <param name="time">Time provider.</param>
    public InstallTicketStore(TimeProvider time)
    {
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    /// <inheritdoc />
    public string Issue(TimeSpan ttl)
    {
        if (ttl < MinTtl)
        {
            ttl = MinTtl;
        }

        if (ttl > MaxTtl)
        {
            ttl = MaxTtl;
        }

        // 20 hex chars (~80 bits) from CSPRNG.
        var bytes = new byte[10];
        RandomNumberGenerator.Fill(bytes);
        var ticket = Convert.ToHexString(bytes);

        lock (_gate)
        {
            _ticket = ticket;
            _expiresUtc = _time.GetUtcNow().Add(ttl);
        }

        return ticket;
    }

    /// <inheritdoc />
    public string? PeekActive()
    {
        lock (_gate)
        {
            if (_ticket is null)
            {
                return null;
            }

            if (_time.GetUtcNow() >= _expiresUtc)
            {
                _ticket = null;
                return null;
            }

            return _ticket;
        }
    }

    /// <inheritdoc />
    public bool TryValidate(string? ticket, out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(ticket))
        {
            error = "INSTALL_TICKET_REQUIRED";
            return false;
        }

        var candidate = ticket.Trim();
        lock (_gate)
        {
            if (_ticket is null)
            {
                error = "INSTALL_TICKET_EXPIRED";
                return false;
            }

            if (_time.GetUtcNow() >= _expiresUtc)
            {
                _ticket = null;
                error = "INSTALL_TICKET_EXPIRED";
                return false;
            }

            if (!string.Equals(_ticket, candidate, StringComparison.Ordinal))
            {
                error = "INSTALL_TICKET_INVALID";
                return false;
            }

            return true;
        }
    }

    /// <inheritdoc />
    public void Revoke()
    {
        lock (_gate)
        {
            _ticket = null;
            _expiresUtc = default;
        }
    }

    /// <inheritdoc />
    public void Seed(string ticket, TimeSpan ttl)
    {
        if (string.IsNullOrWhiteSpace(ticket))
        {
            throw new ArgumentException("Ticket is required.", nameof(ticket));
        }

        if (ttl < MinTtl)
        {
            ttl = MinTtl;
        }

        if (ttl > MaxTtl)
        {
            ttl = MaxTtl;
        }

        lock (_gate)
        {
            _ticket = ticket.Trim();
            _expiresUtc = _time.GetUtcNow().Add(ttl);
        }
    }
}
