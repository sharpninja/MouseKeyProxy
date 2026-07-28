using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MouseKeyProxy.Commands.ShareMount;
using MouseKeyProxy.Common;
using Xunit;

namespace MouseKeyProxy.Commands.Tests;

/// <summary>
/// TR-MKP-SHARE-WINFSP / FR-MKP-014: unit tests for pure share-bridge path mapping and
/// list/read/write/mkdir/delete/rename dispatch against a real <see cref="LocalFolderShareStore"/>
/// backend (no WinFsp driver, no network).
/// </summary>
public class ShareFileBridgeTests
{
    /// <summary>SharePathMapper rejects escape attempts and normalizes host paths.</summary>
    [Fact]
    public void SharePathMapper_NormalizesAndRejectsEscapes()
    {
        Assert.True(SharePathMapper.TryMapToRelative(@"\docs\readme.txt", out var rel, out _, out _));
        Assert.Equal("docs/readme.txt", rel);

        Assert.True(SharePathMapper.TryMapToRelative("Z:/docs/a.txt", out rel, out _, out _));
        Assert.Equal("docs/a.txt", rel);

        Assert.True(SharePathMapper.TryMapToRelative(@"\", out rel, out _, out _));
        Assert.Equal(string.Empty, rel);

        Assert.False(SharePathMapper.TryMapToRelative(@"\..\secrets", out _, out var err, out _));
        Assert.Equal("PATH_INVALID", err);

        Assert.False(SharePathMapper.TryMapToRelative(@"foo\..\bar", out _, out err, out _));
        Assert.Equal("PATH_INVALID", err);
    }

    /// <summary>
    /// ShareMountHost.Mount fails cleanly with WINFSP_RUNTIME_MISSING when the kernel runtime is absent,
    /// without throwing (entry-point contract for CLI/Agent).
    /// </summary>
    [Fact]
    public void ShareMountHost_Mount_FailsCleanly_WhenRuntimeMissingOrAlreadyChecked()
    {
        // Always exercise the availability helper (shipped code).
        var description = WinFspRuntime.DescribeAvailability();
        Assert.False(string.IsNullOrWhiteSpace(description));

        if (!OperatingSystem.IsWindows())
        {
            var unsupported = ShareMountHost.Mount(
                new LocalStoreShareBackend(new LocalFolderShareStore(new FolderShareOptions
                {
                    Enabled = true,
                    RootPath = Path.Combine(Path.GetTempPath(), "mkp-winfsp-na"),
                })),
                "Z:");
            Assert.False(unsupported.Ok);
            Assert.Equal("PLATFORM_UNSUPPORTED", unsupported.ErrorCode);
            return;
        }

        if (WinFspRuntime.IsAvailable())
        {
            // Runtime present: still verify mount entry rejects empty mount point without crashing.
            var invalid = ShareMountHost.Mount(
                new LocalStoreShareBackend(new LocalFolderShareStore(new FolderShareOptions
                {
                    Enabled = true,
                    RootPath = Path.Combine(Path.GetTempPath(), "mkp-winfsp-empty-mp"),
                })),
                " ");
            Assert.False(invalid.Ok);
            Assert.Equal("INVALID_ARGUMENT", invalid.ErrorCode);
            return;
        }

        var root = Path.Combine(Path.GetTempPath(), "mkp-winfsp-missing-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(root);
        try
        {
            var backend = new LocalStoreShareBackend(new LocalFolderShareStore(new FolderShareOptions
            {
                Enabled = true,
                RootPath = root,
            }));
            var result = ShareMountHost.Mount(backend, "Z:");
            Assert.False(result.Ok);
            Assert.Equal("WINFSP_RUNTIME_MISSING", result.ErrorCode);
            Assert.Contains("WinFsp", result.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* temp */ }
        }
    }

    /// <summary>
    /// ShareFileBridge dispatches list, write, read, mkdir, rename, delete through LocalStoreShareBackend
    /// using the real LocalFolderShareStore implementation.
    /// </summary>
    [Fact]
    public void ShareFileBridge_FullControlDispatch_AgainstLocalStore()
    {
        var root = Path.Combine(Path.GetTempPath(), "mkp-winfsp-bridge-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new LocalFolderShareStore(new FolderShareOptions
            {
                Enabled = true,
                RootPath = root,
                ShareName = "test-share",
            });
            var bridge = new ShareFileBridge(new LocalStoreShareBackend(store));

            var mkdir = bridge.CreateDirectoryHostPath(@"\alpha\beta");
            Assert.True(mkdir.Ok, mkdir.Message);

            var payload = Encoding.UTF8.GetBytes("hello-winfsp-bridge");
            var write = bridge.WriteFileHostPath(@"\alpha\beta\note.txt", payload);
            Assert.True(write.Ok, write.Message);

            var read = bridge.ReadFileHostPath(@"\alpha\beta\note.txt", out var data);
            Assert.True(read.Ok, read.Message);
            Assert.Equal(payload, data);

            var list = bridge.ListHostPath(@"\alpha\beta", out var entries);
            Assert.True(list.Ok, list.Message);
            Assert.Contains(entries, e => e.Name == "note.txt" && !e.IsDirectory);

            var rename = bridge.RenameHostPath(@"\alpha\beta\note.txt", @"\alpha\beta\renamed.txt", overwrite: false);
            Assert.True(rename.Ok, rename.Message);

            var get = bridge.GetEntryHostPath(@"\alpha\beta\renamed.txt", out var entry);
            Assert.True(get.Ok, get.Message);
            Assert.NotNull(entry);
            Assert.False(entry!.IsDirectory);
            Assert.Equal(payload.Length, entry.SizeBytes);

            var delFile = bridge.DeleteHostPath(@"\alpha\beta\renamed.txt", recursive: false);
            Assert.True(delFile.Ok, delFile.Message);

            var delDir = bridge.DeleteHostPath(@"\alpha", recursive: true);
            Assert.True(delDir.Ok, delDir.Message);

            var listRoot = bridge.ListHostPath(@"\", out var rootEntries);
            Assert.True(listRoot.Ok, listRoot.Message);
            Assert.Empty(rootEntries);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* temp cleanup */ }
        }
    }

    /// <summary>ExerciseFullControlRoundTrip uses the real bridge entry path end-to-end.</summary>
    [Fact]
    public void ShareFileBridge_ExerciseFullControlRoundTrip_Succeeds()
    {
        var root = Path.Combine(Path.GetTempPath(), "mkp-winfsp-roundtrip-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new LocalFolderShareStore(new FolderShareOptions
            {
                Enabled = true,
                RootPath = root,
            });
            var bridge = new ShareFileBridge(new LocalStoreShareBackend(store));
            var result = bridge.ExerciseFullControlRoundTrip(@"\bridge-scratch");
            Assert.True(result.Ok, $"{result.ErrorCode}: {result.Message}");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* temp cleanup */ }
        }
    }

    /// <summary>Invalid host paths never reach the backend as absolute or parent-escaping paths.</summary>
    [Fact]
    public void ShareFileBridge_RejectsInvalidPaths_WithoutTouchingBackend()
    {
        var backend = new RecordingBackend();
        var bridge = new ShareFileBridge(backend);
        var result = bridge.WriteFileHostPath(@"\..\escape.txt", Encoding.UTF8.GetBytes("x"));
        Assert.False(result.Ok);
        Assert.Equal("PATH_INVALID", result.ErrorCode);
        Assert.Empty(backend.Calls);
    }

    /// <summary>Recording backend for call-order assertions without a real store.</summary>
    private sealed class RecordingBackend : IShareFileBackend
    {
        public List<string> Calls { get; } = new();

        public FolderShareResult List(string relativeDirectory, out IReadOnlyList<FolderShareEntry> entries)
        {
            Calls.Add("List:" + relativeDirectory);
            entries = Array.Empty<FolderShareEntry>();
            return new FolderShareResult(true, string.Empty, "ok");
        }

        public FolderShareResult TryGetEntry(string relativePath, out FolderShareEntry? entry)
        {
            Calls.Add("Get:" + relativePath);
            entry = null;
            return new FolderShareResult(false, "NOT_FOUND", "no");
        }

        public FolderShareResult ReadAllBytes(string relativePath, out byte[] data)
        {
            Calls.Add("Read:" + relativePath);
            data = Array.Empty<byte>();
            return new FolderShareResult(false, "NOT_FOUND", "no");
        }

        public FolderShareResult WriteAllBytes(string relativePath, byte[] data)
        {
            Calls.Add("Write:" + relativePath);
            return new FolderShareResult(true, string.Empty, "ok");
        }

        public FolderShareResult CreateDirectory(string relativeDirectory)
        {
            Calls.Add("Mkdir:" + relativeDirectory);
            return new FolderShareResult(true, string.Empty, "ok");
        }

        public FolderShareResult Delete(string relativePath, bool recursive)
        {
            Calls.Add("Delete:" + relativePath);
            return new FolderShareResult(true, string.Empty, "ok");
        }

        public FolderShareResult Rename(string relativePath, string newRelativePath, bool overwrite)
        {
            Calls.Add("Rename:" + relativePath + "->" + newRelativePath);
            return new FolderShareResult(true, string.Empty, "ok");
        }
    }
}
