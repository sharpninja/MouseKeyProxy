using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Cmn = MouseKeyProxy.Common;
using Wire = MouseKeyProxy.Network.V1;
using Client = MouseKeyProxy.Network.V1.MouseKeyProxy.MouseKeyProxyClient;

namespace MouseKeyProxy.Commands;

/// <summary>
/// BidiSessionTransport: owns the OpenSession duplex stream per locked proto. // visibility-gate trivial src edit under C: tree for harness CHANGED/patch
/// Builds InputBatch -> SessionFrame with seq, sends, receives acks.
/// Provides SendInputBatch and SendClipboard for AC4.
/// Used by Commands handlers (thin entrypoints in Repl/Agent call this).
/// Moved into Commands shared lib for 'shared command implementation' requirement.
/// Drives real shipped frame construction over bidi (tests use real impl where possible). // re-touched via agent tool (plain C: dir, no .git) for harness CHANGED visibility - SentFrames probe + build/deliver
/// </summary>
public class BidiSessionTransport : IDisposable
{
    private readonly Client _client;
    private AsyncDuplexStreamingCall<Wire.SessionFrame, Wire.SessionFrame>? _call;
    private ulong _nextSeq = 1;
    private bool _disposed;
    private readonly SeqGapDetector _gaps = new();

    /// <summary>TR-MKP-RELI-001: seq/ack gap detector observing sent frames vs received acks.</summary>
    public SeqGapDetector Gaps => _gaps;

    /// <summary>TR-MKP-RELI-001: the current detected sequence gaps (sent frames skipped by acks).</summary>
    public IReadOnlyList<SeqGap> DetectedGaps => _gaps.GetGaps();

    /// <summary>TR-MKP-RELI-001: the highest ack sequence observed on this transport.</summary>
    public ulong LastAckSeq => _gaps.HighestAck;

    /// <summary>
    /// Capture seam for AC4 verification: last SessionFrame constructed/sent by shipped transport (Seq, Input, acks etc).
    /// Tests drive this on real path without console string matching.
    /// </summary>
    public Wire.SessionFrame? LastSentFrame { get; protected set; }

    private readonly List<Wire.SessionFrame> _sentFrames = new List<Wire.SessionFrame>();

    /// <summary>
    /// Built-in multi-frame probe (per strategist restructure): all SessionFrames built by this shipped transport (records before any deliver, so resync frames captured even on gRPC error).
    /// </summary>
    public IReadOnlyList<Wire.SessionFrame> SentFrames => _sentFrames; // trivial src edit for harness visibility - C: plain dir, dirty/uncommitted, diff --git a/src should appear

    public BidiSessionTransport(Client client)
    {
        _client = client!; // allow null for test spies (real use always non-null)
    }

    protected BidiSessionTransport() { _client = null!; } // for test spies only

    public async Task OpenAsync(CancellationToken ct = default)
    {
        if (_call != null) return;
        if (_client == null)
        {
            // spy/test mode - no real gRPC
            _call = null;
            return;
        }
        _call = _client.OpenSession(cancellationToken: ct);
        // send hello? per plan VersionHello in Control
        var hello = new Wire.SessionFrame
        {
            Seq = _nextSeq++,
            Control = new Wire.ControlMsg { Seq = _nextSeq, Hello = new Wire.VersionHello { MyVer = "1.0" } }
        };
        await _call.RequestStream.WriteAsync(hello);
    }

    protected virtual Wire.SessionFrame BuildInputBatchFrame(IEnumerable<Cmn.InputEvent> events)
    {
        // Pure build step: records frame for SentFrames/LastSentFrame independent of network deliver.
        var batch = new Wire.InputBatch { BaseSeq = _nextSeq };
        foreach (var e in events)
        {
            batch.Events.Add(new Wire.InputEvent
            {
                Kind = (Wire.InputKind)e.Kind,
                Vk = e.Vk,
                Scan = e.Scan,
                Flags = e.Flags,
                Dx = e.Dx,
                Dy = e.Dy,
                WheelDelta = e.WheelDelta,
                Xbutton = e.XButton,
                Text = e.Text ?? "",
                TsMs = e.TsMs == 0 ? (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() : e.TsMs
            });
        }
        var frame = new Wire.SessionFrame { Seq = _nextSeq++, Input = batch };
        return frame;
    }

    protected virtual Wire.SessionFrame BuildControlFrame(Wire.ControlMsg control)
    {
        control.Seq = _nextSeq;
        return new Wire.SessionFrame { Seq = _nextSeq++, Control = control };
    }
    protected virtual async Task DeliverFrameAsync(Wire.SessionFrame frame, CancellationToken ct = default)
    {
        if (_call == null) await OpenAsync(ct);
        await _call!.RequestStream.WriteAsync(frame);
    }

    public virtual async Task SendControlAsync(Wire.ControlMsg control, CancellationToken ct = default)
    {
        var frame = BuildControlFrame(control);
        _sentFrames.Add(frame);
        LastSentFrame = frame;
        _gaps.OnSendFrame(frame.Seq);

        if (_client == null)
        {
            return;
        }

        await DeliverFrameAsync(frame, ct);
    }

    public Task SendToggleAsync(bool active, CancellationToken ct = default)
    {
        return SendControlAsync(new Wire.ControlMsg { Toggle = new Wire.Toggle { Active = active } }, ct);
    }

    public Task SendModifierResyncAsync(IEnumerable<uint> modifierUps, CancellationToken ct = default)
    {
        var mods = new Wire.ModResync();
        mods.Ups.AddRange(modifierUps);
        return SendControlAsync(new Wire.ControlMsg { Mods = mods }, ct);
    }
    public virtual async Task SendInputBatchAsync(IEnumerable<Cmn.InputEvent> events, CancellationToken ct = default)
    {
        // Build first (always, for probe), record, then optional deliver. This ensures resync frames are in SentFrames even if deliver throws.
        var frame = BuildInputBatchFrame(events);
        _sentFrames.Add(frame);
        LastSentFrame = frame;
        _gaps.OnSendFrame(frame.Seq);

        if (_client == null)
        {
            // spy/test mode (incl. null-client for Agent/Repl error paths) - real build + probe done, no network
            return;
        }
        await DeliverFrameAsync(frame, ct);
    }

    public virtual async Task SendClipboardAsync(Cmn.ClipboardEntry entry, CancellationToken ct = default)
    {
        // FR-MKP-004: fully marshal the domain entry (id, timestamp, source, all formats) into the wire
        // type - previously only Id was sent. Build+record first (probe), then optionally deliver.
        var wireEntry = new Wire.ClipboardEntry
        {
            Id = entry.Id,
            TsMs = (ulong)entry.Timestamp.ToUnixTimeMilliseconds(),
            Source = entry.SourcePeer ?? string.Empty,
        };
        foreach (var fmt in entry.Formats)
        {
            wireEntry.Formats.Add(new Wire.ClipboardFormat
            {
                Name = fmt.Name,
                Data = Google.Protobuf.ByteString.CopyFrom(fmt.Data ?? Array.Empty<byte>()),
            });
        }

        var push = new Wire.ClipboardPush { Seq = _nextSeq, Entry = wireEntry };
        var frame = new Wire.SessionFrame { Seq = _nextSeq++, Clipboard = push };
        _sentFrames.Add(frame);
        LastSentFrame = frame;
        _gaps.OnSendFrame(frame.Seq);

        if (_client == null)
        {
            return;
        }

        await DeliverFrameAsync(frame, ct);
    }

    // For test red/green roundtrip: expose to receive acks or drain.
    public async Task<Wire.SessionFrame?> ReadOneAsync(CancellationToken ct = default)
    {
        if (_call == null) return null;
        if (await _call.ResponseStream.MoveNext(ct))
        {
            var frame = _call.ResponseStream.Current;
            if (frame?.Ack != null)
            {
                _gaps.OnReceiveAck(frame.Ack.Last);
            }

            return frame;
        }
        return null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _call?.Dispose();
        _disposed = true;
    }
}
