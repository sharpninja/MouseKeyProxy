using System;

namespace MouseKeyProxy.Common;

/// <summary>
/// Pure, platform-agnostic support matrix for proxied input.
/// Ordinary keys, modifiers, media, text, and mouse input are supported; secure desktop and
/// unsupported input kinds fail observably (never hang or claim success).
/// </summary>
public static class InputSupportMatrix
{
    public static bool IsSupported(InputKind kind, uint vk = 0, bool isSecureDesktop = false)
    {
        if (isSecureDesktop)
            return false;

        return kind switch
        {
            // A single DELETE event is ordinary input, not a Secure Attention Sequence. Windows
            // protects Ctrl+Alt+Delete before this user-mode path, and secure-desktop forwarding is
            // independently rejected above.
            InputKind.KEY_DOWN or InputKind.KEY_UP => true,
            InputKind.MOUSE_MOVE or InputKind.MOUSE_DOWN or InputKind.MOUSE_UP => true,
            InputKind.MOUSE_WHEEL or InputKind.MOUSE_HWHEEL or InputKind.MOUSE_XBUTTON => true,
            InputKind.TEXT_INPUT => true,
            _ => false,
        };
    }

    public static string GetFailureReason(InputKind kind, uint vk = 0, bool isSecureDesktop = false)
    {
        if (isSecureDesktop) return "SECURE_DESKTOP";
        if (kind == InputKind.UNSPECIFIED) return "UNSPECIFIED_KIND";
        return "UNSUPPORTED_KIND";
    }
}

public enum InputKind
{
    UNSPECIFIED = 0,
    KEY_DOWN = 1,
    KEY_UP = 2,
    MOUSE_MOVE = 3,
    MOUSE_DOWN = 4,
    MOUSE_UP = 5,
    MOUSE_WHEEL = 6,
    MOUSE_XBUTTON = 7,
    TEXT_INPUT = 8,
    MOUSE_HWHEEL = 9,
}
