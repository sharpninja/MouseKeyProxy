using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MouseKeyProxy.Common;
using Xunit;

namespace MouseKeyProxy.Common.Tests;

/// <summary>
/// FR-MKP-013: FileSystemWatcher on the shared FS root raises device events for local handlers and host mirror.
/// </summary>
public class FsShareWatcherTests
{
    /// <summary>Creating a file under the watch root emits FsFileCreated with a relative path.</summary>
    [Fact]
    public async Task CreateFile_EmitsFsFileCreated()
    {
        var root = Path.Combine(Path.GetTempPath(), "mkp-fs-watch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var bus = new DeviceEventBus();
            using var watcher = new FsShareWatcher(bus, root);
            Assert.True(watcher.TryStart());

            var received = new List<DeviceEvent>();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var reader = Task.Run(async () =>
            {
                await foreach (var e in bus.SubscribeAsync(cts.Token))
                {
                    received.Add(e);
                    if (e.Kind == DeviceEventKind.FsFileCreated)
                    {
                        break;
                    }
                }
            }, cts.Token);

            await Task.Delay(150, TestContext.Current.CancellationToken);
            var file = Path.Combine(root, "install-note.txt");
            await File.WriteAllTextAsync(file, "hello", TestContext.Current.CancellationToken);

            await reader;

            Assert.Contains(received, e =>
                e.Kind == DeviceEventKind.FsFileCreated
                && e.Path is not null
                && e.Path.Replace('\\', '/').Contains("install-note.txt", StringComparison.Ordinal));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* best effort */ }
        }
    }

    /// <summary>Deleting a file emits FsFileDeleted.</summary>
    [Fact]
    public async Task DeleteFile_EmitsFsFileDeleted()
    {
        var root = Path.Combine(Path.GetTempPath(), "mkp-fs-watch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "gone.txt");
        await File.WriteAllTextAsync(file, "x", TestContext.Current.CancellationToken);
        try
        {
            var bus = new DeviceEventBus();
            using var watcher = new FsShareWatcher(bus, root);
            Assert.True(watcher.TryStart());

            var received = new List<DeviceEvent>();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var reader = Task.Run(async () =>
            {
                await foreach (var e in bus.SubscribeAsync(cts.Token))
                {
                    if (e.Kind == DeviceEventKind.FsFileDeleted)
                    {
                        received.Add(e);
                        break;
                    }
                }
            }, cts.Token);

            await Task.Delay(150, TestContext.Current.CancellationToken);
            File.Delete(file);
            await reader;

            Assert.Single(received);
            Assert.Equal(DeviceEventKind.FsFileDeleted, received[0].Kind);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* best effort */ }
        }
    }

    /// <summary>TryStart returns false when the path is missing.</summary>
    [Fact]
    public void TryStart_MissingPath_ReturnsFalse()
    {
        var bus = new DeviceEventBus();
        var missing = Path.Combine(Path.GetTempPath(), "mkp-missing-" + Guid.NewGuid().ToString("N"));
        using var watcher = new FsShareWatcher(bus, missing);
        Assert.False(watcher.TryStart());
        Assert.False(watcher.IsWatching);
    }
}
