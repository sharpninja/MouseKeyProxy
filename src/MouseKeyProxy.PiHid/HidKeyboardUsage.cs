namespace MouseKeyProxy.PiHid;

/// <summary>
/// FR-MKP-012 / TR-MKP-HID-001: maps Windows virtual-key codes (and characters) to USB HID Boot
/// Keyboard usage codes (USB HID Usage Tables, section 10). Covers the common set: letters, digits,
/// whitespace/editing keys, arrows, and F1-F12. Unknown keys return false so the encoder can reject
/// them rather than injecting the wrong key.
/// </summary>
public static class HidKeyboardUsage
{
    /// <summary>Maps a virtual key to its HID usage code.</summary>
    /// <param name="vk">The Windows virtual-key code.</param>
    /// <param name="usage">The HID usage code when mapped.</param>
    /// <returns>True when a mapping exists.</returns>
    public static bool TryMap(uint vk, out byte usage)
    {
        // Letters A-Z (VK 0x41-0x5A) -> HID 0x04-0x1D.
        if (vk >= 0x41 && vk <= 0x5A)
        {
            usage = (byte)(0x04 + (vk - 0x41));
            return true;
        }

        // Digits 1-9 (VK 0x31-0x39) -> HID 0x1E-0x26; 0 (VK 0x30) -> 0x27.
        if (vk >= 0x31 && vk <= 0x39)
        {
            usage = (byte)(0x1E + (vk - 0x31));
            return true;
        }

        // F1-F12 (VK 0x70-0x7B) -> HID 0x3A-0x45.
        if (vk >= 0x70 && vk <= 0x7B)
        {
            usage = (byte)(0x3A + (vk - 0x70));
            return true;
        }

        usage = vk switch
        {
            0x30 => 0x27, // 0
            0x0D => 0x28, // Enter
            0x1B => 0x29, // Esc
            0x08 => 0x2A, // Backspace
            0x09 => 0x2B, // Tab
            0x20 => 0x2C, // Space
            0x2D => 0x49, // Insert
            0x24 => 0x4A, // Home
            0x21 => 0x4B, // PageUp
            0x2E => 0x4C, // Delete
            0x23 => 0x4D, // End
            0x22 => 0x4E, // PageDown
            0x27 => 0x4F, // Right
            0x25 => 0x50, // Left
            0x28 => 0x51, // Down
            0x26 => 0x52, // Up
            _ => 0x00,
        };

        return usage != 0x00;
    }

    /// <summary>Maps a character to its HID usage and whether Shift is required.</summary>
    /// <param name="c">The character.</param>
    /// <param name="usage">The HID usage code when mapped.</param>
    /// <param name="needsShift">True when Shift must be held.</param>
    /// <returns>True when a mapping exists.</returns>
    public static bool TryMapChar(char c, out byte usage, out bool needsShift)
    {
        needsShift = false;

        if (c >= 'a' && c <= 'z')
        {
            usage = (byte)(0x04 + (c - 'a'));
            return true;
        }

        if (c >= 'A' && c <= 'Z')
        {
            usage = (byte)(0x04 + (c - 'A'));
            needsShift = true;
            return true;
        }

        if (c >= '1' && c <= '9')
        {
            usage = (byte)(0x1E + (c - '1'));
            return true;
        }

        usage = c switch
        {
            '0' => 0x27,
            ' ' => 0x2C,
            '\n' => 0x28,
            '\r' => 0x28,
            '\t' => 0x2B,
            _ => 0x00,
        };

        return usage != 0x00;
    }
}
