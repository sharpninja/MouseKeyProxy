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
    [Trait("Category", "RawMouseCapture")]
    [Trait("Category", "InputRegression")]
    public void TranslateRawMouseDelta_Maps_Relative_Delta_Without_Screen_Coordinates()
    {
        var zero = RemoteInputForwarder.TranslateRawMouseDelta(0, 0);
        var move = RemoteInputForwarder.TranslateRawMouseDelta(12, -4);

        Assert.Null(zero);
        Assert.NotNull(move);
        Assert.Equal(InputKind.MOUSE_MOVE, move.Kind);
        Assert.Equal(12, move.Dx);
        Assert.Equal(-4, move.Dy);
    }

    [Fact]
    [Trait("Category", "RawMouseCapture")]
    public void TranslateMouseMessage_Does_Not_Forward_Screen_Coordinate_Mouse_Move()
    {
        Assert.Null(RemoteInputForwarder.TranslateMouseMessage(WM_MOUSEMOVE, 0));
    }

    [Fact]
    [Trait("Category", "Hotkey")]
    public void TranslateMouseMessage_Maps_Button_Down_And_Up()
    {
        var down = RemoteInputForwarder.TranslateMouseMessage(WM_LBUTTONDOWN, 0);
        var up = RemoteInputForwarder.TranslateMouseMessage(WM_LBUTTONUP, 0);

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
        const uint wheelData = 120u << 16;

        var wheel = RemoteInputForwarder.TranslateMouseMessage(WM_MOUSEWHEEL, wheelData);

        Assert.NotNull(wheel);
        Assert.Equal(InputKind.MOUSE_WHEEL, wheel.Kind);
        Assert.Equal(120, wheel.WheelDelta);
        Assert.NotEqual(0u, wheel.Flags);
    }
}