using System;
using System.IO;
using System.Text.Json;

namespace MouseKeyProxy.Common;

/// <summary>
/// FR-MKP-001 / TR-MKP-RELI-001: user-configurable hotkey bindings for the toggle and the dedicated
/// emergency-release. Modifier flags use the Win32 RegisterHotKey MOD_* values (ALT=1, CONTROL=2,
/// SHIFT=4, WIN=8). Defaults: toggle Ctrl-Alt-F2, emergency-release Ctrl-Alt-F3 (distinct).
/// </summary>
public sealed class HotkeyConfig
{
    /// <summary>MOD_ALT (0x1) | MOD_CONTROL (0x2).</summary>
    public const uint ModCtrlAlt = 0x0003;

    /// <summary>MOD_CONTROL (0x2) | MOD_SHIFT (0x4).</summary>
    public const uint ModCtrlShift = 0x0006;

    /// <summary>Virtual key for the toggle hotkey (default VK_F2 = 0x71).</summary>
    public uint ToggleVk { get; set; } = 0x71;

    /// <summary>Modifier flags for the toggle hotkey (default Ctrl+Alt).</summary>
    public uint ToggleMods { get; set; } = ModCtrlAlt;

    /// <summary>Virtual key for the dedicated emergency-release hotkey (default VK_F3 = 0x72).</summary>
    public uint EmergencyReleaseVk { get; set; } = 0x72;

    /// <summary>Modifier flags for the emergency-release hotkey (default Ctrl+Alt).</summary>
    public uint EmergencyReleaseMods { get; set; } = ModCtrlAlt;

    /// <summary>When the configuration was last saved.</summary>
    public DateTimeOffset SavedAtUtc { get; set; }
}

/// <summary>
/// FR-MKP-001: persistence for <see cref="HotkeyConfig"/> as JSON under the user's local application
/// data. A missing or unreadable file yields defaults so the app always has a valid binding.
/// </summary>
public static class HotkeyConfigStore
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>The default hotkey-config path.</summary>
    /// <returns>The absolute config file path.</returns>
    public static string DefaultPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MouseKeyProxy",
        "hotkey-config.json");

    /// <summary>Saves the config to <paramref name="path"/>.</summary>
    /// <param name="path">The config file path.</param>
    /// <param name="config">The config to persist.</param>
    public static void Save(string path, HotkeyConfig config)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(config);

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(config, Options));
    }

    /// <summary>Loads the config from <paramref name="path"/>, or returns defaults when absent/invalid.</summary>
    /// <param name="path">The config file path.</param>
    /// <returns>The loaded config, or a default instance.</returns>
    public static HotkeyConfig Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return new HotkeyConfig();
        }

        try
        {
            return JsonSerializer.Deserialize<HotkeyConfig>(File.ReadAllText(path), Options) ?? new HotkeyConfig();
        }
        catch (JsonException)
        {
            return new HotkeyConfig();
        }
    }
}
