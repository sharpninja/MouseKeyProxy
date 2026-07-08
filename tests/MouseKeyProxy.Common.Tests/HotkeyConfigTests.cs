using System.IO;
using MouseKeyProxy.Common;
using Xunit;

namespace MouseKeyProxy.Common.Tests;

/// <summary>
/// FR-MKP-001 / TR-MKP-RELI-001: verifies the persisted hotkey configuration - defaults (toggle
/// Ctrl-Alt-F2, emergency-release Ctrl-Alt-F3, distinct), JSON round-trip, and store load/save with
/// a missing file falling back to defaults.
/// </summary>
public class HotkeyConfigTests
{
    /// <summary>Defaults bind toggle to Ctrl-Alt-F2 and emergency-release to a distinct Ctrl-Alt-F3.</summary>
    [Fact]
    [Trait("Category", "Hotkeys")]
    public void Defaults_ToggleAndEmergency_AreDistinct_CtrlAlt()
    {
        var config = new HotkeyConfig();

        Assert.Equal(0x71u, config.ToggleVk);          // VK_F2
        Assert.Equal(0x72u, config.EmergencyReleaseVk); // VK_F3
        Assert.Equal(HotkeyConfig.ModCtrlAlt, config.ToggleMods);
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

        Assert.Equal(0x71u, loaded.ToggleVk);
        Assert.Equal(0x72u, loaded.EmergencyReleaseVk);
    }
}
