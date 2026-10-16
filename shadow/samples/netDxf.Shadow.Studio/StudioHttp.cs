using System.Globalization;
using System.Text.Json;

namespace netDxf.Shadow.Studio;

internal static class StudioHttp
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal static StudioRequestIdentity Identity(HttpRequest request)
    {
        if (!Guid.TryParseExact(request.Headers["X-Shadow-Document"], "D", out var document)
            || !long.TryParse(request.Headers["X-Shadow-View"], NumberStyles.None, CultureInfo.InvariantCulture, out var view)
            || request.Headers["X-Shadow-Workspace"].Count != 1)
            throw new ShadowConflictException("Fetch the current state and supply its document, workspace and view identity.");
        return new(document, request.Headers["X-Shadow-Workspace"].ToString(), view);
    }

    internal static T Execute<T>(Studio studio, HttpRequest request, Func<T> action) => studio.ExecuteRequest(Identity(request), action);

    internal static void MapStudioWorkspaces(this WebApplication app)
    {
        app.MapPost("/api/branches", (Studio studio, HttpRequest http, ForkRequest request) => Execute(studio, http, () => studio.Fork(request)));
        app.MapPost("/api/workspace", (Studio studio, HttpRequest http, WorkspaceRequest request) => Execute(studio, http, () => studio.SwitchWorkspace(request)));
        app.MapPost("/api/branches/discard", (Studio studio, HttpRequest http, BranchDeleteRequest request) => Execute(studio, http, () => studio.DiscardBranch(request)));
        app.MapPost("/api/merge/preview", (Studio studio, HttpRequest http, MergePreviewRequest request, CancellationToken token) => Execute(studio, http, () => studio.PreviewMerge(request, token)));
        app.MapPost("/api/merge/apply", (Studio studio, HttpRequest http, MergeApplyRequest request) => Execute(studio, http, () => studio.ApplyMerge(request)));
        app.MapGet("/api/events", StreamEvents);
    }

    private static async Task StreamEvents(Studio studio, HttpContext context)
    {
        using var subscription = studio.Subscribe(context.Request.Headers["Last-Event-ID"].ToString());
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        var token = cancellation.Token;
        context.Response.ContentType = "text/event-stream; charset=utf-8";
        context.Response.Headers.CacheControl = "no-cache, no-transform";
        context.Response.Headers["X-Accel-Buffering"] = "no";
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        // Keep outstanding waits across iterations; never accumulate orphaned operations.
        var read = subscription.Reader.WaitToReadAsync(token).AsTask();
        var heartbeat = timer.WaitForNextTickAsync(token).AsTask();
        try
        {
            await context.Response.WriteAsync("retry: 1500\n\n", token);
            while (!token.IsCancellationRequested)
            {
                var completed = await Task.WhenAny(read, heartbeat);
                if (completed == read)
                {
                    if (!await read) break;
                    if (subscription.Reader.TryRead(out var notice))
                        await context.Response.WriteAsync($"id: {notice.EventId}\nevent: state\ndata: {JsonSerializer.Serialize(notice, Json)}\n\n", token);
                    read = subscription.Reader.WaitToReadAsync(token).AsTask();
                }
                else
                {
                    if (!await heartbeat) break;
                    await context.Response.WriteAsync(": keepalive\n\n", token);
                    heartbeat = timer.WaitForNextTickAsync(token).AsTask();
                }
                await context.Response.Body.FlushAsync(token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally
        {
            await cancellation.CancelAsync();
            try { await Task.WhenAll(read, heartbeat); } catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        }
    }
}
