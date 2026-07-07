using System.Net;
using MouseKeyProxy.Commands;

namespace MouseKeyProxy.Commands.Tests;

public class PiHidBackendTests
{
    [Fact]
    [Trait("Category", "HardwareHID")]
    public void AltSpace_Uses_Ordered_Boot_Keyboard_Reports()
    {
        var reports = PiHidReports.CreateKeyboardChordReports(PiHidChord.AltSpace);

        Assert.Equal(4, reports.Count);
        Assert.Equal(new byte[] { 0x04, 0, 0, 0, 0, 0, 0, 0 }, reports[0]);
        Assert.Equal(new byte[] { 0x04, 0, 0x2c, 0, 0, 0, 0, 0 }, reports[1]);
        Assert.Equal(new byte[] { 0x04, 0, 0, 0, 0, 0, 0, 0 }, reports[2]);
        Assert.Equal(new byte[] { 0, 0, 0, 0, 0, 0, 0, 0 }, reports[3]);
    }

    [Theory]
    [InlineData("win+left", 0x50)]
    [InlineData("windows-right", 0x4f)]
    [Trait("Category", "HardwareHID")]
    public void WinArrow_Parses_And_Uses_Gui_Modifier(string chordText, byte arrowUsage)
    {
        Assert.True(PiHidReports.TryParseChord(chordText, out var chord));

        var reports = PiHidReports.CreateKeyboardChordReports(chord);

        Assert.Equal(0x08, reports[0][0]);
        Assert.Equal(0x08, reports[1][0]);
        Assert.Equal(arrowUsage, reports[1][2]);
        Assert.All(reports, report => Assert.Equal(PiHidReports.KeyboardReportLength, report.Length));
    }

    [Fact]
    [Trait("Category", "HardwareHID")]
    public void RelativeMouse_Chunks_Deltas_To_Boot_Mouse_Range()
    {
        var reports = PiHidReports.CreateRelativeMouseReports(300, -129);

        Assert.Equal(3, reports.Count);
        Assert.Equal(new byte[] { 0, 127, unchecked((byte)(sbyte)-127), 0 }, reports[0]);
        Assert.Equal(new byte[] { 0, 127, unchecked((byte)(sbyte)-2), 0 }, reports[1]);
        Assert.Equal(new byte[] { 0, 46, 0, 0 }, reports[2]);
    }

    [Fact]
    [Trait("Category", "HardwareHID")]
    public async Task Client_Posts_Reports_With_Bearer_Token_To_Expected_Endpoints()
    {
        var handler = new RecordingHandler();
        using var http = new HttpClient(handler);
        var client = new PiHidClient(http, new PiHidClientOptions(new Uri("http://mkp-hid-pi:8765/"), "secret"));

        var keyResults = await client.TestChordAsync(PiHidChord.WinLeft);
        var mouseResults = await client.TestMouseAsync(40, 0);
        var clearResult = await client.ClearModifiersAsync();

        Assert.All(keyResults.Concat(mouseResults).Append(clearResult), result => Assert.True(result.Ok));
        Assert.Contains(handler.Requests, r => r.Path == "/keyboard/report" && r.Authorization == "Bearer secret" && r.Body.Length == 8);
        Assert.Contains(handler.Requests, r => r.Path == "/mouse/report" && r.Authorization == "Bearer secret" && r.Body.SequenceEqual(new byte[] { 0, 40, 0, 0 }));
        Assert.Contains(handler.Requests, r => r.Path == "/clear-modifiers" && r.Authorization == "Bearer secret");
    }

    [Fact]
    [Trait("Category", "HardwareHID")]
    public async Task Client_Propagates_Token_Rejection_As_NonSuccess_Result()
    {
        var handler = new RecordingHandler(HttpStatusCode.Unauthorized, "missing bearer token");
        using var http = new HttpClient(handler);
        var client = new PiHidClient(http, new PiHidClientOptions(new Uri("http://mkp-hid-pi:8765/"), string.Empty));

        var result = await client.GetStatusAsync();

        Assert.False(result.Ok);
        Assert.Equal(401, result.StatusCodeValue);
        Assert.Contains("missing bearer token", result.Body);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _body;

        public RecordingHandler(HttpStatusCode statusCode = HttpStatusCode.OK, string body = "ok")
        {
            _statusCode = statusCode;
            _body = body;
        }

        public List<RecordedRequest> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content == null
                ? Array.Empty<byte>()
                : await request.Content.ReadAsByteArrayAsync(cancellationToken);
            Requests.Add(new RecordedRequest(
                request.RequestUri?.AbsolutePath ?? string.Empty,
                request.Headers.Authorization?.ToString() ?? string.Empty,
                body));
            return new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_body),
            };
        }
    }

    private sealed record RecordedRequest(string Path, string Authorization, byte[] Body);
}
