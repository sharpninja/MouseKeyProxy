using System;
using System.IO;
using System.Linq;
using MouseKeyProxy.Service.Pairing;
using Xunit;

namespace MouseKeyProxy.Service.Tests;

/// <summary>
/// TR-MKP-SEC-001: verifies the service pairing state (CA certificate + private key, and paired-peer
/// records) persists to disk and reloads intact, so a paired device stays paired across a Service
/// restart / Pi reboot. On Windows the container is DPAPI-protected; elsewhere it is owner-only.
/// </summary>
public class PairingStateStoreTests
{
    /// <summary>Saving then loading round-trips the CA (with private key) and the paired peers.</summary>
    [Fact]
    [Trait("Category", "Security")]
    public void Save_Then_Load_RoundTrips_Ca_And_Peers()
    {
        var ca = new PairingCertificateAuthority();
        var caThumb = ca.CaCertificate.Thumbprint;

        var now = DateTimeOffset.UtcNow;
        var peers = new[]
        {
            new PairedPeer("peer-a", "THUMB-A", now, now, Revoked: false),
            new PairedPeer("peer-b", "THUMB-B", now, now, Revoked: true),
        };

        var path = Path.Combine(Path.GetTempPath(), $"mkp-state-{Guid.NewGuid():N}.bin");
        try
        {
            PairingStateStore.Save(path, ca.CaCertificate, peers);
            var loaded = PairingStateStore.Load(path);

            Assert.NotNull(loaded);
            Assert.True(loaded!.CaCertificate.HasPrivateKey);
            Assert.Equal(caThumb, loaded.CaCertificate.Thumbprint);
            Assert.Equal(2, loaded.Peers.Count);
            Assert.Contains(loaded.Peers, p => p.PeerId == "peer-a" && !p.Revoked && p.CertThumbprint == "THUMB-A");
            Assert.Contains(loaded.Peers, p => p.PeerId == "peer-b" && p.Revoked);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    /// <summary>A reloaded CA still issues certs that chain to it (the CA key survived the round-trip).</summary>
    [Fact]
    [Trait("Category", "Security")]
    public void Reloaded_Ca_Still_Issues_ChainingCerts()
    {
        var original = new PairingCertificateAuthority();
        var path = Path.Combine(Path.GetTempPath(), $"mkp-state-{Guid.NewGuid():N}.bin");
        try
        {
            PairingStateStore.Save(path, original.CaCertificate, Array.Empty<PairedPeer>());
            var loaded = PairingStateStore.Load(path)!;

            // Rebuild a CA from the persisted certificate and issue a peer cert.
            var reloadedCa = new PairingCertificateAuthority(loaded.CaCertificate);
            using var ecdsa = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
            using var issued = reloadedCa.IssuePeerCertificate("peer-x", ecdsa.ExportSubjectPublicKeyInfo(), TimeSpan.FromDays(1));

            Assert.True(reloadedCa.IsIssuedByThisCa(issued));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    /// <summary>Loading a missing state file returns null (fresh device).</summary>
    [Fact]
    [Trait("Category", "Security")]
    public void Load_MissingFile_ReturnsNull()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mkp-state-missing-{Guid.NewGuid():N}.bin");
        Assert.Null(PairingStateStore.Load(path));
    }

    /// <summary>A peer registered before a restart is still authorized after reloading persisted state.</summary>
    [Fact]
    [Trait("Category", "Security")]
    public void PairedPeer_StaysPaired_AcrossRestart()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mkp-state-{Guid.NewGuid():N}.bin");
        try
        {
            var ca = new PairingCertificateAuthority();

            // First "boot": a store that persists on change.
            var store1 = new PairedPeerStore(
                timeProvider: null,
                initialPeers: null,
                onChanged: peers => PairingStateStore.Save(path, ca.CaCertificate, peers));
            store1.RegisterPeer("peer-1", "THUMB-1");
            Assert.True(store1.IsAuthorized("THUMB-1"));

            // Second "boot": reconstitute from persisted state.
            var loaded = PairingStateStore.Load(path);
            Assert.NotNull(loaded);
            var store2 = new PairedPeerStore(timeProvider: null, initialPeers: loaded!.Peers, onChanged: null);

            Assert.True(store2.IsAuthorized("THUMB-1"));
            Assert.True(store2.HasPairedPeer());
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
