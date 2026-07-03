namespace MouseKeyProxy.Common;

public interface IHotkeyMonitor
{
    event EventHandler<HotkeyEventArgs> HotkeyPressed;
    void Start();
    void Stop();
}

public interface IInputInjector
{
    void Inject(InputEvent ev);
}

public interface ICursorClip
{
    void ClipToRect(System.Drawing.Rectangle rect);
    void Release();
}

public class HotkeyEventArgs : EventArgs
{
    public bool IsLocal { get; set; }
    public int Key { get; set; }
}

public enum HotkeyTransition { None, Activated, Deactivated }
public enum ScreenEdge { None }
public enum OwnedCapability { Input, Hooks, Clip, Send }
