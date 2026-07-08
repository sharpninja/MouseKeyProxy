using System;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Grpc.Net.Client;
using Wire = MouseKeyProxy.Network.V1;

namespace MouseKeyProxy.Commands;

/// <summary>TR-MKP-SEC-001: the credential a peer holds after pairing.</summary>
/// <param name="PeerId">The peer's logical id.</param>
/// <param name="ClientCertificate">The service-signed client certificate, including this peer's private key.</param>
/// <param name="CaCertificate">The service CA certificate the peer trusts for the mTLS channel.</param>
public sealed record PeerCredential(string PeerId, X509Certificate2 ClientCertificate, X509Certificate2 CaCertificate);

/// <summary>TR-MKP-SEC-001: raised when pairing fails.</summary>
public sealed class PairingException : Exception
{
    /// <summary>Creates the exception with a service-provided or local error code.</summary>
    /// <param name="error">The pairing error code.</param>
    public PairingException(string error) : base($"Pairing failed: {error}") => Error = error;

    /// <summary>The pairing error code (service reason or a local SERVER_CERT_UNTRUSTED).</summary>
    public string Error { get; }
}

/// <summary>
/// TR-MKP-SEC-001 / TR-MKP-ARCH-001: peer-side pairing. Generates a keypair, calls the service's
/// Pair RPC with the public key + one-time code, trusts the server certificate on first use and
/// verifies it chains to the returned CA, then binds the issued certificate to the private key.
/// The same credential is used to open a mutually-authenticated channel for effect RPCs.
/// </summary>
public static class PairingClient
{
    /// <summary>Pairs with the service at <paramref name="address"/> and returns the peer credential.</summary>
    /// <param name="address">The service base address (https).</param>
    /// <param name="peerId">This peer's logical id.</param>
    /// <param name="pairingCode">The one-time pairing code obtained out of band.</param>
    /// <param name="protocolVersion">Protocol version string.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The paired credential (client cert with private key + CA cert).</returns>
    public static async Task<PeerCredential> PairAsync(
        string address,
        string peerId,
        string pairingCode,
        string protocolVersion = "v1",
        CancellationToken cancellationToken = default)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var spki = key.ExportSubjectPublicKeyInfo();

        byte[]? serverCertRaw = null;
        var handler = new SocketsHttpHandler
        {
            SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (_, cert, _, _) =>
                {
                    // Trust on first use: capture the presented server cert, verify it chains to the
                    // CA the service returns in the Pair response (below).
                    serverCertRaw = cert?.Export(X509ContentType.Cert);
                    return true;
                }
            }
        };

        using var channel = GrpcChannel.ForAddress(address, new GrpcChannelOptions { HttpHandler = handler });
        var client = new Wire.MouseKeyProxy.MouseKeyProxyClient(channel);

        var response = await client.PairAsync(
            new Wire.PairRequest
            {
                ProtocolVersion = protocolVersion,
                PeerId = peerId,
                PairingCode = pairingCode,
                PublicInfo = ByteString.CopyFrom(spki),
            },
            cancellationToken: cancellationToken);

        if (!response.Success)
        {
            throw new PairingException(string.IsNullOrEmpty(response.Error) ? "UNKNOWN" : response.Error);
        }

        var ca = X509CertificateLoader.LoadCertificate(response.CaCertificate.ToByteArray());
        using var issued = X509CertificateLoader.LoadCertificate(response.PeerCert.ToByteArray());

        if (serverCertRaw is null)
        {
            ca.Dispose();
            throw new PairingException("SERVER_CERT_MISSING");
        }

        using (var serverCert = X509CertificateLoader.LoadCertificate(serverCertRaw))
        {
            if (!ChainsToCa(serverCert, ca))
            {
                ca.Dispose();
                throw new PairingException("SERVER_CERT_UNTRUSTED");
            }
        }

        using var withKey = issued.CopyWithPrivateKey(key);
        // Round-trip through PKCS#12 so the private key is durably bound for the TLS stack. Load it as
        // Exportable so the credential can later be re-exported for at-rest persistence (PeerCredentialStore
        // .Save); without this the private key is non-exportable on Windows and the save throws
        // "Key not valid for use in specified state" (NTE_BAD_KEY_STATE).
        var clientCert = X509CertificateLoader.LoadPkcs12(
            withKey.Export(X509ContentType.Pkcs12),
            null,
            X509KeyStorageFlags.Exportable);
        return new PeerCredential(peerId, clientCert, ca);
    }

    /// <summary>
    /// Opens a mutually-authenticated gRPC channel for effect RPCs: presents the peer client
    /// certificate and validates the server certificate against the paired CA.
    /// </summary>
    /// <param name="address">The service base address (https).</param>
    /// <param name="credential">The paired credential.</param>
    /// <returns>A configured channel (dispose when done).</returns>
    public static GrpcChannel CreateAuthenticatedChannel(string address, PeerCredential credential)
    {
        ArgumentNullException.ThrowIfNull(credential);

        var handler = new SocketsHttpHandler
        {
            EnableMultipleHttp2Connections = true,
            SslOptions = new SslClientAuthenticationOptions
            {
                ClientCertificates = new X509CertificateCollection { credential.ClientCertificate },
                RemoteCertificateValidationCallback = (_, cert, _, _) =>
                {
                    if (cert is null)
                    {
                        return false;
                    }

                    using var presented = X509CertificateLoader.LoadCertificate(cert.Export(X509ContentType.Cert));
                    return ChainsToCa(presented, credential.CaCertificate);
                },
            }
        };

        return GrpcChannel.ForAddress(address, new GrpcChannelOptions { HttpHandler = handler });
    }

    private static bool ChainsToCa(X509Certificate2 certificate, X509Certificate2 ca)
    {
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.CustomTrustStore.Add(ca);
        chain.ChainPolicy.ExtraStore.Add(ca);
        return chain.Build(certificate);
    }
}
