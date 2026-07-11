using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MouseKeyProxy.Common;

namespace MouseKeyProxy.Service.Device;

/// <summary>
/// FR-MKP-013: Linux configfs gadget controller for HID keyboard/mouse and multi-LUN mass storage:
/// disk (FS), CD-ROM, and virtual floppy — each independently enableable with media paths.
/// </summary>
/// <remarks>
/// LUN map under <c>functions/mass_storage.0</c>:
/// <list type="bullet">
/// <item><description><c>lun.0</c> — removable disk (FS share)</description></item>
/// <item><description><c>lun.1</c> — CD-ROM (<c>cdrom=1</c>, RO)</description></item>
/// <item><description><c>lun.2</c> — virtual floppy (small removable disk image)</description></item>
/// </list>
/// While the USB host has mass-storage open, configfs LUN attrs and UDC unbind return EBUSY.
/// Apply therefore: (1) no-ops when the live gadget already matches, (2) only writes changed
/// attributes, (3) unbinds with a newline write (zero-byte unbind is invalid on Linux).
/// </remarks>
public sealed class ConfigfsDeviceFunctionController : IDeviceFunctionController
{
    private const string MassStorageFunction = "mass_storage.0";
    private static readonly TimeSpan UdcSettle = TimeSpan.FromMilliseconds(300);

    private readonly string _gadgetRoot;
    private readonly ILogger<ConfigfsDeviceFunctionController> _logger;
    private DeviceFunctionState _state;

    /// <summary>Creates a controller for the given gadget name under configfs.</summary>
    public ConfigfsDeviceFunctionController(
        ILogger<ConfigfsDeviceFunctionController> logger,
        string? gadgetName = null)
    {
        _logger = logger;
        var name = gadgetName
            ?? Environment.GetEnvironmentVariable("MKP_HID_GADGET_NAME")
            ?? "mkp_hid";
        _gadgetRoot = Path.Combine("/sys/kernel/config/usb_gadget", name);
        _state = ProbeState();
    }

    /// <inheritdoc />
    public DeviceFunctionState GetState() => _state = ProbeState();

    /// <inheritdoc />
    public Task<DeviceConfigureResult> ApplyAsync(DeviceFunctionState desired, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Directory.Exists(_gadgetRoot))
        {
            return Task.FromResult(Fail("GADGET_MISSING", $"gadget root not found: {_gadgetRoot}"));
        }

        var udcPath = Path.Combine(_gadgetRoot, "UDC");
        var previousUdc = ReadText(udcPath).Trim();
        var unbound = false;

        try
        {
            if (!DeviceMediaPathResolver.TryResolve(desired.CdromMedia, out var cdromFile, out var cdErr, out var cdMsg))
            {
                return Task.FromResult(Fail(cdErr, cdMsg));
            }

            if (!DeviceMediaPathResolver.TryResolve(desired.FloppyMedia, out var floppyFile, out var flErr, out var flMsg))
            {
                return Task.FromResult(Fail(flErr, flMsg));
            }

            if (desired.CdromEnabled && string.IsNullOrEmpty(cdromFile))
            {
                return Task.FromResult(Fail("CDROM_MEDIA_REQUIRED", "CD-ROM is enabled but no media path is configured."));
            }

            if (desired.FloppyEnabled && string.IsNullOrEmpty(floppyFile))
            {
                return Task.FromResult(Fail("FLOPPY_MEDIA_REQUIRED", "Floppy is enabled but no media path is configured."));
            }

            var anyStorage = desired.FsEnabled || desired.CdromEnabled || desired.FloppyEnabled;
            var massDir = Path.Combine(_gadgetRoot, "functions", MassStorageFunction);
            if (anyStorage && !Directory.Exists(massDir))
            {
                return Task.FromResult(Fail("FS_NOT_PROVISIONED", $"{MassStorageFunction} function is not present on this gadget"));
            }

            var diskFile = Environment.GetEnvironmentVariable("MKP_FS_DISK_IMAGE")
                ?? Path.Combine("/var/lib/mousekeyproxy", "install.img");
            if (desired.FsEnabled)
            {
                EnsureDiskImage(diskFile);
            }

            var diskPath = desired.FsEnabled && File.Exists(diskFile) ? diskFile : null;

            // Fast path: already at desired topology + LUN attrs → success without touching configfs
            // (critical when the host PC has the mass-storage LUN open; writes return EBUSY).
            if (StateMatchesDesired(desired, anyStorage, diskPath, cdromFile, floppyFile))
            {
                _state = ProbeState() with
                {
                    CdromMedia = desired.CdromMedia,
                    FloppyMedia = desired.FloppyMedia,
                    FsAccess = desired.FsAccess,
                    FsEnabled = desired.FsEnabled,
                    CdromEnabled = desired.CdromEnabled,
                    FloppyEnabled = desired.FloppyEnabled,
                    KeyboardEnabled = desired.KeyboardEnabled,
                    MouseEnabled = desired.MouseEnabled,
                };
                _logger.LogInformation(
                    "Gadget already matches desired config (no-op apply) kb={Kb} mouse={Mouse} fs={Fs}/{Access}",
                    _state.KeyboardEnabled, _state.MouseEnabled, _state.FsEnabled, _state.FsAccess);
                return Task.FromResult(new DeviceConfigureResult(
                    true, string.Empty, "ok (already applied)", _state, Array.Empty<DeviceEvent>()));
            }

            var linksMatch =
                IsLinked("hid.keyboard") == desired.KeyboardEnabled &&
                IsLinked("hid.mouse") == desired.MouseEnabled &&
                IsLinked(MassStorageFunction) == anyStorage;

            // Hot path: only LUN attribute deltas, no UDC cycle.
            if (linksMatch && Directory.Exists(massDir))
            {
                try
                {
                    ConfigureLun(0, desired.FsEnabled, diskPath, cdrom: false,
                        readOnly: desired.FsAccess == DeviceFsAccess.ReadOnly, removable: true);
                    ConfigureLun(1, desired.CdromEnabled, cdromFile, cdrom: true, readOnly: true, removable: true);
                    ConfigureLun(2, desired.FloppyEnabled, floppyFile, cdrom: false, readOnly: false, removable: true);

                    _state = FinishState(desired);
                    _logger.LogInformation(
                        "Gadget hot-applied kb={Kb} mouse={Mouse} fs={Fs}/{Access} cdrom={Cd} floppy={Fl}",
                        _state.KeyboardEnabled, _state.MouseEnabled, _state.FsEnabled, _state.FsAccess,
                        _state.CdromEnabled, _state.FloppyEnabled);
                    return Task.FromResult(new DeviceConfigureResult(
                        true, string.Empty, "ok (hot)", _state, Array.Empty<DeviceEvent>()));
                }
                catch (IOException hotEx)
                {
                    _logger.LogWarning(hotEx, "Hot LUN update failed; falling back to UDC unbind/rebind");
                }
            }

            // Full path: unbind → reconfigure → rebind (finally).
            UnbindUdc(udcPath);
            unbound = true;
            Thread.Sleep(UdcSettle);

            ApplyHidLink("hid.keyboard", desired.KeyboardEnabled);
            ApplyHidLink("hid.mouse", desired.MouseEnabled);

            if (Directory.Exists(massDir))
            {
                ConfigureLun(0, desired.FsEnabled, diskPath, cdrom: false,
                    readOnly: desired.FsAccess == DeviceFsAccess.ReadOnly, removable: true);
                ConfigureLun(1, desired.CdromEnabled, cdromFile, cdrom: true, readOnly: true, removable: true);
                ConfigureLun(2, desired.FloppyEnabled, floppyFile, cdrom: false, readOnly: false, removable: true);

                if (!TryApplyFunctionLink(MassStorageFunction, anyStorage, out var linkErr))
                {
                    return Task.FromResult(Fail("GADGET_LINK_FAILED", linkErr));
                }
            }

            _state = FinishState(desired);
            _logger.LogInformation(
                "Gadget applied kb={Kb} mouse={Mouse} fs={Fs}/{Access} cdrom={Cd} floppy={Fl}",
                _state.KeyboardEnabled, _state.MouseEnabled, _state.FsEnabled, _state.FsAccess,
                _state.CdromEnabled, _state.FloppyEnabled);

            return Task.FromResult(new DeviceConfigureResult(
                true, string.Empty, "ok", _state, Array.Empty<DeviceEvent>()));
        }
        catch (Exception ex)
        {
            // If the live gadget already matches desired, treat host-held EBUSY as success.
            try
            {
                var anyStorage = desired.FsEnabled || desired.CdromEnabled || desired.FloppyEnabled;
                var diskFile = Environment.GetEnvironmentVariable("MKP_FS_DISK_IMAGE")
                    ?? Path.Combine("/var/lib/mousekeyproxy", "install.img");
                var diskPath = desired.FsEnabled && File.Exists(diskFile) ? diskFile : null;
                DeviceMediaPathResolver.TryResolve(desired.CdromMedia, out var cdromFile, out _, out _);
                DeviceMediaPathResolver.TryResolve(desired.FloppyMedia, out var floppyFile, out _, out _);
                if (StateMatchesDesired(desired, anyStorage, diskPath, cdromFile, floppyFile))
                {
                    _state = FinishState(desired);
                    _logger.LogWarning(ex,
                        "Apply hit EBUSY but gadget already matches desired; reporting success");
                    return Task.FromResult(new DeviceConfigureResult(
                        true, string.Empty,
                        "ok (already applied; host holds USB media — eject to change)",
                        _state, Array.Empty<DeviceEvent>()));
                }
            }
            catch
            {
                /* fall through */
            }

            _logger.LogError(ex, "Failed to apply gadget function configuration");
            return Task.FromResult(Fail(
                "GADGET_APPLY_FAILED",
                ex.Message + " (If the Pi shows as a disk on the host, eject it and retry.)"));
        }
        finally
        {
            if (unbound || string.IsNullOrWhiteSpace(ReadText(udcPath).Trim()))
            {
                try
                {
                    RebindUdc(udcPath, previousUdc);
                    Thread.Sleep(UdcSettle);
                }
                catch (Exception rebindEx)
                {
                    _logger.LogError(rebindEx, "Failed to rebind UDC after gadget apply");
                }
            }
        }
    }

    private DeviceFunctionState FinishState(DeviceFunctionState desired)
    {
        var probed = ProbeState();
        return probed with
        {
            CdromMedia = desired.CdromMedia,
            FloppyMedia = desired.FloppyMedia,
            // Prefer operator intent for enable flags when probe under-reports (empty file while enabled).
            KeyboardEnabled = desired.KeyboardEnabled,
            MouseEnabled = desired.MouseEnabled,
            FsEnabled = desired.FsEnabled,
            FsAccess = desired.FsAccess,
            CdromEnabled = desired.CdromEnabled,
            FloppyEnabled = desired.FloppyEnabled,
        };
    }

    private bool StateMatchesDesired(
        DeviceFunctionState desired,
        bool anyStorage,
        string? diskPath,
        string? cdromFile,
        string? floppyFile)
    {
        if (IsLinked("hid.keyboard") != desired.KeyboardEnabled)
        {
            return false;
        }

        if (IsLinked("hid.mouse") != desired.MouseEnabled)
        {
            return false;
        }

        if (IsLinked(MassStorageFunction) != anyStorage)
        {
            return false;
        }

        if (!anyStorage)
        {
            return true;
        }

        // Compare LUN attrs we care about (skip-write equality).
        if (!LunAttrMatches(0, "ro", desired.FsAccess == DeviceFsAccess.ReadOnly ? "1" : "0"))
        {
            return false;
        }

        if (!LunAttrMatches(0, "cdrom", "0"))
        {
            return false;
        }

        if (desired.FsEnabled)
        {
            var currentFile = ReadLunFile(0);
            if (!string.IsNullOrEmpty(diskPath) &&
                !string.Equals(currentFile, diskPath, StringComparison.Ordinal))
            {
                // Empty current + desired path still "matches enough" if FS is enabled and linked
                // and we only need media present after rebind — require path match when both set.
                if (!string.IsNullOrEmpty(currentFile))
                {
                    return false;
                }
            }
        }

        if (desired.CdromEnabled)
        {
            if (!LunAttrMatches(1, "cdrom", "1"))
            {
                return false;
            }

            var cf = ReadLunFile(1);
            if (!string.IsNullOrEmpty(cdromFile) &&
                !string.Equals(cf, cdromFile, StringComparison.Ordinal) &&
                !string.IsNullOrEmpty(cf))
            {
                return false;
            }
        }

        if (desired.FloppyEnabled)
        {
            var ff = ReadLunFile(2);
            if (!string.IsNullOrEmpty(floppyFile) &&
                !string.Equals(ff, floppyFile, StringComparison.Ordinal) &&
                !string.IsNullOrEmpty(ff))
            {
                return false;
            }
        }

        return true;
    }

    private bool LunAttrMatches(int lunIndex, string attr, string expected)
    {
        var p = Path.Combine(_gadgetRoot, "functions", MassStorageFunction, $"lun.{lunIndex}", attr);
        if (!File.Exists(p))
        {
            return true; // nothing to conflict
        }

        var v = ReadText(p).Trim();
        return string.Equals(v, expected, StringComparison.Ordinal);
    }

    private void EnsureDiskImage(string diskFile)
    {
        try
        {
            if (File.Exists(diskFile))
            {
                return;
            }

            var dir = Path.GetDirectoryName(diskFile);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            const long size = 64L * 1024 * 1024;
            using var fs = new FileStream(diskFile, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            fs.SetLength(size);
            _logger.LogInformation("Created disk image {Path} ({Size} bytes)", diskFile, size);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not create disk image {Path}", diskFile);
        }
    }

    private void ConfigureLun(int lunIndex, bool enabled, string? file, bool cdrom, bool readOnly, bool removable)
    {
        var lunDir = Path.Combine(_gadgetRoot, "functions", MassStorageFunction, $"lun.{lunIndex}");
        if (!Directory.Exists(lunDir))
        {
            try
            {
                Directory.CreateDirectory(lunDir);
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "LUN lun.{Index} not available", lunIndex);
                return;
            }
        }

        // Only write attributes that differ. Rewriting `ro` while the host holds the LUN is EBUSY
        // even when the value is already correct (observed on dwc2 + mass_storage).
        WriteSysfsIfChanged(Path.Combine(lunDir, "cdrom"), cdrom ? "1" : "0");
        WriteSysfsIfChanged(Path.Combine(lunDir, "removable"), removable ? "1" : "0");

        var wantRo = readOnly || cdrom ? "1" : "0";
        WriteSysfsIfChanged(Path.Combine(lunDir, "ro"), wantRo);

        var wantFile = enabled && !string.IsNullOrEmpty(file) ? file : string.Empty;
        WriteSysfsIfChanged(Path.Combine(lunDir, "file"), wantFile);
    }

    private void WriteSysfsIfChanged(string path, string value)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var current = ReadText(path).TrimEnd('\n', '\r');
        var desired = value ?? string.Empty;
        if (string.Equals(current, desired, StringComparison.Ordinal))
        {
            return;
        }

        WriteSysfs(path, desired);
    }

    private bool TryApplyFunctionLink(string functionName, bool enabled, out string error)
    {
        error = string.Empty;
        var functionDir = Path.Combine(_gadgetRoot, "functions", functionName);
        var linkPath = Path.Combine(_gadgetRoot, "configs", "c.1", functionName);
        if (!Directory.Exists(functionDir))
        {
            error = $"function directory missing: {functionDir}";
            return false;
        }

        var linked = IsLinked(functionName);
        if (enabled && !linked)
        {
            try
            {
                Directory.CreateSymbolicLink(linkPath, functionDir);
                return true;
            }
            catch (IOException absEx)
            {
                _logger.LogWarning(absEx, "Absolute symlink failed for {Function}; trying relative", functionName);
                try
                {
                    Directory.CreateSymbolicLink(linkPath, Path.Combine("..", "..", "functions", functionName));
                    return true;
                }
                catch (IOException relEx)
                {
                    error = $"link {functionName}: {relEx.Message}";
                    return false;
                }
            }
        }

        if (!enabled && linked)
        {
            try
            {
                if (IsSymlink(linkPath) || File.Exists(linkPath))
                {
                    File.Delete(linkPath);
                }
                else if (Directory.Exists(linkPath))
                {
                    Directory.Delete(linkPath);
                }

                return true;
            }
            catch (IOException ex)
            {
                error = $"unlink {functionName}: {ex.Message}";
                return false;
            }
        }

        return true;
    }

    private void ApplyHidLink(string functionName, bool enabled)
    {
        if (!TryApplyFunctionLink(functionName, enabled, out var err) && !string.IsNullOrEmpty(err))
        {
            _logger.LogWarning("HID link {Function}: {Error}", functionName, err);
        }
    }

    private void UnbindUdc(string udcPath)
    {
        // Zero-byte write fails (EFAULT). Newline matches `echo > UDC`.
        for (var attempt = 1; attempt <= 8; attempt++)
        {
            var current = ReadText(udcPath).Trim();
            if (current.Length == 0)
            {
                return;
            }

            if (attempt >= 2)
            {
                ForceEjectAllLuns();
            }

            try
            {
                // Prefer shell: most reliable on configfs.
                if (TryShellWrite(udcPath, "\n") || TryShellWrite(udcPath, string.Empty))
                {
                    Thread.Sleep(UdcSettle);
                    if (ReadText(udcPath).Trim().Length == 0)
                    {
                        return;
                    }
                }

                WriteSysfs(udcPath, "\n");
                Thread.Sleep(UdcSettle);
                if (ReadText(udcPath).Trim().Length == 0)
                {
                    return;
                }
            }
            catch (IOException ex) when (attempt < 8)
            {
                _logger.LogWarning(ex, "UDC unbind attempt {Attempt} failed", attempt);
                Thread.Sleep(TimeSpan.FromMilliseconds(150 * attempt));
            }
        }

        var still = ReadText(udcPath).Trim();
        if (still.Length > 0)
        {
            throw new IOException(
                $"Device or resource busy: could not unbind UDC '{still}'. " +
                "Eject the Pi USB disk on the host PC and retry Apply.");
        }
    }

    private void ForceEjectAllLuns()
    {
        for (var i = 0; i <= 2; i++)
        {
            var fe = Path.Combine(_gadgetRoot, "functions", MassStorageFunction, $"lun.{i}", "forced_eject");
            try
            {
                if (File.Exists(fe))
                {
                    WriteSysfs(fe, "1");
                }
            }
            catch (IOException ex)
            {
                _logger.LogDebug(ex, "forced_eject lun.{Index} failed", i);
            }
        }

        Thread.Sleep(UdcSettle);
    }

    private void RebindUdc(string udcPath, string previousUdc)
    {
        var target = previousUdc;
        if (string.IsNullOrWhiteSpace(target))
        {
            var udcDir = "/sys/class/udc";
            if (Directory.Exists(udcDir))
            {
                foreach (var d in Directory.EnumerateDirectories(udcDir))
                {
                    target = Path.GetFileName(d);
                    break;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(target))
        {
            _logger.LogWarning("No UDC available to rebind");
            return;
        }

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            var current = ReadText(udcPath).Trim();
            if (string.Equals(current, target, StringComparison.Ordinal))
            {
                return;
            }

            if (current.Length > 0)
            {
                try
                {
                    WriteSysfs(udcPath, "\n");
                    Thread.Sleep(UdcSettle);
                }
                catch (IOException)
                {
                    /* retry */
                }
            }

            try
            {
                if (!TryShellWrite(udcPath, target))
                {
                    WriteSysfs(udcPath, target);
                }

                Thread.Sleep(UdcSettle);
                if (string.Equals(ReadText(udcPath).Trim(), target, StringComparison.Ordinal))
                {
                    return;
                }
            }
            catch (IOException ex) when (attempt < 5)
            {
                _logger.LogWarning(ex, "UDC rebind attempt {Attempt} to {Udc} failed", attempt, target);
                Thread.Sleep(TimeSpan.FromMilliseconds(100 * attempt));
            }
        }

        throw new IOException($"Device or resource busy: could not bind UDC '{target}'");
    }

    private static bool TryShellWrite(string path, string value)
    {
        try
        {
            // printf avoids echo quirks; empty value → unbind UDC.
            var psi = new ProcessStartInfo
            {
                FileName = "/bin/sh",
                ArgumentList = { "-c", "printf %s \"$1\" > \"$2\"", "mkp-sysfs", value ?? string.Empty, path },
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            if (p is null)
            {
                return false;
            }

            if (!p.WaitForExit(3000))
            {
                try { p.Kill(entireProcessTree: true); } catch { /* ignore */ }
                return false;
            }

            return p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private DeviceFunctionState ProbeState()
    {
        if (!Directory.Exists(_gadgetRoot))
        {
            return new DeviceFunctionState(false, false, false, DeviceFsAccess.ReadOnly);
        }

        var kb = IsLinked("hid.keyboard");
        var mouse = IsLinked("hid.mouse");
        var massLinked = IsLinked(MassStorageFunction);

        var diskFile = ReadLunFile(0);
        var cdFile = ReadLunFile(1);
        var flFile = ReadLunFile(2);
        var access = ReadLunRo(0) ? DeviceFsAccess.ReadOnly : DeviceFsAccess.ReadWrite;

        return new DeviceFunctionState(
            KeyboardEnabled: kb,
            MouseEnabled: mouse,
            // Linked mass_storage means storage is enabled; media may be empty (ejected).
            FsEnabled: massLinked,
            FsAccess: access,
            CdromEnabled: massLinked && !string.IsNullOrEmpty(cdFile),
            CdromMedia: string.IsNullOrEmpty(cdFile) ? null : new StorageMediaSpec(DeviceMediaSource.Device, cdFile),
            FloppyEnabled: massLinked && !string.IsNullOrEmpty(flFile),
            FloppyMedia: string.IsNullOrEmpty(flFile) ? null : new StorageMediaSpec(DeviceMediaSource.Device, flFile));
    }

    private string ReadLunFile(int lunIndex)
    {
        var p = Path.Combine(_gadgetRoot, "functions", MassStorageFunction, $"lun.{lunIndex}", "file");
        return ReadText(p).Trim();
    }

    private bool ReadLunRo(int lunIndex)
    {
        var p = Path.Combine(_gadgetRoot, "functions", MassStorageFunction, $"lun.{lunIndex}", "ro");
        var v = ReadText(p).Trim();
        return v is not ("0" or "n" or "N" or "");
    }

    private bool IsLinked(string functionName)
    {
        var linkPath = Path.Combine(_gadgetRoot, "configs", "c.1", functionName);
        return Directory.Exists(linkPath) || File.Exists(linkPath) || IsSymlink(linkPath);
    }

    private static bool IsSymlink(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists && info.Attributes.HasFlag(FileAttributes.ReparsePoint);
        }
        catch
        {
            return false;
        }
    }

    private static string ReadText(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static void WriteSysfs(string path, string value)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var text = value ?? string.Empty;
        if (text.Length == 0 && path.EndsWith("UDC", StringComparison.Ordinal))
        {
            text = "\n";
        }

        var bytes = Encoding.ASCII.GetBytes(text);
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
        fs.Write(bytes, 0, bytes.Length);
        fs.Flush(true);
    }

    private DeviceConfigureResult Fail(string code, string message)
        => new(false, code, message, _state, Array.Empty<DeviceEvent>());
}
