using System;
using System.IO;

namespace MouseKeyProxy.Commands.ShareMount;

/// <summary>
/// TR-MKP-SHARE-WINFSP: maps Windows file-system paths from a virtual volume onto sandboxed
/// share-relative paths (forward slashes, no <c>..</c>, never rooted).
/// </summary>
public static class SharePathMapper
{
    /// <summary>
    /// Converts a WinFsp/volume path (e.g. <c>\folder\file.txt</c> or <c>folder/file.txt</c>) into a
    /// share-relative path, or fails with a stable error code when the path would escape the root.
    /// </summary>
    /// <param name="fileName">Path as presented by the host FS layer (leading separators allowed).</param>
    /// <param name="relative">Normalized relative path on success (empty string for volume root).</param>
    /// <param name="errorCode">Empty on success; otherwise <c>PATH_INVALID</c>.</param>
    /// <param name="errorMessage">Human-readable detail when invalid.</param>
    /// <returns>True when <paramref name="relative"/> is safe to use against the share backend.</returns>
    public static bool TryMapToRelative(
        string? fileName,
        out string relative,
        out string errorCode,
        out string errorMessage)
    {
        relative = string.Empty;
        errorCode = string.Empty;
        errorMessage = string.Empty;

        if (fileName is null)
        {
            return true;
        }

        var normalized = fileName.Replace('\\', '/').Trim();
        while (normalized.StartsWith("/", StringComparison.Ordinal))
        {
            normalized = normalized[1..];
        }

        // Strip drive-letter prefixes that some callers may pass (e.g. "Z:/foo").
        if (normalized.Length >= 2 && normalized[1] == ':' && char.IsLetter(normalized[0]))
        {
            normalized = normalized[2..];
            while (normalized.StartsWith("/", StringComparison.Ordinal))
            {
                normalized = normalized[1..];
            }
        }

        if (normalized.Length == 0)
        {
            relative = string.Empty;
            return true;
        }

        if (normalized.Contains("..", StringComparison.Ordinal)
            || Path.IsPathRooted(normalized)
            || normalized.Contains(':', StringComparison.Ordinal))
        {
            errorCode = "PATH_INVALID";
            errorMessage = "Path must be relative to the share root and must not contain '..' or drive roots.";
            return false;
        }

        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            relative = string.Empty;
            return true;
        }

        foreach (var part in parts)
        {
            if (part is "." or ".." || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                errorCode = "PATH_INVALID";
                errorMessage = $"Illegal path component: '{part}'.";
                return false;
            }
        }

        relative = string.Join('/', parts);
        return true;
    }

    /// <summary>Returns the parent share-relative directory of <paramref name="relativePath"/> (empty for root children).</summary>
    public static string GetParentRelative(string relativePath)
    {
        relativePath ??= string.Empty;
        relativePath = relativePath.Replace('\\', '/').Trim('/');
        if (string.IsNullOrEmpty(relativePath))
        {
            return string.Empty;
        }

        var idx = relativePath.LastIndexOf('/');
        return idx < 0 ? string.Empty : relativePath[..idx];
    }

    /// <summary>Returns the final path segment of a share-relative path.</summary>
    public static string GetLeafName(string relativePath)
    {
        relativePath ??= string.Empty;
        relativePath = relativePath.Replace('\\', '/').Trim('/');
        if (string.IsNullOrEmpty(relativePath))
        {
            return string.Empty;
        }

        var idx = relativePath.LastIndexOf('/');
        return idx < 0 ? relativePath : relativePath[(idx + 1)..];
    }

    /// <summary>Joins a parent directory and leaf name into a share-relative path.</summary>
    public static string CombineRelative(string parentRelative, string leafName)
    {
        parentRelative = (parentRelative ?? string.Empty).Replace('\\', '/').Trim('/');
        leafName = (leafName ?? string.Empty).Replace('\\', '/').Trim('/');
        if (string.IsNullOrEmpty(parentRelative))
        {
            return leafName;
        }

        if (string.IsNullOrEmpty(leafName))
        {
            return parentRelative;
        }

        return parentRelative + "/" + leafName;
    }
}
