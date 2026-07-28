using System;
using System.Runtime.Versioning;
using System.Threading;
using Fsp;

namespace MouseKeyProxy.Commands.ShareMount;

/// <summary>
/// TR-MKP-SHARE-WINFSP: host-side mount/unmount controller for a single WinFsp virtual volume
/// backed by <see cref="IShareFileBackend"/>. Safe when WinFsp is missing (returns a clear error).
/// </summary>
public static class ShareMountHost
{
    private static readonly object Gate = new();
    private static FileSystemHost? _host;
    private static ShareWinFspFileSystem? _fileSystem;
    private static string _mountPoint = string.Empty;

    /// <summary>True when a volume is currently mounted by this process.</summary>
    public static bool IsMounted
    {
        get
        {
            lock (Gate)
            {
                return _host is not null;
            }
        }
    }

    /// <summary>Current mount point when mounted; otherwise empty.</summary>
    public static string CurrentMountPoint
    {
        get
        {
            lock (Gate)
            {
                return _mountPoint;
            }
        }
    }

    /// <summary>
    /// Mounts the share backend as a WinFsp volume at <paramref name="mountPoint"/> (e.g. <c>Z:</c>
    /// or an empty NTFS directory). Fails cleanly when WinFsp is not installed.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static ShareMountResult Mount(
        IShareFileBackend backend,
        string mountPoint,
        string volumeLabel = "MouseKeyProxy")
    {
        if (backend is null)
        {
            return ShareMountResult.Failure("INVALID_ARGUMENT", "Share backend is required.");
        }

        if (string.IsNullOrWhiteSpace(mountPoint))
        {
            return ShareMountResult.Failure("INVALID_ARGUMENT", "Mount point is required (e.g. Z:).");
        }

        if (!OperatingSystem.IsWindows())
        {
            return ShareMountResult.Failure(
                "PLATFORM_UNSUPPORTED",
                "WinFsp virtual drives are only supported on Windows hosts.");
        }

        if (!WinFspRuntime.IsAvailable())
        {
            return ShareMountResult.Failure("WINFSP_RUNTIME_MISSING", WinFspRuntime.DescribeAvailability());
        }

        mountPoint = NormalizeMountPoint(mountPoint);

        lock (Gate)
        {
            if (_host is not null)
            {
                return ShareMountResult.Failure(
                    "ALREADY_MOUNTED",
                    $"A share volume is already mounted at {_mountPoint}. Unmount it first.",
                    _mountPoint);
            }

            try
            {
                var bridge = new ShareFileBridge(backend);
                var fs = new ShareWinFspFileSystem(bridge, volumeLabel);
                var host = new FileSystemHost(fs)
                {
                    SectorSize = 4096,
                    SectorsPerAllocationUnit = 1,
                    MaxComponentLength = 255,
                    FileInfoTimeout = 1000,
                    CaseSensitiveSearch = false,
                    CasePreservedNames = true,
                    UnicodeOnDisk = true,
                    PersistentAcls = false,
                    PostCleanupWhenModifiedOnly = true,
                    VolumeCreationTime = (ulong)DateTime.UtcNow.ToFileTimeUtc(),
                    VolumeSerialNumber = (uint)Environment.TickCount,
                    FileSystemName = "MouseKeyProxy",
                };

                // Synchronized=false: worker threads; Mount returns after the volume is live.
                var status = host.Mount(mountPoint, null, false, 0);
                if (status != FileSystemBase.STATUS_SUCCESS)
                {
                    try { host.Dispose(); } catch { /* ignore */ }
                    return ShareMountResult.Failure(
                        "MOUNT_FAILED",
                        $"WinFsp Mount returned NTSTATUS 0x{status:X8} for '{mountPoint}'. " +
                        "Check that the drive letter is free or the directory mount point exists and is empty.");
                }

                _host = host;
                _fileSystem = fs;
                _mountPoint = host.MountPoint() ?? mountPoint;
                return ShareMountResult.Success(
                    $"Mounted appliance share at {_mountPoint}.",
                    _mountPoint);
            }
            catch (DllNotFoundException ex)
            {
                return ShareMountResult.Failure(
                    "WINFSP_RUNTIME_MISSING",
                    $"{ex.Message}. {WinFspRuntime.InstallHint}");
            }
            catch (Exception ex)
            {
                return ShareMountResult.Failure("MOUNT_FAILED", ex.Message);
            }
        }
    }

    /// <summary>Unmounts the active volume if one is mounted by this process.</summary>
    [SupportedOSPlatform("windows")]
    public static ShareMountResult Unmount()
    {
        if (!OperatingSystem.IsWindows())
        {
            return ShareMountResult.Failure(
                "PLATFORM_UNSUPPORTED",
                "WinFsp virtual drives are only supported on Windows hosts.");
        }

        lock (Gate)
        {
            if (_host is null)
            {
                return ShareMountResult.Failure("NOT_MOUNTED", "No share volume is mounted in this process.");
            }

            var point = _mountPoint;
            try
            {
                _host.Unmount();
                _host.Dispose();
                _host = null;
                _fileSystem = null;
                _mountPoint = string.Empty;
                return ShareMountResult.Success($"Unmounted share volume at {point}.", point);
            }
            catch (Exception ex)
            {
                // Best-effort reset so a stuck host does not permanently block remount.
                try { _host?.Dispose(); } catch { /* ignore */ }
                _host = null;
                _fileSystem = null;
                _mountPoint = string.Empty;
                return ShareMountResult.Failure("UNMOUNT_FAILED", ex.Message, point);
            }
        }
    }

    /// <summary>
    /// Mounts a local directory as the share root (debug / offline demos) without gRPC.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static ShareMountResult MountLocalDirectory(
        string localRootPath,
        string mountPoint,
        string volumeLabel = "MouseKeyProxy")
    {
        if (string.IsNullOrWhiteSpace(localRootPath))
        {
            return ShareMountResult.Failure("INVALID_ARGUMENT", "Local root path is required.");
        }

        var options = new Common.FolderShareOptions
        {
            Enabled = true,
            RootPath = System.IO.Path.GetFullPath(localRootPath),
            ShareName = volumeLabel,
        };
        System.IO.Directory.CreateDirectory(options.RootPath);
        var store = new Common.LocalFolderShareStore(options);
        var backend = new LocalStoreShareBackend(store);
        return Mount(backend, mountPoint, volumeLabel);
    }

    private static string NormalizeMountPoint(string mountPoint)
    {
        mountPoint = mountPoint.Trim();
        if (mountPoint.Length == 1 && char.IsLetter(mountPoint[0]))
        {
            return char.ToUpperInvariant(mountPoint[0]) + ":";
        }

        if (mountPoint.Length == 2 && char.IsLetter(mountPoint[0]) && mountPoint[1] == ':')
        {
            return char.ToUpperInvariant(mountPoint[0]) + ":";
        }

        return mountPoint;
    }
}
