using MouseKeyProxy.PiHid;
using Xunit;

namespace MouseKeyProxy.PiHid.Tests;

/// <summary>
/// TR-MKP-RELI-001: HID link-loss detection helpers used when the gadget loses the USB host.
/// </summary>
public class HidLinkHealthTests
{
    /// <summary>Disconnect error code and transport markers are recognized.</summary>
    [Fact]
    [Trait("Category", "HID")]
    public void IsDisconnectError_RecognizesStableCodeAndTransportMarkers()
    {
        Assert.True(HidLinkHealth.IsDisconnectError(HidLinkHealth.DisconnectedErrorCode));
        Assert.True(HidLinkHealth.IsDisconnectError(HidLinkHealth.FormatDisconnectError("UDC state=not attached")));
        Assert.True(HidLinkHealth.IsDisconnectError("IOException: Broken pipe"));
        Assert.True(HidLinkHealth.IsDisconnectError("ESHUTDOWN"));
        Assert.False(HidLinkHealth.IsDisconnectError("unsupported virtual key"));
        Assert.False(HidLinkHealth.IsDisconnectError(null));
    }

    /// <summary>FormatDisconnectError always includes the stable code prefix.</summary>
    [Fact]
    [Trait("Category", "HID")]
    public void FormatDisconnectError_IncludesCodePrefix()
    {
        var msg = HidLinkHealth.FormatDisconnectError("UDC state=not attached");
        Assert.StartsWith(HidLinkHealth.DisconnectedErrorPrefix, msg);
        Assert.Contains("not attached", msg);
    }
}
