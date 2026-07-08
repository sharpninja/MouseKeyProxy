using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using MouseKeyProxy.Commands;
using MouseKeyProxy.Service;
using NSubstitute;
using Cmn = MouseKeyProxy.Common;
using Wire = MouseKeyProxy.Network.V1;
using Xunit;

namespace MouseKeyProxy.Commands.Tests;

/// <summary>
/// FR-MKP-004 / TR-MKP-CLIP-001: end-to-end clipboard sync - the send path fully marshals every
/// ClipboardEntry field to the wire, and the receive path (OpenSession -> dispatcher -> accessor)
/// merges the entry into the LIFO history and sets it on the peer clipboard.
/// </summary>
public class ClipboardSyncTests
{
    private sealed class ClipSpyTransport : BidiSessionTransport
    {
    }

    private sealed class ClipStreamReader : IAsyncStreamReader<Wire.SessionFrame>
    {
        private readonly IEnumerator<Wire.SessionFrame> _e;
        public ClipStreamReader(IEnumerable<Wire.SessionFrame> items) => _e = items.GetEnumerator();
        public Wire.SessionFrame Current => _e.Current;
        public Task<bool> MoveNext(CancellationToken cancellationToken) => Task.FromResult(_e.MoveNext());
    }

    private sealed class ClipStreamWriter : IServerStreamWriter<Wire.SessionFrame>
    {
        public List<Wire.SessionFrame> Written { get; } = new();
        public WriteOptions? WriteOptions { get; set; }
        public Task WriteAsync(Wire.SessionFrame message)
        {
            Written.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingClipboardAccessor : Cmn.IClipboardAccessor
    {
        public Cmn.ClipboardEntry? Last { get; private set; }

        public event EventHandler<Cmn.ClipboardEventArgs>? ClipboardChanged;

        public void SetClipboard(Cmn.ClipboardEntry entry)
        {
            Last = entry;
            ClipboardChanged?.Invoke(this, new Cmn.ClipboardEventArgs(entry));
        }
    }

    /// <summary>SendClipboardAsync marshals id, timestamp, source, and every format+data to the wire.</summary>
    [Fact]
    [Trait("Category", "Clipboard")]
    public async Task SendClipboardAsync_Populates_All_Wire_Fields()
    {
        var transport = new ClipSpyTransport();
        var entry = new Cmn.ClipboardEntry(
            "clip-1",
            DateTimeOffset.FromUnixTimeMilliseconds(1234),
            "peerA",
            new List<Cmn.ClipboardFormat> { new("UNICODETEXT", new byte[] { 1, 2, 3 }) },
            Seq: 7);

        await transport.SendClipboardAsync(entry, CancellationToken.None);

        var wire = transport.LastSentFrame!.Clipboard.Entry;
        Assert.Equal("clip-1", wire.Id);
        Assert.Equal(1234ul, wire.TsMs);
        Assert.Equal("peerA", wire.Source);
        var fmt = Assert.Single(wire.Formats);
        Assert.Equal("UNICODETEXT", fmt.Name);
        Assert.Equal(new byte[] { 1, 2, 3 }, fmt.Data.ToByteArray());
    }

    /// <summary>A received clipboard frame flows OpenSession -> dispatcher -> accessor and is set on the peer.</summary>
    [Fact]
    [Trait("Category", "Clipboard")]
    public async Task OpenSession_ReceivesClipboard_SetsPeerClipboard()
    {
        var accessor = new RecordingClipboardAccessor();
        var dispatcher = new Cmn.SessionFrameDispatcher(
            Substitute.For<Cmn.IInputInjector>(), new Cmn.ToggleStateMachine(), accessor);
        var impl = new MouseKeyProxyImpl(Substitute.For<ILogger<MouseKeyProxyImpl>>(), dispatcher);

        var wireEntry = new Wire.ClipboardEntry
        {
            Id = "wire-1",
            TsMs = 5555,
            Source = "remote-peer",
        };
        wireEntry.Formats.Add(new Wire.ClipboardFormat
        {
            Name = "UNICODETEXT",
            Data = Google.Protobuf.ByteString.CopyFromUtf8("hello"),
        });
        var frame = new Wire.SessionFrame
        {
            Seq = 3,
            Clipboard = new Wire.ClipboardPush { Seq = 3, Entry = wireEntry },
        };

        var requestStream = new ClipStreamReader(new[] { frame });
        var responseStream = new ClipStreamWriter();
        var ctx = Grpc.Core.Testing.TestServerCallContext.Create("OpenSession", null, DateTime.UtcNow.AddSeconds(10),
            new Metadata(), CancellationToken.None, "127.0.0.1", null, null, null, null, null);

        await impl.OpenSession(requestStream, responseStream, ctx);

        Assert.NotNull(accessor.Last);
        Assert.Equal("wire-1", accessor.Last!.Id);
        Assert.Equal("remote-peer", accessor.Last.SourcePeer);
        var fmt = Assert.Single(accessor.Last.Formats);
        Assert.Equal("hello", System.Text.Encoding.UTF8.GetString(fmt.Data));
    }
}
