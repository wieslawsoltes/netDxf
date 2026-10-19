using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Net.WebSockets;
using System.Text.Json;
using netDxf.Shadow.Mcp;

namespace netDxf.Shadow.AgentHost;

/// <summary>A single explicitly registered browser tab supplies the live document and studio tool catalog.</summary>
public sealed class BrowserStudioRelay : IMcpToolExecutor, IAsyncDisposable
{
    private const int MaximumMessageBytes = 1_048_576;
    private const int MaximumPendingCalls = 32;
    private StudioConnection? _connection;

    public IReadOnlyList<McpToolDefinition> Tools => Volatile.Read(ref _connection)?.Tools ?? ImmutableArray<McpToolDefinition>.Empty;
    public bool IsConnected => Volatile.Read(ref _connection) is { Socket.State: WebSocketState.Open };
    public int PendingCount => Volatile.Read(ref _connection)?.Pending.Count ?? 0;

    public async Task ConnectAsync(WebSocket socket, AgentHostOptions options, CancellationToken cancellationToken)
    {
        StudioConnection? connection = null;
        try
        {
            using var registrationTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            registrationTimeout.CancelAfter(TimeSpan.FromSeconds(10));
            using var registration = await ReadMessageAsync(socket, registrationTimeout.Token).ConfigureAwait(false);
            var root = registration.RootElement;
            if (Text(root, "type") != "register" || !options.ValidateToken(Text(root, "token")))
            {
                await CloseAsync(socket, WebSocketCloseStatus.PolicyViolation, "Invalid bridge credentials.").ConfigureAwait(false);
                return;
            }
            var tools = ReadTools(root);
            connection = new(socket, tools);
            if (Interlocked.CompareExchange(ref _connection, connection, null) is not null)
            {
                await CloseAsync(socket, WebSocketCloseStatus.PolicyViolation, "A studio is already connected. Disconnect it before registering another tab.").ConfigureAwait(false);
                return;
            }
            await connection.SendAsync(new { type = "registered", toolCount = tools.Length }, cancellationToken).ConfigureAwait(false);
            while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                using var message = await ReadMessageAsync(socket, cancellationToken).ConfigureAwait(false);
                var value = message.RootElement;
                if (Text(value, "type") != "result" || Text(value, "id") is not { Length: > 0 and <= 80 } id
                    || !value.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Object
                    || Text(result, "text") is not { } resultText)
                    throw new InvalidDataException("Expected a correlated studio tool result.");
                var isError = result.TryGetProperty("isError", out var error) && error.ValueKind == JsonValueKind.True;
                if (connection.Pending.TryRemove(id, out var pending)) pending.TrySetResult(new(resultText, isError));
                // A late response to an already canceled call is deliberately discarded, never replayed.
            }
        }
        catch (Exception error) when (error is WebSocketException or IOException or JsonException or OperationCanceledException or ArgumentException)
        {
            await CloseAsync(socket, WebSocketCloseStatus.PolicyViolation,
                error is OperationCanceledException ? "Studio connection canceled or timed out." : "Studio connection closed because its message was invalid or interrupted.").ConfigureAwait(false);
        }
        finally
        {
            if (connection is not null)
            {
                Interlocked.CompareExchange(ref _connection, null, connection);
                connection.Stop();
            }
        }
    }

    public async ValueTask<McpToolResult> ExecuteAsync(string name, JsonElement arguments, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var connection = Volatile.Read(ref _connection);
        if (connection is null) return McpToolResult.Error("studio_disconnected", "Connect the desired studio tab to the companion before invoking its tools.");
        if (!connection.Names.Contains(name)) return McpToolResult.Error("unknown_tool", "This tool is not part of the connected studio catalog. List its tools again.");
        if (arguments.ValueKind != JsonValueKind.Object || arguments.GetRawText().Length > MaximumMessageBytes / 2)
            return McpToolResult.Error("invalid_arguments", "Tool arguments must be a bounded JSON object.");
        if (!await connection.Capacity.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            return McpToolResult.Error("studio_busy", "The studio has 32 pending calls. Wait for them to complete before submitting another.");
        var id = Guid.NewGuid().ToString("N");
        var completion = new TaskCompletionSource<McpToolResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.Pending[id] = completion;
        try
        {
            await connection.SendAsync(new { type = "call", id, name, arguments }, cancellationToken).ConfigureAwait(false);
            return await completion.Task.WaitAsync(TimeSpan.FromMinutes(5), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await connection.CancelAsync(id).ConfigureAwait(false);
            throw;
        }
        catch (TimeoutException)
        {
            await connection.CancelAsync(id).ConfigureAwait(false);
            return McpToolResult.Error("studio_tool_timeout", "The studio did not report completion. The operation may have run; inspect the drawing before deciding whether to retry.");
        }
        catch (Exception error) when (error is WebSocketException or IOException or ObjectDisposedException or OperationCanceledException)
        {
            return DisconnectedResult();
        }
        finally
        {
            connection.Pending.TryRemove(id, out _);
            connection.Capacity.Release();
        }
    }

    private static ImmutableArray<McpToolDefinition> ReadTools(JsonElement registration)
    {
        if (!registration.TryGetProperty("tools", out var values) || values.ValueKind != JsonValueKind.Array
            || values.GetArrayLength() is < 1 or > 512) throw new InvalidDataException("The studio catalog must contain 1 to 512 tools.");
        var definitions = ImmutableArray.CreateBuilder<McpToolDefinition>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values.EnumerateArray())
        {
            var name = Text(value, "name");
            var description = Text(value, "description") ?? "";
            if (name is not { Length: > 0 and <= 128 } || name.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '_' and not '-' and not '.')
                || !names.Add(name) || description.Length > 4096 || !value.TryGetProperty("inputSchema", out var schema)
                || schema.ValueKind != JsonValueKind.Object || Text(schema, "type") != "object" || schema.GetRawText().Length > 131_072)
                throw new InvalidDataException("Invalid studio tool definition.");
            var readOnly = value.TryGetProperty("readOnly", out var read) && read.ValueKind == JsonValueKind.True;
            definitions.Add(new(name, description, schema.Clone(), readOnly));
        }
        return definitions.ToImmutable();
    }

    private static async Task<JsonDocument> ReadMessageAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[16_384];
        WebSocketReceiveResult received;
        do
        {
            received = await socket.ReceiveAsync(chunk, cancellationToken).ConfigureAwait(false);
            if (received.MessageType == WebSocketMessageType.Close) throw new IOException("Studio closed its connection.");
            if (received.MessageType != WebSocketMessageType.Text || buffer.Length + received.Count > MaximumMessageBytes)
                throw new InvalidDataException("Studio messages must be bounded UTF-8 JSON text.");
            buffer.Write(chunk, 0, received.Count);
        } while (!received.EndOfMessage);
        return JsonDocument.Parse(buffer.GetBuffer().AsMemory(0, checked((int)buffer.Length)), new JsonDocumentOptions { MaxDepth = 64 });
    }

    private static string? Text(JsonElement value, string property) => value.ValueKind == JsonValueKind.Object
        && value.TryGetProperty(property, out var text) && text.ValueKind == JsonValueKind.String ? text.GetString() : null;

    private static async Task CloseAsync(WebSocket socket, WebSocketCloseStatus code, string reason)
    {
        if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            try { await socket.CloseOutputAsync(code, reason, timeout.Token).ConfigureAwait(false); }
            catch (Exception error) when (error is WebSocketException or OperationCanceledException or ObjectDisposedException) { socket.Abort(); }
        }
    }

    private static McpToolResult DisconnectedResult() => McpToolResult.Error("studio_disconnected",
        "The connected studio disappeared before reporting this call's result. A dispatched edit may have completed; inspect the drawing before retrying. No call was replayed.");

    public ValueTask DisposeAsync()
    {
        var connection = Interlocked.Exchange(ref _connection, null);
        connection?.Stop(); connection?.Socket.Abort();
        return ValueTask.CompletedTask;
    }

    private sealed class StudioConnection(WebSocket socket, ImmutableArray<McpToolDefinition> tools)
    {
        private readonly SemaphoreSlim _send = new(1, 1);
        private readonly CancellationTokenSource _lifetime = new();
        private int _stopped;
        public WebSocket Socket { get; } = socket;
        public ImmutableArray<McpToolDefinition> Tools { get; } = tools;
        public HashSet<string> Names { get; } = tools.Select(tool => tool.Name).ToHashSet(StringComparer.Ordinal);
        public ConcurrentDictionary<string, TaskCompletionSource<McpToolResult>> Pending { get; } = new(StringComparer.Ordinal);
        public SemaphoreSlim Capacity { get; } = new(MaximumPendingCalls, MaximumPendingCalls);

        public async Task SendAsync<T>(T message, CancellationToken cancellationToken)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
            await _send.WaitAsync(linked.Token).ConfigureAwait(false);
            try
            {
                if (Socket.State != WebSocketState.Open) throw new IOException("The studio is disconnected.");
                await Socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(message, McpJson.Options), WebSocketMessageType.Text, true, linked.Token).ConfigureAwait(false);
            }
            finally { _send.Release(); }
        }

        public async Task CancelAsync(string id)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            try { await SendAsync(new { type = "cancel", id }, timeout.Token).ConfigureAwait(false); }
            catch (Exception error) when (error is IOException or WebSocketException or OperationCanceledException or ObjectDisposedException) { }
        }

        public void Stop()
        {
            if (Interlocked.Exchange(ref _stopped, 1) != 0) return;
            _lifetime.Cancel();
            foreach (var (id, _) in Pending)
                if (Pending.TryRemove(id, out var pending)) pending.TrySetResult(DisconnectedResult());
            // Semaphores remain alive until in-flight ExecuteAsync calls have released their permits.
        }
    }
}
