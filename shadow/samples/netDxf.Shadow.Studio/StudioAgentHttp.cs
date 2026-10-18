using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Routing;
using netDxf.Shadow.Mcp;

namespace netDxf.Shadow.Studio;

internal static class StudioAgentHttp
{
    public sealed record Envelope(JsonElement[] Arguments);
    public static void MapStudioAgents(this WebApplication app)
    {
        var ui = new StudioAgentUiRelay();
        // A dedicated loopback-only client is internal to this local, single-user sample.
        var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false }) { Timeout = TimeSpan.FromMinutes(2) };
        var controller = new Lazy<StudioAgentController>(() => new(id =>
        {
            var hub = app.Services.GetRequiredService<StudioDocumentHub>();
            var registry = hub.Use(id, s => s.CreateMcpRegistry());
            registry.Register(new("studio_routes", "List all live Studio route templates and public request records. Drawing writes require identity headers and current body revision.",
                McpJson.Parse("""{"type":"object","properties":{},"additionalProperties":false}"""), true), (_, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                var endpoints = ((IEndpointRouteBuilder)app).DataSources.SelectMany(s => s.Endpoints).OfType<RouteEndpoint>();
                var routes = endpoints.Where(e => e.RoutePattern.RawText?.StartsWith("/api/", StringComparison.Ordinal) == true && !e.RoutePattern.RawText.StartsWith("/api/agent", StringComparison.Ordinal))
                    .SelectMany(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? []).Select(method => new { method, pattern = e.RoutePattern.RawText })).ToArray();
                return ValueTask.FromResult(McpToolResult.FromObject(new { document = id, routes, requestTypes = StudioAgentSchema.DescribeRequests(),
                    identityHeaders = new[] { "X-Shadow-Document", "X-Shadow-Workspace", "X-Shadow-View" }, example = new { method = "GET", path = $"/api/cad/{id}/state" } }));
            });
            registry.Register(new("studio_request", "Execute a discovered Studio API request. Read current state and supply identity/revision before edits; no request is retried.",
                McpJson.Parse("""{"type":"object","properties":{"method":{"type":"string","enum":["GET","POST"]},"path":{"type":"string"},"body":{"type":"object"},"headers":{"type":"object","additionalProperties":{"type":"string"}},"uploadBase64":{"type":"string"}},"required":["method","path"],"additionalProperties":false}"""), false), async (args, ct) =>
            {
                var path = args.GetProperty("path").GetString()!;
                if (!StudioAgentRouteTools.IsStudioPath(path))
                    return McpToolResult.Error("path", "Use a path from studio_routes.");
                var address = app.Urls.FirstOrDefault() ?? "http://127.0.0.1:5080";
                var endpoint = new Uri(address.Replace("localhost", "127.0.0.1", StringComparison.OrdinalIgnoreCase));
                if (!endpoint.IsLoopback) throw new InvalidOperationException("The local agent dispatcher requires a loopback Studio URL.");
                using var request = new HttpRequestMessage(new(args.GetProperty("method").GetString()!), new Uri(endpoint, path));
                request.Headers.Add("X-Shadow-Studio", "1");
                if (args.TryGetProperty("headers", out var headers))
                    foreach (var header in headers.EnumerateObject())
                        if (header.Name is "X-Shadow-Document" or "X-Shadow-Workspace" or "X-Shadow-View") request.Headers.Add(header.Name, header.Value.GetString());
                if (args.TryGetProperty("uploadBase64", out var upload)) request.Content = new ByteArrayContent(Convert.FromBase64String(upload.GetString()!));
                else if (args.TryGetProperty("body", out var body)) request.Content = JsonContent.Create(body);
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                var type = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var buffer = new MemoryStream(); var chunk = new byte[16384];
                while (true)
                {
                    var count = await stream.ReadAsync(chunk, ct); if (count == 0) break;
                    if (buffer.Length + count > 512000) return new("Operation returned a large payload. Use paginated model tools or the Studio export UI.", !response.IsSuccessStatusCode);
                    buffer.Write(chunk, 0, count);
                }
                var bytes = buffer.ToArray();
                return new(type.Contains("json", StringComparison.Ordinal) ? Encoding.UTF8.GetString(bytes) : JsonSerializer.Serialize(new { contentType = type, base64 = Convert.ToBase64String(bytes) }, McpJson.Options), !response.IsSuccessStatusCode);
            });
            registry.Register(new("studio_ui_inspect", "Inspect the active Studio UI: semantic controls, camera, documents, palettes and settings.",
                McpJson.Parse("""{"type":"object","properties":{},"additionalProperties":false}"""), true), async (_, ct) => new((await ui.InvokeAsync("snapshot", McpJson.EmptyObject, id, ct)).GetRawText()));
            registry.Register(new("studio_ui", "Invoke a semantic Studio UI action discovered by studio_ui_inspect. Target document is checked before mutating UI work.",
                McpJson.Parse("""{"type":"object","properties":{"action":{"type":"string"},"arguments":{"type":"object"}},"required":["action"],"additionalProperties":false}"""), false), async (args, ct) =>
                    new((await ui.InvokeAsync(args.GetProperty("action").GetString()!, args.TryGetProperty("arguments", out var a) ? a : McpJson.EmptyObject, id, ct)).GetRawText()));
            StudioAgentRouteTools.RegisterRead(registry);
            return registry;
        }, async (id, ct) => { await ui.InvokeAsync("refresh", McpJson.EmptyObject, id, ct); }));
        app.Lifetime.ApplicationStopping.Register(() => { if (controller.IsValueCreated) controller.Value.Cancel(); });
        app.Lifetime.ApplicationStopped.Register(() => { if (controller.IsValueCreated) _ = controller.Value.DisposeAsync(); http.Dispose(); });

        app.MapPost("/api/agent/{action}", async (string action, Envelope envelope, HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var a = envelope.Arguments ?? [];
            T Get<T>(int i) => i < a.Length ? a[i].Deserialize<T>(McpJson.Options)! : throw new ArgumentException("Missing agent argument.");
            var agent = controller.Value;
            object? value = null;
            switch (action)
            {
                case "capabilities": value = StudioAgentController.Capabilities(); break;
                case "configure": value = agent.Configure(Get<StudioAgentConfiguration>(0)); break;
                case "set-document": value = agent.SetDocument(Get<string>(0)); break;
                case "start": value = agent.Start(Get<string>(0)); break;
                case "retry": value = agent.Start(""); break;
                case "poll":
                    var poll = JsonSerializer.SerializeToNode(agent.Poll(Get<long>(0)), McpJson.Options)!.AsObject();
                    poll["uiRequests"] = JsonSerializer.SerializeToNode(ui.Poll(), McpJson.Options); value = poll; break;
                case "ui-reply": value = ui.Reply(Get<string>(0), a.Length > 1 ? a[1] : null, a.Length > 2 && a[2].ValueKind == JsonValueKind.String ? a[2].GetString() : null); break;
                case "cancel": agent.Cancel(); break;
                case "disconnect": value = await agent.Disconnect(); break;
                case "companion-connect": value = await agent.CompanionConnect(Get<string>(0), Get<string>(1)); break;
                case "companion-disconnect": value = agent.CompanionDisconnect(); break;
                case "approve": value = agent.Approve(Get<string>(0), Get<bool>(1)); break;
                case "compact": await agent.Compact(); break;
                case "reset": agent.Reset(); break;
                case "export": value = agent.Export(); break;
                case "import": agent.Import(Get<string>(0)); break;
                case "mcp-tools": value = agent.Catalog(); break;
                case "mcp-call": value = await agent.Call(Get<string>(0), a[1]); break;
                case "workspace-files": value = agent.Files(); break;
                case "workspace-read": value = agent.Read(Get<string>(0)); break;
                case "workspace-write": value = agent.Write(Get<string>(0), Get<string>(1), a.Length > 2 && a[2].ValueKind == JsonValueKind.Number ? a[2].GetInt64() : null); break;
                case "pin": value = agent.Pin(Get<string>(0)); break;
                case "unpin": value = agent.Unpin(Get<string>(0)); break;
                default: return Results.NotFound(new { error = "Unknown agent operation." });
            }
            return Results.Json(value, McpJson.Options);
        });
    }
}
