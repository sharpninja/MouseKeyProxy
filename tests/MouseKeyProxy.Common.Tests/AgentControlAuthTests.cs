using System.IO;
using MouseKeyProxy.Common;
using Xunit;

namespace MouseKeyProxy.Common.Tests;

/// <summary>
/// TR-MKP-SEC-001: verifies the local agent-control IPC auth token - generation, constant-time
/// validation, and user-scoped file round-trip that lets the service present the token the agent minted.
/// </summary>
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

    /// <summary>The token store writes and reads back the same token from a user-scoped file.</summary>
    [Fact]
    [Trait("Category", "Security")]
    public void TokenStore_RoundTrips()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mkp-token-{System.Guid.NewGuid():N}.tok");
        try
        {
            var token = AgentControlAuth.GenerateToken();
            AgentControlTokenStore.Write(path, token);
            Assert.Equal(token, AgentControlTokenStore.Read(path));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    /// <summary>Reading a missing token file returns null rather than throwing.</summary>
    [Fact]
    [Trait("Category", "Security")]
    public void TokenStore_MissingFile_ReturnsNull()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mkp-missing-{System.Guid.NewGuid():N}.tok");
        Assert.Null(AgentControlTokenStore.Read(path));
    }
}
