using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using netDxf.Shadow.Mcp;

namespace netDxf.Shadow.Studio;

public sealed partial class StudioAgentController
{
    private readonly Dictionary<string, BrowserMcpConnection> browserMcpConnections = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim browserMcpTools = new(1, 1);

    /// <summary>Creates a bounded, document-bound protocol session. The UI grants a MessagePort or WebMCP registration separately.</summary>
    public object OpenMcpSession(string expectedDocumentId)
    {
        lock (lifecycle)
        {
            EnsureIdle();
            if (expectedDocumentId != documentId)
                throw new InvalidOperationException("The active drawing changed. Enable browser MCP again to bind the new drawing.");
            if (browserMcpConnections.Count >= 8)
                throw new InvalidOperationException("At most eight browser MCP sessions may be open. Close an existing connection first.");
            var id = Guid.NewGuid().ToString("N");
            var connection = new BrowserMcpConnection(this, documentId, session.ToolDefinitions.ToArray(), companion);
            browserMcpConnections.Add(id, connection);
            return new { sessionId = id, documentId, maxMessageCharacters = BrowserMcpConnection.MaxMessageCharacters };
        }
    }

    /// <summary>One official MCP SDK JSON-RPC message. Repeated request IDs replay only a stored receipt, never the tool action.</summary>
    public Task<string?> ProcessMcpSession(string sessionId, string message)
    {
        BrowserMcpConnection connection;
        lock (lifecycle)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (sessionId is null || !browserMcpConnections.TryGetValue(sessionId, out connection!))
                throw new InvalidOperationException("The browser MCP session is closed. Connect explicitly to start another session.");
        }
        return connection.ProcessAsync(message);
    }

    public async Task CloseMcpSession(string sessionId)
    {
        BrowserMcpConnection? connection;
        lock (lifecycle)
        {
            if (sessionId is null || !browserMcpConnections.Remove(sessionId, out connection)) return;
        }
        await connection.DisposeAsync().ConfigureAwait(false);
    }

    internal async Task DisposeMcpSessionsAsync()
    {
        BrowserMcpConnection[] connections;
        lock (lifecycle)
        {
            connections = browserMcpConnections.Values.ToArray();
            browserMcpConnections.Clear();
        }
        await Task.WhenAll(connections.Select(static async connection =>
        {
            try { await connection.DisposeAsync().ConfigureAwait(false); }
            catch (Exception) { /* A transport cleanup fault cannot prevent controller credential cleanup. */ }
        })).ConfigureAwait(false);
    }

    private async ValueTask<McpToolResult> CallBrowserMcp(string expectedDocumentId, McpToolDefinition definition,
        WeakReference<StudioAgentCompanion>? expectedCompanion, JsonElement arguments, CancellationToken cancellationToken)
    {
        // Several browser agents can discover tools concurrently. Their tool calls share
        // one ordered lane, while the usual controller lifecycle remains authoritative.
        await browserMcpTools.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Task<McpToolResult> task;
            lock (lifecycle)
            {
                cancellationToken.ThrowIfCancellationRequested();
                EnsureIdle();
                if (expectedDocumentId != documentId)
                    return McpToolResult.Error("drawing_changed", "The active drawing changed. Enable browser MCP again before calling tools.");
                if (definition.Name.StartsWith("companion_", StringComparison.Ordinal) &&
                    (expectedCompanion is null || !expectedCompanion.TryGetTarget(out var grantedCompanion) || !ReferenceEquals(grantedCompanion, companion)))
                    return McpToolResult.Error("companion_changed", "The native companion grant changed. Enable browser MCP again before accessing its workspace.");
                var currentDefinition = session.ToolDefinitions.FirstOrDefault(tool => tool.Name == definition.Name);
                if (currentDefinition is null || currentDefinition.ReadOnly != definition.ReadOnly ||
                    currentDefinition.InputSchema.GetRawText() != definition.InputSchema.GetRawText())
                    return McpToolResult.Error("catalog_changed", "The tool catalog changed. Enable browser MCP again before using this tool.");
                task = session.ExecuteToolAsync(definition.Name, arguments, cancellationToken);
                work = task;
            }
            return await task.ConfigureAwait(false);
        }
        finally { browserMcpTools.Release(); }
    }

    private sealed class BrowserMcpConnection : IAsyncDisposable, IMcpToolExecutor
    {
        internal const int MaxMessageCharacters = 1_048_576;
        private const int MaxReceiptCharacters = 8_388_608;
        private readonly StudioAgentController owner;
        private readonly string document;
        private readonly McpJsonRpcSession protocol;
        private readonly CancellationTokenSource lifetime = new();
        private readonly object gate = new();
        private readonly Dictionary<string, Receipt> receipts = new(StringComparer.Ordinal);
        private readonly IReadOnlyDictionary<string, McpToolDefinition> definitions;
        private readonly WeakReference<StudioAgentCompanion>? companionGrant;
        private int pending;
        private int retainedCharacters;
        private bool closed;

        internal BrowserMcpConnection(StudioAgentController owner, string document, IReadOnlyList<McpToolDefinition> tools, StudioAgentCompanion? companion)
        {
            this.owner = owner; this.document = document; Tools = tools; definitions = tools.ToDictionary(tool => tool.Name, StringComparer.Ordinal);
            companionGrant = companion is null ? null : new(companion);
            protocol = new(this, TimeSpan.FromMinutes(12));
        }

        public IReadOnlyList<McpToolDefinition> Tools { get; }
        public ValueTask<McpToolResult> ExecuteAsync(string name, JsonElement arguments, CancellationToken cancellationToken = default) =>
            definitions.TryGetValue(name, out var definition) ? owner.CallBrowserMcp(document, definition, companionGrant, arguments, cancellationToken) :
                ValueTask.FromResult(McpToolResult.Error("tool_unavailable", "This tool was not in the granted MCP catalog. Enable browser MCP again to refresh its tools."));

        internal async Task<string?> ProcessAsync(string json)
        {
            ArgumentNullException.ThrowIfNull(json);
            if (json.Length > MaxMessageCharacters) return Error(null, -32600, "MCP message exceeds one million characters.");
            JsonElement? id = null;
            string? key = null;
            try
            {
                using var parsed = JsonDocument.Parse(json);
                if (parsed.RootElement.ValueKind == JsonValueKind.Object && parsed.RootElement.TryGetProperty("id", out var value))
                {
                    id = value.Clone();
                    if (value.ValueKind == JsonValueKind.String)
                    {
                        if (value.GetString()!.Length > 256) return Error(null, -32600, "MCP request IDs must contain at most 256 characters.");
                        key = "s:" + value.GetString();
                    }
                    else if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
                        key = "n:" + number.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
            }
            catch (JsonException) { /* The official protocol adapter returns the parse error. */ }

            if (key is null) return await protocol.ProcessAsync(json, lifetime.Token).ConfigureAwait(false);
            var signature = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
            Receipt receipt;
            var dispatch = false;
            lock (gate)
            {
                ObjectDisposedException.ThrowIf(closed, this);
                if (receipts.TryGetValue(key, out receipt!))
                {
                    if (receipt.Signature != signature)
                        return Error(id, -32600, "This MCP request ID was already used with different content. No action was executed.");
                }
                else
                {
                    if (receipts.Count >= 4096 || pending >= 16 || retainedCharacters + (pending + 1) * MaxMessageCharacters > MaxReceiptCharacters)
                        return Error(id, -32001, "This MCP session reached its request or receipt limit. Close it and connect explicitly; no action was executed.");
                    receipt = new(signature); receipts.Add(key, receipt); pending++; dispatch = true;
                }
            }
            if (dispatch) _ = CompleteAsync(json, id, receipt);
            return await receipt.Completion.Task.ConfigureAwait(false);
        }

        private async Task CompleteAsync(string json, JsonElement? id, Receipt receipt)
        {
            string? response;
            try { response = await protocol.ProcessAsync(json, lifetime.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { response = Error(id, -32800, "MCP request cancelled. Inspect Agent activity for completed changes before retrying."); }
            catch (Exception) { response = Error(id, -32000, "The MCP session ended while processing this request. Inspect Agent activity before reconnecting."); }
            if (response?.Length > MaxMessageCharacters)
                response = Error(id, -32001, "The MCP result exceeded the response budget. The action may have completed; inspect Agent activity and request a smaller result.");
            lock (gate) { pending--; retainedCharacters += response?.Length ?? 0; }
            receipt.Completion.TrySetResult(response);
        }

        public async ValueTask DisposeAsync()
        {
            lock (gate) { if (closed) return; closed = true; }
            lifetime.Cancel();
            await protocol.DisposeAsync().ConfigureAwait(false);
            // Pending continuations still inspect the lifetime token after cancellation.
            // The source owns no OS handle and is released with this connection.
        }

        private sealed class Receipt(string signature)
        {
            internal string Signature { get; } = signature;
            internal TaskCompletionSource<string?> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        private static string Error(JsonElement? id, int code, string message) =>
            JsonSerializer.Serialize(new { jsonrpc = "2.0", id, error = new { code, message } }, McpJson.Options);
    }
}
