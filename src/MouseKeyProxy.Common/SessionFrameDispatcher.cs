using System;
using System.Threading;
using System.Threading.Tasks;
using MouseKeyProxy.Common;

namespace MouseKeyProxy.Common;

/// <summary>
/// Pure dispatcher for received frames (AC4/AC5).
/// Calls IInputInjector for input batches; handles toggle for ModResync.
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

    public Task HandleInputBatchAsync(System.Collections.Generic.IEnumerable<InputEvent> events, CancellationToken ct = default)
    {
        if (_injector != null)
        {
            foreach (var e in events)
            {
                _injector.Send(e);
            }
        }
        return Task.CompletedTask;
    }

    public (bool NewActive, bool EmitModResync) HandleToggle(string peer)
    {
        var res = _toggle.ApplyToggle(peer);
        return (res.NewActive, res.EmitModResync);
    }
}