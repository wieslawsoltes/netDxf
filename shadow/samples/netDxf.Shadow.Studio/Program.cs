using System.Net;
using netDxf.Shadow;
using netDxf.Shadow.Rendering;
using netDxf.Shadow.Studio;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(builder.Configuration["urls"] ?? "http://127.0.0.1:5080");
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 20 * 1024 * 1024);
builder.Services.AddSingleton<Studio>();
builder.Services.AddSingleton<StudioDocumentHub>();
builder.WebHost.UseStaticWebAssets();
var app = builder.Build();
app.Use(async (context, next) =>
{
    // This is a local, single-user sample, not an authenticated Internet service.
    var host = context.Request.Host.Host;
    if (host != "localhost" && (!IPAddress.TryParse(host, out var ip) || !IPAddress.IsLoopback(ip)))
    { context.Response.StatusCode = 403; return; }
    if (context.Request.Method != "GET" && context.Request.Headers["X-Shadow-Studio"] != "1")
    { context.Response.StatusCode = 403; return; }
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; img-src 'self' blob:; style-src 'self'; script-src 'self'; connect-src 'self' http://127.0.0.1:* ws://127.0.0.1:*; frame-ancestors 'none'; base-uri 'none'";
    try { await next(); }
    catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
    catch (ShadowConflictException error) when (!context.Response.HasStarted) { context.Response.StatusCode = 409; await context.Response.WriteAsJsonAsync(new { error = error.Message }); }
    catch (Exception error) when (!context.Response.HasStarted && error is (ArgumentException or InvalidOperationException or NotSupportedException or ArithmeticException or FormatException))
    { context.Response.StatusCode = 400; await context.Response.WriteAsJsonAsync(new { error = error.Message }); }
});
app.Use((context, next) =>
{
    if (context.Request.Path == "/diagnostics.html") context.Request.Path = "/" + StudioWebAssets.Diagnostics;
    return next();
});
var defaults = new DefaultFilesOptions();
defaults.DefaultFileNames.Clear();
defaults.DefaultFileNames.Add(StudioWebAssets.Shell);
app.UseDefaultFiles(defaults);
app.UseStaticFiles();
app.MapStudioLegacy();
app.MapStudioWorkspaces();
app.MapCadWorkspace();
app.MapStudioAgents();
app.Run();
