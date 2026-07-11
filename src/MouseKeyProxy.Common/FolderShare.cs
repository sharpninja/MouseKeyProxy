using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace MouseKeyProxy.Common;

/// <summary>
/// FR-MKP-014: options for the appliance folder share (sandboxed directory exposed over gRPC).
/// Enable with <c>MKP_FOLDER_SHARE=1</c>; root via <c>MKP_FOLDER_SHARE_ROOT</c>.
/// </summary>
public sealed class FolderShareOptions
{
    /// <summary>When true, advertise and serve the folder share.</summary>
    public bool Enabled { get; init; }

    /// <summary>Display name for discovery / UI.</summary>
    public string ShareName { get; init; } = "MouseKeyProxy";

    /// <summary>Absolute sandbox root on the device.</summary>
    public string RootPath { get; init; } = DefaultRootPath();

    /// <summary>Maximum single-file upload size in bytes (default 256 MiB).</summary>
    public long MaxFileBytes { get; init; } = 256L * 1024 * 1024;

    /// <summary>Builds options from environment variables.</summary>
    public static FolderShareOptions FromEnvironment()
    {
        var enabled = string.Equals(Environment.GetEnvironmentVariable("MKP_FOLDER_SHARE"), "1", StringComparison.Ordinal);
        var name = Environment.GetEnvironmentVariable("MKP_FOLDER_SHARE_NAME");
        var root = Environment.GetEnvironmentVariable("MKP_FOLDER_SHARE_ROOT");
        return new FolderShareOptions
        {
            Enabled = enabled,
            ShareName = string.IsNullOrWhiteSpace(name) ? "MouseKeyProxy" : name.Trim(),
            RootPath = string.IsNullOrWhiteSpace(root) ? DefaultRootPath() : Path.GetFullPath(root),
        };
    }

    /// <summary>Default share root for the current OS.</summary>
    public static string DefaultRootPath()
    {
        if (OperatingSystem.IsLinux())
        {
            return "/var/lib/mousekeyproxy/share";
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MouseKeyProxy",
            "share");
    }
}

/// <summary>Public info returned for discovery / connect handshake.</summary>
/// <param name="ShareName">Human-readable share name.</param>
/// <param name="RootLabel">Logical root label (not a host path leak of full disk).</param>
/// <param name="ReadWrite">Whether uploads are allowed.</param>
/// <param name="Enabled">Whether the share is currently served.</param>
public sealed record FolderShareInfo(string ShareName, string RootLabel, bool ReadWrite, bool Enabled);

/// <summary>One entry in a folder listing.</summary>
/// <param name="Name">File or directory name.</param>
/// <param name="RelativePath">Path relative to the share root (forward slashes).</param>
/// <param name="IsDirectory">True for directories.</param>
/// <param name="SizeBytes">File size; 0 for directories.</param>
/// <param name="ModifiedUtc">Last write time UTC.</param>
public sealed record FolderShareEntry(
    string Name,
    string RelativePath,
    bool IsDirectory,
    long SizeBytes,
    DateTimeOffset ModifiedUtc);

/// <summary>Result of a folder share operation.</summary>
public sealed record FolderShareResult(bool Ok, string ErrorCode, string Message);

/// <summary>Sandboxed folder share storage used by the gRPC surface.</summary>
public interface IFolderShareStore
{
    /// <summary>Share metadata for discovery / info RPC.</summary>
    FolderShareInfo GetInfo();

    /// <summary>Lists a relative directory (empty = root).</summary>
    FolderShareResult List(string relativeDirectory, out IReadOnlyList<FolderShareEntry> entries);

    /// <summary>Opens a file for reading after path validation.</summary>
    FolderShareResult OpenRead(string relativePath, out Stream? stream, out long length);

    /// <summary>Creates/overwrites a file under the share.</summary>
    FolderShareResult OpenWrite(string relativePath, long expectedLength, out Stream? stream);
}

/// <summary>Local directory implementation of <see cref="IFolderShareStore"/>.</summary>
public sealed class LocalFolderShareStore : IFolderShareStore
{
    private readonly FolderShareOptions _options;

    /// <summary>Creates a store over <paramref name="options"/>.</summary>
    public LocalFolderShareStore(FolderShareOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        if (_options.Enabled)
        {
            Directory.CreateDirectory(_options.RootPath);
        }
    }

    /// <inheritdoc />
    public FolderShareInfo GetInfo()
        => new(
            _options.ShareName,
            RootLabel: "share",
            ReadWrite: true,
            Enabled: _options.Enabled);

    /// <inheritdoc />
    public FolderShareResult List(string relativeDirectory, out IReadOnlyList<FolderShareEntry> entries)
    {
        entries = Array.Empty<FolderShareEntry>();
        if (!_options.Enabled)
        {
            return new FolderShareResult(false, "SHARE_DISABLED", "Folder share is not enabled on this device.");
        }

        if (!TryResolveDirectory(relativeDirectory, out var abs, out var err, out var msg))
        {
            return new FolderShareResult(false, err, msg);
        }

        var list = new List<FolderShareEntry>();
        foreach (var dir in Directory.EnumerateDirectories(abs))
        {
            var name = Path.GetFileName(dir);
            list.Add(new FolderShareEntry(
                name,
                ToRelative(dir),
                IsDirectory: true,
                SizeBytes: 0,
                ModifiedUtc: new DateTimeOffset(Directory.GetLastWriteTimeUtc(dir), TimeSpan.Zero)));
        }

        foreach (var file in Directory.EnumerateFiles(abs))
        {
            var info = new FileInfo(file);
            list.Add(new FolderShareEntry(
                info.Name,
                ToRelative(file),
                IsDirectory: false,
                SizeBytes: info.Length,
                ModifiedUtc: new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero)));
        }

        entries = list.OrderBy(e => e.IsDirectory ? 0 : 1).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
        return new FolderShareResult(true, string.Empty, "ok");
    }

    /// <inheritdoc />
    public FolderShareResult OpenRead(string relativePath, out Stream? stream, out long length)
    {
        stream = null;
        length = 0;
        if (!_options.Enabled)
        {
            return new FolderShareResult(false, "SHARE_DISABLED", "Folder share is not enabled on this device.");
        }

        if (!TryResolveFile(relativePath, out var abs, out var err, out var msg, mustExist: true))
        {
            return new FolderShareResult(false, err, msg);
        }

        stream = new FileStream(abs, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        length = stream.Length;
        return new FolderShareResult(true, string.Empty, "ok");
    }

    /// <inheritdoc />
    public FolderShareResult OpenWrite(string relativePath, long expectedLength, out Stream? stream)
    {
        stream = null;
        if (!_options.Enabled)
        {
            return new FolderShareResult(false, "SHARE_DISABLED", "Folder share is not enabled on this device.");
        }

        if (expectedLength < 0 || expectedLength > _options.MaxFileBytes)
        {
            return new FolderShareResult(false, "FILE_TOO_LARGE", $"File exceeds max size {_options.MaxFileBytes} bytes.");
        }

        if (!TryResolveFile(relativePath, out var abs, out var err, out var msg, mustExist: false))
        {
            return new FolderShareResult(false, err, msg);
        }

        var parent = Path.GetDirectoryName(abs);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        stream = new FileStream(abs, FileMode.Create, FileAccess.Write, FileShare.None);
        return new FolderShareResult(true, string.Empty, "ok");
    }

    private bool TryResolveDirectory(string relative, out string absolute, out string errorCode, out string errorMessage)
    {
        absolute = string.Empty;
        errorCode = string.Empty;
        errorMessage = string.Empty;
        if (!TryMap(relative, out absolute, out errorCode, out errorMessage))
        {
            return false;
        }

        if (!Directory.Exists(absolute))
        {
            errorCode = "NOT_FOUND";
            errorMessage = "Directory not found.";
            return false;
        }

        return true;
    }

    private bool TryResolveFile(string relative, out string absolute, out string errorCode, out string errorMessage, bool mustExist)
    {
        absolute = string.Empty;
        if (!TryMap(relative, out absolute, out errorCode, out errorMessage))
        {
            return false;
        }

        if (mustExist && !File.Exists(absolute))
        {
            errorCode = "NOT_FOUND";
            errorMessage = "File not found.";
            return false;
        }

        if (!mustExist && Directory.Exists(absolute))
        {
            errorCode = "IS_DIRECTORY";
            errorMessage = "Path is a directory.";
            return false;
        }

        return true;
    }

    private bool TryMap(string relative, out string absolute, out string errorCode, out string errorMessage)
    {
        absolute = string.Empty;
        errorCode = string.Empty;
        errorMessage = string.Empty;
        relative ??= string.Empty;
        relative = relative.Replace('\\', '/').Trim('/');

        if (relative.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
        {
            errorCode = "PATH_INVALID";
            errorMessage = "Path must be relative to the share root and must not contain '..'.";
            return false;
        }

        var root = Path.GetFullPath(_options.RootPath);
        absolute = string.IsNullOrEmpty(relative)
            ? root
            : Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));

        var rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!absolute.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(absolute, root, StringComparison.OrdinalIgnoreCase))
        {
            errorCode = "PATH_INVALID";
            errorMessage = "Path escapes the share root.";
            return false;
        }

        return true;
    }

    private string ToRelative(string absolute)
    {
        var root = Path.GetFullPath(_options.RootPath);
        var full = Path.GetFullPath(absolute);
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetFileName(full);
        }

        var rel = full.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return rel.Replace('\\', '/');
    }
}

/// <summary>SHA-256 helper for share transfer integrity checks.</summary>
public static class FolderShareHash
{
    /// <summary>Computes lowercase hex SHA-256 of a stream from the current position.</summary>
    public static async Task<string> Sha256HexAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
