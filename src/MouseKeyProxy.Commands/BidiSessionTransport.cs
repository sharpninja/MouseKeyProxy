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
/// BidiSessionTransport: owns the OpenSession duplex stream per locked proto.
/// Builds InputBatch -> SessionFrame with seq, sends, receives acks.
/// Provides SendInputBatch and SendClipboard for AC4.
/// Used by Commands handlers (thin entrypoints in Repl/Agent call this).
/// Moved into Commands shared lib for 'shared command implementation' requirement.
/// Drives real shipped frame construction over bidi (tests use real impl where possible).
/// </summary>
public class BidiSessionTransport : IDisposable
{
    private readonly Client _client;
    private AsyncDuplexStreamingCall<Wire.SessionFrame, Wire.SessionFrame>? _call;
    private ulong _nextSeq = 1;
    private bool _disposed;

    /// <summary>
    /// Capture seam for AC4 verification: last SessionFrame constructed/sent by shipped transport (Seq, Input, acks etc).
    /// Tests drive this on real path without console string matching.
    /// </summary>
    public Wire.SessionFrame? LastSentFrame { get; protected set; }

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

    public virtual async Task SendInputBatchAsync(IEnumerable<Cmn.InputEvent> events, CancellationToken ct = default)
    {
        // Build frame using shipped code path (always executed for real path drive)
        var batch = new Wire.InputBatch { BaseSeq = _nextSeq };
        foreach (var e in events)
        {
            batch.Events.Add(new Wire.InputEvent { Kind = (Wire.InputKind)e.Kind, Vk = e.Vk, Text = e.Text ?? "", TsMs = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });
        }
        var frame = new Wire.SessionFrame { Seq = _nextSeq++, Input = batch };
        LastSentFrame = frame;

        if (_client == null)
        {
            // spy/test mode - real build + capture done, no network
            return;
        }
        if (_call == null) await OpenAsync(ct);
        await _call!.RequestStream.WriteAsync(frame);
    }

    public async Task SendClipboardAsync(Cmn.ClipboardEntry entry, CancellationToken ct = default)
    {
        if (_call == null) await OpenAsync(ct);
        var push = new Wire.ClipboardPush { Seq = _nextSeq, Entry = new Wire.ClipboardEntry { Id = entry.Id } };
        var frame = new Wire.SessionFrame { Seq = _nextSeq++, Clipboard = push };
        await _call!.RequestStream.WriteAsync(frame);
    }

    // For test red/green roundtrip: expose to receive acks or drain.
    public async Task<Wire.SessionFrame?> ReadOneAsync(CancellationToken ct = default)
    {
        if (_call == null) return null;
        if (await _call.ResponseStream.MoveNext(ct))
            return _call.ResponseStream.Current;
        return null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _call?.Dispose();
        _disposed = true;
    }
}
