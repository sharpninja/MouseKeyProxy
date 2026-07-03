using System;

namespace MouseKeyProxy.Common;

/// <summary>
/// Pure, platform-agnostic support matrix per locked spec in PLAN-MKP-004.
/// No deviation from: ordinary keys, modifiers, media, text, mouse supported; SAS/secure/UIPI explicitly fail observably (never hang/claim success).
/// </summary>
public static class InputSupportMatrix
{
    public static bool IsSupported(InputKind kind, uint vk = 0, bool isSecureDesktop = false)
    {
        if (isSecureDesktop)
            return false;

        return kind switch
        {
            InputKind.KEY_DOWN or InputKind.KEY_UP => !IsSasLike(vk),
            InputKind.MOUSE_MOVE or InputKind.MOUSE_DOWN or InputKind.MOUSE_UP => true,
            InputKind.MOUSE_WHEEL or InputKind.MOUSE_HWHEEL or InputKind.MOUSE_XBUTTON => true,
            InputKind.TEXT_INPUT => true,
            _ => false,
        };
    }

    private static bool IsSasLike(uint vk)
    {
        // Conservative: Del key in context of SAS is rejected at matrix level (full chord in hook state machine per plan)
        const uint VK_DELETE = 0x2E;
        return vk == VK_DELETE;
    }

    public static string GetFailureReason(InputKind kind, uint vk = 0, bool isSecureDesktop = false)
    {
        if (isSecureDesktop) return "SECURE_DESKTOP";
        if ((kind == InputKind.KEY_DOWN || kind == InputKind.KEY_UP) && IsSasLike(vk)) return "SAS_BLOCKED";
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
