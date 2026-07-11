using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MouseKeyProxy.Common;
using Xunit;

namespace MouseKeyProxy.Common.Tests;

/// <summary>
/// FR-MKP-013: device function configure (keyboard / mouse / FS enable + FS RO/RW) and
/// edge-triggered device events for local C# handlers and host mirror.
/// </summary>
public class DeviceFunctionCoordinatorTests
{
    private static DeviceFunctionCoordinator Create(
        DeviceFunctionState initial,
        out InMemoryDeviceFunctionController controller,
        out DeviceEventBus bus)
    {
        bus = new DeviceEventBus();
        controller = new InMemoryDeviceFunctionController(initial);
        return new DeviceFunctionCoordinator(controller, bus, initial);
    }

    /// <summary>Partial configure enables FS read-only and emits FsConnected only.</summary>
    [Fact]
    public async Task Configure_EnableFsReadOnly_EmitsFsConnected()
    {
        var coord = Create(
            new DeviceFunctionState(true, true, false, DeviceFsAccess.ReadOnly),
            out _,
            out _);

        var result = await coord.ConfigureAsync(
            new DeviceFunctionConfig(FsEnabled: true, FsAccess: DeviceFsAccess.ReadOnly),
            TestContext.Current.CancellationToken);

        Assert.True(result.Ok);
        Assert.True(result.State.FsEnabled);
        Assert.Equal(DeviceFsAccess.ReadOnly, result.State.FsAccess);
        Assert.Contains(result.EmittedEvents, e => e.Kind == DeviceEventKind.FsConnected);
        Assert.DoesNotContain(result.EmittedEvents, e => e.Kind == DeviceEventKind.KeyboardDisconnected);
    }

    /// <summary>Disabling keyboard emits KeyboardDisconnected; mouse unchanged.</summary>
    [Fact]
    public async Task Configure_DisableKeyboard_EmitsKeyboardDisconnected_Only()
    {
        var coord = Create(
            new DeviceFunctionState(true, true, false, DeviceFsAccess.ReadOnly),
            out _,
            out _);

        var result = await coord.ConfigureAsync(
            new DeviceFunctionConfig(KeyboardEnabled: false),
            TestContext.Current.CancellationToken);

        Assert.True(result.Ok);
        Assert.False(result.State.KeyboardEnabled);
        Assert.True(result.State.MouseEnabled);
        Assert.Single(result.EmittedEvents);
        Assert.Equal(DeviceEventKind.KeyboardDisconnected, result.EmittedEvents[0].Kind);
    }

    /// <summary>FS RO to RW while connected emits FsDisconnected then FsConnected.</summary>
    [Fact]
    public async Task Configure_FsRoToRw_EmitsDisconnectThenConnect()
    {
        var coord = Create(
            new DeviceFunctionState(true, true, true, DeviceFsAccess.ReadOnly),
            out _,
            out _);

        var result = await coord.ConfigureAsync(
            new DeviceFunctionConfig(FsAccess: DeviceFsAccess.ReadWrite),
            TestContext.Current.CancellationToken);

        Assert.True(result.Ok);
        Assert.Equal(DeviceFsAccess.ReadWrite, result.State.FsAccess);
        Assert.Equal(2, result.EmittedEvents.Count);
        Assert.Equal(DeviceEventKind.FsDisconnected, result.EmittedEvents[0].Kind);
        Assert.Equal(DeviceEventKind.FsConnected, result.EmittedEvents[1].Kind);
    }

    /// <summary>Idempotent configure emits no events when state is unchanged.</summary>
    [Fact]
    public async Task Configure_Idempotent_EmitsNothing()
    {
        var coord = Create(
            new DeviceFunctionState(true, true, true, DeviceFsAccess.ReadOnly),
            out _,
            out _);

        var result = await coord.ConfigureAsync(
            new DeviceFunctionConfig(KeyboardEnabled: true, MouseEnabled: true, FsEnabled: true, FsAccess: DeviceFsAccess.ReadOnly),
            TestContext.Current.CancellationToken);

        Assert.True(result.Ok);
        Assert.Empty(result.EmittedEvents);
    }

    /// <summary>Controller failure does not change coordinator state or emit events.</summary>
    [Fact]
    public async Task Configure_ControllerFails_LeavesStateAndNoEvents()
    {
        var initial = new DeviceFunctionState(true, true, false, DeviceFsAccess.ReadOnly);
        var coord = Create(initial, out var controller, out _);
        controller.FailNextWith = "GADGET_APPLY_FAILED";

        var result = await coord.ConfigureAsync(
            new DeviceFunctionConfig(MouseEnabled: false),
            TestContext.Current.CancellationToken);

        Assert.False(result.Ok);
        Assert.Equal("GADGET_APPLY_FAILED", result.ErrorCode);
        Assert.True(coord.State.MouseEnabled);
        Assert.Empty(result.EmittedEvents);
    }

    /// <summary>BootComplete is emitted once; enabled functions get Connect events.</summary>
    [Fact]
    public async Task NotifyBootComplete_EmitsBootAndConnects_Once()
    {
        var bus = new DeviceEventBus();
        var controller = new InMemoryDeviceFunctionController(
            new DeviceFunctionState(true, false, false, DeviceFsAccess.ReadOnly));
        var coord = new DeviceFunctionCoordinator(
            controller,
            bus,
            initial: new DeviceFunctionState(true, false, false, DeviceFsAccess.ReadOnly));

        var first = await coord.NotifyBootCompleteAsync(TestContext.Current.CancellationToken);
        var second = await coord.NotifyBootCompleteAsync(TestContext.Current.CancellationToken);

        Assert.Contains(first, e => e.Kind == DeviceEventKind.BootComplete);
        Assert.Contains(first, e => e.Kind == DeviceEventKind.KeyboardConnected);
        Assert.DoesNotContain(first, e => e.Kind == DeviceEventKind.MouseConnected);
        Assert.Empty(second);
    }

    /// <summary>Independent enable of mouse and FS in one request emits both connect events.</summary>
    [Fact]
    public async Task Configure_EnableMouseAndFs_EmitsBoth()
    {
        var coord = Create(
            new DeviceFunctionState(true, false, false, DeviceFsAccess.ReadOnly),
            out _,
            out _);

        var result = await coord.ConfigureAsync(
            new DeviceFunctionConfig(MouseEnabled: true, FsEnabled: true, FsAccess: DeviceFsAccess.ReadWrite),
            TestContext.Current.CancellationToken);

        Assert.True(result.Ok);
        Assert.True(result.State.MouseEnabled);
        Assert.True(result.State.FsEnabled);
        Assert.Equal(DeviceFsAccess.ReadWrite, result.State.FsAccess);
        Assert.Contains(result.EmittedEvents, e => e.Kind == DeviceEventKind.MouseConnected);
        Assert.Contains(result.EmittedEvents, e => e.Kind == DeviceEventKind.FsConnected);
    }

    /// <summary>Published events are readable from the bus for local handlers / host mirror.</summary>
    [Fact]
    public async Task Configure_PublishesToEventBus()
    {
        var bus = new DeviceEventBus();
        var controller = new InMemoryDeviceFunctionController();
        var coord = new DeviceFunctionCoordinator(controller, bus);
        var received = new List<DeviceEvent>();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var subscribed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reader = Task.Run(async () =>
        {
            var enumerator = bus.SubscribeAsync(cts.Token).GetAsyncEnumerator(cts.Token);
            subscribed.TrySetResult();
            try
            {
                while (await enumerator.MoveNextAsync())
                {
                    received.Add(enumerator.Current);
                    if (received.Count >= 1)
                    {
                        break;
                    }
                }
            }
            finally
            {
                await enumerator.DisposeAsync();
            }
        }, cts.Token);

        await subscribed.Task.WaitAsync(cts.Token);
        // Subscriber is registered; still give the enumerator a tick to enter WaitToRead.
        await Task.Delay(50, TestContext.Current.CancellationToken);
        await coord.ConfigureAsync(new DeviceFunctionConfig(FsEnabled: true), TestContext.Current.CancellationToken);
        await reader;

        Assert.Contains(received, e => e.Kind == DeviceEventKind.FsConnected);
    }
}
