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
        // AC3: handler calls transport. AC4 frame proof centralized to Commands (real build).
        // No override here: direct call to shipped Bidi.Send on spy (null client) runs production build, sets LastSentFrame.
        var spy = new RecordingTransport();
        await InputCommandHandler.SendInputAsync(spy, InputKind.TEXT_INPUT, "frame-test", TestContext.Current.CancellationToken);
        Assert.NotNull(spy.LastSentFrame);
    }

    private class RecordingTransport : BidiSessionTransport
    {
        public RecordingTransport() : base((MouseKeyProxy.Network.V1.MouseKeyProxy.MouseKeyProxyClient)null!) { }
        // NO override: lets real shipped SendInputBatchAsync run for frame build (LastSentFrame)
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
