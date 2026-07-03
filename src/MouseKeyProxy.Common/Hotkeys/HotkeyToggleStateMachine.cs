namespace MouseKeyProxy.Common.Hotkeys;

public class HotkeyToggleStateMachine
{
    public bool IsActive { get; private set; } = false;

    public HotkeyTransition HandleChord(/* params for chord */)
    {
        // logic for active flip only on correct, no auto edge
        if (/* correct local */)
        {
            IsActive = !IsActive;
            return IsActive ? HotkeyTransition.Activated : HotkeyTransition.Deactivated;
        }
        return HotkeyTransition.None;
    }

    // more methods for release, etc.
}
