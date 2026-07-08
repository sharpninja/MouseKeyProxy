using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MouseKeyProxy.Common;
using Xunit;

namespace MouseKeyProxy.Common.Tests;

/// <summary>
/// FR-MKP-004 / TR-MKP-CLIP-001: verifies SessionFrameDispatcher.HandleClipboardAsync merges a received
/// entry into the LIFO history, sets it on the accessor, and rejects out-of-order (replayed) sequences.
/// </summary>
public class DispatcherClipboardTests
{
    private sealed class RecordingAccessor : IClipboardAccessor
    {
        public ClipboardEntry? Last { get; private set; }
        public int SetCount { get; private set; }
        public event EventHandler<ClipboardEventArgs>? ClipboardChanged;
        public void SetClipboard(ClipboardEntry entry)
        {
            Last = entry;
            SetCount++;
            ClipboardChanged?.Invoke(this, new ClipboardEventArgs(entry));
        }
    }

    private static ClipboardEntry Entry(string id, ulong seq) =>
        new(id, DateTimeOffset.UtcNow, "peer", new List<ClipboardFormat> { new("T", new byte[] { 1 }) }, seq);

    /// <summary>An accepted entry is merged into history and set on the accessor.</summary>
    [Fact]
    [Trait("Category", "Clipboard")]
    public async Task Accepted_Entry_MergesAndSets()
    {
        var accessor = new RecordingAccessor();
        var dispatcher = new SessionFrameDispatcher(null, new ToggleStateMachine(), accessor);

        await dispatcher.HandleClipboardAsync(Entry("a", 1), TestContext.Current.CancellationToken);

        Assert.Single(dispatcher.ClipboardHistory);
        Assert.Equal("a", accessor.Last!.Id);
        Assert.Equal(1, accessor.SetCount);
    }

    /// <summary>A replayed (stale) sequence number is rejected: history and accessor are unchanged.</summary>
    [Fact]
    [Trait("Category", "Clipboard")]
    public async Task OutOfOrderSeq_IsRejected()
    {
        var accessor = new RecordingAccessor();
        var dispatcher = new SessionFrameDispatcher(null, new ToggleStateMachine(), accessor);

        await dispatcher.HandleClipboardAsync(Entry("a", 5), TestContext.Current.CancellationToken);
        await dispatcher.HandleClipboardAsync(Entry("b", 3), TestContext.Current.CancellationToken); // seq 3 <= last 5 -> rejected

        Assert.Single(dispatcher.ClipboardHistory);
        Assert.Equal("a", accessor.Last!.Id);
        Assert.Equal(1, accessor.SetCount);
    }
}
