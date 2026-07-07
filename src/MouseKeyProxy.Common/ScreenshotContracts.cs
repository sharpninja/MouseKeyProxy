using System;

namespace MouseKeyProxy.Common;

public enum ScreenshotTarget
{
    Desktop = 0,
    Foreground = 1,
    Hwnd = 2
}

public sealed record ScreenshotCaptureRequest(
    ScreenshotTarget Target,
    ulong Hwnd,
    string CorrelationId,
    bool IncludeCursor = true);

public sealed record ScreenshotMetadata(
    DateTimeOffset CapturedAtUtc,
    string SourceHost,
    string CorrelationId,
    ScreenshotTarget Target,
    ulong Hwnd,
    int Width,
    int Height,
    string Sha256);

public sealed record ScreenshotCaptureResult(ScreenshotMetadata Metadata, byte[] Png);