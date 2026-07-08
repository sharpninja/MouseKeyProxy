using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace MouseKeyProxy.Service.Pairing;

/// <summary>TR-MKP-SEC-001: the persisted pairing state - the CA (with private key) and the paired peers.</summary>
/// <param name="CaCertificate">The CA certificate including its private key.</param>
/// <param name="Peers">The paired-peer records.</param>
public sealed record PairingState(X509Certificate2 CaCertificate, IReadOnlyList<PairedPeer> Peers);

/// <summary>
/// TR-MKP-SEC-001: at-rest persistence for the service pairing state so a paired device stays paired
/// across a Service restart / Pi reboot. The container (CA PKCS#12 + peer records) is DPAPI-protected
/// on Windows and written owner-only (POSIX 0600) elsewhere - e.g. under /var/lib/mousekeyproxy on the Pi.
/// </summary>
public static class PairingStateStore
{
    private sealed class Container
    {
        public byte[] CaPfx { get; set; } = Array.Empty<byte>();
        public List<PersistedPeer> Peers { get; set; } = new();
    }

    private sealed class PersistedPeer
    {
        public string PeerId { get; set; } = string.Empty;
        public string CertThumbprint { get; set; } = string.Empty;
        public DateTimeOffset PairedUtc { get; set; }
        public DateTimeOffset LastSeenUtc { get; set; }
        public bool Revoked { get; set; }
    }

    /// <summary>The default state path for the current platform.</summary>
    /// <returns>The absolute state file path.</returns>
    public static string DefaultPath()
    {
        var stateDir = Environment.GetEnvironmentVariable("MKP_STATE_DIR");
        if (!string.IsNullOrWhiteSpace(stateDir))
        {
            return Path.Combine(stateDir, "pairing-state.bin");
        }

        var baseDir = OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MouseKeyProxy")
            : "/var/lib/mousekeyproxy";
        return Path.Combine(baseDir, "pairing-state.bin");
    }

    /// <summary>Persists the CA and paired peers to <paramref name="path"/>, protected at rest.</summary>
    /// <param name="path">The state file path.</param>
    /// <param name="caCertificate">The CA certificate (must include its private key).</param>
    /// <param name="peers">The paired-peer records.</param>
    public static void Save(string path, X509Certificate2 caCertificate, IReadOnlyList<PairedPeer> peers)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(caCertificate);
        ArgumentNullException.ThrowIfNull(peers);

        var container = new Container
        {
            CaPfx = caCertificate.Export(X509ContentType.Pkcs12),
            Peers = peers.Select(p => new PersistedPeer
            {
                PeerId = p.PeerId,
                CertThumbprint = p.CertThumbprint,
                PairedUtc = p.PairedUtc,
                LastSeenUtc = p.LastSeenUtc,
                Revoked = p.Revoked,
            }).ToList(),
        };

        var plaintext = JsonSerializer.SerializeToUtf8Bytes(container);
        var atRest = OperatingSystem.IsWindows() ? Protect(plaintext) : plaintext;

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllBytes(path, atRest);
        RestrictToOwner(path);
    }

    /// <summary>Loads the pairing state from <paramref name="path"/>, or null when absent.</summary>
    /// <param name="path">The state file path.</param>
    /// <returns>The pairing state, or null.</returns>
    public static PairingState? Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        var atRest = File.ReadAllBytes(path);
        var plaintext = OperatingSystem.IsWindows() ? Unprotect(atRest) : atRest;
        var container = JsonSerializer.Deserialize<Container>(plaintext)
            ?? throw new InvalidDataException("Pairing state file is corrupt.");

        var ca = X509CertificateLoader.LoadPkcs12(container.CaPfx, null);
        var peers = container.Peers
            .Select(p => new PairedPeer(p.PeerId, p.CertThumbprint, p.PairedUtc, p.LastSeenUtc, p.Revoked))
            .ToList();

        return new PairingState(ca, peers);
    }

    [SupportedOSPlatform("windows")]
    private static byte[] Protect(byte[] plaintext) =>
        ProtectedData.Protect(plaintext, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);

    [SupportedOSPlatform("windows")]
    private static byte[] Unprotect(byte[] atRest) =>
        ProtectedData.Unprotect(atRest, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);

    private static void RestrictToOwner(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (PlatformNotSupportedException)
        {
            // Best effort where POSIX permissions are unavailable.
        }
    }
}
