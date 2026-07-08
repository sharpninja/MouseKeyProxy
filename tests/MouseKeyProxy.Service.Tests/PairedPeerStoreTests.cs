using System;
using MouseKeyProxy.Service.Pairing;
using Xunit;

namespace MouseKeyProxy.Service.Tests;

/// <summary>
/// TR-MKP-SEC-001: verifies the service-owned paired-peer store - service-issued, time-bound,
/// single-use pairing codes; paired-peer registration keyed by client-cert thumbprint;
/// authorization lookups; and revocation. Uses a controllable TimeProvider (no real clock).
/// </summary>
public class PairedPeerStoreTests
{
    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;
        public FakeTimeProvider(DateTimeOffset start) => _now = start;
        public void Advance(TimeSpan by) => _now += by;
        public override DateTimeOffset GetUtcNow() => _now;
    }

    private static (PairedPeerStore store, FakeTimeProvider time) NewStore()
    {
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        return (new PairedPeerStore(time), time);
    }

    /// <summary>An issued pairing code is accepted exactly once; a second use is rejected.</summary>
    [Fact]
    [Trait("Category", "Security")]
    public void PairingCode_IsSingleUse()
    {
        var (store, _) = NewStore();
        var code = store.IssuePairingCode(TimeSpan.FromMinutes(5));

        Assert.True(store.TryConsumePairingCode(code));
        Assert.False(store.TryConsumePairingCode(code));
    }

    /// <summary>An expired pairing code is rejected.</summary>
    [Fact]
    [Trait("Category", "Security")]
    public void PairingCode_ExpiresAfterTtl()
    {
        var (store, time) = NewStore();
        var code = store.IssuePairingCode(TimeSpan.FromMinutes(5));

        time.Advance(TimeSpan.FromMinutes(6));

        Assert.False(store.TryConsumePairingCode(code));
    }

    /// <summary>An unknown/never-issued code is rejected.</summary>
    [Fact]
    [Trait("Category", "Security")]
    public void PairingCode_Unknown_Rejected()
    {
        var (store, _) = NewStore();
        Assert.False(store.TryConsumePairingCode("NOT-A-REAL-CODE"));
    }

    /// <summary>A registered peer is found by cert thumbprint and by peer id, and is authorized.</summary>
    [Fact]
    [Trait("Category", "Security")]
    public void RegisteredPeer_IsFoundAndAuthorized()
    {
        var (store, _) = NewStore();
        var peer = store.RegisterPeer("peer-1", "THUMB-ABC");

        Assert.Equal("peer-1", peer.PeerId);
        Assert.NotNull(store.FindByThumbprint("THUMB-ABC"));
        Assert.NotNull(store.FindByPeerId("peer-1"));
        Assert.True(store.IsAuthorized("THUMB-ABC"));
    }

    /// <summary>A revoked peer is no longer authorized.</summary>
    [Fact]
    [Trait("Category", "Security")]
    public void RevokedPeer_IsNotAuthorized()
    {
        var (store, _) = NewStore();
        store.RegisterPeer("peer-2", "THUMB-XYZ");

        Assert.True(store.Revoke("peer-2"));
        Assert.False(store.IsAuthorized("THUMB-XYZ"));
    }

    /// <summary>An unknown thumbprint is never authorized.</summary>
    [Fact]
    [Trait("Category", "Security")]
    public void UnknownThumbprint_IsNotAuthorized()
    {
        var (store, _) = NewStore();
        Assert.False(store.IsAuthorized("no-such-thumb"));
    }
}
