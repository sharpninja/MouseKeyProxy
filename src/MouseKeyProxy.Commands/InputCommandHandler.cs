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
        if (PiHidClient.IsPiHidBackendEnabled())
        {
            await SendViaPiHidBackendAsync(kind, text, ct).ConfigureAwait(false);
            return;
        }

        var evt = new InputEvent(kind, Text: text, TsMs: (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        await transport.SendInputBatchAsync(new[] { evt }, ct);
    }

    private static async Task SendViaPiHidBackendAsync(InputKind kind, string? text, CancellationToken ct)
    {
        if (kind != InputKind.TEXT_INPUT || !PiHidReports.TryParseChord(text, out var chord))
        {
            throw new NotSupportedException("MKP_INPUT_BACKEND=pi-hid supports text input only when it names a HID test chord such as alt+space, win+left, or win+right.");
        }

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var client = new PiHidClient(http, PiHidClientOptions.FromEnvironment());
        var results = await client.TestChordAsync(chord, ct).ConfigureAwait(false);
        var failed = results.FirstOrDefault(static r => !r.Ok);
        if (failed != null)
        {
            throw new InvalidOperationException($"Pi HID backend rejected {chord}: HTTP {failed.StatusCodeValue} {failed.Body}");
        }
    }

    /// <summary>
    /// Example toggle handler: applies local state + sends control frame via bidi (mod resync etc).
    /// </summary>
    public static async Task<bool> ToggleAsync(ToggleStateMachine state, BidiSessionTransport? transport, string peer, CancellationToken ct = default)
    {
        var res = state.ApplyToggle(peer);
        if (transport != null)
        {
            await transport.SendToggleAsync(res.NewActive, ct);
            if (res.EmitModResync)
            {
                await transport.SendModifierResyncAsync(ModifierReleasePolicy.ModifierVirtualKeys, ct);
            }
        }
        return res.NewActive;
    }
}
