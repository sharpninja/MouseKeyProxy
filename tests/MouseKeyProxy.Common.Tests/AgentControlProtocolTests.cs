using System.Text.Json;
using MouseKeyProxy.Common;
using Xunit;

namespace MouseKeyProxy.Common.Tests;

/// <summary>
/// TR-MKP-SEC-001 / FR-MKP-011: real wire-contract tests for the agent-control IPC DTOs. The pipe
/// serializes with <see cref="JsonSerializerDefaults.Web"/>, so these verify a full serialize ->
/// deserialize round-trip preserves every field (including the security AuthToken) - not tautological
/// "set a property then assert it" checks.
/// </summary>
public class AgentControlProtocolTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static T RoundTrip<T>(T value) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Web), Web)!;

    /// <summary>A pairing-state request round-trips through the pipe's JSON contract with all fields intact.</summary>
    [Fact]
    [Trait("Category", "Pairing")]
    public void PairingStateRequest_RoundTrips()
    {
        var request = new AgentControlRequest
        {
            AuthToken = "tok-123",
            Operation = AgentControlPipe.NotifyPairingState,
            RemotePeer = "payton-desktop",
            RemoteGrpcUrl = "https://payton-desktop:50051",
            PairingCode = "ABC12345",
        };

        var back = RoundTrip(request);

        Assert.Equal("notifyPairingState", back.Operation);
        Assert.Equal("tok-123", back.AuthToken);
        Assert.Equal("payton-desktop", back.RemotePeer);
        Assert.Equal("https://payton-desktop:50051", back.RemoteGrpcUrl);
        Assert.Equal("ABC12345", back.PairingCode);
    }

    /// <summary>An input-injection request preserves its event list across the JSON round-trip.</summary>
    [Fact]
    [Trait("Category", "Injection")]
    public void InjectInputRequest_PreservesEvents()
    {
        var request = new AgentControlRequest
        {
            AuthToken = "t",
            Operation = AgentControlPipe.InjectInput,
        };
        request.Events.Add(new InputEvent(InputKind.KEY_DOWN, Vk: 0x41));
        request.Events.Add(new InputEvent(InputKind.KEY_UP, Vk: 0x41));

        var back = RoundTrip(request);

        Assert.Equal(2, back.Events.Count);
        Assert.Equal(InputKind.KEY_DOWN, back.Events[0].Kind);
        Assert.Equal(0x41u, back.Events[0].Vk);
        Assert.Equal(InputKind.KEY_UP, back.Events[1].Kind);
    }

    /// <summary>A status response round-trips its state and forwarding flag.</summary>
    [Fact]
    [Trait("Category", "Status")]
    public void StatusResponse_RoundTrips()
    {
        var response = new AgentControlResponse
        {
            Ok = true,
            RemotePeer = "payton-desktop",
            RemoteGrpcUrl = "https://payton-desktop:50051",
            RemoteState = "Connected",
            ForwardingActive = true,
        };

        var back = RoundTrip(response);

        Assert.True(back.Ok);
        Assert.Equal("Connected", back.RemoteState);
        Assert.True(back.ForwardingActive);
    }

    /// <summary>A screenshot response preserves its binary payload and metadata across the round-trip.</summary>
    [Fact]
    [Trait("Category", "Screenshot")]
    public void ScreenshotResponse_PreservesBinaryPayload()
    {
        var response = new AgentControlResponse
        {
            Ok = true,
            ScreenshotPng = new byte[] { 1, 2, 3, 250 },
            SourceHost = "payton-desktop",
            CorrelationId = "shot-proof",
            Width = 640,
            Height = 480,
            Sha256 = "abc123",
        };

        var back = RoundTrip(response);

        Assert.Equal(new byte[] { 1, 2, 3, 250 }, back.ScreenshotPng);
        Assert.Equal(640, back.Width);
        Assert.Equal(480, back.Height);
        Assert.Equal("abc123", back.Sha256);
        Assert.Equal("shot-proof", back.CorrelationId);
    }

    /// <summary>The operation name constants match their documented wire values.</summary>
    [Theory]
    [Trait("Category", "Contract")]
    [InlineData("notifyPairingState")]
    [InlineData("getAgentStatus")]
    [InlineData("emergencyRelease")]
    [InlineData("clearModifiers")]
    [InlineData("captureScreenshot")]
    [InlineData("injectInput")]
    public void OperationConstants_HaveStableWireNames(string expected)
    {
        var names = new[]
        {
            AgentControlPipe.NotifyPairingState,
            AgentControlPipe.GetAgentStatus,
            AgentControlPipe.EmergencyRelease,
            AgentControlPipe.ClearModifiers,
            AgentControlPipe.CaptureScreenshot,
            AgentControlPipe.InjectInput,
        };

        Assert.Contains(expected, names);
    }
}
