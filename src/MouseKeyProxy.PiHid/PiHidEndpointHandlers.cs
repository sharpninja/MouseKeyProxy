using System.Net;
using Microsoft.Extensions.Primitives;

namespace MouseKeyProxy.PiHid;

public static class PiHidEndpointHandlers
{
    public static PiHidResponse Status(StringValues authorization, PiHidOptions options, IHidReportWriter writer)
    {
        if (!IsAuthorized(authorization, options))
        {
            return Unauthorized();
        }

        var body = string.Join('\n',
            "MouseKeyProxy Pi HID service",
            "runtime=dotnet-10",
            "rid=linux-arm64",
            $"keyboardDevice={writer.KeyboardDevice}",
            $"mouseDevice={writer.MouseDevice}",
            $"capturedAtUtc={DateTimeOffset.UtcNow:O}");
        return new PiHidResponse((int)HttpStatusCode.OK, body);
    }

    public static async Task<PiHidResponse> KeyboardReportAsync(StringValues authorization, Stream body, PiHidOptions options, IHidReportWriter writer, CancellationToken cancellationToken = default)
    {
        if (!IsAuthorized(authorization, options))
        {
            return Unauthorized();
        }

        var report = await ReadReportAsync(body, HidReportConstants.KeyboardReportLength, cancellationToken).ConfigureAwait(false);
        if (report is null)
        {
            return BadRequest($"keyboard report must be exactly {HidReportConstants.KeyboardReportLength} bytes");
        }

        await writer.WriteKeyboardAsync(report, cancellationToken).ConfigureAwait(false);
        return new PiHidResponse((int)HttpStatusCode.OK, "keyboard report written");
    }

    public static async Task<PiHidResponse> MouseReportAsync(StringValues authorization, Stream body, PiHidOptions options, IHidReportWriter writer, CancellationToken cancellationToken = default)
    {
        if (!IsAuthorized(authorization, options))
        {
            return Unauthorized();
        }

        var report = await ReadReportAsync(body, HidReportConstants.MouseReportLength, cancellationToken).ConfigureAwait(false);
        if (report is null)
        {
            return BadRequest($"mouse report must be exactly {HidReportConstants.MouseReportLength} bytes");
        }

        await writer.WriteMouseAsync(report, cancellationToken).ConfigureAwait(false);
        return new PiHidResponse((int)HttpStatusCode.OK, "mouse report written");
    }

    public static async Task<PiHidResponse> ClearModifiersAsync(StringValues authorization, PiHidOptions options, IHidReportWriter writer, CancellationToken cancellationToken = default)
    {
        if (!IsAuthorized(authorization, options))
        {
            return Unauthorized();
        }

        await WriteNeutralReportsAsync(writer, cancellationToken).ConfigureAwait(false);
        return new PiHidResponse((int)HttpStatusCode.OK, "neutral keyboard and mouse reports written");
    }

    public static async Task<PiHidResponse> ResetAsync(StringValues authorization, PiHidOptions options, IHidReportWriter writer, CancellationToken cancellationToken = default)
    {
        if (!IsAuthorized(authorization, options))
        {
            return Unauthorized();
        }

        await WriteNeutralReportsAsync(writer, cancellationToken).ConfigureAwait(false);
        return new PiHidResponse((int)HttpStatusCode.OK, "hid reset complete");
    }

    private static async Task WriteNeutralReportsAsync(IHidReportWriter writer, CancellationToken cancellationToken)
    {
        await writer.WriteKeyboardAsync(HidReportConstants.KeyboardNeutralReport, cancellationToken).ConfigureAwait(false);
        await writer.WriteMouseAsync(HidReportConstants.MouseNeutralReport, cancellationToken).ConfigureAwait(false);
    }

    private static bool IsAuthorized(StringValues authorization, PiHidOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Token))
        {
            return false;
        }

        return string.Equals(authorization.ToString(), $"Bearer {options.Token}", StringComparison.Ordinal);
    }

    private static async Task<byte[]?> ReadReportAsync(Stream body, int expectedLength, CancellationToken cancellationToken)
    {
        using var memory = new MemoryStream();
        await body.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);
        var report = memory.ToArray();
        return report.Length == expectedLength ? report : null;
    }

    private static PiHidResponse Unauthorized() => new((int)HttpStatusCode.Unauthorized, "missing or invalid bearer token");

    private static PiHidResponse BadRequest(string message) => new((int)HttpStatusCode.BadRequest, message);
}
