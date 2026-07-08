using System;
using System.Collections.Generic;

namespace MouseKeyProxy.Common;

/// <summary>
/// Seams (narrow interfaces) per PLAN-MKP-004. Implemented thinly in Agent/Service; tested with NSubstitute in * .Tests.
/// </summary>

public interface IHotkeyMonitor
{
    event EventHandler<ToggleEventArgs> ToggleRequested;

    /// <summary>FR-MKP-001 / TR-MKP-RELI-001: dedicated emergency-release hotkey, distinct from toggle.</summary>
    event EventHandler<ToggleEventArgs> EmergencyReleaseRequested;

    void StartMonitoring();
    void StopMonitoring();
}

public class ToggleEventArgs : EventArgs
{
    public string Chord { get; }
    public bool IsRemoteChord { get; }
    public ToggleEventArgs(string chord, bool isRemote) { Chord = chord; IsRemoteChord = isRemote; }
}

public interface IInputInjector
{
    void Send(InputEvent evt);
    bool TryInjectBatch(System.Collections.Generic.IEnumerable<InputEvent> events, out string? error);
}

public record InputEvent(
    InputKind Kind,
    uint Vk = 0,
    uint Scan = 0,
    uint Flags = 0,
    int Dx = 0,
    int Dy = 0,
    int WheelDelta = 0,
    uint XButton = 0,
    string? Text = null,
    ulong TsMs = 0
);

public interface IClipboardAccessor
{
    event EventHandler<ClipboardEventArgs> ClipboardChanged;
    void SetClipboard(ClipboardEntry entry);
}

public class ClipboardEventArgs : EventArgs
{
    public ClipboardEntry Entry { get; }
    public ClipboardEventArgs(ClipboardEntry e) => Entry = e;
}

public interface ICursorClip
{
    void ClipToPoint(int x, int y);
    void Release();
    bool IsClipped { get; }
}

public interface IRemoteDesktopController
{
    RemoteControlResult SetMousePosition(string displayId, int x, int y);
    IReadOnlyList<RemoteWindowNode> LocateProcess(string processName, uint pid);
    RemoteControlResult SetFocusByHwnd(ulong hwnd, bool bringToFront);
}

public interface IEmergencyReleaseController
{
    RemoteControlResult EmergencyRelease(string peerId, string correlationId);
}

public interface IModifierReleaseController
{
    RemoteControlResult ClearModifiers(string peerId, string correlationId);
}

public interface IScreenshotCapture
{
    ScreenshotCaptureResult Capture(ScreenshotCaptureRequest request);
}

public readonly record struct RemoteControlResult(bool Ok, string ErrorCode, string Message)
{
    public static RemoteControlResult Success(string message = "ok") => new(true, "0", message);

    public static RemoteControlResult Failure(string errorCode, string message) => new(false, errorCode, message);
}

public sealed record RemoteWindowNode(
    ulong Hwnd,
    string Title,
    string ClassName,
    uint ProcessId,
    IReadOnlyList<RemoteWindowNode> Children);