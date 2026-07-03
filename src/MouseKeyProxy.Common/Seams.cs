using System;

namespace MouseKeyProxy.Common;

/// <summary>
/// Seams (narrow interfaces) per PLAN-MKP-004. Implemented thinly in Agent/Service; tested with NSubstitute in * .Tests.
/// </summary>

public interface IHotkeyMonitor
{
    event EventHandler<ToggleEventArgs> ToggleRequested;
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
