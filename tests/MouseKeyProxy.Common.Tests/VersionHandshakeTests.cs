using MouseKeyProxy.Common;
using Xunit;

namespace MouseKeyProxy.Common.Tests;

/// <summary>
/// TR-MKP-SEC-001 / proto contract: verifies the OpenSession version handshake. Exact major-version
/// match is required; a mismatch yields a VERSION_MISMATCH message naming both versions.
/// </summary>
public class VersionHandshakeTests
{
    /// <summary>Equal major versions are compatible (null result), even when minor differs.</summary>
    [Theory]
    [Trait("Category", "Versioning")]
    [InlineData("1.0", "1.0")]
    [InlineData("1.0", "1.5")]
    [InlineData("1.3", "1.0")]
    public void SameMajor_IsCompatible(string server, string client)
    {
        Assert.Null(VersionHandshake.CheckCompatibility(server, client));
    }

    /// <summary>Different major versions are rejected with a message naming both versions.</summary>
    [Theory]
    [Trait("Category", "Versioning")]
    [InlineData("1.0", "2.0")]
    [InlineData("2.1", "1.9")]
    public void DifferentMajor_IsRejected(string server, string client)
    {
        var result = VersionHandshake.CheckCompatibility(server, client);
        Assert.NotNull(result);
        Assert.StartsWith("VERSION_MISMATCH", result);
        Assert.Contains(server, result);
        Assert.Contains(client, result);
    }

    /// <summary>An empty or unparseable client version is treated as a mismatch, not a pass.</summary>
    [Theory]
    [Trait("Category", "Versioning")]
    [InlineData("")]
    [InlineData("garbage")]
    public void Unparseable_IsRejected(string client)
    {
        Assert.NotNull(VersionHandshake.CheckCompatibility(VersionHandshake.CurrentVersion, client));
    }
}
