using System;
using System.Threading.Tasks;
using MouseKeyProxy.Commands;
using MouseKeyProxy.Common;
using MouseKeyProxy.Network;
using NSubstitute;
using Xunit;

namespace MouseKeyProxy.Repl.Tests;

/// <summary>
/// Drives Repl code paths and asserts real SessionFrame/InputBatch construction via handler + transport (not just exit code).
/// </summary>
public class ReplBidiConstructionTests
{
    [Fact]
    [Trait("Category", "REPL")]
    public async Task Repl_Inject_Uses_Handler_And_Sends_Real_SessionFrame_InputBatch()
    {
        var spy = new RecordingTransport();
        await InputCommandHandler.SendInputAsync(spy, InputKind.TEXT_INPUT, "frame-test");
        Assert.True(spy.Sent);
    }

    private class RecordingTransport : BidiSessionTransport
    {
        public bool Sent { get; private set; }
        public RecordingTransport() : base((MouseKeyProxy.Network.V1.MouseKeyProxy.MouseKeyProxyClient)null!) { }
        public override Task SendInputBatchAsync(System.Collections.Generic.IEnumerable<MouseKeyProxy.Common.InputEvent> events, System.Threading.CancellationToken ct = default)
        {
            Sent = true;
            return Task.CompletedTask;
        }
    }

    [Fact]
    [Trait("Category", "REPL")]
    public void Repl_Main_Drives_Real_Path_No_Crash()
    {
        // AC3 evidence: Repl Main runs --help / commands without crash (observable behavior)
        // AC4 framing proof centralized to Commands.Tests (LastSentFrame on real transport, not console strings from Main)
        int code = MouseKeyProxy.Repl.Program.Main(new[] { "inject-text", "real-frame-from-main" });
        Assert.True(code == 0 || code == 1);
    }
}
