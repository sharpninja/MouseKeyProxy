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

    [Fact]
    [Trait("Category", "EmergencyRelease")]
    public async Task TEST_MKP_013_EmergencyRelease_Delegates_To_Agent_Controller()
    {
        var controller = Substitute.For<IEmergencyReleaseController>();
        controller.EmergencyRelease("payton-desktop", "release-proof")
            .Returns(RemoteControlResult.Success("released"));
        var impl = CreateImpl(Substitute.For<IRemoteDesktopController>(), controller);

        var response = await impl.EmergencyRelease(new EmergencyReleaseRequest
        {
            PeerId = "payton-desktop",
            CorrelationId = "release-proof"
        }, Substitute.For<ServerCallContext>());

        Assert.True(response.Ok);
        Assert.Equal("released", response.Msg);
        controller.Received(1).EmergencyRelease("payton-desktop", "release-proof");
    }

    [Fact]
    [Trait("Category", "ModifierCleanup")]
    [Trait("Category", "InputRegression")]
    public async Task TEST_MKP_023_ClearModifiers_Delegates_To_Modifier_Controller()
    {
        var modifierController = Substitute.For<IModifierReleaseController>();
        modifierController.ClearModifiers("payton-desktop", "clear-proof")
            .Returns(RemoteControlResult.Success("cleared"));
        var impl = CreateImpl(
            Substitute.For<IRemoteDesktopController>(),
            modifierReleaseController: modifierController);

        var response = await impl.ClearModifiers(new ClearModifiersRequest
        {
            PeerId = "payton-desktop",
            CorrelationId = "clear-proof"
        }, Substitute.For<ServerCallContext>());

        Assert.True(response.Ok);
        Assert.Equal("cleared", response.Msg);
        modifierController.Received(1).ClearModifiers("payton-desktop", "clear-proof");
    }

    [Fact]
    [Trait("Category", "WindowProbeE2E")]
    public async Task TEST_MKP_025_CaptureScreenshot_Streams_Metadata_And_Png_Bytes()
    {
        var capture = Substitute.For<IScreenshotCapture>();
        var png = new byte[] { 137, 80, 78, 71, 1, 2, 3 };
        capture.Capture(Arg.Any<ScreenshotCaptureRequest>()).Returns(new ScreenshotCaptureResult(
            new MouseKeyProxy.Common.ScreenshotMetadata(
                DateTimeOffset.Parse("2026-07-07T09:00:00Z"),
                "payton-desktop",
                "shot-proof",
                MouseKeyProxy.Common.ScreenshotTarget.Foreground,
                0x1234,
                640,
                480,
                "abc123"),
            png));
        var impl = CreateImpl(
            Substitute.For<IRemoteDesktopController>(),
            screenshotCapture: capture);
        var writer = new RecordingServerStreamWriter<ScreenshotChunk>();

        await impl.CaptureScreenshot(new CaptureScreenshotRequest
        {
            PeerId = "payton-legion2",
            CorrelationId = "shot-proof",
            Target = MouseKeyProxy.Network.V1.ScreenshotTarget.Foreground,
            Hwnd = 0x1234,
            IncludeCursor = true
        }, writer, Substitute.For<ServerCallContext>());

        var chunk = Assert.Single(writer.Responses);
        Assert.True(chunk.Last);
        Assert.Equal((uint)0, chunk.Index);
        Assert.Equal(png, chunk.Data.ToByteArray());
        Assert.Equal("shot-proof", chunk.Metadata.CorrelationId);
        Assert.Equal("payton-desktop", chunk.Metadata.SourceHost);
        Assert.Equal("abc123", chunk.Metadata.Sha256);
        Assert.Equal((uint)640, chunk.Metadata.Width);
    }

    private sealed class RecordingServerStreamWriter<T> : IServerStreamWriter<T> where T : class
    {
        public List<T> Responses { get; } = new();
        public WriteOptions? WriteOptions { get; set; }
        public Task WriteAsync(T message)
        {
            Responses.Add(message);
            return Task.CompletedTask;
        }
    }
    private static MouseKeyProxyImpl CreateImpl(
        IRemoteDesktopController controller,
        IEmergencyReleaseController? emergencyReleaseController = null,
        IModifierReleaseController? modifierReleaseController = null,
        IScreenshotCapture? screenshotCapture = null)
    {
        return new MouseKeyProxyImpl(
            Substitute.For<ILogger<MouseKeyProxyImpl>>(),
            dispatcher: null,
            desktopController: controller,
            emergencyReleaseController: emergencyReleaseController,
            modifierReleaseController: modifierReleaseController,
            screenshotCapture: screenshotCapture);
    }
}
