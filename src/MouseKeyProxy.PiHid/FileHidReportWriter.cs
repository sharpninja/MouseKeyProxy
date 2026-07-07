namespace MouseKeyProxy.PiHid;

public sealed class FileHidReportWriter : IHidReportWriter
{
    public FileHidReportWriter(string keyboardDevice, string mouseDevice)
    {
        KeyboardDevice = keyboardDevice;
        MouseDevice = mouseDevice;
    }

    public string KeyboardDevice { get; }
    public string MouseDevice { get; }

    public async Task WriteKeyboardAsync(byte[] report, CancellationToken cancellationToken = default)
    {
        await WriteDeviceAsync(KeyboardDevice, report, cancellationToken).ConfigureAwait(false);
    }

    public async Task WriteMouseAsync(byte[] report, CancellationToken cancellationToken = default)
    {
        await WriteDeviceAsync(MouseDevice, report, cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteDeviceAsync(string path, byte[] report, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
        await stream.WriteAsync(report, cancellationToken).ConfigureAwait(false);
    }
}
