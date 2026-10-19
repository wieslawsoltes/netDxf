using System.ComponentModel;
using System.Text.Json;
using netDxf.Shadow.Agents.Acp;
using netDxf.Shadow.Agents.Codex;

namespace netDxf.Shadow.AgentHost;

public static class ExternalAgentHttp
{
    public sealed record StartRequest(string Profile);
    public static void MapExternalAgents(this WebApplication app)
    {
        app.MapGet("/agents/profiles", (ExternalAgentHost host) => host.Profiles);
        app.MapGet("/agents/events", (ExternalAgentHost host, HttpContext context, long? after) => Execute(host, context,
            () => Task.FromResult(host.Poll(after ?? 0))));
        app.MapPost("/agents/start", (ExternalAgentHost host, HttpContext context, StartRequest request) => Execute(host, context,
            () => host.StartAsync(request.Profile, context.Request.Headers.Authorization.ToString()[7..], context.RequestAborted)));
        app.MapPost("/agents/call", (ExternalAgentHost host, HttpContext context, ExternalAgentCall request) => Execute(host, context,
            async () => (object)await host.CallAsync(request, context.RequestAborted)));
        app.MapPost("/agents/reply", (ExternalAgentHost host, HttpContext context, ExternalAgentReply request) => Execute(host, context,
            () => { host.Reply(request); return Task.FromResult<object>(new { accepted = true }); }));
        app.MapPost("/agents/stop", (ExternalAgentHost host, HttpContext context) => Execute(host, context,
            async () => { await host.StopAsync(); return (object)new { stopped = true }; }));
    }
    private static async Task<IResult> Execute(ExternalAgentHost host, HttpContext context, Func<Task<object>> call)
    {
        try { return Results.Json(await call()); }
        catch (OperationCanceledException) { return Results.Json(new { error = "Agent operation was cancelled." }, statusCode: 409); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or IOException or JsonException
            or AcpException or CodexRpcException or NotSupportedException or Win32Exception or TimeoutException)
        { return Results.Json(new { error = host.CleanError(error.Message) }, statusCode: 400); }
    }
}
