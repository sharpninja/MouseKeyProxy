using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Core.Testing;
using Microsoft.Extensions.Logging;
using MouseKeyProxy.Network;
using MouseKeyProxy.Network.V1;
using MouseKeyProxy.Commands; // Bidi moved to Commands lib
using MouseKeyProxy.Service;
using NSubstitute;
using Xunit;
using Cmn = MouseKeyProxy.Common;
using MouseKeyProxy.Common;

namespace MouseKeyProxy.Commands.Tests;

/// <summary>
/// Red/green tests for real bidi SessionFrame roundtrip against shipped impl (per strategist).
/// </summary>
public class BidiRoundtripTests
{
    [Fact]
    public async Task InMemory_Duplex_Roundtrip_Against_Real_OpenSession_Asserts_AckSeq()
    {
        // Arrange: real service impl with dispatcher + spy injector to drive real receive->inject dispatch (shipped path for AC4)
        var logger = Substitute.For<ILogger<MouseKeyProxyImpl>>();
        var injector = Substitute.For<MouseKeyProxy.Common.IInputInjector>();
        var dispatcher = new MouseKeyProxy.Common.SessionFrameDispatcher(injector, new MouseKeyProxy.Common.ToggleStateMachine());
        var impl = new MouseKeyProxyImpl(logger, dispatcher);

        // in-memory request stream with one input batch frame (sim client send)
        var sentFrame = new SessionFrame
        {
            Seq = 7,
            Input = new InputBatch { BaseSeq = 7, Events = { new InputEvent { Kind = InputKind.KeyDown, Vk = 65 } } }
        };
        var requestStream = new TestAsyncStreamReader<SessionFrame>(new[] { sentFrame });

        // response stream we can inspect
        var responseStream = new TestServerStreamWriter<SessionFrame>();

        var ctx = TestServerCallContext.Create("OpenSession", null, System.DateTime.UtcNow.AddSeconds(10),
            new Metadata(), CancellationToken.None, "127.0.0.1", null, null, null, null, null);

        // Act: call the real shipped OpenSession impl
        await impl.OpenSession(requestStream, responseStream, ctx);

        // Assert: ack seq matches sent frame (real roundtrip)
        Assert.NotEmpty(responseStream.Responses);
        Assert.Equal(7u, responseStream.Responses[0].Ack.Last);

        // Drive real dispatch: received frame was passed to injector.Send (real path, not mock of UUT)
        injector.Received().Send(Arg.Any<InputEvent>());
    }

    // Simple test helpers (copied pattern from Service tests for red/green)
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
        // Red/green for shared handler: call SendInputAsync directly on RecordingTransport (null client, NO Send override).
        // This drives the REAL production BidiSessionTransport.SendInputBatchAsync (builds Wire frame from events, sets LastSentFrame using shipped code).
        // AC4: assert on the frame produced by the real shipped build path (not test override or re-impl).
        var spy = new RecordingTransport();
        await InputCommandHandler.SendInputAsync(spy, Cmn.InputKind.TEXT_INPUT, "test");
        Assert.NotNull(spy.LastSentFrame);
        Assert.NotNull(spy.LastSentFrame.Input);
        Assert.Single(spy.LastSentFrame.Input.Events);
        // real build output for this call (seq from _nextSeq=1 in spy Send path)
        Assert.Equal(1u, spy.LastSentFrame.Seq);
        var evt = spy.LastSentFrame.Input.Events[0];
        Assert.Equal("test", evt.Text);
    }

    private class RecordingTransport : BidiSessionTransport
    {
        public RecordingTransport() : base( (MouseKeyProxy.Network.V1.MouseKeyProxy.MouseKeyProxyClient)null! ) { }
        // NO override of SendInputBatchAsync: call on this instance drives the REAL production BidiSessionTransport.SendInputBatchAsync (which does the frame build and sets LastSentFrame)
        // This ensures the test exercises the shipped code path for AC4, not a simulating override.
    }
}
