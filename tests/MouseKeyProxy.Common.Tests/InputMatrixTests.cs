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

    [Fact]
    [Trait("Category", "InputMatrix")]
    public void Sas_OrSecure_ReturnsFalse_ObservableFailure()
    {
        Assert.False(InputSupportMatrix.IsSupported(InputKind.KEY_DOWN, vk: 0x2E /*Del approx for SAS*/));
        Assert.False(InputSupportMatrix.IsSupported(InputKind.KEY_DOWN, 0, isSecureDesktop: true));
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
