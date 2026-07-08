using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using MouseKeyProxy.Common;

namespace MouseKeyProxy.Repl;

/// <summary>
/// FR-MKP-006: the REPL `settings` verb - show/set/clear persisted operator settings. Factored out of
/// the command switch so it is unit-testable against an explicit settings path.
/// </summary>
public static class SettingsCommand
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>
    /// Runs a `settings` subcommand against the given store path.
    /// </summary>
    /// <param name="args">Full REPL args (args[0] == "settings").</param>
    /// <param name="path">The settings file path.</param>
    /// <param name="output">Human-readable output.</param>
    /// <returns>Process exit code (0 success).</returns>
    public static int Run(string[] args, string path, out string output)
    {
        var sub = args.Length > 1 ? args[1].ToLowerInvariant() : "show";
        switch (sub)
        {
            case "show":
            {
                var settings = SettingsStore.Load(path);
                output = args.Any(a => a.Equals("--json", StringComparison.OrdinalIgnoreCase))
                    ? JsonSerializer.Serialize(settings, Json)
                    : Format(settings);
                return 0;
            }

            case "set":
            {
                if (args.Length < 4)
                {
                    output = "usage: settings set <key> <value>";
                    return 1;
                }

                var settings = SettingsStore.Load(path);
                if (!TrySet(settings, args[2], args[3], out var error))
                {
                    output = error;
                    return 1;
                }

                SettingsStore.Save(path, settings);
                output = $"{args[2]} = {args[3]}";
                return 0;
            }

            case "clear":
                SettingsStore.Clear(path);
                output = "settings cleared";
                return 0;

            default:
                output = $"unknown settings subcommand: {sub} (use show | set <key> <value> | clear)";
                return 1;
        }
    }

    private static bool TrySet(AppSettings settings, string key, string value, out string error)
    {
        error = string.Empty;
        switch (key.ToLowerInvariant())
        {
            case "remotepeer":
                settings.RemotePeer = value;
                return true;
            case "remotegrpcurl":
                settings.RemoteGrpcUrl = value;
                return true;
            case "clipboardretentiondays":
                if (!int.TryParse(value, out var days))
                {
                    error = $"clipboardRetentionDays must be an integer, got '{value}'";
                    return false;
                }

                settings.ClipboardRetentionDays = days;
                return true;
            case "loglevel":
                settings.LogLevel = value;
                return true;
            default:
                error = $"unknown setting key: {key} (remotePeer | remoteGrpcUrl | clipboardRetentionDays | logLevel)";
                return false;
        }
    }

    private static string Format(AppSettings s)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"remotePeer             = {s.RemotePeer}");
        sb.AppendLine($"remoteGrpcUrl          = {s.RemoteGrpcUrl}");
        sb.AppendLine($"clipboardRetentionDays = {s.ClipboardRetentionDays}");
        sb.Append($"logLevel               = {s.LogLevel}");
        return sb.ToString();
    }
}
