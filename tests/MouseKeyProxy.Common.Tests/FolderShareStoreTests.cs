using System.IO;
using System.Threading.Tasks;
using MouseKeyProxy.Common;
using Xunit;

namespace MouseKeyProxy.Common.Tests;

/// <summary>FR-MKP-014: local folder share sandbox list/read/write and path rejection.</summary>
public class FolderShareStoreTests
{
    /// <summary>List root returns created files; path traversal is rejected.</summary>
    [Fact]
    public async Task ListAndRejectTraversal()
    {
        var root = Path.Combine(Path.GetTempPath(), "mkp-share-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "a.txt"), "hi", TestContext.Current.CancellationToken);
            var store = new LocalFolderShareStore(new FolderShareOptions
            {
                Enabled = true,
                ShareName = "test",
                RootPath = root,
            });

            var list = store.List("", out var entries);
            Assert.True(list.Ok);
            Assert.Contains(entries, e => e.Name == "a.txt" && !e.IsDirectory);

            var bad = store.List("..", out _);
            Assert.False(bad.Ok);
            Assert.Equal("PATH_INVALID", bad.ErrorCode);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* best effort */ }
        }
    }

    /// <summary>Write then read round-trips content.</summary>
    [Fact]
    public async Task WriteThenRead_RoundTrips()
    {
        var root = Path.Combine(Path.GetTempPath(), "mkp-share-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new LocalFolderShareStore(new FolderShareOptions
            {
                Enabled = true,
                RootPath = root,
            });

            var openW = store.OpenWrite("dir/b.bin", 4, out var ws);
            Assert.True(openW.Ok);
            await using (ws)
            {
                await ws!.WriteAsync(new byte[] { 1, 2, 3, 4 }, TestContext.Current.CancellationToken);
            }

            var openR = store.OpenRead("dir/b.bin", out var rs, out var len);
            Assert.True(openR.Ok);
            Assert.Equal(4, len);
            await using (rs)
            {
                var buf = new byte[4];
                Assert.Equal(4, await rs!.ReadAsync(buf, TestContext.Current.CancellationToken));
                Assert.Equal(new byte[] { 1, 2, 3, 4 }, buf);
            }
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* best effort */ }
        }
    }

    /// <summary>Disabled share rejects operations.</summary>
    [Fact]
    public void Disabled_Rejects()
    {
        var store = new LocalFolderShareStore(new FolderShareOptions { Enabled = false, RootPath = Path.GetTempPath() });
        var info = store.GetInfo();
        Assert.False(info.Enabled);
        Assert.False(store.List("", out _).Ok);
    }

    /// <summary>FR-MKP-014: mkdir, rename, and delete cover full share control.</summary>
    [Fact]
    public void CreateRenameDelete_FullControl()
    {
        var root = Path.Combine(Path.GetTempPath(), "mkp-share-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new LocalFolderShareStore(new FolderShareOptions
            {
                Enabled = true,
                RootPath = root,
            });

            Assert.True(store.CreateDirectory("docs/nested").Ok);
            Assert.True(Directory.Exists(Path.Combine(root, "docs", "nested")));

            File.WriteAllText(Path.Combine(root, "docs", "a.txt"), "x");
            Assert.True(store.Rename("docs/a.txt", "docs/nested/b.txt", overwrite: false).Ok);
            Assert.True(File.Exists(Path.Combine(root, "docs", "nested", "b.txt")));
            Assert.False(File.Exists(Path.Combine(root, "docs", "a.txt")));

            Assert.False(store.Delete("docs", recursive: false).Ok);
            Assert.True(store.Delete("docs", recursive: true).Ok);
            Assert.False(Directory.Exists(Path.Combine(root, "docs")));

            Assert.False(store.Delete("", recursive: true).Ok);
            Assert.Equal("PATH_INVALID", store.Delete("", recursive: true).ErrorCode);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* best effort */ }
        }
    }
}
