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
}
