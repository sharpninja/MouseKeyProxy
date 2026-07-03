namespace MouseKeyProxy.Common;

/// <summary>
/// Pure toggle state machine (hotkey driven, no edge).
/// Locked: explicit hotkey only; on toggle emit ModResync etc.
/// </summary>
public class ToggleStateMachine
{
    public bool IsActive { get; private set; }
    public string ActivePeerId { get; private set; } = string.Empty;

    public (bool Changed, bool NewActive, string? Direction) ApplyToggle(string peerId)
    {
        bool was = IsActive;
        IsActive = !IsActive;
        if (IsActive) ActivePeerId = peerId;
        return (Changed: IsActive != was, NewActive: IsActive, Direction: IsActive ? peerId : null);
    }

    public void Reset() { IsActive = false; ActivePeerId = string.Empty; }
}
