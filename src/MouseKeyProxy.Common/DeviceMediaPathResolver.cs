using System;
using System.IO;

namespace MouseKeyProxy.Common;

/// <summary>
/// Resolves CD-ROM / floppy media paths for device-local vs host-uploaded content.
/// </summary>
public static class DeviceMediaPathResolver
{
    /// <summary>Root for appliance-owned media files.</summary>
    public static string DeviceMediaRoot =>
        Environment.GetEnvironmentVariable("MKP_MEDIA_DEVICE_ROOT")
        ?? (OperatingSystem.IsLinux()
            ? "/var/lib/mousekeyproxy/media/device"
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MouseKeyProxy", "media", "device"));

    /// <summary>Root for host-uploaded media (gRPC put / operator copy).</summary>
    public static string HostMediaRoot =>
        Environment.GetEnvironmentVariable("MKP_MEDIA_HOST_ROOT")
        ?? (OperatingSystem.IsLinux()
            ? "/var/lib/mousekeyproxy/media/host"
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MouseKeyProxy", "media", "host"));

    /// <summary>
    /// Resolves <paramref name="spec"/> to an absolute path on the appliance, or null when ejected.
    /// </summary>
    /// <param name="spec">Media selection.</param>
    /// <param name="absolutePath">Resolved absolute path when successful and not empty.</param>
    /// <param name="errorCode">Error code when resolution fails.</param>
    /// <param name="errorMessage">Human-readable error.</param>
    /// <param name="requireExists">When true, the file must already exist.</param>
    /// <returns>True when ejected (null path) or a valid absolute path was produced.</returns>
    public static bool TryResolve(
        StorageMediaSpec? spec,
        out string? absolutePath,
        out string errorCode,
        out string errorMessage,
        bool requireExists = true)
    {
        absolutePath = null;
        errorCode = string.Empty;
        errorMessage = string.Empty;

        if (spec is null || string.IsNullOrWhiteSpace(spec.Path))
        {
            return true; // ejected / no media
        }

        var path = spec.Path.Trim().Replace('\\', '/');
        if (path.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(path) && spec.Source == DeviceMediaSource.Host)
        {
            // Host media must be relative under the inbox.
            if (spec.Source == DeviceMediaSource.Host)
            {
                errorCode = "MEDIA_PATH_INVALID";
                errorMessage = "Host media path must be a relative name under the host media root (no '..' or absolute paths).";
                return false;
            }
        }

        if (path.Contains("..", StringComparison.Ordinal))
        {
            errorCode = "MEDIA_PATH_INVALID";
            errorMessage = "Media path must not contain '..'.";
            return false;
        }

        string full;
        if (spec.Source == DeviceMediaSource.Host)
        {
            full = Path.GetFullPath(Path.Combine(HostMediaRoot, path));
            var root = Path.GetFullPath(HostMediaRoot);
            if (!full.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
            {
                errorCode = "MEDIA_PATH_INVALID";
                errorMessage = "Host media path escapes the host media root.";
                return false;
            }
        }
        else if (Path.IsPathRooted(spec.Path))
        {
            full = Path.GetFullPath(spec.Path);
        }
        else
        {
            full = Path.GetFullPath(Path.Combine(DeviceMediaRoot, path));
        }

        if (requireExists && !File.Exists(full))
        {
            errorCode = "MEDIA_NOT_FOUND";
            errorMessage = $"Media file not found: {full}";
            return false;
        }

        absolutePath = full;
        return true;
    }
}
