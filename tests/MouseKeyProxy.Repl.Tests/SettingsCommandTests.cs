using System;
using System.IO;
using MouseKeyProxy.Common;
using MouseKeyProxy.Repl;
using Xunit;

namespace MouseKeyProxy.Repl.Tests;

/// <summary>
/// FR-MKP-006: verifies the REPL `settings` verb - show (defaults + json), set (valid + invalid keys),
/// and clear - against an isolated settings file.
/// </summary>
public class SettingsCommandTests
{
    private static string TempPath() => Path.Combine(Path.GetTempPath(), $"mkp-settings-{Guid.NewGuid():N}.json");

    /// <summary>`settings show` on a fresh store prints defaults and exits 0.</summary>
    [Fact]
    [Trait("Category", "Settings")]
    public void Show_Defaults_ExitsZero()
    {
        var path = TempPath();
        try
        {
            var code = SettingsCommand.Run(new[] { "settings", "show" }, path, out var output);
            Assert.Equal(0, code);
            Assert.Contains("logLevel", output);
            Assert.Contains("Information", output);
        }
        finally { SettingsStore.Clear(path); }
    }

    /// <summary>`settings set` persists a value that a subsequent `show` reflects.</summary>
    [Fact]
    [Trait("Category", "Settings")]
    public void Set_Then_Show_ReflectsValue()
    {
        var path = TempPath();
        try
        {
            var set = SettingsCommand.Run(new[] { "settings", "set", "remoteGrpcUrl", "https://host:50051" }, path, out _);
            Assert.Equal(0, set);

            Assert.Equal("https://host:50051", SettingsStore.Load(path).RemoteGrpcUrl);

            SettingsCommand.Run(new[] { "settings", "show" }, path, out var shown);
            Assert.Contains("https://host:50051", shown);
        }
        finally { SettingsStore.Clear(path); }
    }

    /// <summary>An unknown key is rejected with a non-zero exit and no file written.</summary>
    [Fact]
    [Trait("Category", "Settings")]
    public void Set_UnknownKey_Fails()
    {
        var path = TempPath();
        try
        {
            var code = SettingsCommand.Run(new[] { "settings", "set", "bogus", "x" }, path, out var output);
            Assert.NotEqual(0, code);
            Assert.Contains("unknown setting key", output);
        }
        finally { SettingsStore.Clear(path); }
    }

    /// <summary>A non-integer retention value is rejected.</summary>
    [Fact]
    [Trait("Category", "Settings")]
    public void Set_InvalidInteger_Fails()
    {
        var path = TempPath();
        try
        {
            var code = SettingsCommand.Run(new[] { "settings", "set", "clipboardRetentionDays", "notanint" }, path, out _);
            Assert.NotEqual(0, code);
        }
        finally { SettingsStore.Clear(path); }
    }

    /// <summary>`settings clear` removes the persisted file.</summary>
    [Fact]
    [Trait("Category", "Settings")]
    public void Clear_RemovesFile()
    {
        var path = TempPath();
        SettingsStore.Save(path, new AppSettings { RemotePeer = "p" });
        Assert.True(File.Exists(path));

        var code = SettingsCommand.Run(new[] { "settings", "clear" }, path, out _);

        Assert.Equal(0, code);
        Assert.False(File.Exists(path));
    }
}
