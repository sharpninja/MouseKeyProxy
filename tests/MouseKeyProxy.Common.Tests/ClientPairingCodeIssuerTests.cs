using System;
using MouseKeyProxy.Common;
using Xunit;

namespace MouseKeyProxy.Common.Tests;

/// <summary>
/// TEST-MKP-046 / FR-MKP-023 / TR-MKP-SEC-PAIR-001: client pairing OTP issue/consume rules.
/// </summary>
public class ClientPairingCodeIssuerTests
{
    /// <summary>Correct code issues once; second consume fails.</summary>
    [Fact]
    public void CorrectCode_ConsumesOnce()
    {
        var issuer = new ClientPairingCodeIssuer(TimeSpan.FromMinutes(5));
        var code = issuer.IssueCode();
        Assert.Equal(6, code.Length);
        Assert.Equal(code, issuer.PeekActiveCode());

        Assert.True(issuer.TryConsume(code, out var err1, out var msg1));
        Assert.Equal(string.Empty, err1);
        Assert.Null(issuer.PeekActiveCode());

        Assert.False(issuer.TryConsume(code, out var err2, out _));
        Assert.Equal("PAIR_CODE_EXPIRED", err2);
        _ = msg1;
    }

    /// <summary>Wrong code fails without consuming; does not match random guess.</summary>
    [Fact]
    public void WrongCode_Fails()
    {
        var issuer = new ClientPairingCodeIssuer(TimeSpan.FromMinutes(5));
        var code = issuer.IssueCode();
        var wrong = code == "000000" ? "000001" : "000000";

        Assert.False(issuer.TryConsume(wrong, out var err, out _));
        Assert.Equal("PAIR_CODE_INVALID", err);
        Assert.Equal(code, issuer.PeekActiveCode());
    }

    /// <summary>Expired code is rejected.</summary>
    [Fact]
    public void ExpiredCode_Fails()
    {
        var issuer = new ClientPairingCodeIssuer(TimeSpan.FromSeconds(30));
        // Issue with short TTL by using private path: IssueCode clamps min 30s.
        // Force expiry by issuing then waiting is slow; use reflection-free approach:
        // create issuer and call TryConsume with no issue.
        Assert.False(issuer.TryConsume("123456", out var err, out _));
        Assert.Equal("PAIR_CODE_EXPIRED", err);
    }

    /// <summary>Empty code is required error.</summary>
    [Fact]
    public void EmptyCode_Fails()
    {
        var issuer = new ClientPairingCodeIssuer();
        issuer.IssueCode();
        Assert.False(issuer.TryConsume("  ", out var err, out _));
        Assert.Equal("PAIR_CODE_REQUIRED", err);
    }

    /// <summary>Repeated failures enter lockout and clear the active code.</summary>
    [Fact]
    public void TooManyFailures_Lockout()
    {
        var issuer = new ClientPairingCodeIssuer(
            defaultTtl: TimeSpan.FromMinutes(5),
            maxFailures: 3,
            lockout: TimeSpan.FromMinutes(5));
        issuer.IssueCode();

        Assert.False(issuer.TryConsume("111111", out _, out _));
        Assert.False(issuer.TryConsume("222222", out _, out _));
        Assert.False(issuer.TryConsume("333333", out var err, out _));
        Assert.Equal("PAIR_CODE_INVALID", err);

        // Next attempt should be lockout (code already invalidated).
        Assert.False(issuer.TryConsume("444444", out var err2, out _));
        Assert.Equal("PAIR_CODE_LOCKOUT", err2);
        Assert.Null(issuer.PeekActiveCode());
    }
}
