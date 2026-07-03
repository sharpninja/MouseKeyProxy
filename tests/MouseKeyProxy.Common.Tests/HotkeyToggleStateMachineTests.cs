using Xunit;
using MouseKeyProxy.Common.Hotkeys; // will be implemented by claude
using MouseKeyProxy.Common; // for seams if needed

namespace MouseKeyProxy.Common.Tests;

public class HotkeyToggleStateMachineTests
{
    [Fact]
    public void StartsInactive()
    {
        var sm = new HotkeyToggleStateMachine();
        Assert.False(sm.IsActive);
    }

    [Fact]
    public void CorrectLocalChordActivates()
    {
        var sm = new HotkeyToggleStateMachine();
        var transition = sm.HandleChord( /* correct local Ctrl-Alt-F1 */ );
        Assert.Equal(HotkeyTransition.Activated, transition);
        Assert.True(sm.IsActive);
    }

    [Fact]
    public void WrongChordDoesNothing()
    {
        var sm = new HotkeyToggleStateMachine();
        var transition = sm.HandleChord( /* wrong */ );
        Assert.Equal(HotkeyTransition.None, transition);
        Assert.False(sm.IsActive);
    }

    // more exhaustive: release, autorepeat, remote chord, pointer-edge no auto, clip/release only on real state changes, etc.
    // edge cases for visibility, error paths
}

public class OwnershipPolicyTests
{
    [Fact]
    public void AgentOwnsInput()
    {
        // mocks prove
    }

    // etc for Service rejects
}
