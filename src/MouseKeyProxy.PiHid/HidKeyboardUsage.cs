namespace MouseKeyProxy.PiHid;

/// <summary>
/// FR-MKP-012 / TR-MKP-HID-001: maps Windows virtual-key codes (and characters) to USB HID Boot
/// Keyboard usage codes (USB HID Usage Tables, Keyboard/Keypad Page 0x07). Covers letters, digits,
/// whitespace/editing keys, arrows, F1-F12, and US OEM punctuation (comma, period, brackets, etc.).
/// Unknown keys return false so the encoder can reject them rather than injecting the wrong key.
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

        // Numpad 0-9 (VK 0x60-0x69) -> HID keypad 0x62, 0x59-0x61.
        if (vk >= 0x60 && vk <= 0x69)
        {
            usage = vk == 0x60
                ? (byte)0x62
                : (byte)(0x59 + (vk - 0x61));
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
            0x14 => 0x39, // Caps Lock
            0x2C => 0x46, // Print Screen (VK_SNAPSHOT)
            0x91 => 0x47, // Scroll Lock
            0x13 => 0x48, // Pause
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
            0x90 => 0x53, // Num Lock

            // US OEM punctuation (physical keys; shift produces the shifted glyph on the host layout).
            0xBD => 0x2D, // VK_OEM_MINUS  - _
            0xBB => 0x2E, // VK_OEM_PLUS   = +
            0xDB => 0x2F, // VK_OEM_4      [ {
            0xDD => 0x30, // VK_OEM_6      ] }
            0xDC => 0x31, // VK_OEM_5      \ |
            0xBA => 0x33, // VK_OEM_1      ; :
            0xDE => 0x34, // VK_OEM_7      ' "
            0xC0 => 0x35, // VK_OEM_3      ` ~
            0xBC => 0x36, // VK_OEM_COMMA  , <
            0xBE => 0x37, // VK_OEM_PERIOD . >
            0xBF => 0x38, // VK_OEM_2      / ?
            0xE2 => 0x64, // VK_OEM_102    Non-US \ |

            // Numpad operators.
            0x6A => 0x55, // VK_MULTIPLY
            0x6B => 0x57, // VK_ADD
            0x6D => 0x56, // VK_SUBTRACT
            0x6E => 0x63, // VK_DECIMAL
            0x6F => 0x54, // VK_DIVIDE

            _ => 0x00,
        };

        return usage != 0x00;
    }

    /// <summary>Maps a character to its HID usage and whether Shift is required (US QWERTY).</summary>
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

        // Unshifted printable / whitespace.
        usage = c switch
        {
            '0' => 0x27,
            ' ' => 0x2C,
            '\n' => 0x28,
            '\r' => 0x28,
            '\t' => 0x2B,
            '-' => 0x2D,
            '=' => 0x2E,
            '[' => 0x2F,
            ']' => 0x30,
            '\\' => 0x31,
            ';' => 0x33,
            '\'' => 0x34,
            '`' => 0x35,
            ',' => 0x36,
            '.' => 0x37,
            '/' => 0x38,
            _ => 0x00,
        };
        if (usage != 0x00)
        {
            return true;
        }

        // Shifted symbols (US QWERTY).
        needsShift = true;
        usage = c switch
        {
            '!' => 0x1E, // Shift+1
            '@' => 0x1F, // Shift+2
            '#' => 0x20, // Shift+3
            '$' => 0x21, // Shift+4
            '%' => 0x22, // Shift+5
            '^' => 0x23, // Shift+6
            '&' => 0x24, // Shift+7
            '*' => 0x25, // Shift+8
            '(' => 0x26, // Shift+9
            ')' => 0x27, // Shift+0
            '_' => 0x2D, // Shift+-
            '+' => 0x2E, // Shift+=
            '{' => 0x2F, // Shift+[
            '}' => 0x30, // Shift+]
            '|' => 0x31, // Shift+\
            ':' => 0x33, // Shift+;
            '"' => 0x34, // Shift+'
            '~' => 0x35, // Shift+`
            '<' => 0x36, // Shift+,
            '>' => 0x37, // Shift+.
            '?' => 0x38, // Shift+/
            _ => 0x00,
        };

        if (usage == 0x00)
        {
            needsShift = false;
            return false;
        }

        return true;
    }
}
