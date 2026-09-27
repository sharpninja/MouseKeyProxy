using System.IO;
using MouseKeyProxy.Common;
using Xunit;

namespace MouseKeyProxy.Common.Tests;

/// <summary>
/// TR-MKP-SEC-001: verifies the local agent-control IPC auth token - generation, constant-time
/// validation, and user-scoped file round-trip that lets the service present the token the agent minted.
/// </summary>
[Collection(nameof(AgentControlTokenMirrorCollection))]
public class AgentControlAuthTests
{
    /// <summary>A generated token is non-empty and validates against itself.</summary>
    [Fact]
    [Trait("Category", "Security")]
    public void GeneratedToken_ValidatesAgainstItself()
    {
        var token = AgentControlAuth.GenerateToken();
        Assert.False(string.IsNullOrWhiteSpace(token));
        Assert.True(AgentControlAuth.Validate(token, token));
    }

    /// <summary>A mismatched, null, or empty presented token is rejected.</summary>
    [Theory]
    [Trait("Category", "Security")]
    [InlineData("other-token")]
    [InlineData("")]
    [InlineData(null)]
    public void WrongToken_IsRejected(string? presented)
    {
        var expected = AgentControlAuth.GenerateToken();
        Assert.False(AgentControlAuth.Validate(expected, presented));
    }

    /// <summary>Validation with no configured expected token rejects everything.</summary>
    [Fact]
    [Trait("Category", "Security")]
    public void NoExpectedToken_RejectsAll()
    {
        Assert.False(AgentControlAuth.Validate(null, "anything"));
        Assert.False(AgentControlAuth.Validate("", "anything"));
    }

    /// <summary>A custom token path round-trips without changing the machine mirror.</summary>
    [Fact]
    [Trait("Category", "Security")]
    public void TokenStore_RoundTrips()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mkp-token-{System.Guid.NewGuid():N}.tok");
        var machinePath = AgentControlTokenStore.MachinePath();
        var mirrorBefore = File.Exists(machinePath) ? File.ReadAllBytes(machinePath) : null;
        try
        {
            var token = AgentControlAuth.GenerateToken();
            AgentControlTokenStore.Write(path, token);
            Assert.True(AgentControlAuth.Validate(token, AgentControlTokenStore.Read(path)),
                "The custom token must round-trip.");
            var mirrorUnchanged = mirrorBefore is null
                ? !File.Exists(machinePath)
                : File.Exists(machinePath) && mirrorBefore.AsSpan().SequenceEqual(File.ReadAllBytes(machinePath));
            Assert.True(mirrorUnchanged, "Writing a custom token path must preserve the machine mirror.");
        }
        finally
        {
            if (mirrorBefore is not null)
            {
                File.WriteAllBytes(machinePath, mirrorBefore);
            }
            else if (File.Exists(machinePath))
            {
                File.Delete(machinePath);
            }

            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    /// <summary>Normalized default writes mirror the same token and missing default reads retain machine fallback.</summary>
    [Fact]
    [Trait("Category", "Security")]
    public void TokenStore_DefaultPath_PreservesMachineMirrorAndFallback()
    {
        var path = AgentControlTokenStore.DefaultPath();
        var normalizedEquivalent = Path.Combine(Path.GetDirectoryName(path)!, ".", Path.GetFileName(path));
        var machinePath = AgentControlTokenStore.MachinePath();
        var backupPath = path + $".test-backup-{System.Guid.NewGuid():N}";
        var userBefore = File.Exists(path) ? File.ReadAllBytes(path) : null;
        var mirrorBefore = File.Exists(machinePath) ? File.ReadAllBytes(machinePath) : null;
        try
        {
            var token = AgentControlTokenStore.Read(path) ?? AgentControlAuth.GenerateToken();
            AgentControlTokenStore.Write(normalizedEquivalent, token);
            Assert.True(AgentControlAuth.Validate(token, AgentControlTokenStore.Read(machinePath)),
                "Default writes must keep the machine mirror usable.");
            File.Move(path, backupPath);
            Assert.True(AgentControlAuth.Validate(token, AgentControlTokenStore.Read(path)),
                "A missing default token file must fall back to the machine mirror.");
        }
        finally
        {
            try
            {
                if (File.Exists(backupPath))
                {
                    File.Move(backupPath, path, overwrite: true);
                }

                RestoreTokenSnapshot(path, userBefore);
            }
            finally
            {
                RestoreTokenSnapshot(machinePath, mirrorBefore);
            }
        }
    }

    /// <summary>Restores the exact bytes or prior absence of a token file without exposing its contents.</summary>
    /// <param name="path">The token path owned by the test snapshot.</param>
    /// <param name="snapshot">Prior bytes, or null when the file was absent.</param>
    private static void RestoreTokenSnapshot(string path, byte[]? snapshot)
    {
        if (snapshot is not null)
        {
            File.WriteAllBytes(path, snapshot);
        }
        else if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    /// <summary>Reading a missing custom token file returns null without consulting the machine mirror.</summary>
    [Fact]
    [Trait("Category", "Security")]
    public void TokenStore_MissingFile_ReturnsNull()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mkp-missing-{System.Guid.NewGuid():N}.tok");
        Assert.True(AgentControlTokenStore.Read(path) is null, "A missing custom token path must return null.");
    }
}

/// <summary>Serializes token-store checks that snapshot and restore the shared machine mirror.</summary>
[CollectionDefinition(nameof(AgentControlTokenMirrorCollection), DisableParallelization = true)]
public sealed class AgentControlTokenMirrorCollection
{
}
