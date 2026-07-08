using System.Collections.Generic;
using System.Linq;
using MouseKeyProxy.Commands;
using MouseKeyProxy.Common;
using Xunit;

namespace MouseKeyProxy.Commands.Tests;

/// <summary>
/// TR-MKP-RELI-001: verifies sequence/ack gap detection over the bidi channel - a sent frame whose
/// seq is skipped by later acks is reported as a gap, so the transport can observe dropped frames
/// instead of the previously-cosmetic seq counter.
/// </summary>
public class SeqGapDetectorTests
{
    /// <summary>An ack that skips a sent seq records that seq as a one-wide gap.</summary>
    [Fact]
    [Trait("Category", "Reliability")]
    public void SkippedSeq_IsReportedAsGap()
    {
        var d = new SeqGapDetector();
        d.OnSendFrame(1);
        d.OnSendFrame(2);
        d.OnSendFrame(3);
        d.OnReceiveAck(1);
        d.OnReceiveAck(3);

        var gaps = d.GetGaps();
        var gap = Assert.Single(gaps);
        Assert.Equal(2u, gap.Start);
        Assert.Equal(2u, gap.End);
        Assert.Equal(3u, d.HighestAck);
    }

    /// <summary>Consecutive missing seqs coalesce into a single range.</summary>
    [Fact]
    [Trait("Category", "Reliability")]
    public void ConsecutiveMissing_CoalesceIntoOneRange()
    {
        var d = new SeqGapDetector();
        for (uint i = 1; i <= 5; i++)
        {
            d.OnSendFrame(i);
        }
        d.OnReceiveAck(1);
        d.OnReceiveAck(5);

        var gap = Assert.Single(d.GetGaps());
        Assert.Equal(2u, gap.Start);
        Assert.Equal(4u, gap.End);
    }

    /// <summary>A seq that is merely un-acked (no higher ack yet) is not a gap.</summary>
    [Fact]
    [Trait("Category", "Reliability")]
    public void UnackedButNotSkipped_IsNotGap()
    {
        var d = new SeqGapDetector();
        d.OnSendFrame(1);
        d.OnSendFrame(2);
        d.OnReceiveAck(1);

        Assert.Empty(d.GetGaps());
    }

    /// <summary>In-order acks yield no gaps.</summary>
    [Fact]
    [Trait("Category", "Reliability")]
    public void InOrderAcks_NoGaps()
    {
        var d = new SeqGapDetector();
        for (uint i = 1; i <= 4; i++)
        {
            d.OnSendFrame(i);
            d.OnReceiveAck(i);
        }

        Assert.Empty(d.GetGaps());
    }

    /// <summary>BidiSessionTransport records each sent frame's seq so a skipped ack surfaces as a gap.</summary>
    [Fact]
    [Trait("Category", "Reliability")]
    public async System.Threading.Tasks.Task Transport_RecordsSentSeqs_AndReportsGapFromAcks()
    {
        // Spy mode (protected ctor via subclass) - builds + records frames without a live channel.
        var transport = new SpyTransport();
        var evt = new List<InputEvent> { new(InputKind.KEY_DOWN, Vk: 65) };

        await transport.SendInputBatchAsync(evt); // seq 1
        await transport.SendInputBatchAsync(evt); // seq 2
        await transport.SendInputBatchAsync(evt); // seq 3

        // Simulate the server acking 1 and 3 but not 2.
        transport.Gaps.OnReceiveAck(1);
        transport.Gaps.OnReceiveAck(3);

        var gap = Assert.Single(transport.DetectedGaps);
        Assert.Equal(2u, gap.Start);
        Assert.Equal(2u, gap.End);
    }

    private sealed class SpyTransport : BidiSessionTransport
    {
    }
}
