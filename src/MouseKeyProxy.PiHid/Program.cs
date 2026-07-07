using MouseKeyProxy.PiHid;

var options = PiHidOptions.FromEnvironment();
var writer = new FileHidReportWriter(options.KeyboardDevice, options.MouseDevice);

var builder = WebApplication.CreateSlimBuilder(args);
builder.WebHost.UseUrls(options.BindUrl);

var app = builder.Build();

app.MapGet("/status", (HttpContext context) =>
    PiHidEndpointHandlers.Status(context.Request.Headers.Authorization, options, writer).ToResult());

app.MapPost("/keyboard/report", async (HttpContext context) =>
    (await PiHidEndpointHandlers.KeyboardReportAsync(context.Request.Headers.Authorization, context.Request.Body, options, writer, context.RequestAborted).ConfigureAwait(false)).ToResult());

app.MapPost("/mouse/report", async (HttpContext context) =>
    (await PiHidEndpointHandlers.MouseReportAsync(context.Request.Headers.Authorization, context.Request.Body, options, writer, context.RequestAborted).ConfigureAwait(false)).ToResult());

app.MapPost("/clear-modifiers", async (HttpContext context) =>
    (await PiHidEndpointHandlers.ClearModifiersAsync(context.Request.Headers.Authorization, options, writer, context.RequestAborted).ConfigureAwait(false)).ToResult());

app.MapPost("/reset", async (HttpContext context) =>
    (await PiHidEndpointHandlers.ResetAsync(context.Request.Headers.Authorization, options, writer, context.RequestAborted).ConfigureAwait(false)).ToResult());

app.Run();

public partial class Program;
