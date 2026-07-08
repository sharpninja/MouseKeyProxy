using System.Collections.Generic;
using System.Linq;
using MouseKeyProxy.Common;
using MouseKeyProxy.PiHid;
using Xunit;

namespace MouseKeyProxy.PiHid.Tests;

/// <summary>
/// FR-MKP-012 / TR-MKP-HID-001: verifies the Pi HID encoder maps arbitrary InputEvent batches to
/// USB boot-protocol keyboard/mouse reports - any key, modifier sandwiches, chunked relative mouse
/// movement, and buttons - fixing the previous 3-chord limitation. Deterministic, no hardware.
/// </summary>
public class PiHidEncoderTests
{
    private const uint VK_A = 0x41;
    private const uint VK_LWIN = 0x5B;
    private const uint VK_LEFT = 0x25;

    private static InputEvent Key(InputKind kind, uint vk) => new(kind, Vk: vk);

    /// <summary>KEY_DOWN(A) yields one keyboard report with HID usage 0x04 and no modifiers.</summary>
    [Fact]
    [Trait("Category", "HID")]
    public void KeyDown_A_MapsToHidUsage04()
    {
        var encoder = new PiHidEncoder();
        var reports = encoder.Encode(new[] { Key(InputKind.KEY_DOWN, VK_A) }, out var error);

        Assert.Null(error);
        var report = Assert.Single(reports);
        Assert.Equal(HidDevice.Keyboard, report.Device);
        Assert.Equal(8, report.Bytes.Length);
        Assert.Equal(0x00, report.Bytes[0]);  // no modifiers
        Assert.Equal(0x04, report.Bytes[2]);  // 'a' usage
    }

    /// <summary>A Win+Left chord produces the 4-report modifier sandwich (not a hardcoded chord).</summary>
    [Fact]
    [Trait("Category", "HID")]
    public void WinLeft_Chord_ProducesModifierSandwich()
    {
        var encoder = new PiHidEncoder();
        var batch = new[]
        {
            Key(InputKind.KEY_DOWN, VK_LWIN),
            Key(InputKind.KEY_DOWN, VK_LEFT),
            Key(InputKind.KEY_UP, VK_LEFT),
            Key(InputKind.KEY_UP, VK_LWIN),
        };

        var reports = encoder.Encode(batch, out var error);

        Assert.Null(error);
        Assert.Equal(4, reports.Count);
        Assert.Equal(0x08, reports[0].Bytes[0]); // LGui down
        Assert.Equal(0x00, reports[0].Bytes[2]);
        Assert.Equal(0x08, reports[1].Bytes[0]); // still LGui
        Assert.Equal(0x50, reports[1].Bytes[2]); // Left arrow usage
        Assert.Equal(0x08, reports[2].Bytes[0]); // Left released, LGui held
        Assert.Equal(0x00, reports[2].Bytes[2]);
        Assert.Equal(0x00, reports[3].Bytes[0]); // LGui released
    }

    /// <summary>A large relative mouse move is chunked into signed-byte steps that sum to the total.</summary>
    [Fact]
    [Trait("Category", "HID")]
    public void MouseMove_LargeDelta_ChunksToSignedBytes()
    {
        var encoder = new PiHidEncoder();
        var reports = encoder.Encode(new[] { new InputEvent(InputKind.MOUSE_MOVE, Dx: 300, Dy: -129) }, out var error);

        Assert.Null(error);
        Assert.All(reports, r => Assert.Equal(HidDevice.Mouse, r.Device));
        Assert.All(reports, r =>
        {
            var dx = (sbyte)r.Bytes[1];
            var dy = (sbyte)r.Bytes[2];
            Assert.InRange(dx, (sbyte)-127, (sbyte)127);
            Assert.InRange(dy, (sbyte)-127, (sbyte)127);
        });

        var totalDx = reports.Sum(r => (int)(sbyte)r.Bytes[1]);
        var totalDy = reports.Sum(r => (int)(sbyte)r.Bytes[2]);
        Assert.Equal(300, totalDx);
        Assert.Equal(-129, totalDy);
    }

    /// <summary>A mouse-button press sets the left-button bit.</summary>
    [Fact]
    [Trait("Category", "HID")]
    public void MouseDown_SetsButtonBit()
    {
        var encoder = new PiHidEncoder();
        var reports = encoder.Encode(new[] { new InputEvent(InputKind.MOUSE_DOWN) }, out var error);

        Assert.Null(error);
        var report = Assert.Single(reports);
        Assert.Equal(HidDevice.Mouse, report.Device);
        Assert.Equal(0x01, report.Bytes[0] & 0x01);
    }

    /// <summary>An unmapped virtual key is rejected with an error and no reports.</summary>
    [Fact]
    [Trait("Category", "HID")]
    public void UnmappedKey_IsRejected()
    {
        var encoder = new PiHidEncoder();
        var reports = encoder.Encode(new[] { Key(InputKind.KEY_DOWN, 0x00FE) }, out var error);

        Assert.NotNull(error);
        Assert.Empty(reports);
    }
}
