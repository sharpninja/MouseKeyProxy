namespace MouseKeyProxy.Commands;

public enum PiHidChord
{
    AltSpace,
    WinLeft,
    WinRight,
}

public static class PiHidReports
{
    public const int KeyboardReportLength = 8;
    public const int MouseReportLength = 4;

    private const byte ModifierLeftAlt = 0x04;
    private const byte ModifierLeftGui = 0x08;
    private const byte KeySpace = 0x2c;
    private const byte KeyRightArrow = 0x4f;
    private const byte KeyLeftArrow = 0x50;

    public static bool TryParseChord(string? value, out PiHidChord chord)
    {
        var normalized = (value ?? string.Empty)
            .Trim()
            .Replace("-", "+", StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();

        switch (normalized)
        {
            case "alt+space":
                chord = PiHidChord.AltSpace;
                return true;
            case "win+left":
            case "windows+left":
            case "gui+left":
                chord = PiHidChord.WinLeft;
                return true;
            case "win+right":
            case "windows+right":
            case "gui+right":
                chord = PiHidChord.WinRight;
                return true;
            default:
                chord = default;
                return false;
        }
    }

    public static IReadOnlyList<byte[]> CreateKeyboardChordReports(PiHidChord chord)
    {
        return chord switch
        {
            PiHidChord.AltSpace => Chord(ModifierLeftAlt, KeySpace),
            PiHidChord.WinLeft => Chord(ModifierLeftGui, KeyLeftArrow),
            PiHidChord.WinRight => Chord(ModifierLeftGui, KeyRightArrow),
            _ => throw new ArgumentOutOfRangeException(nameof(chord), chord, "Unsupported HID chord."),
        };
    }

    public static IReadOnlyList<byte[]> CreateClearKeyboardReports() => new[] { Keyboard(0, 0) };

    public static IReadOnlyList<byte[]> CreateClearMouseReports() => new[] { Mouse(0, 0, 0, 0) };

    public static IReadOnlyList<byte[]> CreateRelativeMouseReports(int dx, int dy, int wheel = 0, byte buttons = 0)
    {
        var reports = new List<byte[]>();
        do
        {
            var x = TakeChunk(ref dx);
            var y = TakeChunk(ref dy);
            var w = TakeChunk(ref wheel);
            reports.Add(Mouse(buttons, x, y, w));
        }
        while (dx != 0 || dy != 0 || wheel != 0);

        return reports;
    }

    private static IReadOnlyList<byte[]> Chord(byte modifier, byte key)
    {
        return new[]
        {
            Keyboard(modifier, 0),
            Keyboard(modifier, key),
            Keyboard(modifier, 0),
            Keyboard(0, 0),
        };
    }

    private static byte[] Keyboard(byte modifier, byte key)
    {
        return new[] { modifier, (byte)0, key, (byte)0, (byte)0, (byte)0, (byte)0, (byte)0 };
    }

    private static byte[] Mouse(byte buttons, int dx, int dy, int wheel)
    {
        return new[] { buttons, SignedByte(dx), SignedByte(dy), SignedByte(wheel) };
    }

    private static int TakeChunk(ref int value)
    {
        var chunk = Math.Clamp(value, -127, 127);
        value -= chunk;
        return chunk;
    }

    private static byte SignedByte(int value)
    {
        if (value < -127 || value > 127)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "HID relative report values must fit in signed 8-bit boot mouse range.");
        }

        return unchecked((byte)(sbyte)value);
    }
}
