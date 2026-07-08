using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using MouseKeyProxy.Service.Pairing;
using Xunit;

namespace MouseKeyProxy.Service.Tests;

/// <summary>
/// TR-MKP-SEC-001: verifies the pairing authorizer - the pure decision behind the gRPC interceptor.
/// Pairing-bootstrap RPCs are allowed unauthenticated; every effect-bearing RPC requires a
/// CA-issued, paired, non-revoked client certificate.
/// </summary>
public class PairingAuthorizerTests
{
    private static byte[] NewPub()
    {
        using var e = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return e.ExportSubjectPublicKeyInfo();
    }

    private static (PairingAuthorizer auth, PairingCertificateAuthority ca, PairedPeerStore store) New()
    {
        var ca = new PairingCertificateAuthority();
        var store = new PairedPeerStore();
        return (new PairingAuthorizer(ca, store), ca, store);
    }

    /// <summary>Pairing bootstrap RPCs are allowed with no client certificate.</summary>
    [Theory]
    [Trait("Category", "Security")]
    [InlineData("Pair")]
    [InlineData("RequestPairingCode")]
    public void BootstrapRpcs_AllowedWithoutCert(string method)
    {
        var (auth, _, _) = New();
        Assert.Null(auth.Authorize(method, clientCertificate: null));
    }

    /// <summary>An effect-bearing RPC with no client certificate is denied.</summary>
    [Fact]
    [Trait("Category", "Security")]
    public void EffectRpc_NoCert_Denied()
    {
        var (auth, _, _) = New();
        Assert.NotNull(auth.Authorize("InjectInput", clientCertificate: null));
    }

    /// <summary>An effect-bearing RPC with a foreign (non-CA) certificate is denied.</summary>
    [Fact]
    [Trait("Category", "Security")]
    public void EffectRpc_ForeignCert_Denied()
    {
        var (auth, _, _) = New();
        using var e = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var req = new CertificateRequest("CN=intruder", e, HashAlgorithmName.SHA256);
        using var foreign = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));

        Assert.NotNull(auth.Authorize("InjectInput", foreign));
    }

    /// <summary>An effect-bearing RPC with a CA-issued, registered cert is allowed.</summary>
    [Fact]
    [Trait("Category", "Security")]
    public void EffectRpc_PairedCert_Allowed()
    {
        var (auth, ca, store) = New();
        using var cert = ca.IssuePeerCertificate("peer-x", NewPub(), TimeSpan.FromDays(1));
        store.RegisterPeer("peer-x", cert.Thumbprint);

        Assert.Null(auth.Authorize("InjectInput", cert));
    }

    /// <summary>An effect-bearing RPC with a CA-issued but not-yet-registered cert is denied.</summary>
    [Fact]
    [Trait("Category", "Security")]
    public void EffectRpc_IssuedButUnregistered_Denied()
    {
        var (auth, ca, _) = New();
        using var cert = ca.IssuePeerCertificate("peer-y", NewPub(), TimeSpan.FromDays(1));

        Assert.NotNull(auth.Authorize("InjectInput", cert));
    }

    /// <summary>An effect-bearing RPC with a revoked peer's cert is denied.</summary>
    [Fact]
    [Trait("Category", "Security")]
    public void EffectRpc_RevokedCert_Denied()
    {
        var (auth, ca, store) = New();
        using var cert = ca.IssuePeerCertificate("peer-z", NewPub(), TimeSpan.FromDays(1));
        store.RegisterPeer("peer-z", cert.Thumbprint);
        store.Revoke("peer-z");

        Assert.NotNull(auth.Authorize("InjectInput", cert));
    }
}
