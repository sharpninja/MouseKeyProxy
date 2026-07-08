using System;
using System.IO;
using System.Text.Json;

namespace MouseKeyProxy.Common;

/// <summary>
/// FR-MKP-006 / TR-MKP-UI-001: persisted operator settings (non-secret) surfaced by the REPL
/// `settings` verb and consumed by the agent/tray.
/// </summary>
public sealed class AppSettings
{
    /// <summary>The remote peer name last used.</summary>
    public string RemotePeer { get; set; } = string.Empty;

    /// <summary>The remote gRPC endpoint (https).</summary>
    public string RemoteGrpcUrl { get; set; } = string.Empty;

    /// <summary>Days to retain clipboard history (0 = session only).</summary>
    public int ClipboardRetentionDays { get; set; }

    /// <summary>Minimum log level (Information, Warning, Error, ...).</summary>
    public string LogLevel { get; set; } = "Information";
}

/// <summary>
/// FR-MKP-006: JSON persistence for <see cref="AppSettings"/> under the user's local application data.
/// A missing or invalid file yields defaults.
/// </summary>
public static class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>The default settings path.</summary>
    /// <returns>The absolute settings file path.</returns>
    public static string DefaultPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MouseKeyProxy",
        "settings.json");

    /// <summary>Loads settings from <paramref name="path"/>, or defaults when absent/invalid.</summary>
    /// <param name="path">The settings file path.</param>
    /// <returns>The loaded settings, or a default instance.</returns>
    public static AppSettings Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return new AppSettings();
        }

        try
        {
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Options) ?? new AppSettings();
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
    }

    /// <summary>Saves settings to <paramref name="path"/>.</summary>
    /// <param name="path">The settings file path.</param>
    /// <param name="settings">The settings to persist.</param>
    public static void Save(string path, AppSettings settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(settings);

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(settings, Options));
    }

    /// <summary>Deletes the settings file if present.</summary>
    /// <param name="path">The settings file path.</param>
    public static void Clear(string path)
    {
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
