using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MouseKeyProxy.Common;
using MouseKeyProxy.Commands; // Bidi now in shared Commands lib (per skeptic fix)

namespace MouseKeyProxy.Commands;

/// <summary>
/// Shared command handler (the "shared command implementation library" per plan).
/// Thin callers (Repl.Main, Agent tray) resolve deps and call these.
/// Uses BidiSessionTransport to serialize over real bidi OpenSession.
/// </summary>
public static class InputCommandHandler
{
    /// <summary>
    /// Sends a text or input batch over the bidi transport (constructs real SessionFrame/InputBatch).
    /// Real shipped path exercised by Repl Main and roundtrip tests.
    /// </summary>
    public static async Task SendInputAsync(BidiSessionTransport transport, InputKind kind, string? text = null, CancellationToken ct = default)
    {
        var evt = new InputEvent(kind, Text: text, TsMs: (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        await transport.SendInputBatchAsync(new[] { evt }, ct);
    }

    /// <summary>
    /// Example toggle handler: applies local state + sends control frame via bidi (mod resync etc).
    /// </summary>
    public static async Task<bool> ToggleAsync(ToggleStateMachine state, BidiSessionTransport? transport, string peer, CancellationToken ct = default)
    {
        var res = state.ApplyToggle(peer);
        if (transport != null)
        {
            // send control/mod resync frame (real SessionFrame) for toggle
            await transport.SendInputBatchAsync(Array.Empty<InputEvent>(), ct);
        }
        return res.NewActive;
    }
}
