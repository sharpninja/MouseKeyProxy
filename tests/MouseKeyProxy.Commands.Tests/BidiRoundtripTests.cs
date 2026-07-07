using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Core.Testing;
using Microsoft.Extensions.Logging;
using MouseKeyProxy.Commands;
using MouseKeyProxy.Network.V1;
using MouseKeyProxy.Service;
using NSubstitute;
using Xunit;
using Cmn = MouseKeyProxy.Common;

namespace MouseKeyProxy.Commands.Tests;

/// <summary>
/// Red/green tests for real bidi SessionFrame roundtrip against shipped impl (per strategist).
/// </summary>
public class BidiRoundtripTests
{
    [Fact]
    public async Task InMemory_Duplex_Roundtrip_Against_Real_OpenSession_Asserts_AckSeq()
    {
        var logger = Substitute.For<ILogger<MouseKeyProxyImpl>>();
        var injector = new RecordingInjector();
        var dispatcher = new Cmn.SessionFrameDispatcher(injector, new Cmn.ToggleStateMachine());
        var impl = new MouseKeyProxyImpl(logger, dispatcher);

        var sentFrame = new SessionFrame
        {
            Seq = 7,
            Input = new InputBatch { BaseSeq = 7, Events = { new InputEvent { Kind = InputKind.KeyDown, Vk = 65 } } }
        };
        var requestStream = new TestAsyncStreamReader<SessionFrame>(new[] { sentFrame });
        var responseStream = new TestServerStreamWriter<SessionFrame>();
        var ctx = TestServerCallContext.Create("OpenSession", null, System.DateTime.UtcNow.AddSeconds(10),
            new Metadata(), CancellationToken.None, "127.0.0.1", null, null, null, null, null);

        await impl.OpenSession(requestStream, responseStream, ctx);

        Assert.NotEmpty(responseStream.Responses);
        Assert.Equal(7u, responseStream.Responses[0].Ack.Last);
        var eventFromBatch = Assert.Single(injector.LastBatch);
        Assert.Equal(Cmn.InputKind.KEY_DOWN, eventFromBatch.Kind);
        Assert.Equal(65u, eventFromBatch.Vk);
    }

    private sealed class RecordingInjector : Cmn.IInputInjector
    {
        public IReadOnlyList<Cmn.InputEvent> LastBatch { get; private set; } = Array.Empty<Cmn.InputEvent>();

        public void Send(Cmn.InputEvent evt) => LastBatch = new[] { evt };

        public bool TryInjectBatch(IEnumerable<Cmn.InputEvent> events, out string? error)
        {
            LastBatch = events.ToArray();
            error = null;
            return true;
        }
    }

    internal class TestAsyncStreamReader<T> : IAsyncStreamReader<T> where T : class
    {
        private readonly IEnumerator<T> _e;
        public TestAsyncStreamReader(IEnumerable<T> items) { _e = items.GetEnumerator(); }
        public T Current => _e.Current;
        public async Task<bool> MoveNext(CancellationToken ct) { await Task.Yield(); return _e.MoveNext(); }
    }

    internal class TestServerStreamWriter<T> : IServerStreamWriter<T> where T : class
    {
        public List<T> Responses { get; } = new();
        public WriteOptions? WriteOptions { get; set; }
        public Task WriteAsync(T message) { Responses.Add(message); return Task.CompletedTask; }
    }

    [Fact]
    public async Task InjectHandler_Calls_Transport_With_Real_SessionFrame_Containing_InputBatch()
    {
        var spy = new RecordingTransport();
        await InputCommandHandler.SendInputAsync(spy, Cmn.InputKind.TEXT_INPUT, "test");

        Assert.NotNull(spy.LastSentFrame);
        Assert.NotNull(spy.LastSentFrame.Input);
        Assert.Single(spy.LastSentFrame.Input.Events);
        Assert.Equal(1u, spy.LastSentFrame.Seq);
        var evt = spy.LastSentFrame.Input.Events[0];
        Assert.Equal("test", evt.Text);
    }

    private class RecordingTransport : BidiSessionTransport
    {
        public RecordingTransport() : base((MouseKeyProxy.Network.V1.MouseKeyProxy.MouseKeyProxyClient)null!) { }
    }

    [Fact]
    [Trait("Category", "ModifierCleanup")]
    public async Task ToggleAsync_Emits_ModResync_Control_Frame_With_Modifier_Ups_When_Changed()
    {
        var sm = new Cmn.ToggleStateMachine();
        var spy = new RecordingTransport();

        bool active = await InputCommandHandler.ToggleAsync(sm, spy, "peer1");

        Assert.True(active);
        Assert.Equal(2, spy.SentFrames.Count);
        Assert.NotNull(spy.SentFrames[0].Control?.Toggle);
        var last = spy.SentFrames.Last();
        Assert.NotNull(last.Control?.Mods);
        Assert.Equal(Cmn.ModifierReleasePolicy.ModifierVirtualKeys, last.Control.Mods.Ups);
    }
}