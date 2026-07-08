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
/// TEST-MKP-005 / TR-MKP-SEC-001: real pairing negative + positive matrix over the shipped
/// MouseKeyProxyImpl.RequestPairingCode + Pair flow. A pairing code must be minted first, is
/// single-use and time-bound, binds a peer-supplied public key to a service-signed client cert,
/// and registers the peer in the paired-peer store for the authorization interceptor.
/// </summary>
public class SecurityNegativeTests
{
    private static byte[] NewPeerPublicKey()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return ecdsa.ExportSubjectPublicKeyInfo();
    }

    private static (MouseKeyProxyImpl impl, PairedPeerStore store, PairingCertificateAuthority ca) Create()
    {
        var store = new PairedPeerStore();
        var ca = new PairingCertificateAuthority();
        var impl = new MouseKeyProxyImpl(
            Substitute.For<ILogger<MouseKeyProxyImpl>>(),
            pairedPeerStore: store,
            certificateAuthority: ca);
        return (impl, store, ca);
    }

    private static ServerCallContext Ctx() => Substitute.For<ServerCallContext>();

    /// <summary>A pairing attempt with a code that was never issued is rejected.</summary>
    [Fact]
    [Trait("Category", "SecurityNegative")]
    public async Task Pair_Rejects_Unissued_Code()
    {
        var (impl, _, _) = Create();
        var resp = await impl.Pair(
            new PairRequest { PeerId = "peer-a", PairingCode = "NEVERMINTED", PublicInfo = ByteString.CopyFrom(NewPeerPublicKey()) },
            Ctx());

        Assert.False(resp.Success);
        Assert.Equal("INVALID_OR_EXPIRED_CODE", resp.Error);
    }

    /// <summary>A pairing attempt with an empty peer id is rejected before any code is consumed.</summary>
    [Fact]
    [Trait("Category", "SecurityNegative")]
    public async Task Pair_Rejects_Empty_PeerId()
    {
        var (impl, _, _) = Create();
        var resp = await impl.Pair(
            new PairRequest { PeerId = "", PairingCode = "x", PublicInfo = ByteString.CopyFrom(NewPeerPublicKey()) },
            Ctx());

        Assert.False(resp.Success);
        Assert.Equal("MISSING_PEER_ID", resp.Error);
    }

    /// <summary>A pairing attempt without a peer public key is rejected.</summary>
    [Fact]
    [Trait("Category", "SecurityNegative")]
    public async Task Pair_Rejects_Missing_PublicKey()
    {
        var (impl, store, _) = Create();
        var code = (await impl.RequestPairingCode(new RequestPairingCodeRequest(), Ctx())).PairingCode;

        var resp = await impl.Pair(
            new PairRequest { PeerId = "peer-a", PairingCode = code },
            Ctx());

        Assert.False(resp.Success);
        Assert.Equal("MISSING_PUBLIC_KEY", resp.Error);
    }

    /// <summary>A valid code + public key issues a CA-chained cert, returns the CA cert, and registers the peer.</summary>
    [Fact]
    [Trait("Category", "SecurityNegative")]
    public async Task Pair_Succeeds_WithIssuedCode_AndRegistersPeer()
    {
        var (impl, store, ca) = Create();
        var code = (await impl.RequestPairingCode(new RequestPairingCodeRequest { TtlSeconds = 300 }, Ctx())).PairingCode;

        var resp = await impl.Pair(
            new PairRequest { PeerId = "trusted-peer", PairingCode = code, PublicInfo = ByteString.CopyFrom(NewPeerPublicKey()) },
            Ctx());

        Assert.True(resp.Success);
        Assert.False(resp.PeerCert.IsEmpty);
        Assert.False(resp.CaCertificate.IsEmpty);

        using var issued = System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadCertificate(resp.PeerCert.ToByteArray());
        Assert.True(ca.IsIssuedByThisCa(issued));
        Assert.True(store.IsAuthorized(issued.Thumbprint));
    }

    /// <summary>A pairing code is single-use: the second attempt with the same code is rejected.</summary>
    [Fact]
    [Trait("Category", "SecurityNegative")]
    public async Task Pair_Code_Is_SingleUse()
    {
        var (impl, _, _) = Create();
        var code = (await impl.RequestPairingCode(new RequestPairingCodeRequest(), Ctx())).PairingCode;

        var first = await impl.Pair(
            new PairRequest { PeerId = "peer-a", PairingCode = code, PublicInfo = ByteString.CopyFrom(NewPeerPublicKey()) },
            Ctx());
        var second = await impl.Pair(
            new PairRequest { PeerId = "peer-a", PairingCode = code, PublicInfo = ByteString.CopyFrom(NewPeerPublicKey()) },
            Ctx());

        Assert.True(first.Success);
        Assert.False(second.Success);
        Assert.Equal("INVALID_OR_EXPIRED_CODE", second.Error);
    }
}
