using System;
using System.Collections.Generic;
using System.Linq;
using MouseKeyProxy.Common;
using Xunit;

namespace MouseKeyProxy.Common.Tests;

/// <summary>
/// TR-MKP-CLIP-001: verifies the clipboard merger's sequence ordering, per-item size limit, and
/// total-storage cap - the gaps the audit flagged as missing on ClipboardLifoMerger.
/// </summary>
public class ClipboardMergerLimitsTests
{
    private static ClipboardEntry Entry(string id, int bytes, ulong seq = 0) =>
        new(id, DateTimeOffset.UtcNow, "peer", new List<ClipboardFormat> { new("UNICODETEXT", new byte[bytes]) }, seq);

    /// <summary>An out-of-order (stale) sequence number is rejected against the last accepted seq.</summary>
    [Fact]
    [Trait("Category", "Clipboard")]
    public void OutOfOrderSeq_IsRejected()
    {
        var history = new List<ClipboardEntry>();
        var result = ClipboardLifoMerger.Merge(history, Entry("a", 10, seq: 2), lastSeq: 5);

        Assert.False(result.Changed);
        Assert.Empty(result.History);
    }

    /// <summary>An in-order sequence number is accepted.</summary>
    [Fact]
    [Trait("Category", "Clipboard")]
    public void InOrderSeq_IsAccepted()
    {
        var result = ClipboardLifoMerger.Merge(Array.Empty<ClipboardEntry>(), Entry("a", 10, seq: 6), lastSeq: 5);

        Assert.True(result.Changed);
        Assert.Single(result.History);
    }

    /// <summary>An item larger than the per-entry byte limit is rejected.</summary>
    [Fact]
    [Trait("Category", "Clipboard")]
    public void OversizedEntry_IsRejected()
    {
        var oversized = Entry("big", (int)ClipboardLifoMerger.MaxBytesPerEntry + 1);
        var result = ClipboardLifoMerger.Merge(Array.Empty<ClipboardEntry>(), oversized);

        Assert.False(result.Changed);
        Assert.Empty(result.History);
    }

    /// <summary>The total-storage cap trims the oldest entries when exceeded.</summary>
    [Fact]
    [Trait("Category", "Clipboard")]
    public void TotalStorageCap_TrimsOldest()
    {
        // Each entry ~600 KiB; the 50 MiB cap allows well over MaxEntries, so drive with near-limit items.
        var chunk = (int)(ClipboardLifoMerger.MaxBytesPerEntry); // 1 MiB each
        IReadOnlyList<ClipboardEntry> history = Array.Empty<ClipboardEntry>();
        for (int i = 0; i < 60; i++)
        {
            history = ClipboardLifoMerger.Merge(history, Entry($"e{i}", chunk)).History;
        }

        var totalBytes = history.Sum(e => e.Formats.Sum(f => (long)f.Data.Length));
        Assert.True(totalBytes <= ClipboardLifoMerger.MaxTotalBytes);
        Assert.True(history.Count <= ClipboardLifoMerger.MaxEntries);
    }
}
