using MouseKeyProxy.Common;

namespace MouseKeyProxy.Common.Tests;

public class AgentControlProtocolTests
{
    [Fact]
    [Trait("Category", "Pairing")]
    public void AgentControlProtocol_Includes_Pairing_State_Notification()
    {
        var request = new AgentControlRequest
        {
            Operation = AgentControlPipe.NotifyPairingState,
            RemotePeer = "payton-desktop",
            RemoteGrpcUrl = "http://payton-desktop:50051",
            PairingCode = "valid-test"
        };

        Assert.Equal("notifyPairingState", AgentControlPipe.NotifyPairingState);
        Assert.Equal("payton-desktop", request.RemotePeer);
        Assert.Equal("http://payton-desktop:50051", request.RemoteGrpcUrl);
        Assert.Equal("valid-test", request.PairingCode);
    }

    [Fact]
    [Trait("Category", "Hotkey")]
    public void AgentControlProtocol_Includes_Agent_Status_For_Hotkey_Verification()
    {
        var response = new AgentControlResponse
        {
            Ok = true,
            RemotePeer = "payton-desktop",
            RemoteGrpcUrl = "http://payton-desktop:50051",
            RemoteState = "Connected",
            ForwardingActive = true
        };

        Assert.Equal("getAgentStatus", AgentControlPipe.GetAgentStatus);
        Assert.Equal("payton-desktop", response.RemotePeer);
        Assert.Equal("http://payton-desktop:50051", response.RemoteGrpcUrl);
        Assert.Equal("Connected", response.RemoteState);
        Assert.True(response.ForwardingActive);
    }

    [Fact]
    [Trait("Category", "EmergencyRelease")]
    public void AgentControlProtocol_Includes_Emergency_Release()
    {
        var request = new AgentControlRequest
        {
            Operation = AgentControlPipe.EmergencyRelease,
            RemotePeer = "payton-desktop",
            CorrelationId = "release-proof",
            NotifyPeer = true
        };

        Assert.Equal("emergencyRelease", AgentControlPipe.EmergencyRelease);
        Assert.Equal("payton-desktop", request.RemotePeer);
        Assert.Equal("release-proof", request.CorrelationId);
        Assert.True(request.NotifyPeer);
    }

    [Fact]
    [Trait("Category", "ModifierCleanup")]
    public void AgentControlProtocol_Includes_Clear_Modifiers_And_Screenshot_Capture()
    {
        var request = new AgentControlRequest
        {
            Operation = AgentControlPipe.CaptureScreenshot,
            ScreenshotTarget = "foreground",
            Hwnd = 0x1234,
            CorrelationId = "shot-proof",
            IncludeCursor = true
        };
        var response = new AgentControlResponse
        {
            Ok = true,
            ScreenshotPng = new byte[] { 1, 2, 3 },
            SourceHost = "payton-desktop",
            CorrelationId = "shot-proof",
            Width = 640,
            Height = 480,
            Sha256 = "abc123"
        };

        Assert.Equal("clearModifiers", AgentControlPipe.ClearModifiers);
        Assert.Equal("captureScreenshot", AgentControlPipe.CaptureScreenshot);
        Assert.Equal("foreground", request.ScreenshotTarget);
        Assert.Equal(0x1234UL, request.Hwnd);
        Assert.Equal("shot-proof", response.CorrelationId);
        Assert.Equal("abc123", response.Sha256);
        Assert.Equal(3, response.ScreenshotPng.Length);
    }}
