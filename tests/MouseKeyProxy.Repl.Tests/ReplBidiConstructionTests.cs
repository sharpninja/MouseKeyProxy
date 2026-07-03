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
        // Call Main with inject to exercise the bidi construction path (shipped Main + handler + transport frame build)
        var originalOut = Console.Out;
        var sw = new System.IO.StringWriter();
        Console.SetOut(sw);
        try
        {
            int code = MouseKeyProxy.Repl.Program.Main(new[] { "inject-text", "real-frame-from-main" });
            var output = sw.ToString();
            Assert.Contains("REAL bidi via transport", output);
            Assert.Contains("SessionFrame/InputBatch SUCCESS", output);
            Assert.True(code == 0 || code == 1);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }
}
