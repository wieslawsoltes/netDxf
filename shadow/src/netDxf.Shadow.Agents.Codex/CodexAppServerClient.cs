using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

namespace netDxf.Shadow.Agents.Codex;

/// <summary>A bidirectional JSONL client for the official Codex app-server protocol. It owns no OAuth credentials.</summary>
public sealed partial class CodexAppServerClient : IAsyncDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { MaxDepth = 64 };
    private static readonly JsonElement Empty = JsonSerializer.SerializeToElement(new { });
    private readonly Stream input, output;
    private readonly bool leaveOpen;
    private readonly int maximumMessageBytes;
    private readonly TimeSpan timeout;
    private readonly SemaphoreSlim writes = new(1), initialization = new(1);
    private readonly CancellationTokenSource lifetime = new();
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> pending = new();
    private readonly ConcurrentDictionary<long, Task> requests = new();
    private readonly HashSet<string> serverRequestIds = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> serverCancellations = new(StringComparer.Ordinal);
    private readonly Task reader;
    private Process? process;
    private Task? stderr;
    private Exception? failure;
    private long nextId, nextServerRequest;
    private int initialized, disposed;

    /// <summary>Connect injected server stdout/stdin streams. Useful for managed process hosts and deterministic fixtures.</summary>
    public CodexAppServerClient(Stream input, Stream output, int maximumMessageBytes = 4 * 1024 * 1024,
        TimeSpan? requestTimeout = null, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(input); ArgumentNullException.ThrowIfNull(output);
        if (!input.CanRead || !output.CanWrite) throw new ArgumentException("Readable server stdout and writable server stdin are required.");
        if (maximumMessageBytes is < 1024 or > 64 * 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(maximumMessageBytes));
        timeout = requestTimeout ?? TimeSpan.FromMinutes(2);
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromHours(1)) throw new ArgumentOutOfRangeException(nameof(requestTimeout));
        this.input = input; this.output = output; this.maximumMessageBytes = maximumMessageBytes; this.leaveOpen = leaveOpen;
        reader = ReadAsync();
    }

    /// <summary>Ordered protocol notifications. Handlers must return promptly; no credentials are read from local storage.</summary>
    public event Action<CodexNotification>? Notification;
    /// <summary>Server initiated approvals, questions and dynamic tools. Missing handlers return method-not-found; no automatic grants occur.</summary>
    public Func<CodexServerRequest, CancellationToken, ValueTask<JsonElement>>? RequestHandler { get; set; }
    public bool IsInitialized => Volatile.Read(ref initialized) != 0;
    public Task Completion => reader;
    public Exception? ConnectionError => failure;

    public static Task<CodexAppServerClient> StartAsync(CodexProcessOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Executable);
        if (!Path.IsPathFullyQualified(options.WorkingDirectory) || !Directory.Exists(options.WorkingDirectory))
            throw new ArgumentException("Supply an existing absolute working directory.", nameof(options));
        if (options.CodexHome is { } home && !Path.IsPathFullyQualified(home)) throw new ArgumentException("CodexHome must be absolute.", nameof(options));
        if (options.Arguments is null || options.Arguments.Count > 128 || options.Arguments.Any(value => value is null || value.Length > 65536))
            throw new ArgumentException("Invalid startup arguments.", nameof(options));
        if (options.Environment is null || options.Environment.Count > 128 || options.Environment.Any(item =>
            string.IsNullOrWhiteSpace(item.Key) || item.Key.Length > 256 || item.Key.Contains('=') || item.Key.Contains('\0') || item.Value is null || item.Value.Length > 65536 || item.Value.Contains('\0')))
            throw new ArgumentException("Invalid child environment.", nameof(options));
        var start = new ProcessStartInfo(options.Executable)
        {
            WorkingDirectory = options.WorkingDirectory, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        start.ArgumentList.Add("app-server"); start.ArgumentList.Add("--listen"); start.ArgumentList.Add("stdio://");
        foreach (var argument in options.Arguments) start.ArgumentList.Add(argument);
        // Keep runtime/path settings, excluding ambient provider keys and companion tokens.
        var inherited = start.Environment.Where(item => RuntimeEnvironment.Contains(item.Key)).ToArray();
        start.Environment.Clear();
        foreach (var item in inherited) start.Environment[item.Key] = item.Value;
        foreach (var item in options.Environment) start.Environment[item.Key] = item.Value;
        if (options.CodexHome is { } codexHome) start.Environment["CODEX_HOME"] = codexHome;
        var child = Process.Start(start) ?? throw new IOException("Could not start Codex app-server.");
        try
        {
            var client = new CodexAppServerClient(child.StandardOutput.BaseStream, child.StandardInput.BaseStream,
                options.MaximumMessageBytes, options.RequestTimeout) { process = child };
            // Drain stderr to avoid deadlock. Never log provider diagnostics or persist possible secrets.
            client.stderr = child.StandardError.BaseStream.CopyToAsync(Stream.Null, client.lifetime.Token);
            return Task.FromResult(client);
        }
        catch { try { child.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } child.Dispose(); throw; }
    }

    public async Task<JsonElement> InitializeAsync(string name, string title, string version, bool experimentalApi = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name); ArgumentException.ThrowIfNullOrWhiteSpace(title); ArgumentException.ThrowIfNullOrWhiteSpace(version);
        await initialization.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsInitialized) throw new InvalidOperationException("This connection has already initialized.");
            try
            {
                var response = await SendRequestAsync("initialize", JsonSerializer.SerializeToElement(new
                { clientInfo = new { name, title, version }, capabilities = new { experimentalApi } }, Json), cancellationToken).ConfigureAwait(false);
                await WriteAsync(new { method = "initialized", @params = Empty }, cancellationToken).ConfigureAwait(false);
                Volatile.Write(ref initialized, 1); return response;
            }
            catch (Exception error) { Fail(error); throw; } // An uncertain handshake requires a fresh connection.
        }
        finally { initialization.Release(); }
    }

    public Task<JsonElement> CallAsync(string method, JsonElement parameters, CancellationToken cancellationToken = default)
    {
        if (!IsInitialized) throw new InvalidOperationException("Initialize the Codex connection before calling methods.");
        if (method is "initialize" or "initialized") throw new ArgumentException("Use InitializeAsync for the connection handshake.", nameof(method));
        return SendRequestAsync(method, parameters, cancellationToken);
    }

    private async Task<JsonElement> SendRequestAsync(string method, JsonElement parameters, CancellationToken token)
    {
        ThrowIfClosed();
        if (string.IsNullOrWhiteSpace(method) || method.Length > 256) throw new ArgumentException("Supply a protocol method.", nameof(method));
        if (parameters.ValueKind != JsonValueKind.Object) throw new ArgumentException("Protocol parameters must be an object.", nameof(parameters));
        var id = Interlocked.Increment(ref nextId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!pending.TryAdd(id, completion)) throw new InvalidOperationException("Duplicate request ID.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(timeout);
        try
        {
            await WriteAsync(new { id, method, @params = parameters }, deadline.Token).ConfigureAwait(false);
            return await completion.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
        }
        finally { pending.TryRemove(id, out _); }
    }

    private async Task WriteAsync(object value, CancellationToken token)
    {
        ThrowIfClosed();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        token = linked.Token;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, Json);
        if (bytes.Length > maximumMessageBytes) throw new InvalidOperationException("Codex protocol request exceeds the message budget.");
        await writes.WaitAsync(token).ConfigureAwait(false);
        try
        {
            token.ThrowIfCancellationRequested();
            await output.WriteAsync(bytes, token).ConfigureAwait(false);
            await output.WriteAsync(new byte[] { (byte)'\n' }, token).ConfigureAwait(false);
            await output.FlushAsync(token).ConfigureAwait(false);
        }
        catch (Exception error) { Fail(error); throw; } // A partial JSONL write cannot safely be followed by another request.
        finally { writes.Release(); }
    }

    private async Task ReadAsync()
    {
        try
        {
            var buffer = new byte[8192]; using var line = new MemoryStream();
            int count;
            while ((count = await input.ReadAsync(buffer, lifetime.Token).ConfigureAwait(false)) != 0)
                for (var i = 0; i < count; i++)
                {
                    if (buffer[i] == '\n')
                    {
                        if (line.Length > 0) { Receive(line.ToArray()); line.SetLength(0); }
                    }
                    else
                    {
                        if (line.Length >= maximumMessageBytes) throw new IOException("Codex protocol message exceeds the byte budget.");
                        line.WriteByte(buffer[i]);
                    }
                }
            throw new EndOfStreamException(line.Length > 0 ? "Codex closed during a protocol message." : "Codex app-server disconnected.");
        }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
        {
            Fail(error);
        }
    }

    private void Receive(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes, new() { MaxDepth = 64 });
        var message = document.RootElement;
        if (message.ValueKind != JsonValueKind.Object) throw new IOException("Codex protocol message must be an object.");
        var hasId = message.TryGetProperty("id", out var id);
        if (message.TryGetProperty("method", out var method))
        {
            var name = method.GetString() ?? throw new IOException("Missing Codex protocol method.");
            var parameters = message.TryGetProperty("params", out var value) ? value.Clone() : Empty;
            if (!hasId)
            {
                if (name == "serverRequest/resolved" && parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty("requestId", out var resolved)
                    && serverCancellations.TryGetValue(ServerRequestKey(resolved), out var cancellation))
                { try { cancellation.Cancel(); } catch (ObjectDisposedException) { /* The response completed concurrently. */ } }
                if (Notification is { } notification)
                    foreach (Action<CodexNotification> observer in notification.GetInvocationList())
                        try { observer(new(name, parameters)); } catch (Exception) { /* Observers must not close the protocol transport. */ }
                return;
            }
            if (id.ValueKind is not (JsonValueKind.Number or JsonValueKind.String)) throw new IOException("Invalid Codex request identifier.");
            if (!serverRequestIds.Add(ServerRequestKey(id))) throw new IOException("Codex repeated a server request ID; no operation was replayed.");
            if (serverRequestIds.Count > 32768) throw new IOException("Codex server request budget exhausted; reconnect explicitly.");
            if (requests.Count >= 64) throw new IOException("Too many pending Codex server requests.");
            var sequence = Interlocked.Increment(ref nextServerRequest);
            var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            serverCancellations[ServerRequestKey(id)] = requestCancellation;
            // Dispatch outside the reader, including a handler's synchronous prelude.
            // Otherwise a blocking approval UI prevents resolved/interrupt notifications
            // from reaching the very cancellation token that can unblock it.
            var request = new CodexServerRequest(id.Clone(), name, parameters);
            var task = Task.Run(() => RespondAsync(request, requestCancellation));
            requests[sequence] = task;
            _ = task.ContinueWith(completed => { _ = completed.Exception; requests.TryRemove(sequence, out var ignored); }, CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
        else if (hasId && id.ValueKind == JsonValueKind.Number && id.TryGetInt64(out var requestId) && pending.TryGetValue(requestId, out var completion))
        {
            if (message.TryGetProperty("error", out var error))
                completion.TrySetException(new CodexRpcException(error.GetProperty("code").GetInt32(), error.GetProperty("message").GetString() ?? "Codex request failed.",
                    error.TryGetProperty("data", out var data) ? data.Clone() : null));
            else if (message.TryGetProperty("result", out var result)) completion.TrySetResult(result.Clone());
            else throw new IOException("Codex response has neither result nor error.");
        }
        // A response to an expired request is stale and must not be replayed.
    }

    private async Task RespondAsync(CodexServerRequest request, CancellationTokenSource cancellation)
    {
        try
        {
            if (RequestHandler is not { } handler) throw new CodexRpcException(-32601, "This client has no handler for the requested operation.");
            var result = await handler(request, cancellation.Token).ConfigureAwait(false);
            cancellation.Token.ThrowIfCancellationRequested();
            await WriteAsync(new { id = request.Id, result }, cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
        {
            try
            {
                await WriteAsync(new { id = request.Id, error = new { code = error is CodexRpcException rpc ? rpc.Code : -32603,
                    message = error is CodexRpcException ? error.Message : "The client could not complete this operation." } }, lifetime.Token).ConfigureAwait(false);
            }
            catch (Exception) when (lifetime.IsCancellationRequested || Volatile.Read(ref disposed) != 0) { }
        }
        finally { serverCancellations.TryRemove(ServerRequestKey(request.Id), out _); cancellation.Dispose(); }
    }

    // JSON escape spelling is not part of a string request ID's identity.
    private static string ServerRequestKey(JsonElement id) => id.ValueKind == JsonValueKind.String ? "s:" + id.GetString() : "n:" + id.GetRawText();

    private void ThrowIfClosed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (failure is { } error) throw new IOException("The Codex connection is closed; no operation was replayed.", error);
    }

    private void Fail(Exception error)
    {
        Interlocked.CompareExchange(ref failure, error, null);
        foreach (var item in pending.Values) item.TrySetException(error);
        lifetime.Cancel();
    }

    private static readonly HashSet<string> RuntimeEnvironment = new(StringComparer.OrdinalIgnoreCase)
    {
        "PATH", "HOME", "USER", "LOGNAME", "TMPDIR", "TMP", "TEMP", "LANG", "LC_ALL", "LC_CTYPE",
        "SystemRoot", "WINDIR", "USERPROFILE", "HOMEDRIVE", "HOMEPATH", "APPDATA", "LOCALAPPDATA", "PATHEXT", "COMSPEC",
        "XDG_CONFIG_HOME", "XDG_CACHE_HOME", "XDG_DATA_HOME", "XDG_RUNTIME_DIR", "DOTNET_ROOT", "DOTNET_ROOT_X64", "DOTNET_ROOT_ARM64",
    };

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        lifetime.Cancel();
        foreach (var item in pending.Values) item.TrySetException(new ObjectDisposedException(nameof(CodexAppServerClient)));
        if (process is { } child) { try { if (!child.HasExited) child.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } }
        if (!leaveOpen) { await input.DisposeAsync().ConfigureAwait(false); await output.DisposeAsync().ConfigureAwait(false); }
        try { await reader.ConfigureAwait(false); } catch (OperationCanceledException) { }
        if (stderr is { } drain) { try { await drain.ConfigureAwait(false); } catch (Exception error) when (error is OperationCanceledException or IOException or ObjectDisposedException) { } }
        process?.Dispose();
        // Application request handlers must honor cancellation. Do not wait indefinitely for a UI prompt.
    }
}
