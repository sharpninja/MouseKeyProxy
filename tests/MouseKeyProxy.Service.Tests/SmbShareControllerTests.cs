using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using MouseKeyProxy.Service.Device;
using Xunit;

namespace MouseKeyProxy.Service.Tests;

/// <summary>
/// TEST-MKP-038: Samba config writer only allows listed peer IPs (no world/guest).
/// </summary>
public class SmbShareControllerTests
{
    /// <summary>Allowlist IPs appear in hosts allow; deny ALL and no guest map.</summary>
    [Fact]
    public async Task ApplyAllowlist_WritesHostsAllowOnly()
    {
        var path = Path.Combine(Path.GetTempPath(), "mkp-smb-" + System.Guid.NewGuid().ToString("N") + ".conf");
        try
        {
            var ctl = new SmbShareController(
                NullLogger<SmbShareController>.Instance,
                configPath: path,
                sharePath: "/mnt/mkp-deploy/share",
                shareName: "MouseKeyProxy");

            await ctl.ApplyAllowlistAsync(new[] { "192.168.1.10", "192.168.1.20" }, TestContext.Current.CancellationToken);

            var text = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
            Assert.Contains("hosts allow = 127.0.0.1 192.168.1.10 192.168.1.20", text);
            Assert.Contains("hosts deny = ALL", text);
            Assert.Contains("map to guest = never", text);
            Assert.Contains("guest ok = no", text);
            Assert.Contains("path = /mnt/mkp-deploy/share", text);
            Assert.DoesNotContain("hosts allow = ALL", text);
        }
        finally
        {
            try { File.Delete(path); } catch { /* best effort */ }
        }
    }

    /// <summary>Empty allowlist still denies the world (loopback only).</summary>
    [Fact]
    public async Task ApplyAllowlist_Empty_OnlyLoopback()
    {
        var path = Path.Combine(Path.GetTempPath(), "mkp-smb-" + System.Guid.NewGuid().ToString("N") + ".conf");
        try
        {
            var ctl = new SmbShareController(
                NullLogger<SmbShareController>.Instance,
                configPath: path);
            await ctl.ApplyAllowlistAsync(System.Array.Empty<string>(), TestContext.Current.CancellationToken);
            var text = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
            Assert.Contains("hosts allow = 127.0.0.1", text);
            Assert.Contains("hosts deny = ALL", text);
        }
        finally
        {
            try { File.Delete(path); } catch { /* best effort */ }
        }
    }
}
