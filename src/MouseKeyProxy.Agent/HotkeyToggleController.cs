using MouseKeyProxy.Common;

namespace MouseKeyProxy.Agent;

public class HotkeyToggleController
{
    private readonly IHotkeyMonitor _monitor;
    private readonly IInputInjector _injector;
    private readonly ICursorClip _clip;

    public HotkeyToggleController(IHotkeyMonitor monitor, IInputInjector injector, ICursorClip clip)
    {
        _monitor = monitor;
        _injector = injector;
        _clip = clip;
    }

    // wiring etc.
}
