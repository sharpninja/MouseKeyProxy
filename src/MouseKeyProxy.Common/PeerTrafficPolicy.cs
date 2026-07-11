using System;

namespace MouseKeyProxy.Common;

/// <summary>
/// FR-MKP-027: host-side role of a remote peer for traffic policy.
/// Keyboard/mouse go only to the device appliance; clipboard goes only to the USB client Agent path.
/// </summary>
public enum PeerEffectRole
{
    /// <summary>Unknown / legacy peer (conservative: allow nothing new).</summary>
    Unknown = 0,

    /// <summary>Pi / HID appliance: keyboard and mouse input only from host.</summary>
    DeviceAppliance = 1,

    /// <summary>USB client paired for clipboard sync only (not kb/mouse inject target).</summary>
    ClipboardClient = 2,
}

/// <summary>
/// FR-MKP-027: pure policy for which effect types the host Agent may send to a peer role.
/// </summary>
public static class PeerTrafficPolicy
{
    /// <summary>Effect categories the host may emit.</summary>
    public enum EffectKind
    {
        /// <summary>Keyboard / mouse / inject / focus / mouse position.</summary>
        Input = 1,

        /// <summary>Clipboard LIFO push/pull.</summary>
        Clipboard = 2,
    }

    /// <summary>
    /// Returns true when the host may send <paramref name="effect"/> to a peer with <paramref name="role"/>.
    /// </summary>
    /// <param name="role">Remote peer role.</param>
    /// <param name="effect">Effect kind.</param>
    /// <returns>True when allowed.</returns>
    public static bool Allows(PeerEffectRole role, EffectKind effect)
    {
        return role switch
        {
            PeerEffectRole.DeviceAppliance => effect == EffectKind.Input,
            PeerEffectRole.ClipboardClient => effect == EffectKind.Clipboard,
            _ => false,
        };
    }

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> when the host must not send the effect.
    /// </summary>
    /// <param name="role">Remote peer role.</param>
    /// <param name="effect">Effect kind.</param>
    /// <param name="peerId">Peer id for the error message.</param>
    public static void EnsureAllowed(PeerEffectRole role, EffectKind effect, string? peerId = null)
    {
        if (Allows(role, effect))
        {
            return;
        }

        var who = string.IsNullOrWhiteSpace(peerId) ? role.ToString() : $"{peerId} ({role})";
        throw new InvalidOperationException(
            $"Host traffic policy rejects {effect} to peer {who}. " +
            "Keyboard/mouse go to DeviceAppliance only; clipboard goes to ClipboardClient only.");
    }
}
