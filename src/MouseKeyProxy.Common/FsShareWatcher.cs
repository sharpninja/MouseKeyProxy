using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MouseKeyProxy.Common;

/// <summary>
/// FR-MKP-013: watches the shared install image mount / folder with
/// <see cref="FileSystemWatcher"/> and publishes FS content events on the device bus.
/// Local C# handlers and the host mirror both consume those events.
/// </summary>
public sealed class FsShareWatcher : IDisposable
{
    private readonly IDeviceEventBus _bus;
    private readonly string _watchRoot;
    private readonly object _gate = new();
    private FileSystemWatcher? _watcher;
    private bool _disposed;

    /// <summary>
    /// Creates a watcher for <paramref name="watchRoot"/> (directory preferred; a single file is also accepted).
    /// </summary>
    /// <param name="bus">Device event bus.</param>
    /// <param name="watchRoot">Absolute path to the shared folder or image file.</param>
    public FsShareWatcher(IDeviceEventBus bus, string watchRoot)
    {
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        _watchRoot = Path.GetFullPath(watchRoot ?? throw new ArgumentNullException(nameof(watchRoot)));
    }

    /// <summary>Root path being watched.</summary>
    public string WatchRoot => _watchRoot;

    /// <summary>True while the watcher is enabled.</summary>
    public bool IsWatching
    {
        get { lock (_gate) { return _watcher is not null; } }
    }

    /// <summary>
    /// Starts watching. No-op if the path does not exist yet (caller may retry after mount).
    /// </summary>
    /// <returns>True when the watcher is active.</returns>
    public bool TryStart()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_watcher is not null)
            {
                return true;
            }

            if (Directory.Exists(_watchRoot))
            {
                _watcher = CreateDirectoryWatcher(_watchRoot);
            }
            else if (File.Exists(_watchRoot))
            {
                _watcher = CreateFileWatcher(_watchRoot);
            }
            else
            {
                return false;
            }

            AttachHandlers(_watcher);
            _watcher.EnableRaisingEvents = true;
            return true;
        }
    }

    /// <summary>Stops watching and disposes the underlying <see cref="FileSystemWatcher"/>.</summary>
    public void Stop()
    {
        lock (_gate)
        {
            if (_watcher is null)
            {
                return;
            }

            _watcher.EnableRaisingEvents = false;
            DetachHandlers(_watcher);
            _watcher.Dispose();
            _watcher = null;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        Stop();
    }

    private FileSystemWatcher CreateDirectoryWatcher(string directory)
    {
        return new FileSystemWatcher(directory)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName
                | NotifyFilters.DirectoryName
                | NotifyFilters.LastWrite
                | NotifyFilters.Size
                | NotifyFilters.CreationTime,
            Filter = "*.*",
            InternalBufferSize = 64 * 1024,
        };
    }

    private FileSystemWatcher CreateFileWatcher(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath) ?? ".";
        var name = Path.GetFileName(filePath);
        return new FileSystemWatcher(dir, name)
        {
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
            InternalBufferSize = 64 * 1024,
        };
    }

    private void AttachHandlers(FileSystemWatcher watcher)
    {
        watcher.Created += OnCreated;
        watcher.Changed += OnChanged;
        watcher.Deleted += OnDeleted;
        watcher.Renamed += OnRenamed;
        watcher.Error += OnError;
    }

    private void DetachHandlers(FileSystemWatcher watcher)
    {
        watcher.Created -= OnCreated;
        watcher.Changed -= OnChanged;
        watcher.Deleted -= OnDeleted;
        watcher.Renamed -= OnRenamed;
        watcher.Error -= OnError;
    }

    private void OnCreated(object sender, FileSystemEventArgs e)
        => Publish(DeviceEventKind.FsFileCreated, e.FullPath, null, "created");

    private void OnChanged(object sender, FileSystemEventArgs e)
        => Publish(DeviceEventKind.FsFileChanged, e.FullPath, null, "changed");

    private void OnDeleted(object sender, FileSystemEventArgs e)
        => Publish(DeviceEventKind.FsFileDeleted, e.FullPath, null, "deleted");

    private void OnRenamed(object sender, RenamedEventArgs e)
        => Publish(DeviceEventKind.FsFileRenamed, e.FullPath, e.OldFullPath, "renamed");

    private void OnError(object sender, ErrorEventArgs e)
    {
        // Buffer overflow or watcher fault: publish a changed-style detail event for diagnostics.
        Publish(
            DeviceEventKind.FsFileChanged,
            _watchRoot,
            null,
            $"watcher-error: {e.GetException().Message}");
    }

    private void Publish(DeviceEventKind kind, string path, string? oldPath, string detail)
    {
        var relative = ToRelative(path);
        var oldRelative = oldPath is null ? null : ToRelative(oldPath);
        var evt = new DeviceEvent(
            kind,
            DateTimeOffset.UtcNow,
            Guid.NewGuid(),
            Detail: detail,
            Path: relative,
            OldPath: oldRelative);

        // FileSystemWatcher callbacks are not async; fire-and-forget onto the bus.
        _ = PublishSafeAsync(evt);
    }

    private async Task PublishSafeAsync(DeviceEvent evt)
    {
        try
        {
            await _bus.PublishAsync(evt).ConfigureAwait(false);
        }
        catch
        {
            // Bus failures must not tear down the watcher thread.
        }
    }

    private string ToRelative(string fullPath)
    {
        try
        {
            var root = _watchRoot;
            if (File.Exists(root))
            {
                root = Path.GetDirectoryName(root) ?? root;
            }

            var full = Path.GetFullPath(fullPath);
            var rootFull = Path.GetFullPath(root);
            if (full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
            {
                var rel = full.Substring(rootFull.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return string.IsNullOrEmpty(rel) ? Path.GetFileName(full) : rel.Replace('\\', '/');
            }

            return full.Replace('\\', '/');
        }
        catch
        {
            return fullPath.Replace('\\', '/');
        }
    }
}

/// <summary>
/// Options for the shared FS content watcher (env: <c>MKP_FS_SHARE_PATH</c>).
/// </summary>
public sealed class FsShareWatchOptions
{
    /// <summary>
    /// Directory (preferred) or image file to watch. Default
    /// <c>/var/lib/mousekeyproxy/share</c> on Linux, under LocalAppData on Windows.
    /// </summary>
    public string WatchPath { get; init; } = DefaultWatchPath();

    /// <summary>Resolves the default share path for the current OS.</summary>
    public static string DefaultWatchPath()
    {
        var env = Environment.GetEnvironmentVariable("MKP_FS_SHARE_PATH");
        if (!string.IsNullOrWhiteSpace(env))
        {
            return env;
        }

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
