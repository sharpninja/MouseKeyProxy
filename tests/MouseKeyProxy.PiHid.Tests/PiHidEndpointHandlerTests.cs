using Microsoft.Extensions.Primitives;
using MouseKeyProxy.PiHid;

namespace MouseKeyProxy.PiHid.Tests;

public sealed class PiHidEndpointHandlerTests
{
    private static readonly PiHidOptions Options = new(
        "secret",
        "/tmp/hidg0",
        "/tmp/hidg1",
        "http://127.0.0.1:8765");

    [Fact]
    [Trait("Category", "HardwareHID")]
    public void Status_Requires_Bearer_Token()
    {
        var writer = new RecordingWriter();

        var missing = PiHidEndpointHandlers.Status(StringValues.Empty, Options, writer);
        var invalid = PiHidEndpointHandlers.Status("Bearer wrong", Options, writer);
        var ok = PiHidEndpointHandlers.Status("Bearer secret", Options, writer);

        Assert.Equal(401, missing.StatusCode);
        Assert.Equal(401, invalid.StatusCode);
        Assert.Equal(200, ok.StatusCode);
        Assert.Contains("runtime=dotnet-10", ok.Body);
        Assert.Contains("rid=linux-arm64", ok.Body);
    }

    [Fact]
    [Trait("Category", "HardwareHID")]
    public async Task Keyboard_Report_Rejects_Wrong_Length_And_Does_Not_Write()
    {
        var writer = new RecordingWriter();
        using var body = new MemoryStream(new byte[] { 1, 2, 3 });

        var result = await PiHidEndpointHandlers.KeyboardReportAsync("Bearer secret", body, Options, writer);

        Assert.Equal(400, result.StatusCode);
        Assert.Empty(writer.KeyboardReports);
    }

    [Fact]
    [Trait("Category", "HardwareHID")]
    public async Task Mouse_Report_Writes_Exactly_Four_Bytes()
    {
        var writer = new RecordingWriter();
        var report = new byte[] { 0, 40, unchecked((byte)(sbyte)-2), 0 };
        using var body = new MemoryStream(report);

        var result = await PiHidEndpointHandlers.MouseReportAsync("Bearer secret", body, Options, writer);

        Assert.Equal(200, result.StatusCode);
        Assert.Single(writer.MouseReports);
        Assert.Equal(report, writer.MouseReports[0]);
    }

    [Fact]
    [Trait("Category", "HardwareHID")]
    public async Task ClearModifiers_Writes_Neutral_Keyboard_And_Mouse_Reports()
    {
        var writer = new RecordingWriter();

        var result = await PiHidEndpointHandlers.ClearModifiersAsync("Bearer secret", Options, writer);

        Assert.Equal(200, result.StatusCode);
        Assert.Single(writer.KeyboardReports);
        Assert.Single(writer.MouseReports);
        Assert.Equal(new byte[8], writer.KeyboardReports[0]);
        Assert.Equal(new byte[4], writer.MouseReports[0]);
    }

    [Fact]
    [Trait("Category", "HardwareHID")]
    public async Task Reset_Requires_Token_And_Writes_No_Reports_When_Unauthorized()
    {
        var writer = new RecordingWriter();

        var result = await PiHidEndpointHandlers.ResetAsync("Bearer wrong", Options, writer);

        Assert.Equal(401, result.StatusCode);
        Assert.Empty(writer.KeyboardReports);
        Assert.Empty(writer.MouseReports);
    }

    private sealed class RecordingWriter : IHidReportWriter
    {
        public string KeyboardDevice => "/tmp/hidg0";
        public string MouseDevice => "/tmp/hidg1";
        public List<byte[]> KeyboardReports { get; } = new();
        public List<byte[]> MouseReports { get; } = new();

        public Task WriteKeyboardAsync(byte[] report, CancellationToken cancellationToken = default)
        {
            KeyboardReports.Add(report.ToArray());
            return Task.CompletedTask;
        }

        public Task WriteMouseAsync(byte[] report, CancellationToken cancellationToken = default)
        {
            MouseReports.Add(report.ToArray());
            return Task.CompletedTask;
        }
    }
}
