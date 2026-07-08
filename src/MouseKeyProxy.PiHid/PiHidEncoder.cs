using System;
using System.Collections.Generic;
using MouseKeyProxy.Common;

namespace MouseKeyProxy.PiHid;

/// <summary>The HID gadget device a report targets.</summary>
public enum HidDevice
{
    /// <summary>The boot-protocol keyboard gadget (/dev/hidg0).</summary>
    Keyboard,

    /// <summary>The boot-protocol mouse gadget (/dev/hidg1).</summary>
    Mouse,
}

/// <summary>A single HID report destined for a gadget device.</summary>
/// <param name="Device">The target device.</param>
/// <param name="Bytes">The raw report bytes (8 for keyboard, 4 for mouse).</param>
public readonly record struct HidReport(HidDevice Device, byte[] Bytes);

/// <summary>
/// FR-MKP-012 / TR-MKP-HID-001: encodes arbitrary <see cref="InputEvent"/> batches into USB boot-protocol
/// HID keyboard/mouse reports. Stateful across events (tracks held keys, modifiers, and mouse buttons)
/// so chords and drags produce the correct report sequence - not the old 3-hardcoded-chord backend.
/// </summary>
public sealed class PiHidEncoder
{
    // HID modifier byte bits.
    private const byte ModLCtrl = 0x01, ModLShift = 0x02, ModLAlt = 0x04, ModLGui = 0x08;
    private const byte ModRCtrl = 0x10, ModRShift = 0x20, ModRAlt = 0x40, ModRGui = 0x80;

    private readonly List<byte> _keys = new(6);
    private byte _modifiers;
    private byte _mouseButtons;

    /// <summary>Encodes a batch of input events into HID reports.</summary>
    /// <param name="events">The events to encode.</param>
    /// <param name="error">Set to a message when an event is unsupported (encoding stops).</param>
    /// <returns>The reports to write, in order (empty when the first event errored).</returns>
    public IReadOnlyList<HidReport> Encode(IEnumerable<InputEvent> events, out string? error)
    {
        error = null;
        var reports = new List<HidReport>();

        foreach (var e in events)
        {
            switch (e.Kind)
            {
                case InputKind.KEY_DOWN:
                case InputKind.KEY_UP:
                    if (!EncodeKey(e, reports, out error))
                    {
                        return Array.Empty<HidReport>();
                    }

                    break;

                case InputKind.TEXT_INPUT:
                    if (!EncodeText(e.Text, reports, out error))
                    {
                        return Array.Empty<HidReport>();
                    }

                    break;

                case InputKind.MOUSE_MOVE:
                    EncodeMouseMove(e.Dx, e.Dy, reports);
                    break;

                case InputKind.MOUSE_DOWN:
                case InputKind.MOUSE_UP:
                    EncodeMouseButton(e, reports);
                    break;

                case InputKind.MOUSE_WHEEL:
                    EncodeWheel(e.WheelDelta, reports);
                    break;

                default:
                    error = $"unsupported input kind for HID gadget: {e.Kind}";
                    return Array.Empty<HidReport>();
            }
        }

        return reports;
    }

    private bool EncodeKey(InputEvent e, List<HidReport> reports, out string? error)
    {
        error = null;
        var down = e.Kind == InputKind.KEY_DOWN;

        if (TryModifierBit(e.Vk, out var bit))
        {
            if (down)
            {
                _modifiers |= bit;
            }
            else
            {
                _modifiers = (byte)(_modifiers & ~bit);
            }

            reports.Add(KeyboardReport());
            return true;
        }

        if (!HidKeyboardUsage.TryMap(e.Vk, out var usage))
        {
            error = $"unsupported virtual key for HID gadget: 0x{e.Vk:X2}";
            return false;
        }

        if (down)
        {
            if (!_keys.Contains(usage))
            {
                if (_keys.Count >= 6)
                {
                    error = "HID keyboard rollover: more than 6 non-modifier keys held";
                    return false;
                }

                _keys.Add(usage);
            }
        }
        else
        {
            _keys.Remove(usage);
        }

        reports.Add(KeyboardReport());
        return true;
    }

    private bool EncodeText(string? text, List<HidReport> reports, out string? error)
    {
        error = null;
        if (string.IsNullOrEmpty(text))
        {
            return true;
        }

        foreach (var ch in text)
        {
            if (!HidKeyboardUsage.TryMapChar(ch, out var usage, out var needsShift))
            {
                error = $"unsupported character for HID gadget: '{ch}'";
                return false;
            }

            var mods = needsShift ? (byte)(_modifiers | ModLShift) : _modifiers;
            reports.Add(new HidReport(HidDevice.Keyboard, KeyboardBytes(mods, usage)));
            reports.Add(new HidReport(HidDevice.Keyboard, KeyboardBytes(_modifiers, 0))); // release
        }

        return true;
    }

    private void EncodeMouseMove(int dx, int dy, List<HidReport> reports)
    {
        while (dx != 0 || dy != 0)
        {
            var stepX = Math.Clamp(dx, -127, 127);
            var stepY = Math.Clamp(dy, -127, 127);
            reports.Add(new HidReport(HidDevice.Mouse, new byte[] { _mouseButtons, (byte)(sbyte)stepX, (byte)(sbyte)stepY, 0 }));
            dx -= stepX;
            dy -= stepY;
        }
    }

    private void EncodeMouseButton(InputEvent e, List<HidReport> reports)
    {
        byte bit = e.XButton switch
        {
            1 => 0x08,
            2 => 0x10,
            _ => e.Kind == InputKind.MOUSE_UP || e.Kind == InputKind.MOUSE_DOWN ? DefaultButtonBit(e) : (byte)0x01,
        };

        if (e.Kind == InputKind.MOUSE_DOWN)
        {
            _mouseButtons |= bit;
        }
        else
        {
            _mouseButtons = (byte)(_mouseButtons & ~bit);
        }

        reports.Add(new HidReport(HidDevice.Mouse, new byte[] { _mouseButtons, 0, 0, 0 }));
    }

    private static byte DefaultButtonBit(InputEvent e)
    {
        // Flags may encode right/middle; default to the left button when unspecified.
        return e.Flags switch
        {
            2 => 0x02, // right
            4 => 0x04, // middle
            _ => 0x01, // left
        };
    }

    private void EncodeWheel(int wheelDelta, List<HidReport> reports)
    {
        var step = Math.Clamp(wheelDelta / 120, -127, 127);
        if (step == 0)
        {
            step = Math.Sign(wheelDelta);
        }

        reports.Add(new HidReport(HidDevice.Mouse, new byte[] { _mouseButtons, 0, 0, (byte)(sbyte)step }));
    }

    private HidReport KeyboardReport() => new(HidDevice.Keyboard, KeyboardBytes(_modifiers, KeysSnapshot()));

    private static byte[] KeyboardBytes(byte modifiers, byte singleKey)
    {
        var report = new byte[8];
        report[0] = modifiers;
        report[2] = singleKey;
        return report;
    }

    private byte[] KeyboardBytes(byte modifiers, byte[] keys)
    {
        var report = new byte[8];
        report[0] = modifiers;
        for (int i = 0; i < keys.Length && i < 6; i++)
        {
            report[2 + i] = keys[i];
        }

        return report;
    }

    private byte[] KeysSnapshot() => _keys.ToArray();

    private static bool TryModifierBit(uint vk, out byte bit)
    {
        bit = vk switch
        {
            0x11 or 0xA2 => ModLCtrl,
            0xA3 => ModRCtrl,
            0x10 or 0xA0 => ModLShift,
            0xA1 => ModRShift,
            0x12 or 0xA4 => ModLAlt,
            0xA5 => ModRAlt,
            0x5B => ModLGui,
            0x5C => ModRGui,
            _ => 0,
        };
        return bit != 0;
    }
}
