using System.IO;
using System.Text.Json;
using MouseKeyProxy.Common;
using Xunit;

namespace MouseKeyProxy.Common.Tests;

/// <summary>
/// TEST-MKP-044 / FR-MKP-022 / TR-MKP-CFG-001: LiteDB appliance config store round-trip,
/// seed import once when empty, and fail-closed behavior on invalid paths.
/// </summary>
public class LiteDbApplianceConfigStoreTests
{
    /// <summary>Write then reopen returns equal function defaults and media paths.</summary>
    [Fact]
    public void SaveAndReload_RoundTripsConfig()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mkp-cfg-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var dbPath = Path.Combine(dir, "config.db");
        try
        {
            using (var store = new LiteDbApplianceConfigStore(dbPath))
            {
                var cfg = new ApplianceConfig
                {
                    KeyboardEnabled = true,
                    MouseEnabled = false,
                    FsEnabled = true,
                    FsAccess = DeviceFsAccess.ReadWrite,
                    CdromEnabled = true,
                    CdromMediaSource = DeviceMediaSource.Device,
                    CdromMediaPath = "media/device/install.iso",
                    FloppyEnabled = false,
                    FolderShareEnabled = true,
                    SmbEnabled = true,
                    ShareName = "MouseKeyProxy",
                    DeployMountPath = "/mnt/mkp-deploy",
                };
                store.Save(cfg);
            }

            using var reopened = new LiteDbApplianceConfigStore(dbPath);
            var loaded = reopened.Get();
            Assert.True(loaded.KeyboardEnabled);
            Assert.False(loaded.MouseEnabled);
            Assert.True(loaded.FsEnabled);
            Assert.Equal(DeviceFsAccess.ReadWrite, loaded.FsAccess);
            Assert.True(loaded.CdromEnabled);
            Assert.Equal(DeviceMediaSource.Device, loaded.CdromMediaSource);
            Assert.Equal("media/device/install.iso", loaded.CdromMediaPath);
            Assert.False(loaded.FloppyEnabled);
            Assert.True(loaded.FolderShareEnabled);
            Assert.True(loaded.SmbEnabled);
            Assert.Equal("MouseKeyProxy", loaded.ShareName);
            Assert.Equal("/mnt/mkp-deploy", loaded.DeployMountPath);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* best effort */ }
        }
    }

    /// <summary>Seed JSON imports only when the store has no document yet.</summary>
    [Fact]
    public void ImportSeed_OnlyWhenEmpty_DoesNotClobber()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mkp-cfg-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var dbPath = Path.Combine(dir, "config.db");
        var seedPath = Path.Combine(dir, "seed.json");
        try
        {
            var seed = new ApplianceConfig
            {
                KeyboardEnabled = true,
                MouseEnabled = true,
                FsEnabled = true,
                CdromEnabled = true,
                ShareName = "from-seed",
            };
            File.WriteAllText(seedPath, JsonSerializer.Serialize(seed));

            using (var store = new LiteDbApplianceConfigStore(dbPath))
            {
                Assert.True(store.TryImportSeed(seedPath));
                Assert.Equal("from-seed", store.Get().ShareName);

                // Second import must not overwrite operator changes.
                store.Save(store.Get() with { ShareName = "operator" });
                Assert.False(store.TryImportSeed(seedPath));
                Assert.Equal("operator", store.Get().ShareName);
            }
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* best effort */ }
        }
    }

    /// <summary>Empty database path is rejected (fail closed).</summary>
    [Fact]
    public void Constructor_RejectsEmptyPath()
    {
        Assert.Throws<ArgumentException>(() => new LiteDbApplianceConfigStore(""));
        Assert.Throws<ArgumentException>(() => new LiteDbApplianceConfigStore("   "));
    }
}
