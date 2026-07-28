using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using Fsp;
using MouseKeyProxy.Common;
using IoFileAttributes = System.IO.FileAttributes;
using FileInfo = Fsp.Interop.FileInfo;
using VolumeInfo = Fsp.Interop.VolumeInfo;

namespace MouseKeyProxy.Commands.ShareMount;

/// <summary>
/// TR-MKP-SHARE-WINFSP: WinFsp <see cref="FileSystemBase"/> that maps volume operations onto
/// <see cref="ShareFileBridge"/> / <see cref="IShareFileBackend"/> (remote gRPC or local store).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ShareWinFspFileSystem : FileSystemBase
{
    private readonly ShareFileBridge _bridge;
    private readonly string _volumeLabel;
    private readonly byte[] _securityDescriptor;
    private readonly object _gate = new();

    /// <summary>Creates a file system over <paramref name="bridge"/>.</summary>
    public ShareWinFspFileSystem(ShareFileBridge bridge, string volumeLabel = "MouseKeyProxy")
    {
        _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
        _volumeLabel = string.IsNullOrWhiteSpace(volumeLabel) ? "MouseKeyProxy" : volumeLabel.Trim();
        _securityDescriptor = BuildDefaultSecurityDescriptor();
    }

    /// <summary>Underlying bridge (tests / diagnostics).</summary>
    public ShareFileBridge Bridge => _bridge;

    /// <inheritdoc />
    public override int GetVolumeInfo(out VolumeInfo VolumeInfo)
    {
        VolumeInfo = default;
        VolumeInfo.TotalSize = 64UL * 1024 * 1024 * 1024;
        VolumeInfo.FreeSize = 32UL * 1024 * 1024 * 1024;
        try
        {
            VolumeInfo.SetVolumeLabel(_volumeLabel);
        }
        catch
        {
            // label is best-effort
        }

        return STATUS_SUCCESS;
    }

    /// <inheritdoc />
    public override int GetSecurityByName(
        string FileName,
        out uint FileAttributes /* or Null */,
        ref byte[] SecurityDescriptor)
    {
        FileAttributes = (uint)IoFileAttributes.Directory;
        SecurityDescriptor = _securityDescriptor;

        var result = _bridge.GetEntryHostPath(FileName, out var entry);
        if (!result.Ok || entry is null)
        {
            if (string.Equals(NormalizeKey(FileName), string.Empty, StringComparison.Ordinal))
            {
                FileAttributes = (uint)IoFileAttributes.Directory;
                return STATUS_SUCCESS;
            }

            return MapError(result.ErrorCode);
        }

        FileAttributes = entry.IsDirectory
            ? (uint)IoFileAttributes.Directory
            : (uint)(IoFileAttributes.Archive | IoFileAttributes.Normal);
        return STATUS_SUCCESS;
    }

    /// <inheritdoc />
    public override int Open(
        string FileName,
        uint CreateOptions,
        uint GrantedAccess,
        out object FileNode,
        out object FileDesc,
        out FileInfo FileInfo,
        out string NormalizedName)
    {
        FileNode = null!;
        FileDesc = null!;
        FileInfo = default;
        NormalizedName = null!;

        if (!SharePathMapper.TryMapToRelative(FileName, out var relative, out var err, out _))
        {
            return MapError(err);
        }

        lock (_gate)
        {
            var result = _bridge.GetEntryHostPath(FileName, out var entry);
            if (!result.Ok || entry is null)
            {
                if (string.IsNullOrEmpty(relative))
                {
                    var rootNode = FileNodeState.CreateDirectory(string.Empty);
                    FileNode = rootNode;
                    FileDesc = rootNode;
                    FillFileInfo(rootNode, out FileInfo);
                    NormalizedName = "\\";
                    return STATUS_SUCCESS;
                }

                return MapError(result.ErrorCode);
            }

            var node = entry.IsDirectory
                ? FileNodeState.CreateDirectory(relative)
                : OpenFileNode(relative, entry);
            if (node is null)
            {
                return STATUS_OBJECT_NAME_NOT_FOUND;
            }

            FileNode = node;
            FileDesc = node;
            FillFileInfo(node, out FileInfo);
            NormalizedName = "\\" + relative.Replace('/', '\\');
            return STATUS_SUCCESS;
        }
    }

    /// <inheritdoc />
    public override int Create(
        string FileName,
        uint CreateOptions,
        uint GrantedAccess,
        uint FileAttributes,
        byte[] SecurityDescriptor,
        ulong AllocationSize,
        out object FileNode,
        out object FileDesc,
        out FileInfo FileInfo,
        out string NormalizedName)
    {
        FileNode = null!;
        FileDesc = null!;
        FileInfo = default;
        NormalizedName = null!;

        if (!SharePathMapper.TryMapToRelative(FileName, out var relative, out var err, out _))
        {
            return MapError(err);
        }

        if (string.IsNullOrEmpty(relative))
        {
            return STATUS_INVALID_PARAMETER;
        }

        lock (_gate)
        {
            var existing = _bridge.GetEntryHostPath(FileName, out var entry);
            if (existing.Ok && entry is not null)
            {
                return STATUS_OBJECT_NAME_COLLISION;
            }

            var isDir = (CreateOptions & FILE_DIRECTORY_FILE) != 0
                        || (FileAttributes & (uint)IoFileAttributes.Directory) != 0;

            if (isDir)
            {
                var mkdir = _bridge.CreateDirectoryHostPath(FileName);
                if (!mkdir.Ok)
                {
                    return MapError(mkdir.ErrorCode);
                }

                var node = FileNodeState.CreateDirectory(relative);
                FileNode = node;
                FileDesc = node;
                FillFileInfo(node, out FileInfo);
                NormalizedName = "\\" + relative.Replace('/', '\\');
                return STATUS_SUCCESS;
            }

            var write = _bridge.WriteFileHostPath(FileName, Array.Empty<byte>());
            if (!write.Ok)
            {
                return MapError(write.ErrorCode);
            }

            var fileNode = FileNodeState.CreateFile(relative, Array.Empty<byte>(), dirty: false);
            FileNode = fileNode;
            FileDesc = fileNode;
            FillFileInfo(fileNode, out FileInfo);
            NormalizedName = "\\" + relative.Replace('/', '\\');
            return STATUS_SUCCESS;
        }
    }

    /// <inheritdoc />
    public override int Overwrite(
        object FileNode,
        object FileDesc,
        uint FileAttributes,
        bool ReplaceFileAttributes,
        ulong AllocationSize,
        out FileInfo FileInfo)
    {
        FileInfo = default;
        if (FileNode is not FileNodeState node || node.IsDirectory)
        {
            return STATUS_INVALID_PARAMETER;
        }

        lock (_gate)
        {
            node.Data = Array.Empty<byte>();
            node.Dirty = true;
            var write = _bridge.WriteFileHostPath(ToHost(node.RelativePath), node.Data);
            if (!write.Ok)
            {
                return MapError(write.ErrorCode);
            }

            node.Dirty = false;
            FillFileInfo(node, out FileInfo);
            return STATUS_SUCCESS;
        }
    }

    /// <inheritdoc />
    public override void Cleanup(object FileNode, object FileDesc, string FileName, uint Flags)
    {
        if (FileNode is not FileNodeState node)
        {
            return;
        }

        lock (_gate)
        {
            if ((Flags & CleanupDelete) != 0)
            {
                if (!string.IsNullOrEmpty(node.RelativePath))
                {
                    _bridge.DeleteHostPath(ToHost(node.RelativePath), recursive: true);
                }

                return;
            }

            if (!node.IsDirectory && node.Dirty)
            {
                _bridge.WriteFileHostPath(ToHost(node.RelativePath), node.Data ?? Array.Empty<byte>());
                node.Dirty = false;
            }
        }
    }

    /// <inheritdoc />
    public override void Close(object FileNode, object FileDesc)
    {
        if (FileNode is not FileNodeState node)
        {
            return;
        }

        lock (_gate)
        {
            if (!node.IsDirectory && node.Dirty)
            {
                _bridge.WriteFileHostPath(ToHost(node.RelativePath), node.Data ?? Array.Empty<byte>());
                node.Dirty = false;
            }
        }
    }

    /// <inheritdoc />
    public override int Read(
        object FileNode,
        object FileDesc,
        IntPtr Buffer,
        ulong Offset,
        uint Length,
        out uint BytesTransferred)
    {
        BytesTransferred = 0;
        if (FileNode is not FileNodeState node || node.IsDirectory)
        {
            return STATUS_INVALID_PARAMETER;
        }

        var data = node.Data ?? Array.Empty<byte>();
        if (Offset >= (ulong)data.LongLength)
        {
            return STATUS_END_OF_FILE;
        }

        var available = (ulong)data.LongLength - Offset;
        var toCopy = (uint)Math.Min(available, Length);
        if (toCopy > 0)
        {
            Marshal.Copy(data, (int)Offset, Buffer, (int)toCopy);
        }

        BytesTransferred = toCopy;
        return STATUS_SUCCESS;
    }

    /// <inheritdoc />
    public override int Write(
        object FileNode,
        object FileDesc,
        IntPtr Buffer,
        ulong Offset,
        uint Length,
        bool WriteToEndOfFile,
        bool ConstrainedIo,
        out uint BytesTransferred,
        out FileInfo FileInfo)
    {
        BytesTransferred = 0;
        FileInfo = default;
        if (FileNode is not FileNodeState node || node.IsDirectory)
        {
            return STATUS_INVALID_PARAMETER;
        }

        lock (_gate)
        {
            var data = node.Data ?? Array.Empty<byte>();
            var offset = WriteToEndOfFile ? (ulong)data.LongLength : Offset;
            if (ConstrainedIo && offset >= (ulong)data.LongLength)
            {
                BytesTransferred = 0;
                FillFileInfo(node, out FileInfo);
                return STATUS_SUCCESS;
            }

            var end = offset + Length;
            if (end > int.MaxValue)
            {
                return STATUS_INVALID_PARAMETER;
            }

            if ((int)end > data.Length)
            {
                var grown = new byte[(int)end];
                System.Buffer.BlockCopy(data, 0, grown, 0, data.Length);
                data = grown;
            }

            if (Length > 0)
            {
                var chunk = new byte[Length];
                Marshal.Copy(Buffer, chunk, 0, (int)Length);
                System.Buffer.BlockCopy(chunk, 0, data, (int)offset, (int)Length);
            }

            node.Data = data;
            node.Dirty = true;
            BytesTransferred = Length;
            FillFileInfo(node, out FileInfo);
            return STATUS_SUCCESS;
        }
    }

    /// <inheritdoc />
    public override int Flush(object FileNode, object FileDesc, out FileInfo FileInfo)
    {
        FileInfo = default;
        if (FileNode is null)
        {
            return STATUS_SUCCESS;
        }

        if (FileNode is not FileNodeState node)
        {
            return STATUS_INVALID_PARAMETER;
        }

        lock (_gate)
        {
            if (!node.IsDirectory && node.Dirty)
            {
                var write = _bridge.WriteFileHostPath(ToHost(node.RelativePath), node.Data ?? Array.Empty<byte>());
                if (!write.Ok)
                {
                    return MapError(write.ErrorCode);
                }

                node.Dirty = false;
            }

            FillFileInfo(node, out FileInfo);
            return STATUS_SUCCESS;
        }
    }

    /// <inheritdoc />
    public override int GetFileInfo(object FileNode, object FileDesc, out FileInfo FileInfo)
    {
        FileInfo = default;
        if (FileNode is not FileNodeState node)
        {
            return STATUS_INVALID_PARAMETER;
        }

        FillFileInfo(node, out FileInfo);
        return STATUS_SUCCESS;
    }

    /// <inheritdoc />
    public override int SetFileSize(
        object FileNode,
        object FileDesc,
        ulong NewSize,
        bool SetAllocationSize,
        out FileInfo FileInfo)
    {
        FileInfo = default;
        if (FileNode is not FileNodeState node || node.IsDirectory)
        {
            return STATUS_INVALID_PARAMETER;
        }

        if (NewSize > int.MaxValue)
        {
            return STATUS_INVALID_PARAMETER;
        }

        lock (_gate)
        {
            var data = node.Data ?? Array.Empty<byte>();
            if ((int)NewSize != data.Length)
            {
                var resized = new byte[(int)NewSize];
                System.Buffer.BlockCopy(data, 0, resized, 0, Math.Min(data.Length, resized.Length));
                node.Data = resized;
                node.Dirty = true;
            }

            FillFileInfo(node, out FileInfo);
            return STATUS_SUCCESS;
        }
    }

    /// <inheritdoc />
    public override int CanDelete(object FileNode, object FileDesc, string FileName)
    {
        if (FileNode is not FileNodeState node)
        {
            return STATUS_INVALID_PARAMETER;
        }

        if (string.IsNullOrEmpty(node.RelativePath))
        {
            return STATUS_ACCESS_DENIED;
        }

        if (node.IsDirectory)
        {
            var list = _bridge.ListHostPath(ToHost(node.RelativePath), out var entries);
            if (list.Ok && entries.Count > 0)
            {
                return STATUS_DIRECTORY_NOT_EMPTY;
            }
        }

        return STATUS_SUCCESS;
    }

    /// <inheritdoc />
    public override int Rename(
        object FileNode,
        object FileDesc,
        string FileName,
        string NewFileName,
        bool ReplaceIfExists)
    {
        if (FileNode is not FileNodeState node)
        {
            return STATUS_INVALID_PARAMETER;
        }

        lock (_gate)
        {
            if (!node.IsDirectory && node.Dirty)
            {
                _bridge.WriteFileHostPath(ToHost(node.RelativePath), node.Data ?? Array.Empty<byte>());
                node.Dirty = false;
            }

            var rename = _bridge.RenameHostPath(FileName, NewFileName, overwrite: ReplaceIfExists);
            if (!rename.Ok)
            {
                return MapError(rename.ErrorCode);
            }

            if (SharePathMapper.TryMapToRelative(NewFileName, out var newRel, out _, out _))
            {
                node.RelativePath = newRel;
            }

            return STATUS_SUCCESS;
        }
    }

    /// <inheritdoc />
    public override int GetSecurity(object FileNode, object FileDesc, ref byte[] SecurityDescriptor)
    {
        SecurityDescriptor = _securityDescriptor;
        return STATUS_SUCCESS;
    }

    /// <inheritdoc />
    public override bool ReadDirectoryEntry(
        object FileNode,
        object FileDesc,
        string Pattern,
        string Marker,
        ref object Context,
        out string FileName,
        out FileInfo FileInfo)
    {
        FileName = null!;
        FileInfo = default;
        if (FileNode is not FileNodeState node || !node.IsDirectory)
        {
            return false;
        }

        if (Context is not DirEnumState state)
        {
            var list = _bridge.ListHostPath(ToHost(node.RelativePath), out var entries);
            var names = new List<(string Name, FileNodeState Meta)>();
            if (string.IsNullOrEmpty(Marker))
            {
                names.Add((".", node));
                names.Add(("..", node));
            }

            if (list.Ok)
            {
                foreach (var e in entries.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrEmpty(Pattern) && Pattern != "*"
                        && !MatchesSimplePattern(e.Name, Pattern))
                    {
                        continue;
                    }

                    var childRel = string.IsNullOrEmpty(node.RelativePath)
                        ? e.Name
                        : node.RelativePath + "/" + e.Name;
                    var child = e.IsDirectory
                        ? FileNodeState.CreateDirectory(childRel)
                        : FileNodeState.CreateFile(childRel, Array.Empty<byte>(), dirty: false, sizeHint: e.SizeBytes, modified: e.ModifiedUtc);
                    names.Add((e.Name, child));
                }
            }

            var start = 0;
            if (!string.IsNullOrEmpty(Marker))
            {
                for (var i = 0; i < names.Count; i++)
                {
                    if (string.Equals(names[i].Name, Marker, StringComparison.OrdinalIgnoreCase))
                    {
                        start = i + 1;
                        break;
                    }
                }
            }

            state = new DirEnumState(names, start);
            Context = state;
        }

        if (state.Index >= state.Entries.Count)
        {
            return false;
        }

        var item = state.Entries[state.Index++];
        FileName = item.Name;
        FillFileInfo(item.Meta, out FileInfo);
        return true;
    }

    private FileNodeState? OpenFileNode(string relative, FolderShareEntry entry)
    {
        var read = _bridge.Backend.ReadAllBytes(relative, out var data);
        if (!read.Ok)
        {
            // Directory open path already handled; treat missing as not found
            return null;
        }

        return FileNodeState.CreateFile(relative, data, dirty: false, sizeHint: data.LongLength, modified: entry.ModifiedUtc);
    }

    private static void FillFileInfo(FileNodeState node, out FileInfo info)
    {
        info = default;
        info.FileAttributes = node.IsDirectory
            ? (uint)IoFileAttributes.Directory
            : (uint)(IoFileAttributes.Archive | IoFileAttributes.Normal);
        var size = node.IsDirectory ? 0UL : (ulong)(node.Data?.LongLength ?? node.SizeHint);
        info.FileSize = size;
        info.AllocationSize = (size + 4095) / 4096 * 4096;
        var ft = ToFileTime(node.ModifiedUtc);
        info.CreationTime = ft;
        info.LastAccessTime = ft;
        info.LastWriteTime = ft;
        info.ChangeTime = ft;
        info.IndexNumber = node.IndexNumber;
    }

    private static ulong ToFileTime(DateTimeOffset dto)
    {
        try
        {
            return (ulong)dto.UtcDateTime.ToFileTimeUtc();
        }
        catch
        {
            return (ulong)DateTime.UtcNow.ToFileTimeUtc();
        }
    }

    private static string ToHost(string relative)
        => string.IsNullOrEmpty(relative) ? "\\" : "\\" + relative.Replace('/', '\\');

    private static string NormalizeKey(string? fileName)
    {
        SharePathMapper.TryMapToRelative(fileName, out var rel, out _, out _);
        return rel;
    }

    private static int MapError(string? errorCode) => errorCode switch
    {
        "NOT_FOUND" => STATUS_OBJECT_NAME_NOT_FOUND,
        "PATH_INVALID" => STATUS_OBJECT_NAME_INVALID,
        "ALREADY_EXISTS" => STATUS_OBJECT_NAME_COLLISION,
        "NOT_EMPTY" => STATUS_DIRECTORY_NOT_EMPTY,
        "IS_DIRECTORY" => STATUS_FILE_IS_A_DIRECTORY,
        "IS_FILE" => STATUS_NOT_A_DIRECTORY,
        "SHARE_DISABLED" => STATUS_DEVICE_OFF_LINE,
        "FILE_TOO_LARGE" => STATUS_DISK_FULL,
        "ACCESS_DENIED" => STATUS_ACCESS_DENIED,
        _ => STATUS_ACCESS_DENIED,
    };

    private static bool MatchesSimplePattern(string name, string pattern)
    {
        // Minimal * and ? support for directory enumeration.
        pattern ??= "*";
        if (pattern is "*" or "*.*")
        {
            return true;
        }

        try
        {
            return System.Text.RegularExpressions.Regex.IsMatch(
                name,
                "^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        }
        catch
        {
            return string.Equals(name, pattern, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static byte[] BuildDefaultSecurityDescriptor()
    {
        // Allow Everyone full control on the virtual volume (share is already sandboxed/auth'd remotely).
        var security = new RawSecurityDescriptor(
            "O:BAG:BAD:P(A;;FA;;;WD)");
        var bytes = new byte[security.BinaryLength];
        security.GetBinaryForm(bytes, 0);
        return bytes;
    }

    private sealed class DirEnumState
    {
        public DirEnumState(List<(string Name, FileNodeState Meta)> entries, int index)
        {
            Entries = entries;
            Index = index;
        }

        public List<(string Name, FileNodeState Meta)> Entries { get; }
        public int Index { get; set; }
    }

    /// <summary>Per-handle state for an open file or directory on the virtual volume.</summary>
    internal sealed class FileNodeState
    {
        private static long _nextIndex = 1;

        private FileNodeState(string relativePath, bool isDirectory, byte[]? data, long sizeHint, DateTimeOffset modifiedUtc)
        {
            RelativePath = relativePath;
            IsDirectory = isDirectory;
            Data = data;
            SizeHint = sizeHint;
            ModifiedUtc = modifiedUtc;
            IndexNumber = (ulong)System.Threading.Interlocked.Increment(ref _nextIndex);
        }

        public string RelativePath { get; set; }
        public bool IsDirectory { get; }
        public byte[]? Data { get; set; }
        public long SizeHint { get; set; }
        public bool Dirty { get; set; }
        public DateTimeOffset ModifiedUtc { get; set; }
        public ulong IndexNumber { get; }

        public static FileNodeState CreateDirectory(string relative)
            => new(relative, true, null, 0, DateTimeOffset.UtcNow);

        public static FileNodeState CreateFile(
            string relative,
            byte[] data,
            bool dirty,
            long sizeHint = -1,
            DateTimeOffset? modified = null)
        {
            data ??= Array.Empty<byte>();
            return new FileNodeState(
                relative,
                false,
                data,
                sizeHint >= 0 ? sizeHint : data.LongLength,
                modified ?? DateTimeOffset.UtcNow)
            {
                Dirty = dirty,
            };
        }
    }
}
