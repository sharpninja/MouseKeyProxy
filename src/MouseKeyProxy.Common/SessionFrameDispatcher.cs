using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MouseKeyProxy.Common;

/// <summary>
/// Pure dispatcher for received frames (AC4/AC5).
/// Calls IInputInjector for complete batches and converts modifier resync into real key-up events.
/// </summary>
public class SessionFrameDispatcher
{
    private readonly IInputInjector? _injector;
    private readonly ToggleStateMachine _toggle;

    public SessionFrameDispatcher(IInputInjector? injector, ToggleStateMachine toggle)
    {
        _injector = injector;
        _toggle = toggle ?? throw new ArgumentNullException(nameof(toggle));
    }

    public Task HandleInputBatchAsync(IEnumerable<InputEvent> events, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (_injector is null)
        {
            return Task.CompletedTask;
        }

        var batch = events.ToArray();
        if (batch.Length == 0)
        {
            return Task.CompletedTask;
        }

        if (!_injector.TryInjectBatch(batch, out var error))
        {
            throw new InvalidOperationException(error ?? "Input batch injection failed.");
        }

        return Task.CompletedTask;
    }

    public Task HandleModifierResyncAsync(IEnumerable<uint> ups, CancellationToken ct = default)
    {
        var releases = ModifierReleasePolicy.CreateKeyUpEvents(ups).ToArray();
        return HandleInputBatchAsync(releases, ct);
    }

    public Task ClearModifiersAsync(CancellationToken ct = default)
    {
        return HandleInputBatchAsync(ModifierReleasePolicy.CreateKeyUpEvents(), ct);
    }

    public (bool NewActive, bool EmitModResync) HandleToggle(string peer)
    {
        var res = _toggle.ApplyToggle(peer);
        return (res.NewActive, res.EmitModResync);
    }
}