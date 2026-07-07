using System.Net;
using System.Net.Http.Headers;

namespace MouseKeyProxy.Commands;

public sealed record PiHidClientOptions(Uri BaseUri, string Token)
{
    public const int DefaultPort = 8765;

    public static PiHidClientOptions FromEnvironment()
    {
        var url = Environment.GetEnvironmentVariable("MKP_HID_PI_URL");
        if (string.IsNullOrWhiteSpace(url))
        {
            var host = Environment.GetEnvironmentVariable("MKP_HID_PI_HOST");
            if (string.IsNullOrWhiteSpace(host))
            {
                host = "mkp-hid-pi";
            }

            var portText = Environment.GetEnvironmentVariable("MKP_HID_PI_PORT");
            var port = int.TryParse(portText, out var parsedPort) ? parsedPort : DefaultPort;
            url = $"http://{host}:{port}/";
        }

        if (!url.EndsWith("/", StringComparison.Ordinal))
        {
            url += "/";
        }

        var token = Environment.GetEnvironmentVariable("MKP_HID_PI_TOKEN") ?? string.Empty;
        return new PiHidClientOptions(new Uri(url, UriKind.Absolute), token);
    }
}

public sealed record PiHidHttpResult(bool Ok, HttpStatusCode StatusCode, string Body)
{
    public int StatusCodeValue => (int)StatusCode;
}

public sealed class PiHidClient
{
    private readonly HttpClient _httpClient;
    private readonly PiHidClientOptions _options;

    public PiHidClient(HttpClient httpClient, PiHidClientOptions options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    public static bool IsPiHidBackendEnabled()
    {
        return string.Equals(Environment.GetEnvironmentVariable("MKP_INPUT_BACKEND"), "pi-hid", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<PiHidHttpResult> GetStatusAsync(CancellationToken ct = default)
    {
        return await SendAsync(HttpMethod.Get, "status", null, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PiHidHttpResult>> SendKeyboardReportsAsync(IEnumerable<byte[]> reports, CancellationToken ct = default)
    {
        return await SendReportsAsync("keyboard/report", reports, PiHidReports.KeyboardReportLength, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PiHidHttpResult>> SendMouseReportsAsync(IEnumerable<byte[]> reports, CancellationToken ct = default)
    {
        return await SendReportsAsync("mouse/report", reports, PiHidReports.MouseReportLength, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PiHidHttpResult>> TestChordAsync(PiHidChord chord, CancellationToken ct = default)
    {
        return await SendKeyboardReportsAsync(PiHidReports.CreateKeyboardChordReports(chord), ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PiHidHttpResult>> TestMouseAsync(int dx, int dy, int wheel = 0, CancellationToken ct = default)
    {
        return await SendMouseReportsAsync(PiHidReports.CreateRelativeMouseReports(dx, dy, wheel), ct).ConfigureAwait(false);
    }

    public async Task<PiHidHttpResult> ClearModifiersAsync(CancellationToken ct = default)
    {
        return await SendAsync(HttpMethod.Post, "clear-modifiers", new ByteArrayContent(Array.Empty<byte>()), ct).ConfigureAwait(false);
    }

    public async Task<PiHidHttpResult> ResetAsync(CancellationToken ct = default)
    {
        return await SendAsync(HttpMethod.Post, "reset", new ByteArrayContent(Array.Empty<byte>()), ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<PiHidHttpResult>> SendReportsAsync(string endpoint, IEnumerable<byte[]> reports, int expectedLength, CancellationToken ct)
    {
        var results = new List<PiHidHttpResult>();
        foreach (var report in reports)
        {
            if (report.Length != expectedLength)
            {
                throw new ArgumentException($"HID report for {endpoint} must be {expectedLength} bytes, but was {report.Length}.", nameof(reports));
            }

            results.Add(await SendAsync(HttpMethod.Post, endpoint, new ByteArrayContent(report), ct).ConfigureAwait(false));
        }

        return results;
    }

    private async Task<PiHidHttpResult> SendAsync(HttpMethod method, string relativePath, HttpContent? content, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, new Uri(_options.BaseUri, relativePath));
        if (!string.IsNullOrWhiteSpace(_options.Token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.Token);
        }

        if (content != null)
        {
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            request.Content = content;
        }

        using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return new PiHidHttpResult(response.IsSuccessStatusCode, response.StatusCode, body);
    }
}
