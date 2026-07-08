using System;
using MouseKeyProxy.Common;
using Xunit;

namespace MouseKeyProxy.Common.Tests;

/// <summary>
/// TR-MKP-RELI-001 / FR-MKP-001: verifies the connection safety failsafe deadlines with a controllable
/// clock - the 2s force-release-on-silence deadline and the 5s reconnect give-up. Replaces the prior
/// no-assert LatencyFailsafeHarness with real, deterministic behavioral assertions.
/// </summary>
public class ConnectionFailsafeTests
{
    /// <summary>A controllable clock for deterministic deadline tests.</summary>
    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }

    /// <summary>An active hold with a fresh heartbeat is not force-released before the deadline.</summary>
    [Fact]
    [Trait("Category", "Failsafe")]
    public void ActiveWithRecentHeartbeat_DoesNotForceRelease()
    {
        var clock = new TestClock();
        var failsafe = new ConnectionFailsafe(clock);
        failsafe.OnActivated();

        clock.Advance(TimeSpan.FromMilliseconds(1900));
        Assert.False(failsafe.ShouldForceRelease());
    }

    /// <summary>Silence past the 2s deadline forces a release; a heartbeat resets the window.</summary>
    [Fact]
    [Trait("Category", "Failsafe")]
    public void Silence_Past_2s_ForcesRelease_HeartbeatResets()
    {
        var clock = new TestClock();
        var failsafe = new ConnectionFailsafe(clock);
        failsafe.OnActivated();

        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.True(failsafe.ShouldForceRelease());

        failsafe.OnHeartbeat();
        Assert.False(failsafe.ShouldForceRelease());

        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.True(failsafe.ShouldForceRelease());
    }

    /// <summary>Once released, no force-release is signalled regardless of elapsed time.</summary>
    [Fact]
    [Trait("Category", "Failsafe")]
    public void AfterRelease_NoForceRelease()
    {
        var clock = new TestClock();
        var failsafe = new ConnectionFailsafe(clock);
        failsafe.OnActivated();
        failsafe.OnReleased();

        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.False(failsafe.ShouldForceRelease());
    }

    /// <summary>Reconnect is only abandoned once the 5s window elapses; a heartbeat cancels it.</summary>
    [Fact]
    [Trait("Category", "Failsafe")]
    public void Reconnect_GivesUp_After_5s()
    {
        var clock = new TestClock();
        var failsafe = new ConnectionFailsafe(clock);
        failsafe.OnActivated();
        failsafe.OnDisconnected();

        clock.Advance(TimeSpan.FromSeconds(4));
        Assert.False(failsafe.ShouldGiveUpReconnect());

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(failsafe.ShouldGiveUpReconnect());
    }

    /// <summary>A heartbeat during the reconnect window cancels the give-up.</summary>
    [Fact]
    [Trait("Category", "Failsafe")]
    public void Heartbeat_Cancels_ReconnectGiveUp()
    {
        var clock = new TestClock();
        var failsafe = new ConnectionFailsafe(clock);
        failsafe.OnActivated();
        failsafe.OnDisconnected();

        clock.Advance(TimeSpan.FromSeconds(3));
        failsafe.OnHeartbeat();

        clock.Advance(TimeSpan.FromSeconds(3));
        Assert.False(failsafe.ShouldGiveUpReconnect());
    }
}
