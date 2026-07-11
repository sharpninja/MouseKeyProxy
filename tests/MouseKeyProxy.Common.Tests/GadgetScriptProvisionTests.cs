using System.IO;
using Xunit;

namespace MouseKeyProxy.Common.Tests;

/// <summary>
/// TEST-MKP-030 / TEST-MKP-040 / TEST-MKP-047: static checks that the Pi gadget
/// provision script creates single-LUN mass_storage (folder-backed thumb image)
/// and binary HID descriptors.
/// </summary>
public class GadgetScriptProvisionTests
{
    private static string ScriptPath
    {
        get
        {
            // tests run from bin/Debug/net10.0 → walk up to repo root
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null)
            {
                var candidate = Path.Combine(dir.FullName, "scripts", "pi", "setup-configfs-gadget.sh");
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                dir = dir.Parent;
            }

            throw new FileNotFoundException("Could not locate scripts/pi/setup-configfs-gadget.sh from " + AppContext.BaseDirectory);
        }
    }

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

    /// <summary>Deploy layout doc describes FAT32 MKP-DEPLOY folders.</summary>
    [Fact]
    public void DeployLayoutDoc_DocumentsFat32Tree()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        string? path = null;
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "scripts", "pi", "mkp-deploy-layout.md");
            if (File.Exists(candidate))
            {
                path = candidate;
                break;
            }

            dir = dir.Parent;
        }

        Assert.NotNull(path);
        var text = File.ReadAllText(path!);
        Assert.Contains("MKP-DEPLOY", text, StringComparison.Ordinal);
        Assert.Contains("FAT32", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("media/", text, StringComparison.Ordinal);
        Assert.Contains("share/", text, StringComparison.Ordinal);
        Assert.Contains("install/", text, StringComparison.Ordinal);
        Assert.Contains("/etc/mkp", text, StringComparison.Ordinal);
    }
}
