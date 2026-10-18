using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace netDxf.Shadow.Mcp;

/// <summary>
/// Browser/in-process JSON-RPC bridge backed by the official MCP server and bounded streams.
/// One instance represents one protocol session; concurrent requests are correlated by id.
/// </summary>
public sealed class McpJsonRpcSession : IAsyncDisposable
{
    private readonly Pipe requests = new(new PipeOptions(pauseWriterThreshold: 1_048_576, resumeWriterThreshold: 524_288, useSynchronizationContext: false));
    private readonly Pipe responses = new(new PipeOptions(pauseWriterThreshold: 1_048_576, resumeWriterThreshold: 524_288, useSynchronizationContext: false));
    private readonly ConcurrentDictionary<string, TaskCompletionSource<string>> pending = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim writeGate = new(1, 1);
    private readonly SemaphoreSlim requestSlots = new(64, 64);
    private readonly CancellationTokenSource lifetime = new();
    private readonly McpServer server;
    private readonly Task runTask;
    private readonly Task pumpTask;
    private readonly TimeSpan timeout;
    private int disposed;

    public McpJsonRpcSession(IMcpToolExecutor executor, TimeSpan? requestTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(executor);
        timeout = requestTimeout ?? TimeSpan.FromMinutes(5);
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(requestTimeout));
        server = ShadowMcpServer.Create(requests.Reader.AsStream(leaveOpen: true), responses.Writer.AsStream(leaveOpen: true), executor);
        runTask = ObserveServerAsync();
        pumpTask = PumpAsync();
    }

    /// <summary>Protocol notifications or server requests; handlers must return promptly.</summary>
    public event Action<string>? NotificationReceived;

    /// <summary>
    /// Processes one JSON-RPC object and returns its correlated response, or null for a notification.
    /// Initialize and notifications/initialized are ordinary SDK messages. No network listener is required.
    /// </summary>
    public async ValueTask<string?> ProcessAsync(string json, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        ArgumentNullException.ThrowIfNull(json);
        cancellationToken.ThrowIfCancellationRequested();
        if (json.Length > 1_048_576) return Error(null, -32600, "JSON-RPC message exceeds one million characters.");
        JsonElement message;
        try { message = McpJson.Parse(json); }
        catch (JsonException) { return Error(null, -32700, "Parse error: expected one valid JSON-RPC object."); }
        if (message.ValueKind != JsonValueKind.Object || !message.TryGetProperty("jsonrpc", out var version) || version.ValueKind != JsonValueKind.String || version.GetString() != "2.0" ||
            !message.TryGetProperty("method", out var method) || method.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(method.GetString()) ||
            message.TryGetProperty("params", out var parameters) && parameters.ValueKind != JsonValueKind.Object)
            return Error(null, -32600, "Invalid JSON-RPC request: jsonrpc 2.0, method and optional object params are required.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        if (message.EnumerateObject().Any(p => !names.Add(p.Name))) return Error(null, -32600, "Duplicate JSON-RPC properties are not allowed.");
        var hasId = message.TryGetProperty("id", out var id);
        if (!hasId)
        {
            await WriteAsync(JsonSerializer.Serialize(message, McpJson.Options), cancellationToken).ConfigureAwait(false);
            return null;
        }
        if (!TryKey(id, out var key)) return Error(null, -32600, "MCP request ids must be strings or 64-bit integers.");
        await requestSlots.WaitAsync(cancellationToken).ConfigureAwait(false);
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var added = false;
        try
        {
            added = pending.TryAdd(key, completion);
            if (!added) return Error(id, -32600, "A request with this id is already running.");
            await WriteAsync(JsonSerializer.Serialize(message, McpJson.Options), cancellationToken).ConfigureAwait(false);
            try { return await completion.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false); }
            catch (Exception error) when (error is OperationCanceledException or TimeoutException)
            {
                if (!lifetime.IsCancellationRequested)
                {
                    var cancel = JsonSerializer.Serialize(new { jsonrpc = "2.0", method = "notifications/cancelled", @params = new { requestId = id, reason = error is TimeoutException ? "Client request deadline exceeded" : "Caller cancelled" } }, McpJson.Options);
                    try { await WriteAsync(cancel, lifetime.Token).ConfigureAwait(false); } catch (OperationCanceledException) { }
                }
                if (error is TimeoutException) return Error(id, -32001, "MCP request deadline exceeded. The server was notified to cancel the request.");
                throw;
            }
        }
        finally
        {
            if (added) pending.TryRemove(key, out _);
            requestSlots.Release();
        }
    }

    private async Task WriteAsync(string json, CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        await writeGate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            var flush = await requests.Writer.WriteAsync(Encoding.UTF8.GetBytes(json + "\n"), linked.Token).ConfigureAwait(false);
            if (flush.IsCompleted) throw new ObjectDisposedException(nameof(McpJsonRpcSession));
        }
        finally { writeGate.Release(); }
    }

    private async Task ObserveServerAsync()
    {
        Exception? failure = null;
        try { await server.RunAsync(lifetime.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error) { failure = error; }
        finally
        {
            foreach (var completion in pending.Values) completion.TrySetException(failure ?? new ObjectDisposedException(nameof(McpJsonRpcSession)));
            await responses.Writer.CompleteAsync(failure).ConfigureAwait(false);
        }
    }

    private async Task PumpAsync()
    {
        using var reader = new StreamReader(responses.Reader.AsStream(leaveOpen: true), Encoding.UTF8, detectEncodingFromByteOrderMarks: false);
        try
        {
            while (await reader.ReadLineAsync(lifetime.Token).ConfigureAwait(false) is { } line)
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (root.TryGetProperty("id", out var id) && TryKey(id, out var key) &&
                    (root.TryGetProperty("result", out _) || root.TryGetProperty("error", out _)) && pending.TryGetValue(key, out var completion))
                    completion.TrySetResult(line);
                else if (NotificationReceived is { } handlers)
                    foreach (Action<string> handler in handlers.GetInvocationList())
                        try { handler(line); } catch { /* A UI subscriber cannot fault the protocol pump. */ }
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error)
        {
            foreach (var completion in pending.Values) completion.TrySetException(error);
        }
        finally { await responses.Reader.CompleteAsync().ConfigureAwait(false); }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        lifetime.Cancel();
        foreach (var completion in pending.Values) completion.TrySetException(new ObjectDisposedException(nameof(McpJsonRpcSession)));
        await requests.Writer.CompleteAsync().ConfigureAwait(false);
        await server.DisposeAsync().ConfigureAwait(false);
        await Task.WhenAll(runTask, pumpTask).ConfigureAwait(false);
        await requests.Reader.CompleteAsync().ConfigureAwait(false);
        lifetime.Dispose();
        // Active callers release their slots after receiving the disposal error. Semaphores
        // intentionally remain usable until those continuations finish; neither owns a wait handle.
    }

    private static bool TryKey(JsonElement id, out string key)
    {
        if (id.ValueKind == JsonValueKind.String) { key = "s:" + id.GetString(); return true; }
        if (id.ValueKind == JsonValueKind.Number && id.TryGetInt64(out var number)) { key = "n:" + number.ToString(System.Globalization.CultureInfo.InvariantCulture); return true; }
        key = ""; return false;
    }
    private static string Error(JsonElement? id, int code, string message) => JsonSerializer.Serialize(new { jsonrpc = "2.0", id, error = new { code, message } }, McpJson.Options);
}
