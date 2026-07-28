using System;
using System.Collections.Generic;
using MouseKeyProxy.Common;

namespace MouseKeyProxy.Commands.ShareMount;

/// <summary>
/// TR-MKP-SHARE-WINFSP: injectable seam between WinFsp (or other host FS layers) and the appliance
/// folder-share surface. Implementations may be local (<see cref="LocalFolderShareStore"/>) or remote
/// (gRPC <see cref="FolderShareClient"/>).
/// </summary>
public interface IShareFileBackend
{
    /// <summary>Lists entries under a share-relative directory (empty = root).</summary>
    FolderShareResult List(string relativeDirectory, out IReadOnlyList<FolderShareEntry> entries);

    /// <summary>Gets metadata for a single entry, or the root when <paramref name="relativePath"/> is empty.</summary>
    FolderShareResult TryGetEntry(string relativePath, out FolderShareEntry? entry);

    /// <summary>Reads an entire file into memory.</summary>
    FolderShareResult ReadAllBytes(string relativePath, out byte[] data);

    /// <summary>Creates or overwrites a file with the given content.</summary>
    FolderShareResult WriteAllBytes(string relativePath, byte[] data);

    /// <summary>Creates a directory (and parents) under the share root.</summary>
    FolderShareResult CreateDirectory(string relativeDirectory);

    /// <summary>Deletes a file or directory under the share root.</summary>
    FolderShareResult Delete(string relativePath, bool recursive);

    /// <summary>Renames or moves an entry within the share root.</summary>
    FolderShareResult Rename(string relativePath, string newRelativePath, bool overwrite);
}
