using MouseKeyProxy.Agent;
using MouseKeyProxy.Common;
using Xunit;

namespace MouseKeyProxy.Agent.Tests;

/// <summary>
/// FR-MKP-001 / TR-MKP-RELI-001: verifies the dedicated emergency-release hotkey on Win32HotkeyMonitor
/// fires its own event (distinct from toggle), is debounced independently, and honors configuration.
/// </summary>
public class EmergencyReleaseHotkeyTests
{
    /// <summary>RaiseEmergencyRelease fires EmergencyReleaseRequested with the chord, not ToggleRequested.</summary>
    [Fact]
    [Trait("Category", "Hotkeys")]
    public void RaiseEmergencyRelease_Fires_EmergencyEvent_Only()
    {
        var monitor = new Win32HotkeyMonitor();
        var emergencyFired = false;
        string? chord = null;
        var toggleFired = false;
        monitor.EmergencyReleaseRequested += (_, e) => { emergencyFired = true; chord = e.Chord; };
        monitor.ToggleRequested += (_, _) => toggleFired = true;

        monitor.RaiseEmergencyRelease("Ctrl-Alt-F3", remote: false);

        Assert.True(emergencyFired);
        Assert.Equal("Ctrl-Alt-F3", chord);
        Assert.False(toggleFired);
    }

    /// <summary>A second emergency raise within the debounce window is suppressed.</summary>
    [Fact]
    [Trait("Category", "Hotkeys")]
    public void EmergencyRelease_IsDebounced()
    {
        var monitor = new Win32HotkeyMonitor();
        var count = 0;
        monitor.EmergencyReleaseRequested += (_, _) => count++;

        monitor.RaiseEmergencyRelease("Ctrl-Alt-F3", false);
        monitor.RaiseEmergencyRelease("Ctrl-Alt-F3", false); // within 300ms -> suppressed

        Assert.Equal(1, count);
    }

    /// <summary>Emergency and toggle debounce independently - both fire back-to-back.</summary>
    [Fact]
    [Trait("Category", "Hotkeys")]
    public void EmergencyAndToggle_DebounceIndependently()
    {
        var monitor = new Win32HotkeyMonitor();
        var toggle = 0;
        var emergency = 0;
        monitor.ToggleRequested += (_, _) => toggle++;
        monitor.EmergencyReleaseRequested += (_, _) => emergency++;

        monitor.RaiseToggle("Ctrl-Alt-F2", false);
        monitor.RaiseEmergencyRelease("Ctrl-Alt-F3", false);

        Assert.Equal(1, toggle);
        Assert.Equal(1, emergency);
    }

    /// <summary>The monitor exposes its configuration so callers can register the configured bindings.</summary>
    [Fact]
    [Trait("Category", "Hotkeys")]
    public void Monitor_ExposesConfiguredBindings()
    {
        var config = new HotkeyConfig { EmergencyReleaseVk = 0x7A }; // VK_F11
        var monitor = new Win32HotkeyMonitor(config);

        Assert.Equal(0x7Au, monitor.Config.EmergencyReleaseVk);
    }
}
