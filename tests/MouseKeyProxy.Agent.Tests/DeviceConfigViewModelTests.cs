using MouseKeyProxy.Agent;
using MouseKeyProxy.Network.V1;
using Xunit;

namespace MouseKeyProxy.Agent.Tests;

/// <summary>
/// TEST-MKP-041 / TEST-MKP-043 / FR-MKP-018: device config mappers and SMB UNC builder.
/// </summary>
public class DeviceConfigViewModelTests
{
    /// <summary>FromState maps every Configure/Get field used by the agent form.</summary>
    [Fact]
    public void FromState_MapsAllFunctionFields()
    {
        var state = new DeviceFunctionStateMsg
        {
            KeyboardEnabled = true,
            MouseEnabled = false,
            FsEnabled = true,
            FsAccess = DeviceFsAccessMode.FsReadWrite,
            CdromEnabled = true,
            CdromMedia = new StorageMediaSpecMsg
            {
                Source = DeviceMediaSource.MediaSourceHost,
                Path = "install.iso",
            },
            FloppyEnabled = true,
            FloppyMedia = new StorageMediaSpecMsg
            {
                Source = DeviceMediaSource.MediaSourceDevice,
                Path = "media/device/boot.img",
            },
        };

        var model = DeviceConfigViewModel.FromState(state);
        Assert.True(model.KeyboardEnabled);
        Assert.False(model.MouseEnabled);
        Assert.True(model.FsEnabled);
        Assert.True(model.FsReadWrite);
        Assert.True(model.CdromEnabled);
        Assert.True(model.CdromFromHost);
        Assert.Equal("install.iso", model.CdromPath);
        Assert.True(model.FloppyEnabled);
        Assert.False(model.FloppyFromHost);
        Assert.Equal("media/device/boot.img", model.FloppyPath);
    }

    /// <summary>ToConfigureRequest includes media updates when flags are set.</summary>
    [Fact]
    public void ToConfigureRequest_IncludesMediaWhenRequested()
    {
        var model = new DeviceConfigUiModel
        {
            KeyboardEnabled = true,
            MouseEnabled = true,
            FsEnabled = true,
            CdromEnabled = true,
            UpdateCdromMedia = true,
            CdromFromHost = true,
            CdromPath = "foo.iso",
        };

        var req = DeviceConfigViewModel.ToConfigureRequest(model, "desktop");
        Assert.True(req.KeyboardEnabled);
        Assert.True(req.UpdateCdromMedia);
        Assert.NotNull(req.CdromMedia);
        Assert.Equal(DeviceMediaSource.MediaSourceHost, req.CdromMedia.Source);
        Assert.Equal("foo.iso", req.CdromMedia.Path);
        Assert.False(req.UpdateFloppyMedia);
    }

    /// <summary>SMB UNC is built from host and share name.</summary>
    [Fact]
    public void BuildSmbUnc_FromHostAndShare()
    {
        Assert.Equal(@"\\192.168.1.200\MouseKeyProxy", DeviceConfigViewModel.BuildSmbUnc("192.168.1.200"));
        Assert.Equal(@"\\pi.local\share1", DeviceConfigViewModel.BuildSmbUnc("https://pi.local:50051", "share1"));
    }

    /// <summary>Pairing code client-side validation rejects empty and too-short values.</summary>
    [Fact]
    public void IsPlausiblePairingCode_ValidatesLength()
    {
        Assert.False(DeviceConfigViewModel.IsPlausiblePairingCode(null));
        Assert.False(DeviceConfigViewModel.IsPlausiblePairingCode("12"));
        Assert.True(DeviceConfigViewModel.IsPlausiblePairingCode("123456"));
    }
}
