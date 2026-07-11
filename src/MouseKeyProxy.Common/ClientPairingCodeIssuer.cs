using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;

namespace MouseKeyProxy.Common;

/// <summary>
/// FR-MKP-023 / TR-MKP-SEC-PAIR-001: issues short-lived one-time codes for Agent client pairing.
/// The connecting machine must type the code; wrong/expired codes do not issue credentials.
/// </summary>
public interface IClientPairingCodeIssuer
{
    /// <summary>
    /// Generates a new code (invalidates any previous outstanding code for the same session).
    /// </summary>
    /// <param name="ttl">Lifetime to live; defaults to 5 minutes when null or non-positive.</param>
    /// <returns>The plaintext code for display on USB host / console.</returns>
    string IssueCode(TimeSpan? ttl = null);

    /// <summary>
    /// Validates and consumes a code. Returns true once; subsequent use of the same code fails.
    /// </summary>
    /// <param name="code">Operator-entered code from the connecting machine.</param>
    /// <param name="errorCode">Stable error when false.</param>
    /// <param name="message">Human message when false.</param>
    bool TryConsume(string? code, out string errorCode, out string message);

    /// <summary>Current outstanding code for display assist (null if none or expired).</summary>
    string? PeekActiveCode();

    /// <summary>UTC expiry of the active code, if any.</summary>
    DateTimeOffset? ActiveExpiresUtc { get; }
}

/// <summary>In-memory OTP issuer with rate limiting on failed consume attempts.</summary>
public sealed class ClientPairingCodeIssuer : IClientPairingCodeIssuer
{
    private readonly object _gate = new();
    private readonly TimeSpan _defaultTtl;
    private readonly int _maxFailures;
    private readonly TimeSpan _lockout;

    private string? _activeCode;
    private DateTimeOffset _expiresUtc;
    private int _failures;
    private DateTimeOffset _lockoutUntilUtc;

    /// <summary>Creates an issuer.</summary>
    /// <param name="defaultTtl">Default code lifetime (clamped to at least 30 seconds).</param>
    /// <param name="maxFailures">Failed attempts before temporary lockout.</param>
    /// <param name="lockout">Lockout duration after <paramref name="maxFailures"/>.</param>
    public ClientPairingCodeIssuer(
        TimeSpan? defaultTtl = null,
        int maxFailures = 5,
        TimeSpan? lockout = null)
    {
        _defaultTtl = defaultTtl is { } t && t > TimeSpan.FromSeconds(30)
            ? t
            : TimeSpan.FromMinutes(5);
        _maxFailures = Math.Max(1, maxFailures);
        _lockout = lockout is { } l && l > TimeSpan.Zero ? l : TimeSpan.FromMinutes(2);
    }

    /// <inheritdoc />
    public DateTimeOffset? ActiveExpiresUtc
    {
        get
        {
            lock (_gate)
            {
                if (_activeCode is null || DateTimeOffset.UtcNow >= _expiresUtc)
                {
                    return null;
                }

                return _expiresUtc;
            }
        }
    }

    /// <inheritdoc />
    public string IssueCode(TimeSpan? ttl = null)
    {
        var life = ttl is { } t && t > TimeSpan.FromSeconds(30) ? t : _defaultTtl;
        // 6-digit numeric is easy to type; cryptographically random.
        var value = RandomNumberGenerator.GetInt32(0, 1_000_000)
            .ToString("D6", CultureInfo.InvariantCulture);

        lock (_gate)
        {
            _activeCode = value;
            _expiresUtc = DateTimeOffset.UtcNow.Add(life);
            _failures = 0;
            _lockoutUntilUtc = DateTimeOffset.MinValue;
            return value;
        }
    }

    /// <inheritdoc />
    public string? PeekActiveCode()
    {
        lock (_gate)
        {
            if (_activeCode is null || DateTimeOffset.UtcNow >= _expiresUtc)
            {
                return null;
            }

            return _activeCode;
        }
    }

    /// <inheritdoc />
    public bool TryConsume(string? code, out string errorCode, out string message)
    {
        errorCode = string.Empty;
        message = string.Empty;

        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            if (now < _lockoutUntilUtc)
            {
                errorCode = "PAIR_CODE_LOCKOUT";
                message = "Too many failed pairing code attempts; try again later.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(code))
            {
                RegisterFailure(now);
                errorCode = "PAIR_CODE_REQUIRED";
                message = "Pairing code is required.";
                return false;
            }

            if (_activeCode is null || now >= _expiresUtc)
            {
                errorCode = "PAIR_CODE_EXPIRED";
                message = "No active pairing code (expired or not issued).";
                return false;
            }

            if (!FixedTimeEquals(_activeCode, code.Trim()))
            {
                RegisterFailure(now);
                errorCode = "PAIR_CODE_INVALID";
                message = "Pairing code does not match.";
                return false;
            }

            // Consume: one-time use.
            _activeCode = null;
            _expiresUtc = DateTimeOffset.MinValue;
            _failures = 0;
            return true;
        }
    }

    private void RegisterFailure(DateTimeOffset now)
    {
        _failures++;
        if (_failures >= _maxFailures)
        {
            _lockoutUntilUtc = now.Add(_lockout);
            _failures = 0;
            // Invalidate code on lockout.
            _activeCode = null;
            _expiresUtc = DateTimeOffset.MinValue;
        }
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        var ba = System.Text.Encoding.UTF8.GetBytes(a);
        var bb = System.Text.Encoding.UTF8.GetBytes(b);
        if (ba.Length != bb.Length)
        {
            // Still compare to reduce timing signal on length (hash both to fixed size).
            return CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(ba),
                SHA256.HashData(bb)) && ba.Length == bb.Length;
        }

        return CryptographicOperations.FixedTimeEquals(ba, bb);
    }
}
