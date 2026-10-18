using System.Globalization;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace netDxf.Shadow.Mcp;

/// <summary>
/// Official MCP SDK adapter. The SDK owns initialization/version negotiation, JSON-RPC,
/// cancellation and transport lifecycle; these handlers expose the host's live tool catalog.
/// </summary>
public static class ShadowMcpServer
{
    public const string Instructions = "This server controls a netDxf document and its shadow/editor/application models. " +
        "Start with shadow_state and discover schemas before editing. Preserve documentId, workspaceId, viewVersion and revision from current state. " +
        "Use bounded pages and exact JSON Pointer paths; atoms retain invariant text and CLR types. " +
        "Property and structural transactions use native validation and undo. If a revision conflict occurs, inspect current state and rebuild the proposal. " +
        "Tool execution errors are returned as isError results and may be corrected. Preview validation covers the immutable graph; native source validation happens on apply.";

    public static McpServerOptions CreateOptions(IMcpToolExecutor executor)
    {
        var options = new McpServerOptions();
        Configure(options, executor);
        return options;
    }

    public static void Configure(McpServerOptions options, IMcpToolExecutor executor)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(executor);
        options.ServerInfo = new() { Name = "netDxf.Shadow.Mcp", Version = typeof(ShadowMcpServer).Assembly.GetName().Version?.ToString(3) ?? "1.0.0" };
        options.ServerInstructions = Instructions;
        options.ScopeRequests = false;
        options.Capabilities = new()
        {
            Tools = new() { ListChanged = false },
            Resources = new() { ListChanged = false, Subscribe = false },
            Prompts = new() { ListChanged = false }
        };
        options.Handlers.ListToolsHandler = (request, token) =>
        {
            token.ThrowIfCancellationRequested();
            var offset = Cursor(request.Params?.Cursor);
            var tools = executor.Tools;
            const int pageSize = 100;
            return ValueTask.FromResult(new ListToolsResult
            {
                Tools = tools.Skip(offset).Take(pageSize).Select(definition => new Tool
                {
                    Name = definition.Name, Description = definition.Description, InputSchema = definition.InputSchema,
                    Annotations = new() { ReadOnlyHint = definition.ReadOnly, DestructiveHint = !definition.ReadOnly }
                }).ToList(),
                NextCursor = offset + pageSize < tools.Count ? (offset + pageSize).ToString(CultureInfo.InvariantCulture) : null
            });
        };
        options.Handlers.CallToolHandler = async (request, token) =>
        {
            var parameters = request.Params ?? throw new McpException("tools/call requires parameters.");
            var arguments = parameters.Arguments is null ? McpJson.EmptyObject : McpJson.Element(parameters.Arguments);
            var result = await executor.ExecuteAsync(parameters.Name, arguments, token).ConfigureAwait(false);
            return new CallToolResult { Content = [new TextContentBlock { Text = result.Text }], IsError = result.IsError };
        };
        options.Handlers.ListResourcesHandler = (request, token) =>
        {
            token.ThrowIfCancellationRequested();
            var resources = new List<Resource>
            {
                new() { Uri = "shadow://server/tools", Name = "Tool catalog", Description = "Complete executable tool definitions and argument schemas.", MimeType = "application/json" },
                new() { Uri = "shadow://server/instructions", Name = "Editing protocol", Description = "Identity, revision, value and transaction conventions.", MimeType = "text/plain" }
            };
            if (Has(executor, "shadow_state")) resources.Add(new() { Uri = "shadow://workspace/state", Name = "Current workspace", MimeType = "application/json" });
            if (Has(executor, "shadow_diagnostics")) resources.Add(new() { Uri = "shadow://workspace/diagnostics", Name = "Model diagnostics", MimeType = "application/json" });
            var offset = Cursor(request.Params?.Cursor);
            return ValueTask.FromResult(new ListResourcesResult { Resources = resources.Skip(offset).ToList() });
        };
        options.Handlers.ListResourceTemplatesHandler = (request, token) =>
        {
            token.ThrowIfCancellationRequested();
            var templates = new List<ResourceTemplate>();
            if (Has(executor, "shadow_read")) templates.Add(new() { UriTemplate = "shadow://workspace/objects/{id}", Name = "Shadow object", Description = "A detached node; add ?path=/Property/0&offset=0&limit=32 for nested pages.", MimeType = "application/json" });
            if (Has(executor, "shadow_schema")) templates.Add(new() { UriTemplate = "shadow://schema/types/{type}", Name = "netDxf type schema", MimeType = "application/json" });
            return ValueTask.FromResult(new ListResourceTemplatesResult { ResourceTemplates = templates.Skip(Cursor(request.Params?.Cursor)).ToList() });
        };
        options.Handlers.ReadResourceHandler = async (request, token) =>
        {
            var resourceUri = request.Params?.Uri ?? throw new McpException("resources/read requires a URI.");
            var (text, mimeType) = await ReadResourceAsync(executor, resourceUri, token).ConfigureAwait(false);
            return new ReadResourceResult { Contents = [new TextResourceContents { Uri = resourceUri, MimeType = mimeType, Text = text }] };
        };
        options.Handlers.ListPromptsHandler = (request, token) =>
        {
            token.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new ListPromptsResult
            {
                Prompts = Prompts().Skip(Cursor(request.Params?.Cursor)).ToList()
            });
        };
        options.Handlers.GetPromptHandler = (request, token) =>
        {
            token.ThrowIfCancellationRequested();
            var parameters = request.Params ?? throw new McpException("prompts/get requires parameters.");
            var definition = Prompts().FirstOrDefault(p => p.Name == parameters.Name) ?? throw new McpException("Unknown prompt.");
            var task = parameters.Arguments is { } suppliedArguments && suppliedArguments.TryGetValue("task", out var suppliedTask)
                ? suppliedTask.ToString() : "Inspect the current drawing and report findings.";
            if (task.Length > 16_384) throw new McpException("Prompt task exceeds 16384 characters.");
            var guidance = definition.Name == "inspect_drawing"
                ? "Inspect current state, diagnostics and relevant object/reference paths; cite object identities and exact values in your findings."
                : "Inspect current state and relevant schemas, plan atomic edits, apply them through editor tools, and verify resulting objects and diagnostics. Use undo for correction when appropriate.";
            return ValueTask.FromResult(new GetPromptResult
            {
                Description = definition.Description,
                Messages = [new PromptMessage { Role = Role.User, Content = new TextContentBlock { Text = guidance + "\n\nTask: " + task } }]
            });
        };
    }

    /// <summary>The caller owns the streams and runs/disposes the returned official SDK server.</summary>
    public static McpServer Create(Stream input, Stream output, IMcpToolExecutor executor) =>
        McpServer.Create(new StreamServerTransport(input, output), CreateOptions(executor));

    private static async ValueTask<(string Text, string MimeType)> ReadResourceAsync(IMcpToolExecutor executor, string rawUri, CancellationToken token)
    {
        if (!Uri.TryCreate(rawUri, UriKind.Absolute, out var uri) || uri.Scheme != "shadow") throw new McpException("Unknown resource URI.");
        if (uri.Host == "server" && uri.AbsolutePath == "/instructions") return (Instructions, "text/plain");
        if (uri.Host == "server" && uri.AbsolutePath == "/tools")
            return (JsonSerializer.Serialize(executor.Tools.Select(t => new { t.Name, t.Description, t.InputSchema, t.ReadOnly }), McpJson.Options), "application/json");
        string tool;
        var arguments = new Dictionary<string, object?>();
        if (uri.Host == "workspace" && uri.AbsolutePath == "/state") tool = "shadow_state";
        else if (uri.Host == "workspace" && uri.AbsolutePath == "/diagnostics") tool = "shadow_diagnostics";
        else if (uri.Host == "workspace" && uri.AbsolutePath.StartsWith("/objects/", StringComparison.Ordinal))
        { tool = "shadow_read"; arguments["id"] = Uri.UnescapeDataString(uri.AbsolutePath[9..]); }
        else if (uri.Host == "schema" && uri.AbsolutePath.StartsWith("/types/", StringComparison.Ordinal))
        { tool = "shadow_schema"; arguments["type"] = Uri.UnescapeDataString(uri.AbsolutePath[7..]); }
        else throw new McpException("Unknown resource URI.");
        foreach (var parameter in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = parameter.Split('=', 2); var name = Uri.UnescapeDataString(pair[0]); var value = pair.Length > 1 ? Uri.UnescapeDataString(pair[1]) : "";
            if (name == "path") arguments[name] = value;
            else if (name is "offset" or "limit" or "depth" or "textOffset" or "textLimit" && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number)) arguments[name] = number;
            else throw new McpException("Unsupported resource query parameter.");
        }
        var result = await executor.ExecuteAsync(tool, McpJson.Element(arguments), token).ConfigureAwait(false);
        if (result.IsError) throw new McpException(result.Text);
        return (result.Text, "application/json");
    }

    private static bool Has(IMcpToolExecutor executor, string tool) => executor.Tools.Any(t => t.Name == tool);
    private static int Cursor(string? cursor) => cursor is null ? 0 :
        int.TryParse(cursor, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value >= 0 ? value : throw new McpException("Invalid pagination cursor.");
    private static Prompt[] Prompts() =>
    [
        new() { Name = "inspect_drawing", Description = "Inspect DXF/shadow structure, geometry and diagnostics.", Arguments = [new() { Name = "task", Description = "Inspection goal", Required = false }] },
        new() { Name = "edit_drawing", Description = "Perform and verify revision-checked editor transactions.", Arguments = [new() { Name = "task", Description = "Requested drawing change", Required = false }] }
    ];
}
