using Grpc.Core;
using Microsoft.Extensions.Logging;
using MouseKeyProxy.Common;
using MouseKeyProxy.Network.V1;
using NSubstitute;

namespace MouseKeyProxy.Service.Tests;

public class AdvancedControlServiceTests
{
    [Fact]
    [Trait("Category", "AdvancedControl")]
    public async Task TEST_MKP_010_SetMousePosition_Delegates_To_Remote_Desktop_Controller()
    {
        var controller = Substitute.For<IRemoteDesktopController>();
        controller.SetMousePosition("DISPLAY1", 123, 456)
            .Returns(RemoteControlResult.Success("cursor moved"));
        var impl = CreateImpl(controller);

        var response = await impl.SetMousePosition(new SetMousePositionRequest
        {
            PeerId = "payton-desktop",
            DisplayId = "DISPLAY1",
            X = 123,
            Y = 456,
            CorrelationId = "cursor-proof"
        }, Substitute.For<ServerCallContext>());

        Assert.True(response.Ok);
        Assert.Equal("cursor moved", response.Msg);
        controller.Received(1).SetMousePosition("DISPLAY1", 123, 456);
    }

    [Fact]
    [Trait("Category", "AdvancedControl")]
    public async Task TEST_MKP_011_LocateProcess_Returns_Window_Tree_From_Controller()
    {
        var controller = Substitute.For<IRemoteDesktopController>();
        controller.LocateProcess("notepad", 0)
            .Returns(new[]
            {
                new RemoteWindowNode(0x1234, "Untitled - Notepad", "Notepad", 4242, new[]
                {
                    new RemoteWindowNode(0x1235, "Edit", "Edit", 4242, Array.Empty<RemoteWindowNode>())
                })
            });
        var impl = CreateImpl(controller);

        var response = await impl.LocateProcess(new LocateProcessRequest
        {
            PeerId = "payton-desktop",
            ProcessName = "notepad"
        }, Substitute.For<ServerCallContext>());

        Assert.Equal("0", response.ErrorCode);
        var node = Assert.Single(response.Nodes);
        Assert.Equal(0x1234UL, node.Hwnd);
        Assert.Equal("Untitled - Notepad", node.Title);
        Assert.Equal("Notepad", node.ClassName);
        Assert.Equal(4242U, node.ProcessId);
        Assert.Single(node.Children);
        controller.Received(1).LocateProcess("notepad", 0);
    }

    [Fact]
    [Trait("Category", "AdvancedControl")]
    public async Task TEST_MKP_011_SetFocusByHwnd_Delegates_And_Propagates_Failure()
    {
        var controller = Substitute.For<IRemoteDesktopController>();
        controller.SetFocusByHwnd(0x1234, bringToFront: true)
            .Returns(RemoteControlResult.Failure("FOCUS_DENIED", "window not focusable"));
        var impl = CreateImpl(controller);

        var response = await impl.SetFocusByHwnd(new SetFocusByHwndRequest
        {
            PeerId = "payton-desktop",
            Hwnd = 0x1234,
            BringToFront = true,
            CorrelationId = "focus-proof"
        }, Substitute.For<ServerCallContext>());

        Assert.False(response.Ok);
        Assert.Equal("FOCUS_DENIED", response.Err);
        Assert.Equal("window not focusable", response.Msg);
        controller.Received(1).SetFocusByHwnd(0x1234, bringToFront: true);
    }

    private static MouseKeyProxyImpl CreateImpl(IRemoteDesktopController controller)
    {
        return new MouseKeyProxyImpl(
            Substitute.For<ILogger<MouseKeyProxyImpl>>(),
            dispatcher: null,
            desktopController: controller);
    }
}
