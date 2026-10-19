using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;

namespace netDxf.Shadow.Agents.Acp;

/// <summary>ACP newline framing, independent full-duplex dispatch, bounded messages and outstanding requests.</summary>
internal sealed class AcpJsonRpcConnection : IAsyncDisposable
{
    private readonly Stream input, output;
    private readonly bool leaveOpen;
    private readonly AcpConnectionOptions options;
    private readonly Func<string, string, JsonElement, CancellationToken, Task<JsonElement>> requestHandler;
    private readonly Action<string, JsonElement> notification;
    private readonly SemaphoreSlim writeGate = new(1);
    private readonly CancellationTokenSource stop = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> pending = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> incoming = new();
    private readonly ConcurrentDictionary<long, Task> handlers = new();
    private long nextId, nextHandler;
    private readonly Task reader;
    private int disposed, pendingCount;
    private Exception? failure;
    internal Task Completion => reader;
    internal CancellationToken Lifetime => stop.Token;
    internal Exception? ConnectionError => Volatile.Read(ref failure);
    internal bool HasPending(JsonElement id) => id.ValueKind == JsonValueKind.String && pending.ContainsKey(id.GetString()!);

    internal AcpJsonRpcConnection(Stream input, Stream output, AcpConnectionOptions options,
        Func<string, string, JsonElement, CancellationToken, Task<JsonElement>> requestHandler,
        Action<string, JsonElement> notification, bool leaveOpen)
    {
        this.input = input; this.output = output; this.options = options; this.requestHandler = requestHandler;
        this.notification = notification; this.leaveOpen = leaveOpen;
        reader = Task.Run(ReadAsync);
    }

    internal async Task<JsonElement> RequestAsync(string method, JsonElement parameters, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        ThrowIfFailed(); stop.Token.ThrowIfCancellationRequested(); cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.Increment(ref pendingCount) > options.MaximumPendingRequests)
        {
            Interlocked.Decrement(ref pendingCount);
            throw new AcpException(-32000, "Too many pending ACP requests.");
        }
        var id = Interlocked.Increment(ref nextId).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var result = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            if (!pending.TryAdd(id, result)) throw new InvalidOperationException("Duplicate request ID.");
            await SendAsync(new { jsonrpc = "2.0", id, method, @params = parameters }, cancellationToken).ConfigureAwait(false);
            return await result.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested && ConnectionError is not null)
        {
            // Transport EOF must not be disguised as caller cancellation when the lifetime token wins the race.
            ThrowIfFailed(); throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested && !stop.IsCancellationRequested)
        {
            try { await NotifyAsync("$/cancel_request", AcpJson.Element(new { requestId = id }), stop.Token).ConfigureAwait(false); }
            catch (Exception error) when (error is IOException or OperationCanceledException or ObjectDisposedException) { }
            throw;
        }
        finally { pending.TryRemove(id, out _); Interlocked.Decrement(ref pendingCount); }
    }
    internal Task NotifyAsync(string method, JsonElement parameters, CancellationToken ct)
        => SendAsync(new { jsonrpc = "2.0", method, @params = parameters }, ct);

    private async Task SendAsync<T>(T message, CancellationToken ct)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message, AcpJson.Options);
        if (bytes.Length > options.MaximumMessageBytes) throw new AcpException(-32600, "ACP message exceeds the configured byte limit.");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, stop.Token);
        await writeGate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            await output.WriteAsync(bytes, linked.Token).ConfigureAwait(false);
            await output.WriteAsync(new byte[] { 10 }, linked.Token).ConfigureAwait(false);
            await output.FlushAsync(linked.Token).ConfigureAwait(false);
        }
        catch (Exception error) { Fail(error); throw; } // A partial frame cannot safely be followed by another request.
        finally { writeGate.Release(); }
    }

    private async Task ReadAsync()
    {
        Exception? failure = null;
        try
        {
            var buffer = new byte[8192]; using var frame = new MemoryStream();
            while (true)
            {
                var count = await input.ReadAsync(buffer, stop.Token).ConfigureAwait(false);
                if (count == 0) throw new EndOfStreamException(frame.Length == 0 ? "ACP agent disconnected." : "ACP ended inside a message.");
                var start = 0;
                for (var i = 0; i < count; i++)
                {
                    if (buffer[i] != 10) continue;
                    Append(start, i - start); start = i + 1;
                    if (frame.Length == 0) throw new AcpException(-32700, "Empty ACP message.");
                    using var document = JsonDocument.Parse(frame.ToArray(), new() { MaxDepth = 64 });
                    Dispatch(document.RootElement); frame.SetLength(0);
                }
                Append(start, count - start);
                void Append(int offset, int length)
                {
                    if (frame.Length + length > options.MaximumMessageBytes) throw new AcpException(-32600, "ACP message exceeds the configured byte limit.");
                    frame.Write(buffer, offset, length);
                }
            }
        }
        catch (Exception error) { failure = error; }
        finally
        {
            if (!stop.IsCancellationRequested) Fail(failure ?? new EndOfStreamException("ACP connection closed."));
            foreach (var task in pending.Values) task.TrySetException(ConnectionError ?? failure ?? new EndOfStreamException("ACP connection closed."));
            foreach (var cancellation in incoming.Values) CancelSafely(cancellation);
        }
    }

    private void Dispatch(JsonElement message)
    {
        if (message.ValueKind != JsonValueKind.Object || !message.TryGetProperty("jsonrpc", out var version) || version.GetString() != "2.0")
            throw new AcpException(-32600, "Invalid ACP JSON-RPC envelope.");
        var hasId = message.TryGetProperty("id", out var id);
        if (hasId && id.ValueKind is not (JsonValueKind.String or JsonValueKind.Number)) throw new AcpException(-32600, "Invalid ACP request ID.");
        if (message.TryGetProperty("method", out var methodElement))
        {
            var method = methodElement.GetString() ?? throw new AcpException(-32600, "Invalid ACP method.");
            var parameters = message.TryGetProperty("params", out var p) ? p.Clone() : AcpJson.Empty;
            if (!hasId)
            {
                if (method == "$/cancel_request" && parameters.TryGetProperty("requestId", out var cancelled))
                { if (incoming.TryGetValue(IdKey(cancelled), out var source)) CancelSafely(source); }
                else notification(method, parameters);
                return;
            }
            var idClone = id.Clone(); var key = IdKey(id);
            if (incoming.Count >= options.MaximumIncomingRequests || incoming.ContainsKey(key))
                throw new AcpException(-32600, "Too many or duplicate incoming ACP requests.");
            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
            if (!incoming.TryAdd(key, cancellation)) { cancellation.Dispose(); throw new AcpException(-32600, "Duplicate ACP request."); }
            // Request callbacks must never block the reader: permission and terminal waits need cancellation/updates to keep flowing.
            var taskId = Interlocked.Increment(ref nextHandler);
            var task = Task.Run(async () =>
            {
                try
                {
                    var result = await requestHandler(key, method, parameters, cancellation.Token).ConfigureAwait(false);
                    await SendAsync(new { jsonrpc = "2.0", id = idClone, result }, stop.Token).ConfigureAwait(false);
                }
                catch (Exception error)
                {
                    var code = error is AcpException acp ? acp.Code : error is OperationCanceledException ? -32800 : -32603;
                    var message = error is AcpException ? error.Message : code == -32800 ? "Request cancelled." : "Client request failed.";
                    try { await SendAsync(new { jsonrpc = "2.0", id = idClone, error = new { code, message } }, stop.Token).ConfigureAwait(false); }
                    catch (Exception sendError) when (sendError is IOException or OperationCanceledException or ObjectDisposedException) { }
                }
                finally { incoming.TryRemove(key, out _); cancellation.Dispose(); }
            });
            handlers[taskId] = task;
            _ = task.ContinueWith(_ => handlers.TryRemove(taskId, out var ignored), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return;
        }
        if (!hasId) throw new AcpException(-32600, "ACP response lacks an ID.");
        // Client-generated IDs are strings; integer IDs from an agent remain valid for its own requests.
        if (id.ValueKind != JsonValueKind.String || !pending.TryGetValue(id.GetString()!, out var completion)) return;
        if (message.TryGetProperty("error", out var errorValue))
        {
            var code = errorValue.GetProperty("code").GetInt32();
            var detail = errorValue.TryGetProperty("data", out var data) ? (JsonElement?)data.Clone() : null;
            completion.TrySetException(new AcpException(code, errorValue.GetProperty("message").GetString() ?? "ACP error", detail));
        }
        else if (message.TryGetProperty("result", out var result)) completion.TrySetResult(result.Clone());
        else throw new AcpException(-32600, "ACP response has neither result nor error.");
    }
    private static string IdKey(JsonElement id) => id.ValueKind == JsonValueKind.String ? "s:" + id.GetString() : "n:" + id.GetRawText();
    private void Fail(Exception error)
    {
        Interlocked.CompareExchange(ref failure, error, null);
        foreach (var task in pending.Values) task.TrySetException(ConnectionError!);
        stop.Cancel();
    }
    private void ThrowIfFailed()
    { if (ConnectionError is { } error) ExceptionDispatchInfo.Capture(error).Throw(); }
    private static void CancelSafely(CancellationTokenSource source) { try { source.Cancel(); } catch (ObjectDisposedException) { } }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        stop.Cancel();
        if (!leaveOpen) { await input.DisposeAsync().ConfigureAwait(false); if (!ReferenceEquals(input, output)) await output.DisposeAsync().ConfigureAwait(false); }
        try { await reader.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); } catch (Exception error) when (error is TimeoutException or OperationCanceledException) { }
        try { await Task.WhenAll(handlers.Values).WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); } catch (Exception) { }
        // Callbacks that ignore cancellation can still finish later; synchronization objects remain valid for their final cleanup.
    }
}
