using MouseKeyProxy.Common;
using Xunit;

namespace MouseKeyProxy.Common.Tests;

/// <summary>
/// LL hook injected-event policy: service→agent SendInput must pass through host hooks
/// so Windows software injection (and co-resident forwarders) cannot swallow remote input.
/// </summary>
public class LowLevelInputFlagsTests
{
    /// <summary>Plain physical keyboard flags are not treated as injected.</summary>
    [Fact]
    public void IsInjectedKeyboard_Physical_IsFalse()
    {
        Assert.False(LowLevelInputFlags.IsInjectedKeyboard(0));
        Assert.False(LowLevelInputFlags.IsInjectedKeyboard(LowLevelInputFlags.LLKHF_EXTENDED));
    }

    /// <summary>SendInput sets LLKHF_INJECTED; lower-IL inject uses LLKHF_LOWER_IL_INJECTED.</summary>
    [Theory]
    [InlineData(LowLevelInputFlags.LLKHF_INJECTED)]
    [InlineData(LowLevelInputFlags.LLKHF_LOWER_IL_INJECTED)]
    [InlineData(LowLevelInputFlags.LLKHF_INJECTED | LowLevelInputFlags.LLKHF_EXTENDED)]
    public void IsInjectedKeyboard_InjectedVariants_IsTrue(uint flags)
    {
        Assert.True(LowLevelInputFlags.IsInjectedKeyboard(flags));
    }

    /// <summary>Mouse LLMHF_INJECTED variants are detected.</summary>
    [Theory]
    [InlineData(0u, false)]
    [InlineData(LowLevelInputFlags.LLMHF_INJECTED, true)]
    [InlineData(LowLevelInputFlags.LLMHF_LOWER_IL_INJECTED, true)]
    public void IsInjectedMouse_MatchesFlag(uint flags, bool expected)
    {
        Assert.Equal(expected, LowLevelInputFlags.IsInjectedMouse(flags));
    }

    /// <summary>
    /// When remote capture is active, only non-injected events are consumed for forward;
    /// injected events always pass through to the local desktop.
    /// </summary>
    [Theory]
    [InlineData(true, 0u, true)]
    [InlineData(true, LowLevelInputFlags.LLKHF_INJECTED, false)]
    [InlineData(false, 0u, false)]
    [InlineData(false, LowLevelInputFlags.LLKHF_INJECTED, false)]
    public void ShouldConsumeForRemoteForward_Keyboard(bool captureActive, uint flags, bool expectedConsume)
    {
        Assert.Equal(expectedConsume, LowLevelInputFlags.ShouldConsumeForRemoteForward(captureActive, flags, isMouse: false));
    }

    /// <summary>Same policy for mouse LLMHF flags.</summary>
    [Theory]
    [InlineData(true, 0u, true)]
    [InlineData(true, LowLevelInputFlags.LLMHF_INJECTED, false)]
    [InlineData(false, LowLevelInputFlags.LLMHF_INJECTED, false)]
    public void ShouldConsumeForRemoteForward_Mouse(bool captureActive, uint flags, bool expectedConsume)
    {
        Assert.Equal(expectedConsume, LowLevelInputFlags.ShouldConsumeForRemoteForward(captureActive, flags, isMouse: true));
    }
}
