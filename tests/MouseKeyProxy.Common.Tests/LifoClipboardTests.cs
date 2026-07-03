using MouseKeyProxy.Common;
using Xunit;

namespace MouseKeyProxy.Common.Tests;

public class LifoClipboardTests
{
    [Fact]
    [Trait("Category", "LIFO")]
    public void Merge_NewEntry_BecomesTopOfLifo()
    {
        var current = new List<ClipboardEntry>();
        var incoming = new ClipboardEntry("id1", DateTimeOffset.UtcNow, "peerA", new[] { new ClipboardFormat("UNICODETEXT", System.Text.Encoding.Unicode.GetBytes("hello")) }, 1);
        var result = ClipboardLifoMerger.Merge(current, incoming);
        Assert.True(result.Changed);
        Assert.Single(result.History);
        Assert.Equal("id1", result.History[0].Id);
    }

    [Fact]
    [Trait("Category", "LIFO")]
    public void Privacy_Formats_Are_Skipped()
    {
        var incoming = new ClipboardEntry("priv", DateTimeOffset.UtcNow, "peer", new[] { new ClipboardFormat("CF_CLIPBOARD_VIEWER_IGNORE", new byte[1]) });
        var res = ClipboardLifoMerger.Merge(Array.Empty<ClipboardEntry>(), incoming);
        Assert.False(res.Changed, "privacy format must be skipped, no change to history");
        Assert.True(ClipboardLifoMerger.IsPrivacyFormat(incoming.Formats[0]));
    }

    [Fact]
    [Trait("Category", "LIFO")]
    public void Cap_Enforced_AtMax()
    {
        var hist = Enumerable.Range(0, 60).Select(i => new ClipboardEntry($"e{i}", DateTimeOffset.UtcNow.AddSeconds(-i), "p", new[] { new ClipboardFormat("t", new byte[0]) }, (ulong)i )).ToList();
        var newOne = new ClipboardEntry("new", DateTimeOffset.UtcNow, "p", new[] { new ClipboardFormat("t", new byte[0]) }, 100);
        var res = ClipboardLifoMerger.Merge(hist, newOne);
        Assert.True(res.History.Count <= ClipboardLifoMerger.MaxEntries);
    }

    [Fact]
    [Trait("Category", "LIFO")]
    public void DPAPI_Persist_Load_Real_Temp_LocalAppData_Path()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "mkt-dpapi-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var file = Path.Combine(tempDir, "clip.bin");

        var entry = new ClipboardEntry("dp1", DateTimeOffset.UtcNow, "p", new[] { new ClipboardFormat("UNICODETEXT", System.Text.Encoding.Unicode.GetBytes("secret")) }, 5);
        var m = ClipboardLifoMerger.Merge(System.Array.Empty<ClipboardEntry>(), entry);
        ClipboardLifoMerger.PersistHistory(m.History, file);

        var loaded = ClipboardLifoMerger.LoadHistory(file);
        Assert.Single(loaded);
        var plain = System.Text.Encoding.Unicode.GetString(loaded[0].Formats[0].Data);
        Assert.Equal("secret", plain);

        // cleanup
        try { Directory.Delete(tempDir, true); } catch { }
    }
}
