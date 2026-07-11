using System;
using MouseKeyProxy.Common;
using Xunit;

namespace MouseKeyProxy.Common.Tests;

/// <summary>
/// FR-MKP-025 / TEST: install ticket issue, validate, expiry, revoke.
/// </summary>
public class InstallTicketStoreTests
{
    /// <summary>Issued ticket validates until revoked or expired.</summary>
    [Fact]
    public void Issue_ThenValidate_Succeeds()
    {
        var store = new InstallTicketStore();
        var ticket = store.Issue(TimeSpan.FromHours(1));
        Assert.False(string.IsNullOrWhiteSpace(ticket));
        Assert.Equal(ticket, store.PeekActive());
        Assert.True(store.TryValidate(ticket, out var err));
        Assert.Equal(string.Empty, err);
        // Multi-use until expiry: still valid.
        Assert.True(store.TryValidate(ticket, out _));
    }

    /// <summary>Wrong ticket is rejected without clearing the active ticket.</summary>
    [Fact]
    public void WrongTicket_Fails()
    {
        var store = new InstallTicketStore();
        var ticket = store.Issue(TimeSpan.FromHours(1));
        Assert.False(store.TryValidate("deadbeef", out var err));
        Assert.Equal("INSTALL_TICKET_INVALID", err);
        Assert.Equal(ticket, store.PeekActive());
    }

    /// <summary>Empty ticket yields INSTALL_TICKET_REQUIRED.</summary>
    [Fact]
    public void EmptyTicket_Fails()
    {
        var store = new InstallTicketStore();
        store.Issue(TimeSpan.FromHours(1));
        Assert.False(store.TryValidate("  ", out var err));
        Assert.Equal("INSTALL_TICKET_REQUIRED", err);
    }

    /// <summary>Expired ticket fails and clears peek.</summary>
    [Fact]
    public void ExpiredTicket_Fails()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-07-09T12:00:00Z"));
        var store = new InstallTicketStore(clock);
        var ticket = store.Issue(TimeSpan.FromMinutes(5));
        clock.Advance(TimeSpan.FromMinutes(6));
        Assert.False(store.TryValidate(ticket, out var err));
        Assert.Equal("INSTALL_TICKET_EXPIRED", err);
        Assert.Null(store.PeekActive());
    }

    /// <summary>Revoke clears the ticket.</summary>
    [Fact]
    public void Revoke_Clears()
    {
        var store = new InstallTicketStore();
        var ticket = store.Issue(TimeSpan.FromHours(1));
        store.Revoke();
        Assert.Null(store.PeekActive());
        Assert.False(store.TryValidate(ticket, out var err));
        Assert.Equal("INSTALL_TICKET_EXPIRED", err);
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _utc;

        public FakeTimeProvider(DateTimeOffset utc) => _utc = utc;

        public override DateTimeOffset GetUtcNow() => _utc;

        public void Advance(TimeSpan delta) => _utc = _utc.Add(delta);
    }
}
