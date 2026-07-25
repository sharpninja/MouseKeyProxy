using MouseKeyProxy.Common;
using Xunit;

namespace MouseKeyProxy.Common.Tests;

public class InputMatrixTests
{
    [Fact]
    [Trait("Category", "InputMatrix")]
    public void Supported_OrdinaryKey_IsSupported_ReturnsTrue_WhenNotStub()
    {
        // This will be red while stub always returns false. Per Byrd: first red, then green impl.
        // Locked: ordinary keys supported, SAS/secure not.
        var result = InputSupportMatrix.IsSupported(InputKind.KEY_DOWN, vk: (uint)'A');
        Assert.True(result, "Ordinary key (A) must be supported per matrix");
    }

    [Theory]
    [Trait("Category", "InputMatrix")]
    [Trait("Category", "InputRegression")]
    [InlineData(0x2D)] // Insert
    [InlineData(0x2E)] // Delete
    [InlineData(0x24)] // Home
    [InlineData(0x23)] // End
    [InlineData(0x21)] // Page Up
    [InlineData(0x22)] // Page Down
    public void EditingAndNavigationKeys_AreSupported(uint vk)
    {
        Assert.True(InputSupportMatrix.IsSupported(InputKind.KEY_DOWN, vk));
        Assert.True(InputSupportMatrix.IsSupported(InputKind.KEY_UP, vk));
    }

    [Fact]
    [Trait("Category", "InputMatrix")]
    public void SecureDesktop_ReturnsFalse_ObservableFailure()
    {
        Assert.False(InputSupportMatrix.IsSupported(InputKind.KEY_DOWN, 0, isSecureDesktop: true));
        Assert.Equal("SECURE_DESKTOP", InputSupportMatrix.GetFailureReason(InputKind.KEY_DOWN, isSecureDesktop: true));
    }

    [Theory]
    [Trait("Category", "InputMatrix")]
    [InlineData(InputKind.MOUSE_MOVE)]
    [InlineData(InputKind.MOUSE_WHEEL)]
    [InlineData(InputKind.TEXT_INPUT)]
    public void MouseAndText_AreSupported(InputKind kind)
    {
        Assert.True(InputSupportMatrix.IsSupported(kind), $"Expected {kind} supported");
    }
}
