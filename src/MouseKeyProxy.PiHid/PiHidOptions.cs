namespace MouseKeyProxy.PiHid;

public sealed record PiHidOptions(
    string Token,
    string KeyboardDevice,
    string MouseDevice,
    string BindUrl)
{
    public static PiHidOptions FromEnvironment()
    {
        return new PiHidOptions(
            Environment.GetEnvironmentVariable("MKP_HID_PI_TOKEN") ?? string.Empty,
            Environment.GetEnvironmentVariable("MKP_HID_KEYBOARD_DEVICE") ?? "/dev/hidg0",
            Environment.GetEnvironmentVariable("MKP_HID_MOUSE_DEVICE") ?? "/dev/hidg1",
            Environment.GetEnvironmentVariable("MKP_HID_BIND_URL") ?? "http://0.0.0.0:8765");
    }
}
