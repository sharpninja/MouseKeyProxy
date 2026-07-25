using System.IO;
using MouseKeyProxy.Common;
using Xunit;

namespace MouseKeyProxy.Common.Tests;

/// <summary>
/// FR-MKP-001 / TR-MKP-RELI-001: verifies the persisted hotkey configuration - defaults (toggle
/// Ctrl-Win-F1, emergency-release Ctrl-Alt-F3, distinct), JSON round-trip, and store load/save with
/// a missing file falling back to defaults.
/// </summary>
public class HotkeyConfigTests
{
    /// <summary>F1 with any two supported modifiers is always a remote-activation chord.</summary>
    /// <param name="controlDown">Whether either Control key is held.</param>
    /// <param name="altDown">Whether either Alt key is held.</param>
    /// <param name="winDown">Whether either Windows key is held.</param>
    [Theory]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    [Trait("Category", "Hotkeys")]
    [Trait("Category", "RemoteActivation")]
    public void RemoteActivationChord_F1WithAnyTwoSupportedModifiers_Matches(
        bool controlDown,
        bool altDown,
        bool winDown)
    {
        Assert.True(RemoteActivationChord.Matches(0x70, controlDown, altDown, winDown));
    }

    /// <summary>F1 with fewer than two supported modifiers does not trigger remote activation.</summary>
    /// <param name="controlDown">Whether either Control key is held.</param>
    /// <param name="altDown">Whether either Alt key is held.</param>
    /// <param name="winDown">Whether either Windows key is held.</param>
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [Trait("Category", "Hotkeys")]
    [Trait("Category", "RemoteActivation")]
    public void RemoteActivationChord_F1WithFewerThanTwoSupportedModifiers_DoesNotMatch(
        bool controlDown,
        bool altDown,
        bool winDown)
    {
        Assert.False(RemoteActivationChord.Matches(0x70, controlDown, altDown, winDown));
    }

    /// <summary>A non-F1 key does not trigger the fixed remote-activation chord.</summary>
    [Fact]
    [Trait("Category", "Hotkeys")]
    [Trait("Category", "RemoteActivation")]
    public void RemoteActivationChord_NonF1WithSupportedModifiers_DoesNotMatch()
    {
        Assert.False(RemoteActivationChord.Matches(0x71, true, true, true));
    }

    /// <summary>F3 with any two supported modifiers is always an emergency-release chord.</summary>
    /// <param name="controlDown">Whether either Control key is held.</param>
    /// <param name="altDown">Whether either Alt key is held.</param>
    /// <param name="winDown">Whether either Windows key is held.</param>
    [Theory]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    [Trait("Category", "Hotkeys")]
    [Trait("Category", "EmergencyRelease")]
    public void EmergencyReleaseChord_F3WithAnyTwoSupportedModifiers_Matches(
        bool controlDown,
        bool altDown,
        bool winDown)
    {
        Assert.True(EmergencyReleaseChord.Matches(0x72, controlDown, altDown, winDown));
    }

    /// <summary>F3 with fewer than two supported modifiers does not trigger emergency release.</summary>
    /// <param name="controlDown">Whether either Control key is held.</param>
    /// <param name="altDown">Whether either Alt key is held.</param>
    /// <param name="winDown">Whether either Windows key is held.</param>
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [Trait("Category", "Hotkeys")]
    [Trait("Category", "EmergencyRelease")]
    public void EmergencyReleaseChord_F3WithFewerThanTwoSupportedModifiers_DoesNotMatch(
        bool controlDown,
        bool altDown,
        bool winDown)
    {
        Assert.False(EmergencyReleaseChord.Matches(0x72, controlDown, altDown, winDown));
    }

    /// <summary>A non-F3 key does not trigger the fixed emergency-release chord.</summary>
    [Fact]
    [Trait("Category", "Hotkeys")]
    [Trait("Category", "EmergencyRelease")]
    public void EmergencyReleaseChord_NonF3WithSupportedModifiers_DoesNotMatch()
    {
        Assert.False(EmergencyReleaseChord.Matches(0x71, true, true, true));
    }

    /// <summary>Defaults bind toggle to Ctrl-Win-F1 and emergency-release to a distinct Ctrl-Alt-F3.</summary>
    [Fact]
    [Trait("Category", "Hotkeys")]
    public void Defaults_ToggleAndEmergency_AreDistinct_CtrlWinToggle()
    {
        var config = new HotkeyConfig();

        Assert.Equal(0x70u, config.ToggleVk);          // VK_F1
        Assert.Equal(0x72u, config.EmergencyReleaseVk); // VK_F3
        Assert.Equal(HotkeyConfig.ModCtrlWin, config.ToggleMods);
        Assert.Equal(HotkeyConfig.ModCtrlAlt, config.EmergencyReleaseMods);
        Assert.NotEqual(config.ToggleVk, config.EmergencyReleaseVk);
    }

    /// <summary>Custom binding survives a JSON round-trip.</summary>
    [Fact]
    [Trait("Category", "Hotkeys")]
    public void Roundtrip_Serialize_Deserialize_Preserves_Values()
    {
        var config = new HotkeyConfig
        {
            ToggleVk = 0x70,
            ToggleMods = HotkeyConfig.ModCtrlAlt,
            EmergencyReleaseVk = 0x73,
            EmergencyReleaseMods = HotkeyConfig.ModCtrlShift,
        };

        var json = System.Text.Json.JsonSerializer.Serialize(config);
        var back = System.Text.Json.JsonSerializer.Deserialize<HotkeyConfig>(json)!;

        Assert.Equal(0x70u, back.ToggleVk);
        Assert.Equal(0x73u, back.EmergencyReleaseVk);
        Assert.Equal(HotkeyConfig.ModCtrlShift, back.EmergencyReleaseMods);
    }

    /// <summary>The store writes then reads back the same config.</summary>
    [Fact]
    [Trait("Category", "Hotkeys")]
    public void Store_Save_Then_Load_RoundTrips()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mkp-hk-{System.Guid.NewGuid():N}.json");
        try
        {
            var config = new HotkeyConfig { ToggleVk = 0x42, EmergencyReleaseVk = 0x43 };
            HotkeyConfigStore.Save(path, config);
            var loaded = HotkeyConfigStore.Load(path);

            Assert.Equal(0x42u, loaded.ToggleVk);
            Assert.Equal(0x43u, loaded.EmergencyReleaseVk);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    /// <summary>Loading a missing config file returns defaults rather than throwing.</summary>
    [Fact]
    [Trait("Category", "Hotkeys")]
    public void Store_Load_MissingFile_ReturnsDefaults()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mkp-hk-missing-{System.Guid.NewGuid():N}.json");
        var loaded = HotkeyConfigStore.Load(path);

        Assert.Equal(0x70u, loaded.ToggleVk);
        Assert.Equal(HotkeyConfig.ModCtrlWin, loaded.ToggleMods);
        Assert.Equal(0x72u, loaded.EmergencyReleaseVk);
    }

}
