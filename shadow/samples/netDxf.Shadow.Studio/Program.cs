using System.Net;
using netDxf.Shadow;
using netDxf.Shadow.Rendering;
using netDxf.Shadow.Studio;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:5080");
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 20 * 1024 * 1024);
builder.Services.AddSingleton<Studio>();
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
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; img-src 'self' blob:; style-src 'self'; script-src 'self'; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'";
    try { await next(); }
    catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
    catch (ShadowConflictException error) when (!context.Response.HasStarted) { context.Response.StatusCode = 409; await context.Response.WriteAsJsonAsync(new { error = error.Message }); }
    catch (Exception error) when (!context.Response.HasStarted && error is (ArgumentException or InvalidOperationException or NotSupportedException or ArithmeticException or FormatException))
    { context.Response.StatusCode = 400; await context.Response.WriteAsJsonAsync(new { error = error.Message }); }
});
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapGet("/api/state", (Studio studio) => studio.State());
app.MapGet("/api/render", (Studio studio, HttpRequest request, int width, int height, double? cx, double? cy, double? scale, string? backend, long? snapshot, string? merge, CancellationToken cancellationToken) =>
{
    var image = StudioHttp.Execute(studio, request, () => studio.Render(width, height, cx, cy, scale, backend ?? "skia", snapshot, cancellationToken, merge));
    return Results.Bytes(image.Bytes, image.ContentType);
});
app.MapGet("/api/graph", (Studio studio, HttpRequest request, CancellationToken token) => StudioHttp.Execute(studio, request, () => studio.Graph(token)));
app.MapGet("/api/object/{id}", (Studio studio, HttpRequest request, string id) => StudioHttp.Execute(studio, request, () => studio.Inspect(new(id))));
app.MapPost("/api/pick", (Studio studio, HttpRequest http, PickRequest request) => StudioHttp.Execute(studio, http, () => studio.Pick(request)));
app.MapPost("/api/select", (Studio studio, HttpRequest http, SelectRequest request) => StudioHttp.Execute(studio, http, () => studio.Select(request)));
app.MapPost("/api/transform", (Studio studio, HttpRequest http, TransformRequest request) => StudioHttp.Execute(studio, http, () => studio.Transform(request)));
app.MapPost("/api/translate", (Studio studio, HttpRequest http, MoveRequest request) => StudioHttp.Execute(studio, http, () => studio.Translate(request)));
app.MapPost("/api/property", (Studio studio, HttpRequest http, PropertyRequest request) => StudioHttp.Execute(studio, http, () => studio.SetProperty(request)));
app.MapPost("/api/history/{operation}", (Studio studio, HttpRequest http, string operation, RevisionRequest request) => StudioHttp.Execute(studio, http, () => studio.History(operation, request.Revision)));
app.MapPost("/api/layout", (Studio studio, HttpRequest http, LayoutRequest request) => StudioHttp.Execute(studio, http, () => studio.SetLayout(request)));
app.MapPost("/api/add", (Studio studio, HttpRequest http, AddRequest request) => StudioHttp.Execute(studio, http, () => studio.Add(request)));
app.MapPost("/api/delete", (Studio studio, HttpRequest http, RevisionRequest request) => StudioHttp.Execute(studio, http, () => studio.Delete(request.Revision)));
app.MapPost("/api/source-demo", (Studio studio, HttpRequest http, RevisionRequest request) => StudioHttp.Execute(studio, http, () => studio.SourceDemo(request.Revision)));
app.MapPost("/api/snapshot", (Studio studio, HttpRequest http, RevisionRequest request) => StudioHttp.Execute(studio, http, () => studio.Retain(request.Revision)));
app.MapPost("/api/load", async (Studio studio, HttpRequest request, CancellationToken token) =>
{
    using var memory = new MemoryStream();
    await request.Body.CopyToAsync(memory, token);
    if (memory.Length > 20 * 1024 * 1024) return Results.BadRequest(new { error = "Sample upload limit is 20 MiB." });
    memory.Position = 0;
    return Results.Ok(StudioHttp.Execute(studio, request, () => { studio.Load(memory); return studio.State(); }));
});
app.MapPost("/api/font", async (Studio studio, HttpRequest request, long revision, string name, CancellationToken token) =>
{
    const int limit = 4 * 1024 * 1024;
    if (request.ContentLength > limit) return Results.BadRequest(new { error = "Sample font limit is 4 MiB." });
    using var memory = new MemoryStream(); var buffer = new byte[32 * 1024];
    while (true)
    {
        var count = await request.Body.ReadAsync(buffer, token); if (count == 0) break;
        if (memory.Length + count > limit) return Results.BadRequest(new { error = "Sample font limit is 4 MiB." });
        memory.Write(buffer, 0, count);
    }
    return Results.Ok(StudioHttp.Execute(studio, request, () => studio.LoadFont(revision, name, memory.ToArray())));
});
app.MapGet("/api/save", (Studio studio) => Results.File(studio.Save(), "application/dxf", "shadow-edited.dxf"));
app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));
app.MapStudioWorkspaces();
app.Run();
