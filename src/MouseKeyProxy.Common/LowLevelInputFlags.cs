namespace MouseKeyProxy.Common;

/// <summary>
/// Win32 low-level hook flag bits for keyboard (<c>KBDLLHOOKSTRUCT.flags</c>) and mouse
/// (<c>MSLLHOOKSTRUCT.flags</c>). Used so remote-capture hooks never swallow
/// <see cref="IInputInjector"/> / SendInput events destined for the local desktop.
/// </summary>
public static class LowLevelInputFlags
{
    /// <summary>Test-and-consume extended key flag (keyboard).</summary>
    public const uint LLKHF_EXTENDED = 0x01;

    /// <summary>Injected by another process with a lower integrity level (keyboard).</summary>
    public const uint LLKHF_LOWER_IL_INJECTED = 0x02;

    /// <summary>Injected by SendInput or similar (keyboard).</summary>
    public const uint LLKHF_INJECTED = 0x10;

    /// <summary>Injected by SendInput or similar (mouse).</summary>
    public const uint LLMHF_INJECTED = 0x01;

    /// <summary>Injected by another process with a lower integrity level (mouse).</summary>
    public const uint LLMHF_LOWER_IL_INJECTED = 0x02;

    /// <summary>True when the keyboard LL event was synthesized (SendInput / lower-IL inject).</summary>
    public static bool IsInjectedKeyboard(uint flags)
        => (flags & (LLKHF_INJECTED | LLKHF_LOWER_IL_INJECTED)) != 0;

    /// <summary>True when the mouse LL event was synthesized (SendInput / lower-IL inject).</summary>
    public static bool IsInjectedMouse(uint flags)
        => (flags & (LLMHF_INJECTED | LLMHF_LOWER_IL_INJECTED)) != 0;

    /// <summary>
    /// Whether a remote-input forwarder should consume (eat) this LL event for network forward.
    /// Injected events always pass through so the service→agent software injection path can
    /// drive the local desktop even if capture hooks are installed.
    /// </summary>
    /// <param name="captureActive">True when the forwarder is actively capturing for remote control.</param>
    /// <param name="flags">Hook struct flags.</param>
    /// <param name="isMouse">True for mouse hook; false for keyboard.</param>
    /// <returns>True to consume/forward; false to call the next hook (local delivery).</returns>
    public static bool ShouldConsumeForRemoteForward(bool captureActive, uint flags, bool isMouse)
    {
        if (!captureActive)
        {
            return false;
        }

        return isMouse ? !IsInjectedMouse(flags) : !IsInjectedKeyboard(flags);
    }
}
