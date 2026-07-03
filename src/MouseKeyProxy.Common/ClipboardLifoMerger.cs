using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace MouseKeyProxy.Common;

/// <summary>
/// LIFO stack merge logic + DPAPI at-rest (CurrentUser) for AC4.
/// Pure merge + DPAPI protect for persisted bytes (test can use temp file path).
/// </summary>
public static class ClipboardLifoMerger
{
    public const int MaxEntries = 50;
    private static readonly HashSet<string> PrivacyFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        "ExcludeClipboardContentFromMonitorProcessing",
        "CF_CLIPBOARD_VIEWER_IGNORE"
    };

    public static MergeResult Merge(IReadOnlyList<ClipboardEntry> current, ClipboardEntry incoming, bool skipPrivacy = true)
    {
        if (skipPrivacy && incoming.Formats.Any(IsPrivacyFormat))
        {
            return new MergeResult(current, false);
        }

        var list = current.ToList();

        string hash = ComputeHash(incoming);
        var existing = list.FirstOrDefault(e => ComputeHash(e) == hash);
        if (existing != null)
        {
            list.Remove(existing);
        }

        list.Insert(0, incoming with { Timestamp = DateTimeOffset.UtcNow });

        if (list.Count > MaxEntries)
            list = list.Take(MaxEntries).ToList();

        return new MergeResult(list, true);
    }

    public static bool IsPrivacyFormat(ClipboardFormat fmt) => PrivacyFormats.Contains(fmt.Name);

    private static string ComputeHash(ClipboardEntry e)
    {
        if (e.Formats.Count == 0) return e.Id;
        using var sha = SHA256.Create();
        var data = e.Formats[0].Data;
        var h = sha.ComputeHash(data);
        return Convert.ToHexString(h) + "|" + e.Formats[0].Name;
    }

    // DPAPI for AC4 at-rest
    public static byte[] ProtectForPersist(ClipboardEntry entry)
    {
        var json = JsonSerializer.Serialize(entry);
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        return ProtectedData.Protect(bytes, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);
    }

    public static ClipboardEntry UnprotectFromPersist(byte[] protectedBytes)
    {
        var bytes = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);
        var json = System.Text.Encoding.UTF8.GetString(bytes);
        return JsonSerializer.Deserialize<ClipboardEntry>(json)!;
    }

    // Component test helper: persist to isolated path (real LocalAppData style)
    public static void PersistHistory(IReadOnlyList<ClipboardEntry> history, string filePath)
    {
        var protectedList = history.Select(ProtectForPersist).ToList();
        File.WriteAllBytes(filePath, JsonSerializer.SerializeToUtf8Bytes(protectedList));
    }

    public static List<ClipboardEntry> LoadHistory(string filePath)
    {
        if (!File.Exists(filePath)) return new List<ClipboardEntry>();
        var protectedList = JsonSerializer.Deserialize<List<byte[]>>(File.ReadAllBytes(filePath))!;
        return protectedList.Select(UnprotectFromPersist).ToList();
    }
}
