using MouseKeyProxy.Common;
using MouseKeyProxy.PiHid;
using Xunit;

namespace MouseKeyProxy.PiHid.Tests;

/// <summary>
/// FR-MKP-012 / TR-MKP-HID-001: USB HID boot keyboard must map US OEM punctuation VKs and
/// printable characters so remote typing includes comma, period, and other punctuation.
/// </summary>
public class HidKeyboardUsagePunctuationTests
{
    // Windows OEM virtual keys (US QWERTY).
    private const uint VK_OEM_1 = 0xBA;      // ; :
    private const uint VK_OEM_PLUS = 0xBB;   // = +
    private const uint VK_OEM_COMMA = 0xBC;  // , <
    private const uint VK_OEM_MINUS = 0xBD;  // - _
    private const uint VK_OEM_PERIOD = 0xBE; // . >
    private const uint VK_OEM_2 = 0xBF;      // / ?
    private const uint VK_OEM_3 = 0xC0;      // ` ~
    private const uint VK_OEM_4 = 0xDB;      // [ {
    private const uint VK_OEM_5 = 0xDC;      // \ |
    private const uint VK_OEM_6 = 0xDD;      // ] }
    private const uint VK_OEM_7 = 0xDE;      // ' "

    /// <summary>VK_OEM_COMMA maps to HID usage 0x36 (Keyboard , and &lt;).</summary>
    [Fact]
    [Trait("Category", "HID")]
    public void TryMap_OemComma_IsHidUsage36()
    {
        Assert.True(HidKeyboardUsage.TryMap(VK_OEM_COMMA, out var usage));
        Assert.Equal(0x36, usage);
    }

    /// <summary>All standard US OEM punctuation VKs map to the HID boot-keyboard usages.</summary>
    [Theory]
    [Trait("Category", "HID")]
    [InlineData(VK_OEM_1, 0x33)]
    [InlineData(VK_OEM_PLUS, 0x2E)]
    [InlineData(VK_OEM_COMMA, 0x36)]
    [InlineData(VK_OEM_MINUS, 0x2D)]
    [InlineData(VK_OEM_PERIOD, 0x37)]
    [InlineData(VK_OEM_2, 0x38)]
    [InlineData(VK_OEM_3, 0x35)]
    [InlineData(VK_OEM_4, 0x2F)]
    [InlineData(VK_OEM_5, 0x31)]
    [InlineData(VK_OEM_6, 0x30)]
    [InlineData(VK_OEM_7, 0x34)]
    public void TryMap_OemPunctuation_MapsToHidUsages(uint vk, byte expectedUsage)
    {
        Assert.True(HidKeyboardUsage.TryMap(vk, out var usage), $"VK 0x{vk:X2} should map");
        Assert.Equal(expectedUsage, usage);
    }

    /// <summary>Printable punctuation characters map with correct Shift requirement (US QWERTY).</summary>
    [Theory]
    [Trait("Category", "HID")]
    [InlineData(',', 0x36, false)]
    [InlineData('<', 0x36, true)]
    [InlineData('.', 0x37, false)]
    [InlineData('>', 0x37, true)]
    [InlineData('/', 0x38, false)]
    [InlineData('?', 0x38, true)]
    [InlineData(';', 0x33, false)]
    [InlineData(':', 0x33, true)]
    [InlineData('\'', 0x34, false)]
    [InlineData('"', 0x34, true)]
    [InlineData('[', 0x2F, false)]
    [InlineData('{', 0x2F, true)]
    [InlineData(']', 0x30, false)]
    [InlineData('}', 0x30, true)]
    [InlineData('\\', 0x31, false)]
    [InlineData('|', 0x31, true)]
    [InlineData('`', 0x35, false)]
    [InlineData('~', 0x35, true)]
    [InlineData('-', 0x2D, false)]
    [InlineData('_', 0x2D, true)]
    [InlineData('=', 0x2E, false)]
    [InlineData('+', 0x2E, true)]
    [InlineData('!', 0x1E, true)]  // Shift+1
    [InlineData('@', 0x1F, true)]  // Shift+2
    [InlineData('#', 0x20, true)]
    [InlineData('$', 0x21, true)]
    [InlineData('%', 0x22, true)]
    [InlineData('^', 0x23, true)]
    [InlineData('&', 0x24, true)]
    [InlineData('*', 0x25, true)]
    [InlineData('(', 0x26, true)]
    [InlineData(')', 0x27, true)]
    public void TryMapChar_Punctuation_MapsWithShiftFlag(char c, byte expectedUsage, bool needsShift)
    {
        Assert.True(HidKeyboardUsage.TryMapChar(c, out var usage, out var shift), $"char '{c}' should map");
        Assert.Equal(expectedUsage, usage);
        Assert.Equal(needsShift, shift);
    }

    /// <summary>Encoder emits a keyboard report for comma key-down (does not reject the batch).</summary>
    [Fact]
    [Trait("Category", "HID")]
    public void Encode_CommaKeyDown_ProducesKeyboardReport()
    {
        var encoder = new PiHidEncoder();
        var reports = encoder.Encode(
            new[] { new InputEvent(InputKind.KEY_DOWN, Vk: VK_OEM_COMMA) },
            out var error);

        Assert.Null(error);
        var report = Assert.Single(reports);
        Assert.Equal(HidDevice.Keyboard, report.Device);
        Assert.Equal(0x36, report.Bytes[2]);
    }

    /// <summary>Encoder TEXT_INPUT path types a comma without error.</summary>
    [Fact]
    [Trait("Category", "HID")]
    public void Encode_TextWithComma_ProducesDownAndUpReports()
    {
        var encoder = new PiHidEncoder();
        var reports = encoder.Encode(
            new[] { new InputEvent(InputKind.TEXT_INPUT, Text: "a,b") },
            out var error);

        Assert.Null(error);
        // each char -> down + release
        Assert.Equal(6, reports.Count);
        Assert.Equal(0x04, reports[0].Bytes[2]); // a
        Assert.Equal(0x36, reports[2].Bytes[2]); // comma
        Assert.Equal(0x05, reports[4].Bytes[2]); // b
    }
}
