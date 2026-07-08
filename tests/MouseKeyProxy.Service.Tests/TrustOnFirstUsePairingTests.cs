using System.Security.Cryptography;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using MouseKeyProxy.Network.V1;
using MouseKeyProxy.Service;
using MouseKeyProxy.Service.Pairing;
using NSubstitute;

namespace MouseKeyProxy.Service.Tests;

/// <summary>
/// TR-MKP-SEC-001: verifies trust-on-first-use (ToFU) pairing - the plug-n-play path. When ToFU is
/// enabled and no peer is yet paired, the first Pair request is accepted without a one-time code and
/// registers that peer; once paired, ToFU closes and further pairings require a code. With ToFU off,
/// a code is always required.
/// </summary>
public class TrustOnFirstUsePairingTests
{
    private static byte[] NewPub()
    {
        using var e = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return e.ExportSubjectPublicKeyInfo();
    }

    private static MouseKeyProxyImpl Impl(PairedPeerStore store, PairingCertificateAuthority ca, bool tofu) =>
        new(Substitute.For<ILogger<MouseKeyProxyImpl>>(), pairedPeerStore: store, certificateAuthority: ca,
            pairingOptions: new ServicePairingOptions { TrustOnFirstUse = tofu });

    private static ServerCallContext Ctx() => Substitute.For<ServerCallContext>();

    /// <summary>With ToFU on and no paired peer, a Pair without a code succeeds and registers the peer.</summary>
    [Fact]
    [Trait("Category", "SecurityNegative")]
    public async Task Tofu_FirstPair_WithoutCode_Succeeds()
    {
        var store = new PairedPeerStore();
        var ca = new PairingCertificateAuthority();
        var impl = Impl(store, ca, tofu: true);

        var resp = await impl.Pair(
            new PairRequest { PeerId = "first", PublicInfo = ByteString.CopyFrom(NewPub()) },
            Ctx());

        Assert.True(resp.Success);
        Assert.True(store.HasPairedPeer());
    }

    /// <summary>After the first ToFU pairing, a second codeless Pair is rejected (ToFU closed).</summary>
    [Fact]
    [Trait("Category", "SecurityNegative")]
    public async Task Tofu_ClosesAfterFirstPair()
    {
        var store = new PairedPeerStore();
        var ca = new PairingCertificateAuthority();
        var impl = Impl(store, ca, tofu: true);

        await impl.Pair(new PairRequest { PeerId = "first", PublicInfo = ByteString.CopyFrom(NewPub()) }, Ctx());
        var second = await impl.Pair(new PairRequest { PeerId = "second", PublicInfo = ByteString.CopyFrom(NewPub()) }, Ctx());

        Assert.False(second.Success);
        Assert.Equal("INVALID_OR_EXPIRED_CODE", second.Error);
    }

    /// <summary>With ToFU off, a codeless Pair is rejected even on a fresh device.</summary>
    [Fact]
    [Trait("Category", "SecurityNegative")]
    public async Task TofuOff_CodelessPair_IsRejected()
    {
        var store = new PairedPeerStore();
        var ca = new PairingCertificateAuthority();
        var impl = Impl(store, ca, tofu: false);

        var resp = await impl.Pair(
            new PairRequest { PeerId = "first", PublicInfo = ByteString.CopyFrom(NewPub()) },
            Ctx());

        Assert.False(resp.Success);
        Assert.Equal("INVALID_OR_EXPIRED_CODE", resp.Error);
    }

    /// <summary>With ToFU on, a valid one-time code still pairs normally.</summary>
    [Fact]
    [Trait("Category", "SecurityNegative")]
    public async Task Tofu_ValidCode_StillWorks()
    {
        var store = new PairedPeerStore();
        var ca = new PairingCertificateAuthority();
        var impl = Impl(store, ca, tofu: true);

        var code = (await impl.RequestPairingCode(new RequestPairingCodeRequest(), Ctx())).PairingCode;
        var resp = await impl.Pair(
            new PairRequest { PeerId = "coded", PairingCode = code, PublicInfo = ByteString.CopyFrom(NewPub()) },
            Ctx());

        Assert.True(resp.Success);
    }
}
