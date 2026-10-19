using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using netDxf.Shadow.Agents;
using netDxf.Shadow.Mcp;
using netDxf.Shadow.Agents.Acp;
using netDxf.Shadow.Agents.Codex;

namespace netDxf.Shadow.AgentHost;

public sealed record ExternalAgentEvent(long Sequence, string Kind, string Method, JsonElement Data);
public sealed record ExternalAgentCall(string Method, JsonElement Parameters, IReadOnlyList<DrawingObjectReference>? References = null);
public sealed record ExternalAgentReply(string RequestId, JsonElement Result);

/// <summary>Server-owned limits; browser requests cannot change these settings.</summary>
public sealed record ExternalAgentHostSettings
{
    public TimeSpan ApprovalTimeout { get; init; } = TimeSpan.FromMinutes(10);
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromMinutes(2);
    public int MaximumRequestBytes { get; init; } = 128 * 1024;
    public int MaximumEventBytes { get; init; } = 256 * 1024;
    public int MaximumBufferedEventBytes { get; init; } = 2 * 1024 * 1024;
    internal void Validate()
    {
        if (ApprovalTimeout <= TimeSpan.Zero || ApprovalTimeout > TimeSpan.FromMinutes(30)
            || RequestTimeout <= TimeSpan.Zero || RequestTimeout > TimeSpan.FromMinutes(10)
            || MaximumRequestBytes is < 1024 or > 1024 * 1024
            || MaximumEventBytes is < 1024 or > 1024 * 1024
            || MaximumBufferedEventBytes < MaximumEventBytes || MaximumBufferedEventBytes > 16 * 1024 * 1024)
            throw new ArgumentException("External agent limits must be finite and within the supported bounds.");
    }
}

/// <summary>Hosts separately configured agent processes; their own tool loop remains outside AgentSession.</summary>
public sealed partial class ExternalAgentHost : IAsyncDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly AgentHostOptions options;
    private readonly ExternalAgentHostSettings settings;
    private readonly BrowserStudioRelay? drawing;
    private readonly SemaphoreSlim lifecycle = new(1, 1);
    private readonly object sync = new();
    private readonly Queue<(ExternalAgentEvent Event, int Bytes)> events = new();
    private readonly Dictionary<string, Pending> pending = new(StringComparer.Ordinal);
    private sealed record Pending(State Owner, string Method, JsonElement Parameters, TaskCompletionSource<JsonElement> Completion, CancellationToken Cancellation, DateTimeOffset Expires);
    private sealed class State(string protocol)
    {
        internal string Protocol { get; } = protocol;
        internal string Status = "starting";
        internal readonly CancellationTokenSource Lifetime = new();
        internal CodexAppServerClient? Codex;
        internal AcpClient? Acp;
        internal int Calls;
        internal bool Connected => Status == "connected" && (Codex is { Completion.IsCompleted: false } || Acp is { IsConnected: true });
    }
    private State? current;
    private string? bridgeToken;
    private long sequence;
    private int bufferedBytes, disposed;

    public ExternalAgentHost(AgentHostOptions options, ExternalAgentHostSettings? settings = null, BrowserStudioRelay? drawing = null)
    {
        this.drawing = drawing; this.options = options; this.settings = settings ?? new(); this.settings.Validate();
    }
    public object Profiles => new { workspace = options.Workspace, profiles = new[] {
        new { id = "codex", name = "Codex app-server", enabled = options.CodexExecutable is not null },
        new { id = "acp", name = "ACP coding agent", enabled = options.AcpExecutable is not null } } };

    public async Task<object> StartAsync(string profile, string token, CancellationToken cancellationToken)
    {
        if (!options.ValidateToken(token)) throw new ArgumentException("Supply the companion's bridge token.");
        if (options.Workspace is null) throw new InvalidOperationException("Configure a companion workspace before starting a coding agent.");
        if (!(profile == "codex" && options.CodexExecutable is not null || profile == "acp" && options.AcpExecutable is not null))
            throw new ArgumentException("This agent profile is not enabled. Configure its executable at companion startup.");
        await lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
            await StopCoreAsync().ConfigureAwait(false);
            var state = new State(profile);
            lock (sync) { current = state; bridgeToken = token; }
            using var startup = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, state.Lifetime.Token);
            try
            {
                JsonElement initialization;
                if (profile == "codex")
                {
                    state.Codex = await CodexAppServerClient.StartAsync(new CodexProcessOptions {
                        Executable = options.CodexExecutable!, WorkingDirectory = options.Workspace, RequestTimeout = settings.RequestTimeout,
                        Arguments = ["-c", "mcp_servers.shadow_studio.url=\"http://127.0.0.1:" + options.Port + "/mcp\"",
                            "-c", "mcp_servers.shadow_studio.bearer_token_env_var=\"SHADOW_STUDIO_MCP_TOKEN\""],
                        Environment = new Dictionary<string, string> { ["SHADOW_STUDIO_MCP_TOKEN"] = token } }, startup.Token).ConfigureAwait(false);
                    state.Codex.Notification += value => Emit(state, "notification", value.Method, value.Parameters);
                    state.Codex.RequestHandler = (request, ct) => new(AskAsync(state, request.Method, request.Parameters, ct));
                    initialization = await state.Codex.InitializeAsync("netdxf_shadow_studio", "netDxf Shadow Studio", "1.0.0", cancellationToken: startup.Token).ConfigureAwait(false);
                }
                else
                {
                    state.Acp = await AcpClient.StartAsync(new(options.AcpExecutable!, options.Workspace, options.AcpArguments), new AcpClientServices
                    {
                        Notification = (method, data) => Emit(state, "notification", method, data),
                        RequestPermissionAsync = async (request, ct) =>
                        {
                            var response = await AskAsync(state, "session/request_permission", request.Raw, ct).ConfigureAwait(false);
                            return response.TryGetProperty("optionId", out var choice) && choice.ValueKind == JsonValueKind.String ? choice.GetString() : null;
                        }
                    }, new AcpConnectionOptions { RequestTimeout = settings.RequestTimeout, PermissionTimeout = settings.ApprovalTimeout }, startup.Token).ConfigureAwait(false);
                    initialization = state.Acp.Initialization;
                }
                lock (sync) state.Status = "connected";
                _ = ObserveCompletionAsync(state, state.Codex?.Completion ?? state.Acp!.Completion);
                return new { protocol = profile, initialization = Clean(initialization) };
            }
            catch { await StopCoreAsync().ConfigureAwait(false); throw; }
        }
        finally { lifecycle.Release(); }
    }

    public object Poll(long after)
    {
        if (after < 0) throw new ArgumentOutOfRangeException(nameof(after));
        lock (sync) return new { protocol = current?.Protocol, status = current?.Status ?? "stopped", connected = current?.Connected ?? false,
            sequence, truncated = after < (events.Count == 0 ? sequence : events.Peek().Event.Sequence - 1),
            events = events.Where(e => e.Event.Sequence > after).Select(e => e.Event).ToArray(),
            pending = pending.Select(p => new { requestId = p.Key, method = p.Value.Method, parameters = Clean(p.Value.Parameters) }).ToArray() };
    }

    public async Task<JsonElement> CallAsync(ExternalAgentCall call, CancellationToken cancellationToken)
    {
        RequireObject(call.Parameters, settings.MaximumRequestBytes);
        if (call.Method is not { Length: > 0 and <= 128 }) throw new ArgumentException("Supply a supported agent method.");
        State state; CancellationTokenSource linked;
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed != 0, this);
            state = current is { Connected: true } active ? active : throw new InvalidOperationException("Start an external agent before sending requests; its process must still be connected.");
            if (state.Calls >= 32) throw new InvalidOperationException("Too many pending external agent calls.");
            linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, state.Lifetime.Token); state.Calls++;
        }
        using (linked)
        try
        {
            if (call.References is { Count: > 0 })
            {
                if (call.Method is not ("turn/start" or "session/prompt")) throw new ArgumentException("Drawing references belong to a prompt.");
                if (drawing is null) throw new InvalidOperationException("Connect the Studio drawing relay before attaching references.");
                var references = await DrawingReferences.ValidateCurrentAsync(drawing, call.References, linked.Token).ConfigureAwait(false);
                var property = call.Method == "turn/start" ? "input" : "prompt";
                var content = TextContent(call.Parameters, property);
                var text = string.Join("\n", content.Select(part => part!["text"]!.GetValue<string>()));
                var parameters = JsonNode.Parse(call.Parameters.GetRawText())!.AsObject();
                parameters[property] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = new AgentPrompt(text, references).ToContextText() });
                call = call with { Parameters = JsonSerializer.SerializeToElement(parameters, Json) };
            }
            JsonElement result;
            if (state.Codex is { } codex)
                result = await codex.CallAsync(call.Method, CodexParameters(call), linked.Token).ConfigureAwait(false);
            else if (call.Method == "session/cancel")
            {
                await state.Acp!.CancelSessionAsync(RequiredString(call.Parameters, "sessionId"), linked.Token).ConfigureAwait(false);
                result = JsonSerializer.SerializeToElement(new { });
            }
            else result = await state.Acp!.RequestAsync(call.Method, AcpParameters(state, call), linked.Token).ConfigureAwait(false);
            return Clean(result);
        }
        finally { lock (sync) state.Calls--; }
    }

    private async Task ObserveCompletionAsync(State state, Task completion)
    {
        try { await completion.ConfigureAwait(false); } catch (Exception) { /* Transport details may contain private agent data. */ }
        lock (sync)
        {
            if (current != state || state.Status == "stopped") return;
            state.Status = "disconnected";
            Emit(state, "status", "host/disconnected", JsonSerializer.SerializeToElement(new { message = "The agent process disconnected. Start the profile again to reconnect." }));
            CancelPending(state);
            state.Lifetime.Cancel();
        }
    }

    internal string CleanError(string message)
    {
        lock (sync)
        {
            if (bridgeToken is { Length: > 0 } secret) message = message.Replace(secret, "[companion credential]", StringComparison.Ordinal);
            return message.Length > 4096 ? message[..4096] : message;
        }
    }
    private JsonElement Clean(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Undefined) return JsonSerializer.SerializeToElement<object?>(null);
        string? secret; lock (sync) secret = bridgeToken;
        if (string.IsNullOrEmpty(secret)) return value.Clone();
        JsonNode? Visit(JsonNode? node) => node switch {
            JsonValue v when v.TryGetValue<string>(out var text) => JsonValue.Create(text.Replace(secret, "[companion credential]", StringComparison.Ordinal)),
            JsonObject obj => new JsonObject(obj.Select(p => KeyValuePair.Create(p.Key.Replace(secret, "[companion credential]", StringComparison.Ordinal), Visit(p.Value)))),
            JsonArray array => new JsonArray(array.Select(Visit).ToArray()), _ => node?.DeepClone() };
        return JsonSerializer.SerializeToElement(Visit(JsonNode.Parse(value.GetRawText())), Json);
    }
    private void Emit(State state, string kind, string method, JsonElement data)
    {
        lock (sync)
        {
            if (current != state) return;
            var clean = Clean(data); var bytes = Encoding.UTF8.GetByteCount(clean.GetRawText());
            if (bytes > settings.MaximumEventBytes)
            {
                clean = JsonSerializer.SerializeToElement(new { truncated = true, originalBytes = bytes, message = "Agent event exceeded the display budget; use history pagination to inspect persisted items." });
                bytes = Encoding.UTF8.GetByteCount(clean.GetRawText());
            }
            method = CleanError(method);
            var item = new ExternalAgentEvent(++sequence, kind, method.Length > 256 ? method[..256] : method, clean);
            bytes += Encoding.UTF8.GetByteCount(item.Method) + 128;
            events.Enqueue((item, bytes)); bufferedBytes += bytes;
            while (events.Count > 512 || bufferedBytes > settings.MaximumBufferedEventBytes) bufferedBytes -= events.Dequeue().Bytes;
        }
    }
    public async Task StopAsync()
    {
        // Stop also interrupts a handshake holding the lifecycle gate.
        lock (sync) current?.Lifetime.Cancel();
        await lifecycle.WaitAsync().ConfigureAwait(false);
        try { await StopCoreAsync().ConfigureAwait(false); } finally { lifecycle.Release(); }
    }
    private void CancelPending(State state)
    {
        foreach (var p in pending.Where(p => p.Value.Owner == state).ToArray())
        { pending.Remove(p.Key); p.Value.Completion.TrySetCanceled(); }
    }
    private async Task StopCoreAsync()
    {
        State? state;
        lock (sync)
        {
            state = current; current = null;
            events.Clear(); bufferedBytes = 0;
            if (state is not null) { state.Status = "stopped"; CancelPending(state); state.Lifetime.Cancel(); }
        }
        if (state?.Codex is { } oldCodex) await oldCodex.DisposeAsync().ConfigureAwait(false);
        if (state?.Acp is { } oldAcp) await oldAcp.DisposeAsync().ConfigureAwait(false);
        state?.Lifetime.Dispose();
    }
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        await StopAsync().ConfigureAwait(false);
        // A concurrent caller may already be waiting; keeping this tiny gate alive lets it observe disposed safely.
    }
}
