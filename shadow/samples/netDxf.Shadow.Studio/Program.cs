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
    catch (ShadowConflictException error) { context.Response.StatusCode = 409; await context.Response.WriteAsJsonAsync(new { error = error.Message }); }
    catch (Exception error) when (error is ArgumentException or InvalidOperationException or NotSupportedException or ArithmeticException)
    { context.Response.StatusCode = 400; await context.Response.WriteAsJsonAsync(new { error = error.Message }); }
});
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapGet("/api/state", (Studio studio) => studio.State());
app.MapGet("/api/render", (Studio studio, int width, int height, double? cx, double? cy, double? scale, string? backend, long? snapshot, CancellationToken cancellationToken) =>
{
    var image = studio.Render(width, height, cx, cy, scale, backend ?? "skia", snapshot, cancellationToken);
    return Results.Bytes(image.Bytes, image.ContentType);
});
app.MapGet("/api/graph", (Studio studio, CancellationToken token) => studio.Graph(token));
app.MapGet("/api/object/{id}", (Studio studio, string id) => studio.Inspect(new(id)));
app.MapPost("/api/pick", (Studio studio, PickRequest request) => studio.Pick(request));
app.MapPost("/api/select", (Studio studio, SelectRequest request) => studio.Select(request));
app.MapPost("/api/translate", (Studio studio, MoveRequest request) => studio.Translate(request));
app.MapPost("/api/property", (Studio studio, PropertyRequest request) => studio.SetProperty(request));
app.MapPost("/api/history/{operation}", (Studio studio, string operation, RevisionRequest request) => studio.History(operation, request.Revision));
app.MapPost("/api/layout", (Studio studio, LayoutRequest request) => studio.SetLayout(request));
app.MapPost("/api/add", (Studio studio, AddRequest request) => studio.Add(request));
app.MapPost("/api/delete", (Studio studio, RevisionRequest request) => studio.Delete(request.Revision));
app.MapPost("/api/source-demo", (Studio studio, RevisionRequest request) => studio.SourceDemo(request.Revision));
app.MapPost("/api/snapshot", (Studio studio, RevisionRequest request) => studio.Retain(request.Revision));
app.MapPost("/api/load", async (Studio studio, HttpRequest request, CancellationToken token) =>
{
    using var memory = new MemoryStream();
    await request.Body.CopyToAsync(memory, token);
    if (memory.Length > 20 * 1024 * 1024) return Results.BadRequest(new { error = "Sample upload limit is 20 MiB." });
    memory.Position = 0;
    studio.Load(memory);
    return Results.Ok(studio.State());
});
app.MapGet("/api/save", (Studio studio) => Results.File(studio.Save(), "application/dxf", "shadow-edited.dxf"));
app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));
app.Run();
