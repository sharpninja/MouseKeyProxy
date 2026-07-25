using System;
using System.IO;
using System.Text.Json;

namespace MouseKeyProxy.Common;

/// <summary>
/// FR-MKP-001 / TR-MKP-RELI-001: user-configurable hotkey bindings for the toggle and the dedicated
/// emergency-release. Modifier flags use the Win32 RegisterHotKey MOD_* values (ALT=1, CONTROL=2,
/// SHIFT=4, WIN=8). Defaults: toggle Ctrl-Win-F1, emergency-release Ctrl-Alt-F3 (distinct).
/// </summary>
public sealed class HotkeyConfig
{
    /// <summary>MOD_ALT (0x1) | MOD_CONTROL (0x2).</summary>
    public const uint ModCtrlAlt = 0x0003;

    /// <summary>MOD_CONTROL (0x2) | MOD_SHIFT (0x4).</summary>
    public const uint ModCtrlShift = 0x0006;

    /// <summary>MOD_CONTROL (0x2) | MOD_WIN (0x8).</summary>
    public const uint ModCtrlWin = 0x000A;

    /// <summary>Virtual key for the toggle hotkey (default VK_F1 = 0x70).</summary>
    public uint ToggleVk { get; set; } = 0x70;

    /// <summary>Modifier flags for the toggle hotkey (default Ctrl+Win).</summary>
    public uint ToggleMods { get; set; } = ModCtrlWin;

    /// <summary>Virtual key for the dedicated emergency-release hotkey (default VK_F3 = 0x72).</summary>
    public uint EmergencyReleaseVk { get; set; } = 0x72;

    /// <summary>Modifier flags for the emergency-release hotkey (default Ctrl+Alt).</summary>
    public uint EmergencyReleaseMods { get; set; } = ModCtrlAlt;

    /// <summary>When the configuration was last saved.</summary>
    public DateTimeOffset SavedAtUtc { get; set; }
}

/// <summary>Matches a fixed key with at least two of Control, Alt, and Windows held.</summary>
internal static class TwoOfThreeModifierChord
{
    /// <summary>Determines whether a key and modifier state matches the two-of-three rule.</summary>
    /// <param name="virtualKey">The virtual-key code for the key-down event.</param>
    /// <param name="targetVirtualKey">The virtual-key code required by the chord.</param>
    /// <param name="controlDown">Whether either Control key is held.</param>
    /// <param name="altDown">Whether either Alt key is held.</param>
    /// <param name="winDown">Whether either Windows key is held.</param>
    /// <returns><see langword="true"/> when the key matches and at least two modifiers are held.</returns>
    public static bool Matches(
        uint virtualKey,
        uint targetVirtualKey,
        bool controlDown,
        bool altDown,
        bool winDown)
    {
        if (virtualKey != targetVirtualKey)
        {
            return false;
        }

        var modifierCount = (controlDown ? 1 : 0) + (altDown ? 1 : 0) + (winDown ? 1 : 0);
        return modifierCount >= 2;
    }
}

/// <summary>
/// Defines the non-configurable remote-activation fallback accepted by every keyboard-hook path.
/// F1 activates remote control whenever at least two of Control, Alt, and Windows are held.
/// </summary>
public static class RemoteActivationChord
{
    /// <summary>Virtual-key code for F1.</summary>
    public const uint VirtualKey = 0x70;

    /// <summary>Determines whether the supplied key and modifier state activates remote control.</summary>
    /// <param name="virtualKey">The virtual-key code for the key-down event.</param>
    /// <param name="controlDown">Whether either Control key is held.</param>
    /// <param name="altDown">Whether either Alt key is held.</param>
    /// <param name="winDown">Whether either Windows key is held.</param>
    /// <returns><see langword="true"/> when F1 and at least two supported modifiers are held.</returns>
    public static bool Matches(uint virtualKey, bool controlDown, bool altDown, bool winDown)
    {
        return TwoOfThreeModifierChord.Matches(virtualKey, VirtualKey, controlDown, altDown, winDown);
    }
}

/// <summary>
/// Defines the non-configurable emergency fallback chord accepted by every keyboard-hook path.
/// F3 triggers emergency release whenever at least two of Control, Alt, and Windows are held.
/// </summary>
public static class EmergencyReleaseChord
{
    /// <summary>Virtual-key code for F3.</summary>
    public const uint VirtualKey = 0x72;

    /// <summary>Determines whether the supplied key and modifier state is an emergency-release chord.</summary>
    /// <param name="virtualKey">The virtual-key code for the key-down event.</param>
    /// <param name="controlDown">Whether either Control key is held.</param>
    /// <param name="altDown">Whether either Alt key is held.</param>
    /// <param name="winDown">Whether either Windows key is held.</param>
    /// <returns>
    /// <see langword="true"/> when <paramref name="virtualKey"/> is F3 and at least two supported
    /// modifiers are held; otherwise, <see langword="false"/>.
    /// </returns>
    public static bool Matches(uint virtualKey, bool controlDown, bool altDown, bool winDown)
    {
        return TwoOfThreeModifierChord.Matches(virtualKey, VirtualKey, controlDown, altDown, winDown);
    }
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
