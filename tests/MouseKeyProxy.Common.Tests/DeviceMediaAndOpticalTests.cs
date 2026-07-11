using System.IO;
using System.Threading.Tasks;
using MouseKeyProxy.Common;
using Xunit;

namespace MouseKeyProxy.Common.Tests;

/// <summary>
/// FR-MKP-013: CD-ROM / virtual floppy independent enablement and device vs host media paths.
/// </summary>
public class DeviceMediaAndOpticalTests
{
    /// <summary>Enabling CD-ROM with device media emits CdromConnected.</summary>
    [Fact]
    public async Task Configure_EnableCdrom_EmitsCdromConnected()
    {
        var bus = new DeviceEventBus();
        var initial = new DeviceFunctionState(true, true, false, DeviceFsAccess.ReadOnly);
        var controller = new InMemoryDeviceFunctionController(initial);
        var coord = new DeviceFunctionCoordinator(controller, bus, initial);

        var media = new StorageMediaSpec(DeviceMediaSource.Device, "/var/lib/mousekeyproxy/media/device/install.iso");
        var result = await coord.ConfigureAsync(
            new DeviceFunctionConfig(CdromEnabled: true, CdromMedia: media),
            TestContext.Current.CancellationToken);

        Assert.True(result.Ok);
        Assert.True(result.State.CdromEnabled);
        Assert.Equal(media.Path, result.State.CdromMedia?.Path);
        Assert.Contains(result.EmittedEvents, e => e.Kind == DeviceEventKind.CdromConnected);
    }

    /// <summary>Enabling floppy independently of CD-ROM emits only FloppyConnected.</summary>
    [Fact]
    public async Task Configure_EnableFloppyOnly_DoesNotTouchCdrom()
    {
        var bus = new DeviceEventBus();
        var initial = new DeviceFunctionState(true, true, false, DeviceFsAccess.ReadOnly);
        var controller = new InMemoryDeviceFunctionController(initial);
        var coord = new DeviceFunctionCoordinator(controller, bus, initial);

        var media = new StorageMediaSpec(DeviceMediaSource.Host, "boot.img");
        var result = await coord.ConfigureAsync(
            new DeviceFunctionConfig(FloppyEnabled: true, FloppyMedia: media),
            TestContext.Current.CancellationToken);

        Assert.True(result.Ok);
        Assert.True(result.State.FloppyEnabled);
        Assert.False(result.State.CdromEnabled);
        Assert.Equal(DeviceMediaSource.Host, result.State.FloppyMedia?.Source);
        Assert.Contains(result.EmittedEvents, e => e.Kind == DeviceEventKind.FloppyConnected);
        Assert.DoesNotContain(result.EmittedEvents, e => e.Kind == DeviceEventKind.CdromConnected);
    }

    /// <summary>Changing CD media while connected emits CdromMediaChanged.</summary>
    [Fact]
    public async Task Configure_ChangeCdromMedia_EmitsMediaChanged()
    {
        var bus = new DeviceEventBus();
        var media1 = new StorageMediaSpec(DeviceMediaSource.Device, "a.iso");
        var initial = new DeviceFunctionState(
            true, true, false, DeviceFsAccess.ReadOnly,
            CdromEnabled: true, CdromMedia: media1);
        var controller = new InMemoryDeviceFunctionController(initial);
        var coord = new DeviceFunctionCoordinator(controller, bus, initial);

        var media2 = new StorageMediaSpec(DeviceMediaSource.Host, "b.iso");
        var result = await coord.ConfigureAsync(
            new DeviceFunctionConfig(CdromMedia: media2),
            TestContext.Current.CancellationToken);

        Assert.True(result.Ok);
        Assert.Equal("b.iso", result.State.CdromMedia?.Path);
        Assert.Contains(result.EmittedEvents, e => e.Kind == DeviceEventKind.CdromMediaChanged);
    }

    /// <summary>Host media paths cannot escape the host inbox.</summary>
    [Fact]
    public void Resolve_HostPathWithDotDot_Fails()
    {
        var ok = DeviceMediaPathResolver.TryResolve(
            new StorageMediaSpec(DeviceMediaSource.Host, "../secret"),
            out _,
            out var code,
            out _,
            requireExists: false);
        Assert.False(ok);
        Assert.Equal("MEDIA_PATH_INVALID", code);
    }

    /// <summary>Device absolute path resolves when file exists.</summary>
    [Fact]
    public void Resolve_DeviceAbsolute_Exists()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "mkp-media-" + System.Guid.NewGuid().ToString("N") + ".iso");
        File.WriteAllText(tmp, "iso");
        try
        {
            var ok = DeviceMediaPathResolver.TryResolve(
                new StorageMediaSpec(DeviceMediaSource.Device, tmp),
                out var abs,
                out var code,
                out _);
            Assert.True(ok);
            Assert.Equal(string.Empty, code);
            Assert.Equal(Path.GetFullPath(tmp), abs);
        }
        finally
        {
            File.Delete(tmp);
        }
    }
}
