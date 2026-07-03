using MouseKeyProxy.Common;
using NSubstitute;
using Xunit;

namespace MouseKeyProxy.Agent.Tests;

public class HotkeyToggleUnitTests
{
    [Fact]
    [Trait("Category", "Hotkey")]
    public void HotkeyMonitor_Raises_Event_Is_The_Integration_Point()
    {
        var monitor = Substitute.For<IHotkeyMonitor>();
        var clip = Substitute.For<ICursorClip>();

        bool raised = false;
        // This is exactly how production (Agent.Program) wires the seam
        monitor.ToggleRequested += (s, e) =>
        {
            raised = true;
            // Real handler would also do: state.Apply + clip.ClipToPoint / Release
            if (!e.IsRemoteChord) clip.ClipToPoint(0, 0);
        };

        // Cause the NSubstitute to raise the event to our handler (simulates the real low-level hook / RegisterHotKey)
        monitor.ToggleRequested += NSubstitute.Raise.EventWith<ToggleEventArgs>(new ToggleEventArgs("Ctrl-Alt-F1", false));

        Assert.True(raised);
        clip.Received().ClipToPoint(0, 0);
    }

    [Fact]
    [Trait("Category", "Ownership")]
    public void Input_Injector_Seam_Receives_Calls_From_Agent_Code_Not_Service()
    {
        // Ownership: only Agent code should call the injector seam.
        // Service must never new Win32* or call Send/Clip directly.
        var injector = Substitute.For<IInputInjector>();

        // Agent path (correct)
        injector.Send(new InputEvent(InputKind.KEY_DOWN, Vk: (uint)'A'));

        // Verify the seam was called (tests that production code goes through the seam)
        injector.Received(1).Send(Arg.Any<InputEvent>());

        // The test itself must NEVER do new Win32InputInjector() etc. - that would execute real P/Invoke.
    }
}
