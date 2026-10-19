using System.Text;
using System.Text.Json;
using Microsoft.JSInterop;
using netDxf.Shadow.Agents;
using netDxf.Shadow.Mcp;

namespace netDxf.Shadow.Studio.Browser;

public static partial class BrowserRuntime
{
    [JSInvokable] public static object AgentCapabilities() => StudioAgentController.Capabilities();
    [JSInvokable] public static object AgentConfigure(StudioAgentConfiguration options) => App.Agents.Configure(options);
    [JSInvokable] public static Task<AgentModelDiscoveryResult> AgentModels(string request) => App.Agents.ModelsAsync(request);
    [JSInvokable] public static object AgentSetDocument(string documentId) => App.Agents.SetDocument(documentId);
    [JSInvokable] public static object AgentStart(string prompt) => App.Agents.Start(prompt);
    [JSInvokable] public static Task<object> AgentStartPrompt(string prompt) => App.Agents.StartPromptAsync(JsonSerializer.Deserialize<AgentPrompt>(prompt, McpJson.Options) ?? throw new ArgumentException("Missing prompt."));
    [JSInvokable] public static object AgentRetry() => App.Agents.Start("");
    [JSInvokable] public static object AgentPoll(long afterSequence) => App.Agents.Poll(afterSequence);
    [JSInvokable] public static void AgentCancel() => App.Agents.Cancel();
    [JSInvokable] public static Task<object> AgentDisconnect() => App.Agents.Disconnect();
    [JSInvokable] public static Task<object> AgentCompanionConnect(string endpoint, string token) => App.Agents.CompanionConnect(endpoint, token);
    [JSInvokable] public static object AgentCompanionDisconnect() => App.Agents.CompanionDisconnect();
    [JSInvokable] public static bool AgentApprove(string requestId, bool approved) => App.Agents.Approve(requestId, approved);
    [JSInvokable] public static Task AgentCompact() => App.Agents.Compact();
    [JSInvokable] public static void AgentReset() => App.Agents.Reset();
    [JSInvokable] public static string AgentExport() => App.Agents.Export();
    [JSInvokable] public static void AgentImport(string json) => App.Agents.Import(json);
    [JSInvokable] public static IReadOnlyList<McpToolDefinition> AgentMcpTools() => App.Agents.Catalog();
    [JSInvokable] public static Task<McpToolResult> AgentMcpCall(string name, JsonElement arguments) => App.Agents.Call(name, arguments);
    [JSInvokable] public static IReadOnlyList<AgentFileInfo> AgentWorkspaceFiles() => App.Agents.Files();
    [JSInvokable] public static IReadOnlyList<AgentFile> AgentWorkspaceExport() => App.Agents.ExportFiles();
    [JSInvokable] public static object AgentWorkspaceApply(AgentFileChange[] changes) => App.Agents.ApplyFiles(changes);
    [JSInvokable] public static AgentFileSlice AgentWorkspaceRead(string path) => App.Agents.Read(path);
    [JSInvokable] public static AgentFileInfo AgentWorkspaceWrite(string path, string content, long? expectedVersion) => App.Agents.Write(path, content, expectedVersion);
    [JSInvokable] public static AgentPinnedContext AgentPin(string text) => App.Agents.Pin(text);
    [JSInvokable] public static bool AgentUnpin(string id) => App.Agents.Unpin(id);
}

internal sealed partial class BrowserApplication
{
    private StudioAgentController? agents;
    internal StudioAgentController Agents => agents ?? throw new InvalidOperationException("The agent runtime is not initialized.");
    internal void InitializeAgents(IJSRuntime javascript)
    {
        agents = new StudioAgentController(id =>
        {
            var registry = documents.Use(id, s => s.CreateMcpRegistry());
            StudioAgentSchema.RegisterRoutes(registry, id, () => routes.Select(r => new StudioAgentSchema.Route(r.Method, r.Pattern)));
            registry.Register(new("studio_request", "Execute any discovered Studio route against the live .NET application. Supply current identity headers and body revision for edits. Never call agent endpoints. Binary responses return a bounded base64 payload.",
                McpJson.Parse("""{"type":"object","properties":{"method":{"type":"string","enum":["GET","POST"]},"path":{"type":"string"},"body":{"type":"object"},"headers":{"type":"object","additionalProperties":{"type":"string"}},"uploadBase64":{"type":"string"}},"required":["method","path"],"additionalProperties":false}"""), false), (args, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                var path = args.GetProperty("path").GetString()!;
                if (!StudioAgentRouteTools.IsStudioPath(path))
                    return ValueTask.FromResult(McpToolResult.Error("path", "Use a path from studio_routes."));
                var headers = args.TryGetProperty("headers", out var h) ? h.Deserialize<Dictionary<string, string>>(McpJson.Options)! : new();
                headers["X-Shadow-Studio"] = "1";
                var body = args.TryGetProperty("body", out var b) ? (JsonElement?)b : null;
                var upload = args.TryGetProperty("uploadBase64", out var u) ? Convert.FromBase64String(u.GetString()!) : null;
                var reply = Dispatch(new(args.GetProperty("method").GetString()!, path, headers, body), upload);
                string text;
                if (reply.ContentType.Contains("json", StringComparison.Ordinal)) text = reply.Json ?? Encoding.UTF8.GetString(reply.Bytes ?? []);
                else if ((reply.Bytes?.Length ?? 0) <= 64000) text = JsonSerializer.Serialize(new { reply.ContentType, base64 = Convert.ToBase64String(reply.Bytes ?? []) }, McpJson.Options);
                else text = JsonSerializer.Serialize(new { reply.ContentType, length = reply.Bytes!.Length, message = "Binary result exceeds tool response budget. Use the Studio save/export UI to download it." }, McpJson.Options);
                return ValueTask.FromResult(new McpToolResult(text, reply.Status >= 400));
            });
            registry.Register(new("studio_ui_inspect", "Inspect current UI state, document, selection, camera, palettes, settings and semantic controls.",
                McpJson.Parse("""{"type":"object","properties":{},"additionalProperties":false}"""), true), async (_, ct) =>
            {
                var value = await javascript.InvokeAsync<JsonElement>("shadowStudioAgentUi.snapshot", ct);
                return new(value.GetRawText());
            });
            registry.Register(new("studio_ui", "Control the full Studio UI through semantic actions. First inspect studio_ui_inspect to discover actions. The target drawing is checked before any mutation.",
                McpJson.Parse("""{"type":"object","properties":{"action":{"type":"string"},"arguments":{"type":"object"}},"required":["action"],"additionalProperties":false}"""), false), async (args, ct) =>
            {
                var value = await javascript.InvokeAsync<JsonElement>("shadowStudioAgentUi.execute", ct, args.GetProperty("action").GetString(),
                    args.TryGetProperty("arguments", out var a) ? a : McpJson.EmptyObject, id);
                return new(value.GetRawText());
            });
            StudioAgentRouteTools.RegisterRead(registry);
            return registry;
        }, async (id, ct) => { await javascript.InvokeVoidAsync("shadowStudioAgentUi.execute", ct, "refresh", McpJson.EmptyObject, id); }, new BrowserAgentCodeRunner(javascript));
    }
}

internal sealed class BrowserAgentCodeRunner(IJSRuntime javascript) : IAgentCodeRunner
{
    public async ValueTask<AgentCodeResult> ExecuteAsync(AgentCodeRequest request, CancellationToken cancellationToken = default)
    {
        try { return await javascript.InvokeAsync<AgentCodeResult>("shadowStudioAgentCode.execute", cancellationToken, request); }
        finally
        {
            if (cancellationToken.IsCancellationRequested)
                await javascript.InvokeVoidAsync("shadowStudioAgentCode.cancel", CancellationToken.None, request.Id);
        }
    }
}
