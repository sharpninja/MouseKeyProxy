using MouseKeyProxy.Common;

namespace MouseKeyProxy.Agent.Tests;

/// <summary>
/// TEST-MKP-006: clipboard merge order and concurrent receive semantics via shipped ClipboardLifoMerger.
/// Agent tray/REPL paths call this shared merger for LIFO history.
/// </summary>
public class ClipboardMergeTests
{
    [Fact]
    [Trait("Category", "ClipboardMerge")]
    public void TEST_MKP_006_Sequential_Receives_Produce_Receive_Time_Lifo_Order()
    {
        var history = new List<ClipboardEntry>();
        var first = Entry("first", 1, "hello");
        var second = Entry("second", 2, "world");

        history = ClipboardLifoMerger.Merge(history, first).History.ToList();
        history = ClipboardLifoMerger.Merge(history, second).History.ToList();

        Assert.Equal("second", history[0].Id);
        Assert.Equal("first", history[1].Id);
    }

    [Fact]
    [Trait("Category", "ClipboardMerge")]
    public void TEST_MKP_006_Concurrent_Style_Receives_Preserve_Seq_Order_On_Merge()
    {
        var history = new List<ClipboardEntry>();
        var baseTime = DateTimeOffset.UtcNow;
        var lowSeq = Entry("low", 1, "a", baseTime.AddMilliseconds(5));
        var highSeq = Entry("high", 3, "b", baseTime.AddMilliseconds(1));

        history = ClipboardLifoMerger.Merge(history, lowSeq).History.ToList();
        history = ClipboardLifoMerger.Merge(history, highSeq).History.ToList();

        Assert.Equal("high", history[0].Id);
        Assert.Equal("low", history[1].Id);
    }

    [Fact]
    [Trait("Category", "ClipboardMerge")]
    public void TEST_MKP_006_Dedup_Moves_Duplicate_To_Top_Without_Loop()
    {
        var bytes = System.Text.Encoding.Unicode.GetBytes("dup-text");
        var original = Entry("orig", 1, bytes);
        var history = ClipboardLifoMerger.Merge(Array.Empty<ClipboardEntry>(), original).History.ToList();
        var filler = Entry("filler", 2, "other");
        history = ClipboardLifoMerger.Merge(history, filler).History.ToList();

        var duplicate = Entry("dup", 3, bytes);
        history = ClipboardLifoMerger.Merge(history, duplicate).History.ToList();

        Assert.Equal(2, history.Count);
        Assert.Equal("dup", history[0].Id);
        Assert.Equal("filler", history[1].Id);
    }

    [Fact]
    [Trait("Category", "ClipboardMerge")]
    public void TEST_MKP_006_Binary_And_Text_Formats_Preserved_Through_Merge()
    {
        var text = new ClipboardFormat("UNICODETEXT", System.Text.Encoding.Unicode.GetBytes("text"));
        var binary = new ClipboardFormat("PNG", new byte[] { 0x89, 0x50, 0x4E, 0x47 });
        var entry = new ClipboardEntry("multi", DateTimeOffset.UtcNow, "peer", new[] { text, binary }, 7);

        var result = ClipboardLifoMerger.Merge(Array.Empty<ClipboardEntry>(), entry);

        Assert.True(result.Changed);
        Assert.Equal(2, result.History[0].Formats.Count);
        Assert.Equal("UNICODETEXT", result.History[0].Formats[0].Name);
        Assert.Equal("PNG", result.History[0].Formats[1].Name);
        Assert.Equal("text", System.Text.Encoding.Unicode.GetString(result.History[0].Formats[0].Data));
    }

    private static ClipboardEntry Entry(string id, ulong seq, string text, DateTimeOffset? ts = null) =>
        Entry(id, seq, System.Text.Encoding.Unicode.GetBytes(text), ts);

    private static ClipboardEntry Entry(string id, ulong seq, byte[] data, DateTimeOffset? ts = null) =>
        new(id, ts ?? DateTimeOffset.UtcNow, "peer", new[] { new ClipboardFormat("UNICODETEXT", data) }, seq);
}