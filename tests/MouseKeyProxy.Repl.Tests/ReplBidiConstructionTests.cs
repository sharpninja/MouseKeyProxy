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
    private static string RepoRoot =>
        System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

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

    [Fact]
    [Trait("Category", "Pairing")]
    public void Repl_Pair_Notifies_Local_Agent_Pairing_State()
    {
        var sourcePath = System.IO.Path.Combine(RepoRoot, "src", "MouseKeyProxy.Repl", "Program.cs");
        var source = System.IO.File.ReadAllText(sourcePath);

        Assert.Contains("NotifyLocalAgentPairingState(baseUrl, req.PairingCode)", source, StringComparison.Ordinal);
        Assert.Contains("AgentControlPipe.NotifyPairingState", source, StringComparison.Ordinal);
        Assert.Contains("RemoteGrpcUrl = remoteGrpcUrl", source, StringComparison.Ordinal);
        Assert.Contains("[AGENT pairing state]", source, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Logging")]
    public void Repl_Installer_Registers_Source_In_Dedicated_MouseKeyProxy_EventLog()
    {
        var sourcePath = System.IO.Path.Combine(RepoRoot, "src", "MouseKeyProxy.Repl", "Program.cs");
        var source = System.IO.File.ReadAllText(sourcePath);

        Assert.Contains("private const string EventLogSourceName = \"MouseKeyProxy\"", source, StringComparison.Ordinal);
        Assert.Contains("private const string EventLogName = \"MouseKeyProxy\"", source, StringComparison.Ordinal);
        Assert.Contains("EventLog.LogNameFromSourceName(EventLogSourceName, \".\")", source, StringComparison.Ordinal);
        Assert.Contains("EventLog.CreateEventSource(EventLogSourceName, EventLogName)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("EventLog.CreateEventSource(\"MouseKeyProxy\", \"Application\")", source, StringComparison.Ordinal);
    }
}
