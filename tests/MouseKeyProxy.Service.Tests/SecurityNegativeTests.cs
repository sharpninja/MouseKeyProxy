using Grpc.Core;
using Microsoft.Extensions.Logging;
using MouseKeyProxy.Network.V1;
using MouseKeyProxy.Service;
using NSubstitute;

namespace MouseKeyProxy.Service.Tests;

/// <summary>
/// TEST-MKP-005: pairing security negative matrix via shipped MouseKeyProxyImpl.Pair.
/// </summary>
public class SecurityNegativeTests
{
    private static MouseKeyProxyImpl CreateImpl() =>
        new(Substitute.For<ILogger<MouseKeyProxyImpl>>());

    [Fact]
    [Trait("Category", "SecurityNegative")]
    public async Task TEST_MKP_005_Pair_Rejects_Bad_Secret_Before_Success()
    {
        var impl = CreateImpl();
        var resp = await impl.Pair(
            new PairRequest { PeerId = "peer-a", PairingCode = "invalid-code" },
            Substitute.For<ServerCallContext>());

        Assert.False(resp.Success);
        Assert.Equal("AUTH_FAIL", resp.Error);
    }

    [Fact]
    [Trait("Category", "SecurityNegative")]
    public async Task TEST_MKP_005_Pair_Rejects_Empty_PeerId()
    {
        var impl = CreateImpl();
        var resp = await impl.Pair(
            new PairRequest { PeerId = "", PairingCode = "valid-test" },
            Substitute.For<ServerCallContext>());

        Assert.False(resp.Success);
        Assert.Equal("AUTH_FAIL", resp.Error);
    }

    [Fact]
    [Trait("Category", "SecurityNegative")]
    public async Task TEST_MKP_005_Pair_Rejects_Revoked_Or_Unpaired_Credentials()
    {
        var impl = CreateImpl();
        var resp = await impl.Pair(
            new PairRequest { PeerId = "revoked-peer", PairingCode = "0000" },
            Substitute.For<ServerCallContext>());

        Assert.False(resp.Success);
        Assert.Equal("AUTH_FAIL", resp.Error);
    }

    [Fact]
    [Trait("Category", "SecurityNegative")]
    public async Task TEST_MKP_005_Valid_Credentials_Succeed_While_Invalid_Rejected()
    {
        var impl = CreateImpl();
        var ctx = Substitute.For<ServerCallContext>();
        var good = await impl.Pair(
            new PairRequest { PeerId = "trusted-peer", PairingCode = "valid-test" },
            ctx);
        var bad = await impl.Pair(
            new PairRequest { PeerId = "trusted-peer", PairingCode = "wrong-secret" },
            ctx);

        Assert.True(good.Success);
        Assert.False(bad.Success);
        Assert.Equal("AUTH_FAIL", bad.Error);
    }
}