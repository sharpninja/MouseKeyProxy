using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MouseKeyProxy.Common;

namespace MouseKeyProxy.Commands.ShareMount;

/// <summary>
/// TR-MKP-SHARE-WINFSP: <see cref="IShareFileBackend"/> over an in-process
/// <see cref="IFolderShareStore"/> (unit tests and local mount demos).
/// </summary>
public sealed class LocalStoreShareBackend : IShareFileBackend
{
    private readonly IFolderShareStore _store;

    /// <summary>Creates a backend over <paramref name="store"/>.</summary>
    public LocalStoreShareBackend(IFolderShareStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <inheritdoc />
    public FolderShareResult List(string relativeDirectory, out IReadOnlyList<FolderShareEntry> entries)
        => _store.List(relativeDirectory, out entries);

    /// <inheritdoc />
    public FolderShareResult TryGetEntry(string relativePath, out FolderShareEntry? entry)
    {
        entry = null;
        relativePath ??= string.Empty;
        relativePath = relativePath.Replace('\\', '/').Trim('/');

        if (string.IsNullOrEmpty(relativePath))
        {
            entry = new FolderShareEntry(
                Name: string.Empty,
                RelativePath: string.Empty,
                IsDirectory: true,
                SizeBytes: 0,
                ModifiedUtc: DateTimeOffset.UtcNow);
            return new FolderShareResult(true, string.Empty, "ok");
        }

        var parent = SharePathMapper.GetParentRelative(relativePath);
        var leaf = SharePathMapper.GetLeafName(relativePath);
        var list = _store.List(parent, out var entries);
        if (!list.Ok)
        {
            return list;
        }

        entry = entries.FirstOrDefault(e =>
            string.Equals(e.Name, leaf, StringComparison.OrdinalIgnoreCase)
            || string.Equals(e.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            return new FolderShareResult(false, "NOT_FOUND", "Path not found.");
        }

        return new FolderShareResult(true, string.Empty, "ok");
    }

    /// <inheritdoc />
    public FolderShareResult ReadAllBytes(string relativePath, out byte[] data)
    {
        data = Array.Empty<byte>();
        var open = _store.OpenRead(relativePath, out var stream, out _);
        if (!open.Ok || stream is null)
        {
            return open;
        }

        try
        {
            using (stream)
            {
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                data = ms.ToArray();
            }

            return new FolderShareResult(true, string.Empty, "ok");
        }
        catch (Exception ex)
        {
            return new FolderShareResult(false, "IO_ERROR", ex.Message);
        }
    }

    /// <inheritdoc />
    public FolderShareResult WriteAllBytes(string relativePath, byte[] data)
    {
        data ??= Array.Empty<byte>();
        var open = _store.OpenWrite(relativePath, data.LongLength, out var stream);
        if (!open.Ok || stream is null)
        {
            return open;
        }

        try
        {
            using (stream)
            {
                stream.Write(data, 0, data.Length);
                stream.Flush();
            }

            return new FolderShareResult(true, string.Empty, "ok");
        }
        catch (Exception ex)
        {
            return new FolderShareResult(false, "IO_ERROR", ex.Message);
        }
    }

    /// <inheritdoc />
    public FolderShareResult CreateDirectory(string relativeDirectory)
        => _store.CreateDirectory(relativeDirectory);

    /// <inheritdoc />
    public FolderShareResult Delete(string relativePath, bool recursive)
        => _store.Delete(relativePath, recursive);

    /// <inheritdoc />
    public FolderShareResult Rename(string relativePath, string newRelativePath, bool overwrite)
        => _store.Rename(relativePath, newRelativePath, overwrite);
}
