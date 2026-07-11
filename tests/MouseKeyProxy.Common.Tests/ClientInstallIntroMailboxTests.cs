using System;
using MouseKeyProxy.Common;
using Xunit;

namespace MouseKeyProxy.Common.Tests;

/// <summary>
/// FR-MKP-026 / TEST: device mailbox for USB client clipboard intros (host claim once).
/// </summary>
public class ClientInstallIntroMailboxTests
{
    /// <summary>Queue then peek returns the intro; claim removes it.</summary>
    [Fact]
    public void Queue_Peek_Claim_Once()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-07-09T12:00:00Z"));
        var box = new ClientInstallIntroMailbox(clock);
        var now = clock.GetUtcNow();
        var intro = new ClientInstallIntro(
            "payton-desktop",
            "https://payton-desktop:50051",
            "123456",
            now,
            now.AddMinutes(5));

        box.Queue(intro);
        var pending = box.PeekPending();
        Assert.Single(pending);
        Assert.Equal("payton-desktop", pending[0].ClientPeerId);

        var claimed = box.Claim("payton-desktop");
        Assert.NotNull(claimed);
        Assert.Equal("123456", claimed!.ClipboardIntroCode);
        Assert.Empty(box.PeekPending());
        Assert.Null(box.Claim("payton-desktop"));
    }

    /// <summary>Expired intros are not returned.</summary>
    [Fact]
    public void Expired_Purged()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-07-09T12:00:00Z"));
        var box = new ClientInstallIntroMailbox(clock);
        var now = clock.GetUtcNow();
        box.Queue(new ClientInstallIntro(
            "payton-desktop",
            "https://payton-desktop:50051",
            "999999",
            now,
            now.AddMinutes(1)));

        clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Empty(box.PeekPending());
        Assert.Null(box.Claim("payton-desktop"));
    }

    /// <summary>Re-queue replaces prior intro for the same client.</summary>
    [Fact]
    public void Requeue_Replaces()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-07-09T12:00:00Z"));
        var box = new ClientInstallIntroMailbox(clock);
        var now = clock.GetUtcNow();
        box.Queue(new ClientInstallIntro("desk", "https://a:50051", "111111", now, now.AddMinutes(5)));
        box.Queue(new ClientInstallIntro("desk", "https://b:50051", "222222", now, now.AddMinutes(5)));
        var pending = box.PeekPending();
        Assert.Single(pending);
        Assert.Equal("https://b:50051", pending[0].ClipboardEndpoint);
        Assert.Equal("222222", pending[0].ClipboardIntroCode);
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _utc;

        public FakeTimeProvider(DateTimeOffset utc) => _utc = utc;

        public override DateTimeOffset GetUtcNow() => _utc;

        public void Advance(TimeSpan delta) => _utc = _utc.Add(delta);
    }
}
