using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace MouseKeyProxy.Common.Tests;

/// <summary>
/// TEST-MKP-030 / TEST-MKP-040 / TEST-MKP-047: static checks that the Pi gadget
/// provision script creates single-LUN mass_storage (folder-backed thumb image)
/// and binary HID descriptors. Also locks firstboot/gadget boot-order and UDC
/// fallback contracts proven on Orange Pi Zero 2W (musb).
/// </summary>
public class GadgetScriptProvisionTests
{
    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "scripts", "pi", "setup-configfs-gadget.sh")))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            throw new FileNotFoundException("Could not locate repo root (scripts/pi/setup-configfs-gadget.sh) from " + AppContext.BaseDirectory);
        }
    }

    private static string ScriptPath => Path.Combine(RepoRoot, "scripts", "pi", "setup-configfs-gadget.sh");

    private static string FirstbootScriptPath => Path.Combine(RepoRoot, "scripts", "pi", "firstboot-linux-appliance.sh");

    private static string FirstbootUnitPath => Path.Combine(RepoRoot, "assets", "systemd", "mkp-firstboot.service");

    /// <summary>Script embeds base64 HID descriptors and verifies lengths (binary path).</summary>
    [Fact]
    public void Script_HasBinaryHidDescriptorsAndSelfPowered()
    {
        var text = File.ReadAllText(ScriptPath);
        Assert.Contains("base64 -d", text, StringComparison.Ordinal);
        Assert.Contains("KEYBOARD_DESC_LEN=63", text, StringComparison.Ordinal);
        Assert.Contains("MOUSE_DESC_LEN=52", text, StringComparison.Ordinal);
        Assert.Contains("0xC0", text, StringComparison.Ordinal);
        Assert.DoesNotContain("printf", text.Split('\n').FirstOrDefault(l => l.Contains("report_desc") && l.Contains("\\x")) ?? string.Empty);
    }

    /// <summary>Script provisions one mass_storage LUN backed by a VFAT image of a folder.</summary>
    [Fact]
    public void Script_ProvisionsSingleFolderBackedThumbLun()
    {
        var text = File.ReadAllText(ScriptPath);
        Assert.Contains("mass_storage.0", text, StringComparison.Ordinal);
        Assert.Contains("lun.0", text, StringComparison.Ordinal);
        Assert.Contains("MKP_THUMB_FOLDER", text, StringComparison.Ordinal);
        Assert.Contains("MKP_FS_DISK_IMAGE", text, StringComparison.Ordinal);
        Assert.Contains("prepare_thumb_image", text, StringComparison.Ordinal);
        Assert.Contains("mkfs.vfat", text, StringComparison.Ordinal);
        Assert.Contains("/mnt/mkp-deploy/share", text, StringComparison.Ordinal);
        // Must not create empty multi-LUN placeholders (Windows "No Media" drives).
        Assert.DoesNotContain("mkdir -p \"${MS}/lun.1\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("mkdir -p \"${MS}/lun.2\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("MKP_CDROM_IMAGE", text, StringComparison.Ordinal);
        Assert.DoesNotContain("MKP_FLOPPY_IMAGE", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// UDC bind must progressively drop RNDIS then remaining USB net so HID+storage
    /// still comes up when musb rejects composite RNDIS (err -19 / EBUSY).
    /// </summary>
    [Fact]
    public void Script_FallsBackFromRndisBindFailureToHidStorage()
    {
        var text = File.ReadAllText(ScriptPath);

        // Must not treat a failed bare UDC write as fatal under set -e (must be wrapped).
        var bareUdcWrite = new Regex(
            @"^\s*echo\s+""\$\{UDC_NAME\}""\s+>\s+""\$\{GADGET_ROOT\}/UDC""\s*$",
            RegexOptions.Multiline);
        Assert.DoesNotMatch(bareUdcWrite, text);

        Assert.Contains("falling back", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("rndis", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("HID+storage", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("strip_rndis", text, StringComparison.Ordinal);
        Assert.Contains("strip_ecm", text, StringComparison.Ordinal);
        Assert.Contains("bind_gadget_to_udc", text, StringComparison.Ordinal);
        // Final failure path for HID+storage only.
        Assert.Contains("exit 5", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Firstboot unit may order Before= gadget/service, but the script must never
    /// block on systemctl start/restart of those units (systemd job deadlock).
    /// </summary>
    [Fact]
    public void Firstboot_DoesNotBlockOnGadgetOrServiceStart()
    {
        Assert.True(File.Exists(FirstbootScriptPath), "firstboot-linux-appliance.sh missing");
        Assert.True(File.Exists(FirstbootUnitPath), "mkp-firstboot.service missing");

        var script = File.ReadAllText(FirstbootScriptPath);
        var unit = File.ReadAllText(FirstbootUnitPath);

        Assert.Contains("Before=mkp-hid-gadget.service mousekeyproxy.service", unit, StringComparison.Ordinal);
        Assert.Contains("systemctl enable mkp-hid-gadget.service", script, StringComparison.Ordinal);
        Assert.Contains("systemctl enable mousekeyproxy.service", script, StringComparison.Ordinal);

        // Blocking start/restart deadlocks when unit has Before= those services.
        Assert.DoesNotContain("systemctl start mkp-hid-gadget.service", script, StringComparison.Ordinal);
        Assert.DoesNotContain("systemctl restart mousekeyproxy.service", script, StringComparison.Ordinal);
        Assert.DoesNotContain("systemctl start mousekeyproxy.service", script, StringComparison.Ordinal);

        // Optional non-blocking kick is allowed after enable.
        if (script.Contains("mkp-hid-gadget.service", StringComparison.Ordinal)
            && script.Contains("systemctl start", StringComparison.Ordinal))
        {
            Assert.Contains("systemctl start --no-block mkp-hid-gadget.service", script, StringComparison.Ordinal);
        }
    }

    /// <summary>Deploy layout doc describes FAT32 MKP-DEPLOY folders.</summary>
    [Fact]
    public void DeployLayoutDoc_DocumentsFat32Tree()
    {
        var path = Path.Combine(RepoRoot, "scripts", "pi", "mkp-deploy-layout.md");
        Assert.True(File.Exists(path), path);
        var text = File.ReadAllText(path);
        Assert.Contains("MKP-DEPLOY", text, StringComparison.Ordinal);
        Assert.Contains("FAT32", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("media/", text, StringComparison.Ordinal);
        Assert.Contains("share/", text, StringComparison.Ordinal);
        Assert.Contains("install/", text, StringComparison.Ordinal);
        Assert.Contains("/etc/mkp", text, StringComparison.Ordinal);
    }
}
