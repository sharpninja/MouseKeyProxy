using System;
using System.Net.Sockets;
using MouseKeyProxy.Common;

namespace MouseKeyProxy.Integration.Tests;

/// <summary>
/// Two-machine lab E2E. These require the named lab peers with a live gRPC service, so they are
/// opt-in: they skip unless MKP_LAB_E2E=1 (set by the Nuke IntegrationTest target on a lab peer).
/// This keeps a routine unit run from depending on live lab hardware.
/// </summary>
public class TransitionE2ETests
{
    private static bool LabE2EEnabled =>
        string.Equals(Environment.GetEnvironmentVariable("MKP_LAB_E2E"), "1", StringComparison.Ordinal);

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
        Assert.SkipUnless(LabE2EEnabled, "Lab two-machine E2E disabled; set MKP_LAB_E2E=1 on a lab peer.");
        var (local, _) = LabTopology.ResolvePeers();
        AssertGrpcReachable(local, LabTopology.GrpcPort);
    }

    [Fact]
    [Trait("Category", "TwoMachineE2E")]
    public void Transition_Remote_Lab_Peer_Grpc_Reachable()
    {
        Assert.SkipUnless(LabE2EEnabled, "Lab two-machine E2E disabled; set MKP_LAB_E2E=1 on a lab peer.");
        var (_, remote) = LabTopology.ResolvePeers();
        AssertGrpcReachable(remote, LabTopology.GrpcPort);
    }

    [Fact]
    [Trait("Category", "TwoMachineE2E")]
    public void Transition_Lab_Pair_Is_Legion2_And_Desktop()
    {
        Assert.SkipUnless(LabE2EEnabled, "Lab two-machine E2E disabled; set MKP_LAB_E2E=1 on a lab peer.");
        var (local, remote) = LabTopology.ResolvePeers();
        Assert.Contains(local, new[] { LabTopology.Legion2, LabTopology.Desktop }, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(remote, new[] { LabTopology.Legion2, LabTopology.Desktop }, StringComparer.OrdinalIgnoreCase);
        Assert.NotEqual(local, remote, StringComparer.OrdinalIgnoreCase);
    }
}