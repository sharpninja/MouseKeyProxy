using System.Net.Sockets;
using MouseKeyProxy.Common;

namespace MouseKeyProxy.Integration.Tests;

public class TransitionE2ETests
{
    private static void AssertGrpcReachable(string host, int port)
    {
        using var client = new TcpClient();
        client.ReceiveTimeout = 5000;
        client.SendTimeout = 5000;
        client.Connect(host, port);
        Assert.True(client.Connected, $"gRPC endpoint {host}:{port} must be reachable on lab peer");
    }

    [Fact]
    [Trait("Category", "TwoMachineE2E")]
    public void Transition_Local_Lab_Peer_Grpc_Reachable()
    {
        var (local, _) = LabTopology.ResolvePeers();
        AssertGrpcReachable(local, LabTopology.GrpcPort);
    }

    [Fact]
    [Trait("Category", "TwoMachineE2E")]
    public void Transition_Remote_Lab_Peer_Grpc_Reachable()
    {
        var (_, remote) = LabTopology.ResolvePeers();
        AssertGrpcReachable(remote, LabTopology.GrpcPort);
    }

    [Fact]
    [Trait("Category", "TwoMachineE2E")]
    public void Transition_Lab_Pair_Is_Legion2_And_Desktop()
    {
        var (local, remote) = LabTopology.ResolvePeers();
        Assert.Contains(local, new[] { LabTopology.Legion2, LabTopology.Desktop }, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(remote, new[] { LabTopology.Legion2, LabTopology.Desktop }, StringComparer.OrdinalIgnoreCase);
        Assert.NotEqual(local, remote, StringComparer.OrdinalIgnoreCase);
    }
}