using System;
using System.Collections.Generic;
using System.Linq;

namespace MouseKeyProxy.Common;

public static class ModifierReleasePolicy
{
    public const uint VK_SHIFT = 0x10;
    public const uint VK_CONTROL = 0x11;
    public const uint VK_MENU = 0x12;
    public const uint VK_LSHIFT = 0xA0;
    public const uint VK_RSHIFT = 0xA1;
    public const uint VK_LCONTROL = 0xA2;
    public const uint VK_RCONTROL = 0xA3;
    public const uint VK_LMENU = 0xA4;
    public const uint VK_RMENU = 0xA5;
    public const uint VK_LWIN = 0x5B;
    public const uint VK_RWIN = 0x5C;

    public static readonly IReadOnlyList<uint> ModifierVirtualKeys = new[]
    {
        VK_SHIFT,
        VK_LSHIFT,
        VK_RSHIFT,
        VK_CONTROL,
        VK_LCONTROL,
        VK_RCONTROL,
        VK_MENU,
        VK_LMENU,
        VK_RMENU,
        VK_LWIN,
        VK_RWIN
    };

    public static IReadOnlyList<InputEvent> CreateKeyUpEvents(ulong? tsMs = null)
    {
        var timestamp = tsMs ?? NowMs();
        return ModifierVirtualKeys
            .Select(vk => new InputEvent(InputKind.KEY_UP, Vk: vk, TsMs: timestamp))
            .ToArray();
    }

    public static IReadOnlyList<InputEvent> CreateKeyUpEvents(IEnumerable<uint> virtualKeys, ulong? tsMs = null)
    {
        ArgumentNullException.ThrowIfNull(virtualKeys);
        var timestamp = tsMs ?? NowMs();
        return virtualKeys
            .Where(IsModifier)
            .Distinct()
            .Select(vk => new InputEvent(InputKind.KEY_UP, Vk: vk, TsMs: timestamp))
            .ToArray();
    }

    public static bool IsModifier(uint vk) => ModifierVirtualKeys.Contains(vk);

    private static ulong NowMs() => (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}