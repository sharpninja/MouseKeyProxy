using System;
using System.Collections.Generic;
using System.Linq;
using MouseKeyProxy.Common;

namespace MouseKeyProxy.Commands.ShareMount;

/// <summary>
/// TR-MKP-SHARE-WINFSP: pure operation-dispatch layer from host file paths onto
/// <see cref="IShareFileBackend"/>. Unit tests drive this type against a fake or local backend
/// without the WinFsp kernel driver.
/// </summary>
public sealed class ShareFileBridge
{
    private readonly IShareFileBackend _backend;

    /// <summary>Creates a bridge over <paramref name="backend"/>.</summary>
    public ShareFileBridge(IShareFileBackend backend)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    /// <summary>Backend used by this bridge (for adapters / diagnostics).</summary>
    public IShareFileBackend Backend => _backend;

    /// <summary>Maps a host path and lists the corresponding share directory.</summary>
    public FolderShareResult ListHostPath(string hostPath, out IReadOnlyList<FolderShareEntry> entries)
    {
        entries = Array.Empty<FolderShareEntry>();
        if (!SharePathMapper.TryMapToRelative(hostPath, out var relative, out var err, out var msg))
        {
            return new FolderShareResult(false, err, msg);
        }

        return _backend.List(relative, out entries);
    }

    /// <summary>Gets a single entry by host path (root when path is empty or <c>\</c>).</summary>
    public FolderShareResult GetEntryHostPath(string hostPath, out FolderShareEntry? entry)
    {
        entry = null;
        if (!SharePathMapper.TryMapToRelative(hostPath, out var relative, out var err, out var msg))
        {
            return new FolderShareResult(false, err, msg);
        }

        return _backend.TryGetEntry(relative, out entry);
    }

    /// <summary>Reads a file by host path.</summary>
    public FolderShareResult ReadFileHostPath(string hostPath, out byte[] data)
    {
        data = Array.Empty<byte>();
        if (!SharePathMapper.TryMapToRelative(hostPath, out var relative, out var err, out var msg))
        {
            return new FolderShareResult(false, err, msg);
        }

        return _backend.ReadAllBytes(relative, out data);
    }

    /// <summary>Writes a file by host path (create or overwrite).</summary>
    public FolderShareResult WriteFileHostPath(string hostPath, byte[] data)
    {
        if (!SharePathMapper.TryMapToRelative(hostPath, out var relative, out var err, out var msg))
        {
            return new FolderShareResult(false, err, msg);
        }

        return _backend.WriteAllBytes(relative, data ?? Array.Empty<byte>());
    }

    /// <summary>Creates a directory by host path.</summary>
    public FolderShareResult CreateDirectoryHostPath(string hostPath)
    {
        if (!SharePathMapper.TryMapToRelative(hostPath, out var relative, out var err, out var msg))
        {
            return new FolderShareResult(false, err, msg);
        }

        if (string.IsNullOrEmpty(relative))
        {
            return new FolderShareResult(false, "PATH_INVALID", "Cannot create the share root.");
        }

        return _backend.CreateDirectory(relative);
    }

    /// <summary>Deletes a file or directory by host path.</summary>
    public FolderShareResult DeleteHostPath(string hostPath, bool recursive)
    {
        if (!SharePathMapper.TryMapToRelative(hostPath, out var relative, out var err, out var msg))
        {
            return new FolderShareResult(false, err, msg);
        }

        if (string.IsNullOrEmpty(relative))
        {
            return new FolderShareResult(false, "PATH_INVALID", "Cannot delete the share root.");
        }

        return _backend.Delete(relative, recursive);
    }

    /// <summary>Renames/moves by host paths.</summary>
    public FolderShareResult RenameHostPath(string hostFrom, string hostTo, bool overwrite)
    {
        if (!SharePathMapper.TryMapToRelative(hostFrom, out var from, out var err, out var msg))
        {
            return new FolderShareResult(false, err, msg);
        }

        if (!SharePathMapper.TryMapToRelative(hostTo, out var to, out err, out msg))
        {
            return new FolderShareResult(false, err, msg);
        }

        if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to))
        {
            return new FolderShareResult(false, "PATH_INVALID", "Cannot rename the share root.");
        }

        return _backend.Rename(from, to, overwrite);
    }

    /// <summary>
    /// Convenience used by unit tests and diagnostics: perform list → write → read → mkdir → rename → delete
    /// against the backend via host-style paths and return the first failure (or success).
    /// </summary>
    public FolderShareResult ExerciseFullControlRoundTrip(string scratchDirHostPath = "\\bridge-scratch")
    {
        if (!SharePathMapper.TryMapToRelative(scratchDirHostPath, out var dirRel, out var err, out var msg)
            || string.IsNullOrEmpty(dirRel))
        {
            return new FolderShareResult(false, err.Length > 0 ? err : "PATH_INVALID", msg.Length > 0 ? msg : "scratch path invalid");
        }

        var mkdir = CreateDirectoryHostPath(scratchDirHostPath);
        if (!mkdir.Ok)
        {
            return mkdir;
        }

        var fileHost = scratchDirHostPath.TrimEnd('\\', '/') + "\\note.txt";
        var payload = System.Text.Encoding.UTF8.GetBytes("winfsp-bridge-ok");
        var write = WriteFileHostPath(fileHost, payload);
        if (!write.Ok)
        {
            return write;
        }

        var read = ReadFileHostPath(fileHost, out var data);
        if (!read.Ok)
        {
            return read;
        }

        if (!payload.SequenceEqual(data))
        {
            return new FolderShareResult(false, "CONTENT_MISMATCH", "Read-back bytes did not match write.");
        }

        var list = ListHostPath(scratchDirHostPath, out var entries);
        if (!list.Ok)
        {
            return list;
        }

        if (!entries.Any(e => string.Equals(e.Name, "note.txt", StringComparison.OrdinalIgnoreCase)))
        {
            return new FolderShareResult(false, "LIST_MISMATCH", "Created file not present in listing.");
        }

        var renamedHost = scratchDirHostPath.TrimEnd('\\', '/') + "\\renamed.txt";
        var rename = RenameHostPath(fileHost, renamedHost, overwrite: false);
        if (!rename.Ok)
        {
            return rename;
        }

        var delFile = DeleteHostPath(renamedHost, recursive: false);
        if (!delFile.Ok)
        {
            return delFile;
        }

        var delDir = DeleteHostPath(scratchDirHostPath, recursive: true);
        return delDir;
    }
}
