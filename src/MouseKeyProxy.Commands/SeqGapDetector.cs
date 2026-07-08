using System.Collections.Generic;
using System.Linq;

namespace MouseKeyProxy.Commands;

/// <summary>TR-MKP-RELI-001: a contiguous range of sent-but-never-acked sequence numbers.</summary>
/// <param name="Start">First missing seq (inclusive).</param>
/// <param name="End">Last missing seq (inclusive).</param>
public sealed record SeqGap(ulong Start, ulong End);

/// <summary>
/// TR-MKP-RELI-001: tracks sent frame sequence numbers against received acks and reports gaps -
/// a sent seq is a gap once a higher seq has been acked but it never was. Thread-safe.
/// </summary>
public sealed class SeqGapDetector
{
    private readonly object _gate = new();
    private readonly SortedSet<ulong> _sent = new();
    private readonly SortedSet<ulong> _acked = new();
    private ulong _highestAck;

    /// <summary>The highest ack sequence observed so far.</summary>
    public ulong HighestAck
    {
        get { lock (_gate) { return _highestAck; } }
    }

    /// <summary>Records that a frame with the given seq was sent.</summary>
    /// <param name="seq">The frame sequence number.</param>
    public void OnSendFrame(ulong seq)
    {
        lock (_gate)
        {
            _sent.Add(seq);
        }
    }

    /// <summary>Records that an ack for the given seq was received.</summary>
    /// <param name="seq">The acknowledged sequence number.</param>
    public void OnReceiveAck(ulong seq)
    {
        lock (_gate)
        {
            _acked.Add(seq);
            if (seq > _highestAck)
            {
                _highestAck = seq;
            }
        }
    }

    /// <summary>Returns the current gaps: sent seqs below the highest ack that were never acked, coalesced into ranges.</summary>
    /// <returns>The detected gap ranges (empty when none).</returns>
    public IReadOnlyList<SeqGap> GetGaps()
    {
        lock (_gate)
        {
            var missing = _sent.Where(s => s < _highestAck && !_acked.Contains(s)).OrderBy(s => s).ToList();
            var gaps = new List<SeqGap>();
            ulong? start = null;
            ulong prev = 0;
            foreach (var s in missing)
            {
                if (start is null)
                {
                    start = s;
                    prev = s;
                }
                else if (s == prev + 1)
                {
                    prev = s;
                }
                else
                {
                    gaps.Add(new SeqGap(start.Value, prev));
                    start = s;
                    prev = s;
                }
            }

            if (start is not null)
            {
                gaps.Add(new SeqGap(start.Value, prev));
            }

            return gaps;
        }
    }

    /// <summary>Clears all tracked state.</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _sent.Clear();
            _acked.Clear();
            _highestAck = 0;
        }
    }
}
