namespace MouseKeyProxy.PiHid;

public interface IHidReportWriter
{
    string KeyboardDevice { get; }
    string MouseDevice { get; }

    Task WriteKeyboardAsync(byte[] report, CancellationToken cancellationToken = default);
    Task WriteMouseAsync(byte[] report, CancellationToken cancellationToken = default);
}
