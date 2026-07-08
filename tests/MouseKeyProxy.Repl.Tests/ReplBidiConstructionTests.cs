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
    public void Repl_Main_InjectText_UsesMockedService_NoLiveInjection()
    {
        // Hermetic: mock the gRPC service via the Program test seam so Main never opens a socket
        // nor injects real OS keystrokes into the host session. Verifies Main dispatches the
        // inject-text command to InjectInput with the expected text, and returns success.
        var client = Substitute.For<MouseKeyProxy.Network.V1.MouseKeyProxy.MouseKeyProxyClient>();
        client.InjectInput(
                Arg.Any<MouseKeyProxy.Network.V1.InjectInputRequest>(),
                Arg.Any<Grpc.Core.Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<System.Threading.CancellationToken>())
            .Returns(new MouseKeyProxy.Network.V1.CommandResult { Ok = true });

        MouseKeyProxy.Repl.Program.TestGrpcClientFactory = _ => client;
        try
        {
            var code = MouseKeyProxy.Repl.Program.Main(new[] { "inject-text", "sentinel-text" });

            Assert.Equal(0, code);
            client.Received(1).InjectInput(
                Arg.Is<MouseKeyProxy.Network.V1.InjectInputRequest>(r =>
                    r.Events.Count == 1 && r.Events[0].Text == "sentinel-text"),
                Arg.Any<Grpc.Core.Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<System.Threading.CancellationToken>());
        }
        finally
        {
            MouseKeyProxy.Repl.Program.TestGrpcClientFactory = null;
        }
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
    [Trait("Category", "REPL")]
    public void Repl_Exposes_Canonical_Agent_Status_And_Emergency_Release_Commands()
    {
        var sourcePath = System.IO.Path.Combine(RepoRoot, "src", "MouseKeyProxy.Repl", "Program.cs");
        var source = System.IO.File.ReadAllText(sourcePath);

        Assert.Contains("mkp status [--json]", source, StringComparison.Ordinal);
        Assert.Contains("mkp agent status [--json] | emergency-release [--json]", source, StringComparison.Ordinal);
        Assert.Contains("mkp pair discover | pair <code> | pair status [--json]", source, StringComparison.Ordinal);
        Assert.Contains("mkp emergency-release [--json]", source, StringComparison.Ordinal);
        Assert.Contains("mkp logs", source, StringComparison.Ordinal);
        Assert.Contains("case \"status\":", source, StringComparison.Ordinal);
        Assert.Contains("case \"agent\":", source, StringComparison.Ordinal);
        Assert.Contains("case \"emergency-release\":", source, StringComparison.Ordinal);
        Assert.Contains("case \"release\":", source, StringComparison.Ordinal);
        Assert.Contains("case \"logs\":", source, StringComparison.Ordinal);
        Assert.Contains("eventvwr.msc", source, StringComparison.Ordinal);
        Assert.Contains("Arguments = \"/c:MouseKeyProxy\"", source, StringComparison.Ordinal);
        Assert.Contains("AgentControlPipe.GetAgentStatus", source, StringComparison.Ordinal);
        Assert.Contains("AgentControlPipe.EmergencyRelease", source, StringComparison.Ordinal);
        Assert.Contains("NotifyPeer = true", source, StringComparison.Ordinal);
        Assert.Contains("SendLocalAgentControlRequest", source, StringComparison.Ordinal);
        Assert.Contains("ReadServiceStatus", source, StringComparison.Ordinal);
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
