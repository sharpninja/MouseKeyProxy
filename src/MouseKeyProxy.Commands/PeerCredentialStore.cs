using System;
using System.IO;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace MouseKeyProxy.Commands;

/// <summary>
/// TR-MKP-SEC-001: at-rest persistence for a paired <see cref="PeerCredential"/>. On Windows the
/// serialized container is DPAPI-protected (CurrentUser); on other platforms (e.g. the Linux/Pi peer)
/// the file is written with owner-only permissions. The client certificate keeps its private key.
/// </summary>
public static class PeerCredentialStore
{
    private sealed class Container
    {
        public string PeerId { get; set; } = string.Empty;
        public byte[] ClientPfx { get; set; } = Array.Empty<byte>();
        public byte[] CaCert { get; set; } = Array.Empty<byte>();
    }

    /// <summary>The default credential path under the user's local application data.</summary>
    /// <returns>The absolute credential file path.</returns>
    public static string DefaultPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MouseKeyProxy",
        "peer-credential.bin");

    /// <summary>Persists the credential at <paramref name="path"/>, protected at rest.</summary>
    /// <param name="path">The credential file path.</param>
    /// <param name="credential">The paired credential to persist.</param>
    public static void Save(string path, PeerCredential credential)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(credential);

        var container = new Container
        {
            PeerId = credential.PeerId,
            ClientPfx = credential.ClientCertificate.Export(X509ContentType.Pkcs12),
            CaCert = credential.CaCertificate.Export(X509ContentType.Cert),
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

    /// <summary>Loads the credential from <paramref name="path"/>, or null when absent.</summary>
    /// <param name="path">The credential file path.</param>
    /// <returns>The credential, or null.</returns>
    public static PeerCredential? Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        var atRest = File.ReadAllBytes(path);
        var plaintext = OperatingSystem.IsWindows() ? Unprotect(atRest) : atRest;

        var container = JsonSerializer.Deserialize<Container>(plaintext)
            ?? throw new InvalidDataException("Peer credential file is corrupt.");

        // Exportable so a reloaded credential can itself be re-persisted (Save re-exports the key).
        var client = X509CertificateLoader.LoadPkcs12(container.ClientPfx, null, X509KeyStorageFlags.Exportable);
        var ca = X509CertificateLoader.LoadCertificate(container.CaCert);
        return new PeerCredential(container.PeerId, client, ca);
    }

    /// <summary>Deletes the credential file at <paramref name="path"/> if it exists.</summary>
    /// <param name="path">Credential file path.</param>
    /// <returns>True when a file was removed.</returns>
    public static bool Delete(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        File.Delete(path);
        return true;
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
            return; // LocalApplicationData is already user-scoped on NTFS.
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
