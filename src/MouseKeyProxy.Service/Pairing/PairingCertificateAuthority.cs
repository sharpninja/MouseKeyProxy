using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace MouseKeyProxy.Service.Pairing;

/// <summary>
/// TR-MKP-SEC-001: the service pairing certificate authority. Signs a per-peer client certificate
/// bound to a peer-supplied public key during pairing; the peer then presents that certificate for
/// mTLS and the authorization interceptor accepts only certificates this CA issued.
/// </summary>
public interface IPairingCertificateAuthority
{
    /// <summary>The CA certificate (public); peers trust it and the service validates client certs against it.</summary>
    X509Certificate2 CaCertificate { get; }

    /// <summary>Issues a client certificate for a peer, bound to the peer's supplied public key.</summary>
    /// <param name="peerId">The peer's logical id (used as the certificate subject CN).</param>
    /// <param name="peerPublicKeySpki">The peer's public key in SubjectPublicKeyInfo (DER) form.</param>
    /// <param name="validity">How long the issued certificate is valid.</param>
    /// <returns>The signed client certificate (public only; the peer holds the private key).</returns>
    X509Certificate2 IssuePeerCertificate(string peerId, byte[] peerPublicKeySpki, TimeSpan validity);

    /// <summary>Verifies a presented client certificate chains to this CA.</summary>
    /// <param name="clientCertificate">The certificate presented by a caller.</param>
    /// <returns>True when the certificate was issued by this CA.</returns>
    bool IsIssuedByThisCa(X509Certificate2 clientCertificate);
}

/// <summary>
/// TR-MKP-SEC-001: ECDSA (P-256) pairing CA. The CA key is generated in-process by default;
/// a persisted CA certificate (with private key) may be supplied so pairings survive restarts.
/// </summary>
public sealed class PairingCertificateAuthority : IPairingCertificateAuthority, IDisposable
{
    private static readonly Oid ClientAuthEku = new("1.3.6.1.5.5.7.3.2");

    private readonly X509Certificate2 _ca;

    /// <summary>Creates the CA, generating a fresh self-signed CA certificate unless one is supplied.</summary>
    /// <param name="caCertificate">Optional persisted CA certificate (must include its private key).</param>
    public PairingCertificateAuthority(X509Certificate2? caCertificate = null)
    {
        _ca = caCertificate ?? CreateCa();
    }

    /// <inheritdoc />
    public X509Certificate2 CaCertificate => _ca;

    /// <inheritdoc />
    public X509Certificate2 IssuePeerCertificate(string peerId, byte[] peerPublicKeySpki, TimeSpan validity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(peerId);
        ArgumentNullException.ThrowIfNull(peerPublicKeySpki);

        using var peerKey = ECDsa.Create();
        peerKey.ImportSubjectPublicKeyInfo(peerPublicKeySpki, out _);

        var request = new CertificateRequest(new X500DistinguishedName($"CN={peerId}"), peerKey, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { ClientAuthEku }, true));

        var now = DateTimeOffset.UtcNow;
        var serial = RandomNumberGenerator.GetBytes(16);
        return request.Create(_ca, now.AddMinutes(-5), now.Add(validity), serial);
    }

    /// <inheritdoc />
    public bool IsIssuedByThisCa(X509Certificate2 clientCertificate)
    {
        ArgumentNullException.ThrowIfNull(clientCertificate);

        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.CustomTrustStore.Add(_ca);
        chain.ChainPolicy.ExtraStore.Add(_ca);
        return chain.Build(clientCertificate);
    }

    private static X509Certificate2 CreateCa()
    {
        using var caKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=MouseKeyProxy Pairing CA", caKey, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));

        var now = DateTimeOffset.UtcNow;
        return request.CreateSelfSigned(now.AddDays(-1), now.AddYears(10));
    }

    /// <inheritdoc />
    public void Dispose() => _ca.Dispose();
}
