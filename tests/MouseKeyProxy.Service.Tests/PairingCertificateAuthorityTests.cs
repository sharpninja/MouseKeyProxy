using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using MouseKeyProxy.Service.Pairing;
using Xunit;

namespace MouseKeyProxy.Service.Tests;

/// <summary>
/// TR-MKP-SEC-001: verifies the service pairing CA issues per-peer client certificates bound to a
/// peer-supplied public key, that issued certs chain to the CA, and that foreign certs do not.
/// </summary>
public class PairingCertificateAuthorityTests
{
    private static byte[] NewPeerPublicKey()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return ecdsa.ExportSubjectPublicKeyInfo();
    }

    /// <summary>A cert issued for a peer public key chains to the CA and carries the peer id in its subject.</summary>
    [Fact]
    [Trait("Category", "Security")]
    public void IssuedPeerCert_ChainsToCa_AndCarriesPeerId()
    {
        var ca = new PairingCertificateAuthority();
        var pub = NewPeerPublicKey();

        using var cert = ca.IssuePeerCertificate("peer-42", pub, TimeSpan.FromDays(30));

        Assert.Contains("peer-42", cert.Subject, StringComparison.Ordinal);
        Assert.True(ca.IsIssuedByThisCa(cert));
        Assert.False(string.IsNullOrWhiteSpace(cert.Thumbprint));
    }

    /// <summary>A certificate not issued by this CA is rejected.</summary>
    [Fact]
    [Trait("Category", "Security")]
    public void ForeignCert_IsRejected()
    {
        var ca = new PairingCertificateAuthority();

        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var req = new CertificateRequest("CN=intruder", ecdsa, HashAlgorithmName.SHA256);
        using var foreign = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));

        Assert.False(ca.IsIssuedByThisCa(foreign));
    }

    /// <summary>Two peers get distinct thumbprints from the same CA.</summary>
    [Fact]
    [Trait("Category", "Security")]
    public void DistinctPeers_GetDistinctThumbprints()
    {
        var ca = new PairingCertificateAuthority();
        using var a = ca.IssuePeerCertificate("peer-a", NewPeerPublicKey(), TimeSpan.FromDays(1));
        using var b = ca.IssuePeerCertificate("peer-b", NewPeerPublicKey(), TimeSpan.FromDays(1));

        Assert.NotEqual(a.Thumbprint, b.Thumbprint);
        Assert.True(ca.IsIssuedByThisCa(a));
        Assert.True(ca.IsIssuedByThisCa(b));
    }
}
