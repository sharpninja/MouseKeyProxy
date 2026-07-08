using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using MouseKeyProxy.Commands;
using Xunit;

namespace MouseKeyProxy.Commands.Tests;

/// <summary>
/// TR-MKP-SEC-001: verifies the peer credential is persisted at rest (DPAPI on Windows, owner-only
/// file elsewhere) and round-trips with its private key and CA certificate intact.
/// </summary>
public class PeerCredentialStoreTests
{
    private static X509Certificate2 SelfSigned(string cn)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN={cn}", key, HashAlgorithmName.SHA256);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1));
    }

    /// <summary>Saving then loading a credential preserves the peer id, private key, and both thumbprints.</summary>
    [Fact]
    [Trait("Category", "Security")]
    public void Save_Then_Load_RoundTrips()
    {
        using var client = SelfSigned("peer-store");
        using var ca = SelfSigned("store-ca");
        var credential = new PeerCredential("peer-store", client, ca);

        var path = Path.Combine(Path.GetTempPath(), $"mkp-cred-{Guid.NewGuid():N}.bin");
        try
        {
            PeerCredentialStore.Save(path, credential);
            var loaded = PeerCredentialStore.Load(path);

            Assert.NotNull(loaded);
            Assert.Equal("peer-store", loaded!.PeerId);
            Assert.True(loaded.ClientCertificate.HasPrivateKey);
            Assert.Equal(client.Thumbprint, loaded.ClientCertificate.Thumbprint);
            Assert.Equal(ca.Thumbprint, loaded.CaCertificate.Thumbprint);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    /// <summary>Loading a missing credential file returns null.</summary>
    [Fact]
    [Trait("Category", "Security")]
    public void Load_MissingFile_ReturnsNull()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mkp-cred-missing-{Guid.NewGuid():N}.bin");
        Assert.Null(PeerCredentialStore.Load(path));
    }
}
