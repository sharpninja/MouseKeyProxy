using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace MouseKeyProxy.Common;

/// <summary>
/// FR-MKP-013: lifecycle events for the Pi USB gadget appliance (keyboard, mouse, mass storage, boot).
/// Processed locally in C# and mirrored to paired hosts over gRPC.
/// </summary>
public enum DeviceEventKind
{
    /// <summary>Service and gadget manager are ready.</summary>
    BootComplete = 1,

    /// <summary>Keyboard HID function is active toward the USB host.</summary>
    KeyboardConnected = 2,

    /// <summary>Keyboard HID function is inactive.</summary>
    KeyboardDisconnected = 3,

    /// <summary>Mouse HID function is active toward the USB host.</summary>
    MouseConnected = 4,

    /// <summary>Mouse HID function is inactive.</summary>
    MouseDisconnected = 5,

    /// <summary>Mass-storage (install FS) LUN is exposed to the USB host.</summary>
    FsConnected = 6,

    /// <summary>Mass-storage LUN is withdrawn.</summary>
    FsDisconnected = 7,

    /// <summary>A file or directory was created under the shared FS watch root.</summary>
    FsFileCreated = 8,

    /// <summary>A file or directory was changed under the shared FS watch root.</summary>
    FsFileChanged = 9,

    /// <summary>A file or directory was deleted under the shared FS watch root.</summary>
    FsFileDeleted = 10,

    /// <summary>A file or directory was renamed under the shared FS watch root.</summary>
    FsFileRenamed = 11,

    /// <summary>CD-ROM LUN is exposed to the USB host.</summary>
    CdromConnected = 12,

    /// <summary>CD-ROM LUN is withdrawn.</summary>
    CdromDisconnected = 13,

    /// <summary>Virtual floppy LUN is exposed to the USB host.</summary>
    FloppyConnected = 14,

    /// <summary>Virtual floppy LUN is withdrawn.</summary>
    FloppyDisconnected = 15,

    /// <summary>CD-ROM media path/source changed while the LUN remains connected.</summary>
    CdromMediaChanged = 16,

    /// <summary>Floppy media path/source changed while the LUN remains connected.</summary>
    FloppyMediaChanged = 17,
}

/// <summary>A single device lifecycle or FS content event.</summary>
/// <param name="Kind">Event kind.</param>
/// <param name="AtUtc">UTC timestamp when the transition was committed.</param>
/// <param name="CorrelationId">Idempotency / audit correlation id.</param>
/// <param name="Detail">Optional human-readable detail (paths, UDC state).</param>
/// <param name="Path">Relative or absolute path for FS content events.</param>
/// <param name="OldPath">Previous path for rename events.</param>
public sealed record DeviceEvent(
    DeviceEventKind Kind,
    DateTimeOffset AtUtc,
    Guid CorrelationId,
    string? Detail = null,
    string? Path = null,
    string? OldPath = null);

/// <summary>Access mode for the USB mass-storage (disk) LUN.</summary>
public enum DeviceFsAccess
{
    /// <summary>Host may only read the install volume.</summary>
    ReadOnly = 0,

    /// <summary>Host may read and write the install volume.</summary>
    ReadWrite = 1,
}

/// <summary>Where CD-ROM / floppy media content is sourced from.</summary>
public enum DeviceMediaSource
{
    /// <summary>Path is on the appliance (Pi) filesystem.</summary>
    Device = 0,

    /// <summary>
    /// Path is a logical name under the host-upload inbox on the appliance
    /// (files placed via gRPC file put or copy into the host media root).
    /// </summary>
    Host = 1,
}

/// <summary>Media selection for CD-ROM or virtual floppy.</summary>
/// <param name="Source">Device path vs host-upload inbox.</param>
/// <param name="Path">
/// Device: absolute or relative under the device media root.
/// Host: relative name under the host media inbox (no <c>..</c>).
/// Empty path means no media (ejected).
/// </param>
public sealed record StorageMediaSpec(DeviceMediaSource Source, string Path);

/// <summary>
/// Desired enablement of gadget functions. Null fields mean "leave unchanged" on configure;
/// use <see cref="DeviceFunctionState"/> for the resolved snapshot.
/// When <see cref="CdromMedia"/> / <see cref="FloppyMedia"/> is non-null, media is updated
/// (use <see cref="StorageMediaSpec"/> with empty path to eject).
/// </summary>
public sealed record DeviceFunctionConfig(
    bool? KeyboardEnabled = null,
    bool? MouseEnabled = null,
    bool? FsEnabled = null,
    DeviceFsAccess? FsAccess = null,
    bool? CdromEnabled = null,
    StorageMediaSpec? CdromMedia = null,
    bool? FloppyEnabled = null,
    StorageMediaSpec? FloppyMedia = null);

/// <summary>Resolved gadget function state after configure or query.</summary>
/// <param name="KeyboardEnabled">Keyboard HID linked and active.</param>
/// <param name="MouseEnabled">Mouse HID linked and active.</param>
/// <param name="FsEnabled">Removable disk LUN exposed.</param>
/// <param name="FsAccess">RO or RW for the disk LUN.</param>
/// <param name="CdromEnabled">CD-ROM LUN exposed.</param>
/// <param name="CdromMedia">Configured CD media (null = none).</param>
/// <param name="FloppyEnabled">Virtual floppy LUN exposed.</param>
/// <param name="FloppyMedia">Configured floppy media (null = none).</param>
public sealed record DeviceFunctionState(
    bool KeyboardEnabled,
    bool MouseEnabled,
    bool FsEnabled,
    DeviceFsAccess FsAccess,
    bool CdromEnabled = false,
    StorageMediaSpec? CdromMedia = null,
    bool FloppyEnabled = false,
    StorageMediaSpec? FloppyMedia = null);

/// <summary>Result of applying a device configuration.</summary>
/// <param name="Ok">True when the configuration was applied.</param>
/// <param name="ErrorCode">Short error code when <paramref name="Ok"/> is false.</param>
/// <param name="Message">Human-readable status or error.</param>
/// <param name="State">State after the attempt (previous on failure when known).</param>
/// <param name="EmittedEvents">Events published for this transition.</param>
public sealed record DeviceConfigureResult(
    bool Ok,
    string ErrorCode,
    string Message,
    DeviceFunctionState State,
    IReadOnlyList<DeviceEvent> EmittedEvents);

/// <summary>
/// FR-MKP-013: in-process bus for device events. Local handlers subscribe; a host mirror
/// also consumes the same stream for gRPC push.
/// </summary>
public interface IDeviceEventBus
{
    /// <summary>Publishes an event to all local subscribers (ordered for a single publisher).</summary>
    /// <param name="deviceEvent">The event to publish.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    ValueTask PublishAsync(DeviceEvent deviceEvent, CancellationToken cancellationToken = default);

    /// <summary>Subscribes to the event stream. Multiple subscribers are supported.</summary>
    /// <param name="cancellationToken">Cancellation ends the enumeration.</param>
    /// <returns>Async sequence of events.</returns>
    IAsyncEnumerable<DeviceEvent> SubscribeAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Platform seam that applies keyboard / mouse / mass-storage enablement to the USB gadget
/// (configfs on Linux). No-ops or platform-not-supported on Windows.
/// </summary>
public interface IDeviceFunctionController
{
    /// <summary>Reads the current hardware-facing function state.</summary>
    DeviceFunctionState GetState();

    /// <summary>
    /// Applies the desired enablement. Implementations should be idempotent.
    /// </summary>
    /// <param name="desired">Full desired state (not a partial patch).</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Ok when the gadget matches <paramref name="desired"/>.</returns>
    Task<DeviceConfigureResult> ApplyAsync(DeviceFunctionState desired, CancellationToken cancellationToken = default);
}

/// <summary>
/// Fan-out <see cref="IDeviceEventBus"/>: each subscriber gets its own channel so local handlers
/// and the host mirror both receive every event (not competing consumers).
/// </summary>
public sealed class DeviceEventBus : IDeviceEventBus
{
    private readonly object _gate = new();
    private readonly List<Channel<DeviceEvent>> _subscribers = new();

    /// <inheritdoc />
    public async ValueTask PublishAsync(DeviceEvent deviceEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(deviceEvent);
        Channel<DeviceEvent>[] snapshot;
        lock (_gate)
        {
            snapshot = _subscribers.ToArray();
        }

        foreach (var ch in snapshot)
        {
            await ch.Writer.WriteAsync(deviceEvent, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<DeviceEvent> SubscribeAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var channel = Channel.CreateUnbounded<DeviceEvent>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
        lock (_gate)
        {
            _subscribers.Add(channel);
        }

        try
        {
            while (await channel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                while (channel.Reader.TryRead(out var item))
                {
                    yield return item;
                }
            }
        }
        finally
        {
            lock (_gate)
            {
                _subscribers.Remove(channel);
            }

            channel.Writer.TryComplete();
        }
    }
}

/// <summary>
/// FR-MKP-013 / device configure endpoint: merges partial config, applies via
/// <see cref="IDeviceFunctionController"/>, and publishes edge-triggered
/// <see cref="DeviceEvent"/>s for local C# handlers and host mirror.
/// </summary>
public sealed class DeviceFunctionCoordinator
{
    private readonly IDeviceFunctionController _controller;
    private readonly IDeviceEventBus _bus;
    private readonly object _gate = new();
    private DeviceFunctionState _state;
    private bool _bootComplete;

    /// <summary>Creates the coordinator over a platform controller and event bus.</summary>
    /// <param name="controller">Gadget function controller.</param>
    /// <param name="bus">Device event bus.</param>
    /// <param name="initial">Optional initial state (defaults: KB+MS on, FS off, RO).</param>
    public DeviceFunctionCoordinator(
        IDeviceFunctionController controller,
        IDeviceEventBus bus,
        DeviceFunctionState? initial = null)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        _state = initial ?? new DeviceFunctionState(
            KeyboardEnabled: true,
            MouseEnabled: true,
            FsEnabled: false,
            FsAccess: DeviceFsAccess.ReadOnly);
    }

    /// <summary>Current resolved function state.</summary>
    public DeviceFunctionState State
    {
        get { lock (_gate) { return _state; } }
    }

    /// <summary>
    /// Emits <see cref="DeviceEventKind.BootComplete"/> once, then applies the current state
    /// to hardware so connect events fire for enabled functions.
    /// </summary>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Events emitted (including BootComplete).</returns>
    public async Task<IReadOnlyList<DeviceEvent>> NotifyBootCompleteAsync(CancellationToken cancellationToken = default)
    {
        List<DeviceEvent> emitted;
        DeviceFunctionState snapshot;
        lock (_gate)
        {
            if (_bootComplete)
            {
                return Array.Empty<DeviceEvent>();
            }

            _bootComplete = true;
            snapshot = _state;
            emitted = new List<DeviceEvent>
            {
                NewEvent(DeviceEventKind.BootComplete, "boot complete"),
            };
        }

        await PublishAllAsync(emitted, cancellationToken).ConfigureAwait(false);

        // Re-apply so Connect edges fire for enabled functions at boot.
        var apply = await ConfigureAsync(
            new DeviceFunctionConfig(
                KeyboardEnabled: snapshot.KeyboardEnabled,
                MouseEnabled: snapshot.MouseEnabled,
                FsEnabled: snapshot.FsEnabled,
                FsAccess: snapshot.FsAccess,
                CdromEnabled: snapshot.CdromEnabled,
                CdromMedia: snapshot.CdromMedia,
                FloppyEnabled: snapshot.FloppyEnabled,
                FloppyMedia: snapshot.FloppyMedia),
            forceEmitConnects: true,
            cancellationToken).ConfigureAwait(false);

        var all = new List<DeviceEvent>(emitted);
        all.AddRange(apply.EmittedEvents);
        return all;
    }

    /// <summary>
    /// Configures keyboard, mouse, and FS enablement independently; sets FS RO/RW.
    /// Null fields in <paramref name="config"/> leave that dimension unchanged.
    /// </summary>
    /// <param name="config">Partial configuration patch.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Apply result with updated state and emitted events.</returns>
    public Task<DeviceConfigureResult> ConfigureAsync(
        DeviceFunctionConfig config,
        CancellationToken cancellationToken = default)
        => ConfigureAsync(config, forceEmitConnects: false, cancellationToken);

    private async Task<DeviceConfigureResult> ConfigureAsync(
        DeviceFunctionConfig config,
        bool forceEmitConnects,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(config);

        DeviceFunctionState previous;
        DeviceFunctionState desired;
        lock (_gate)
        {
            previous = _state;
            desired = new DeviceFunctionState(
                KeyboardEnabled: config.KeyboardEnabled ?? previous.KeyboardEnabled,
                MouseEnabled: config.MouseEnabled ?? previous.MouseEnabled,
                FsEnabled: config.FsEnabled ?? previous.FsEnabled,
                FsAccess: config.FsAccess ?? previous.FsAccess,
                CdromEnabled: config.CdromEnabled ?? previous.CdromEnabled,
                CdromMedia: config.CdromMedia ?? previous.CdromMedia,
                FloppyEnabled: config.FloppyEnabled ?? previous.FloppyEnabled,
                FloppyMedia: config.FloppyMedia ?? previous.FloppyMedia);
        }

        var applied = await _controller.ApplyAsync(desired, cancellationToken).ConfigureAwait(false);
        if (!applied.Ok)
        {
            return applied with { EmittedEvents = Array.Empty<DeviceEvent>() };
        }

        // Prefer controller-reported state when apply succeeds.
        var next = applied.State;
        var events = BuildEdgeEvents(previous, next, forceEmitConnects);

        lock (_gate)
        {
            _state = next;
        }

        await PublishAllAsync(events, cancellationToken).ConfigureAwait(false);

        return new DeviceConfigureResult(
            Ok: true,
            ErrorCode: string.Empty,
            Message: "configured",
            State: next,
            EmittedEvents: events);
    }

    private static List<DeviceEvent> BuildEdgeEvents(
        DeviceFunctionState previous,
        DeviceFunctionState next,
        bool forceEmitConnects)
    {
        var list = new List<DeviceEvent>();

        void Edge(bool was, bool now, DeviceEventKind connect, DeviceEventKind disconnect, string detail)
        {
            if (now && (!was || forceEmitConnects))
            {
                list.Add(NewEvent(connect, detail));
            }
            else if (!now && was)
            {
                list.Add(NewEvent(disconnect, detail));
            }
        }

        Edge(previous.KeyboardEnabled, next.KeyboardEnabled,
            DeviceEventKind.KeyboardConnected, DeviceEventKind.KeyboardDisconnected, "keyboard");
        Edge(previous.MouseEnabled, next.MouseEnabled,
            DeviceEventKind.MouseConnected, DeviceEventKind.MouseDisconnected, "mouse");
        Edge(previous.FsEnabled, next.FsEnabled,
            DeviceEventKind.FsConnected, DeviceEventKind.FsDisconnected,
            next.FsEnabled ? $"fs access={next.FsAccess}" : "fs");
        Edge(previous.CdromEnabled, next.CdromEnabled,
            DeviceEventKind.CdromConnected, DeviceEventKind.CdromDisconnected,
            FormatMedia("cdrom", next.CdromMedia));
        Edge(previous.FloppyEnabled, next.FloppyEnabled,
            DeviceEventKind.FloppyConnected, DeviceEventKind.FloppyDisconnected,
            FormatMedia("floppy", next.FloppyMedia));

        // FS access mode change while remaining connected: disconnect+connect so consumers refresh.
        if (previous.FsEnabled && next.FsEnabled && previous.FsAccess != next.FsAccess && !forceEmitConnects)
        {
            list.Add(NewEvent(DeviceEventKind.FsDisconnected, $"fs access was {previous.FsAccess}"));
            list.Add(NewEvent(DeviceEventKind.FsConnected, $"fs access={next.FsAccess}"));
        }

        // Media change while LUN stays connected.
        if (previous.CdromEnabled && next.CdromEnabled
            && !MediaEquals(previous.CdromMedia, next.CdromMedia) && !forceEmitConnects)
        {
            list.Add(NewEvent(DeviceEventKind.CdromMediaChanged, FormatMedia("cdrom", next.CdromMedia),
                path: next.CdromMedia?.Path));
        }

        if (previous.FloppyEnabled && next.FloppyEnabled
            && !MediaEquals(previous.FloppyMedia, next.FloppyMedia) && !forceEmitConnects)
        {
            list.Add(NewEvent(DeviceEventKind.FloppyMediaChanged, FormatMedia("floppy", next.FloppyMedia),
                path: next.FloppyMedia?.Path));
        }

        return list;
    }

    private static bool MediaEquals(StorageMediaSpec? a, StorageMediaSpec? b)
    {
        if (a is null && b is null)
        {
            return true;
        }

        if (a is null || b is null)
        {
            return false;
        }

        return a.Source == b.Source
            && string.Equals(a.Path ?? string.Empty, b.Path ?? string.Empty, StringComparison.Ordinal);
    }

    private static string FormatMedia(string kind, StorageMediaSpec? media)
    {
        if (media is null || string.IsNullOrEmpty(media.Path))
        {
            return $"{kind} media=none";
        }

        return $"{kind} source={media.Source} path={media.Path}";
    }

    private async Task PublishAllAsync(IReadOnlyList<DeviceEvent> events, CancellationToken cancellationToken)
    {
        foreach (var e in events)
        {
            await _bus.PublishAsync(e, cancellationToken).ConfigureAwait(false);
        }
    }

    private static DeviceEvent NewEvent(DeviceEventKind kind, string? detail, string? path = null)
        => new(kind, DateTimeOffset.UtcNow, Guid.NewGuid(), detail, path);
}

/// <summary>
/// In-memory <see cref="IDeviceFunctionController"/> for tests and non-Linux hosts.
/// Tracks desired state without touching configfs.
/// </summary>
public sealed class InMemoryDeviceFunctionController : IDeviceFunctionController
{
    private DeviceFunctionState _state;

    /// <summary>Creates a controller with the given initial state.</summary>
    /// <param name="initial">Initial state.</param>
    public InMemoryDeviceFunctionController(DeviceFunctionState? initial = null)
    {
        _state = initial ?? new DeviceFunctionState(true, true, false, DeviceFsAccess.ReadOnly);
    }

    /// <summary>When set, the next <see cref="ApplyAsync"/> fails with this code.</summary>
    public string? FailNextWith { get; set; }

    /// <inheritdoc />
    public DeviceFunctionState GetState() => _state;

    /// <inheritdoc />
    public Task<DeviceConfigureResult> ApplyAsync(DeviceFunctionState desired, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (FailNextWith is { Length: > 0 } code)
        {
            FailNextWith = null;
            return Task.FromResult(new DeviceConfigureResult(
                false, code, "apply failed", _state, Array.Empty<DeviceEvent>()));
        }

        _state = desired;
        return Task.FromResult(new DeviceConfigureResult(
            true, string.Empty, "ok", _state, Array.Empty<DeviceEvent>()));
    }
}
