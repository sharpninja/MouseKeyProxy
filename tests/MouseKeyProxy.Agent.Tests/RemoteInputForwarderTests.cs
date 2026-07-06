using MouseKeyProxy.Agent;
using MouseKeyProxy.Common;
using Xunit;

namespace MouseKeyProxy.Agent.Tests;

public class RemoteInputForwarderTests
{
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_MOUSEMOVE = 0x0200;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_MOUSEWHEEL = 0x020A;

    [Fact]
    [Trait("Category", "Hotkey")]
    public void TranslateKeyboardMessage_Maps_Key_Down_And_Up()
    {
        var down = RemoteInputForwarder.TranslateKeyboardMessage(WM_KEYDOWN, 0x41, 0x1E, 0);
        var up = RemoteInputForwarder.TranslateKeyboardMessage(WM_KEYUP, 0x41, 0x1E, 0x80);

        Assert.NotNull(down);
        Assert.NotNull(up);
        Assert.Equal(InputKind.KEY_DOWN, down.Kind);
        Assert.Equal(InputKind.KEY_UP, up.Kind);
        Assert.Equal(0x41u, down.Vk);
        Assert.Equal(0x41u, up.Vk);
    }

    [Fact]
    [Trait("Category", "Hotkey")]
    public void TranslateMouseMessage_Maps_Move_To_Relative_Delta()
    {
        int? lastX = null;
        int? lastY = null;

        var first = RemoteInputForwarder.TranslateMouseMessage(WM_MOUSEMOVE, 100, 200, 0, ref lastX, ref lastY);
        var second = RemoteInputForwarder.TranslateMouseMessage(WM_MOUSEMOVE, 112, 196, 0, ref lastX, ref lastY);

        Assert.Null(first);
        Assert.NotNull(second);
        Assert.Equal(InputKind.MOUSE_MOVE, second.Kind);
        Assert.Equal(12, second.Dx);
        Assert.Equal(-4, second.Dy);
    }

    [Fact]
    [Trait("Category", "Hotkey")]
    public void TranslateMouseMessage_Maps_Button_Down_And_Up()
    {
        int? lastX = 10;
        int? lastY = 10;

        var down = RemoteInputForwarder.TranslateMouseMessage(WM_LBUTTONDOWN, 10, 10, 0, ref lastX, ref lastY);
        var up = RemoteInputForwarder.TranslateMouseMessage(WM_LBUTTONUP, 10, 10, 0, ref lastX, ref lastY);

        Assert.NotNull(down);
        Assert.NotNull(up);
        Assert.Equal(InputKind.MOUSE_DOWN, down.Kind);
        Assert.Equal(InputKind.MOUSE_UP, up.Kind);
        Assert.NotEqual(0u, down.Flags);
        Assert.NotEqual(0u, up.Flags);
    }

    [Fact]
    [Trait("Category", "Hotkey")]
    public void TranslateMouseMessage_Maps_Wheel_Delta()
    {
        int? lastX = 0;
        int? lastY = 0;
        const uint wheelData = 120u << 16;

        var wheel = RemoteInputForwarder.TranslateMouseMessage(WM_MOUSEWHEEL, 0, 0, wheelData, ref lastX, ref lastY);

        Assert.NotNull(wheel);
        Assert.Equal(InputKind.MOUSE_WHEEL, wheel.Kind);
        Assert.Equal(120, wheel.WheelDelta);
        Assert.NotEqual(0u, wheel.Flags);
    }
}
