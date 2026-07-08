using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace MouseKeyProxy.Common;

/// <summary>
/// TR-MKP-SEC-001: authentication for the local agent-control named pipe. The agent mints a random
/// per-session token; the (same-user) service reads it from a user-scoped file and presents it on
/// every request. Validation is constant-time to avoid leaking the token via timing.
/// </summary>
public static class AgentControlAuth
{
    /// <summary>Generates a new random URL-safe token.</summary>
    /// <returns>A 256-bit base64url token.</returns>
    public static string GenerateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>Constant-time validation of a presented token against the expected token.</summary>
    /// <param name="expected">The token the agent minted (null/empty means no auth configured -> reject all).</param>
    /// <param name="presented">The token presented by the caller.</param>
    /// <returns>True only when both are non-empty and equal.</returns>
    public static bool Validate(string? expected, string? presented)
    {
        if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(presented))
        {
            return false;
        }

        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var presentedBytes = Encoding.UTF8.GetBytes(presented);
        return CryptographicOperations.FixedTimeEquals(expectedBytes, presentedBytes);
    }
}

/// <summary>
/// TR-MKP-SEC-001: user-scoped persistence for the agent-control auth token. On Windows the file is
/// ACL-restricted to the current user; on other platforms POSIX file permissions (0600) are applied.
/// </summary>
public static class AgentControlTokenStore
{
    /// <summary>The default token path under the user's local application data.</summary>
    /// <returns>The absolute token file path.</returns>
    public static string DefaultPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MouseKeyProxy",
        "agent-control.token");

    /// <summary>Writes the token to <paramref name="path"/> with owner-only permissions.</summary>
    /// <param name="path">The token file path.</param>
    /// <param name="token">The token to persist.</param>
    public static void Write(string path, string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, token);
        RestrictToOwner(path);
    }

    /// <summary>Reads the token from <paramref name="path"/>, or null when the file is absent.</summary>
    /// <param name="path">The token file path.</param>
    /// <returns>The token, or null.</returns>
    public static string? Read(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        var token = File.ReadAllText(path).Trim();
        return string.IsNullOrEmpty(token) ? null : token;
    }

    private static void RestrictToOwner(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return; // NTFS inherits the user-profile ACL; LocalApplicationData is already user-scoped.
        }

        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (PlatformNotSupportedException)
        {
            // Best effort on platforms without POSIX permissions.
        }
    }
}
