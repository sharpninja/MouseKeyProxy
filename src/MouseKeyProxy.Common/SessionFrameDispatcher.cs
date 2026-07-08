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
    private readonly IClipboardAccessor? _clipboard;
    private readonly object _clipGate = new();
    private IReadOnlyList<ClipboardEntry> _clipHistory = Array.Empty<ClipboardEntry>();
    private ulong _lastClipSeq;

    public SessionFrameDispatcher(IInputInjector? injector, ToggleStateMachine toggle, IClipboardAccessor? clipboard = null)
    {
        _injector = injector;
        _toggle = toggle ?? throw new ArgumentNullException(nameof(toggle));
        _clipboard = clipboard;
    }

    /// <summary>The current merged clipboard history (newest first).</summary>
    public IReadOnlyList<ClipboardEntry> ClipboardHistory
    {
        get { lock (_clipGate) { return _clipHistory; } }
    }

    /// <summary>
    /// FR-MKP-004 / TR-MKP-CLIP-001: merges a received clipboard entry into the LIFO history (sequence
    /// ordering + size/cap limits) and, when accepted, sets it on the local clipboard accessor.
    /// </summary>
    /// <param name="entry">The received clipboard entry.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>A completed task.</returns>
    public Task HandleClipboardAsync(ClipboardEntry entry, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(entry);

        bool accepted;
        lock (_clipGate)
        {
            var result = ClipboardLifoMerger.Merge(_clipHistory, entry, skipPrivacy: true, lastSeq: _lastClipSeq);
            accepted = result.Changed;
            if (accepted)
            {
                _clipHistory = result.History;
                if (entry.Seq > _lastClipSeq)
                {
                    _lastClipSeq = entry.Seq;
                }
            }
        }

        if (accepted)
        {
            _clipboard?.SetClipboard(entry);
        }

        return Task.CompletedTask;
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