using MouseKeyProxy.Agent;
using MouseKeyProxy.Common;
using Xunit;

namespace MouseKeyProxy.Agent.Tests;

/// <summary>
/// FR-MKP-011 / FR-MKP-001: behavioral tests over the real Win32HotkeyMonitor toggle seam (no source
/// scanning, no over-mocked harness) - RaiseToggle fires ToggleRequested with the chord and is
/// debounced, and toggle/emergency dispatch to distinct events.
/// </summary>
public class HotkeyMonitorBehaviorTests
{
    /// <summary>RaiseToggle fires ToggleRequested carrying the chord and remote flag.</summary>
    [Fact]
    [Trait("Category", "Hotkeys")]
    public void RaiseToggle_Fires_ToggleRequested_WithChord()
    {
        var monitor = new Win32HotkeyMonitor();
        ToggleEventArgs? seen = null;
        monitor.ToggleRequested += (_, e) => seen = e;

        monitor.RaiseToggle("Ctrl-Alt-F2", remote: true);

        Assert.NotNull(seen);
        Assert.Equal("Ctrl-Alt-F2", seen!.Chord);
        Assert.True(seen.IsRemoteChord);
    }

    /// <summary>A second toggle within the debounce window is suppressed.</summary>
    [Fact]
    [Trait("Category", "Hotkeys")]
    public void RaiseToggle_IsDebounced()
    {
        var monitor = new Win32HotkeyMonitor();
        var count = 0;
        monitor.ToggleRequested += (_, _) => count++;

        monitor.RaiseToggle("Ctrl-Alt-F2", false);
        monitor.RaiseToggle("Ctrl-Alt-F2", false);

        Assert.Equal(1, count);
    }

    /// <summary>Toggle and emergency-release fire independent events, never crossing wires.</summary>
    [Fact]
    [Trait("Category", "Hotkeys")]
    public void Toggle_And_Emergency_DoNotCrossWires()
    {
        var monitor = new Win32HotkeyMonitor();
        var toggles = 0;
        var emergencies = 0;
        monitor.ToggleRequested += (_, _) => toggles++;
        monitor.EmergencyReleaseRequested += (_, _) => emergencies++;

        monitor.RaiseToggle("Ctrl-Alt-F2", false);

        Assert.Equal(1, toggles);
        Assert.Equal(0, emergencies);
    }
}
