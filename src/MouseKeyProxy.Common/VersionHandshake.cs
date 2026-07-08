using System;
using System.Globalization;

namespace MouseKeyProxy.Common;

/// <summary>
/// TR-MKP-SEC-001 / proto contract: the OpenSession protocol-version handshake. An exact major-version
/// match is required; a mismatch is reported with a VERSION_MISMATCH message naming both versions.
/// </summary>
public static class VersionHandshake
{
    /// <summary>The protocol version this build speaks.</summary>
    public const string CurrentVersion = "1.0";

    /// <summary>Parses the major-version component, or -1 when the value is empty or unparseable.</summary>
    /// <param name="version">A version string such as "1.0".</param>
    /// <returns>The major version, or -1 when it cannot be determined.</returns>
    public static int MajorOf(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return -1;
        }

        var dot = version.IndexOf('.');
        var head = dot >= 0 ? version[..dot] : version;
        return int.TryParse(head, NumberStyles.Integer, CultureInfo.InvariantCulture, out var major) ? major : -1;
    }

    /// <summary>
    /// Checks whether a client version is compatible with a server version (exact major match).
    /// </summary>
    /// <param name="serverVersion">The server's protocol version.</param>
    /// <param name="clientVersion">The peer's declared protocol version.</param>
    /// <returns>Null when compatible; otherwise a VERSION_MISMATCH message naming both versions.</returns>
    public static string? CheckCompatibility(string serverVersion, string clientVersion)
    {
        var serverMajor = MajorOf(serverVersion);
        var clientMajor = MajorOf(clientVersion);
        if (serverMajor < 0 || clientMajor < 0 || serverMajor != clientMajor)
        {
            return $"VERSION_MISMATCH server={serverVersion} client={clientVersion}";
        }

        return null;
    }
}
