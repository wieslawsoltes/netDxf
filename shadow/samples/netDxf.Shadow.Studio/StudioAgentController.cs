using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.AI;
using netDxf.Shadow.Agents;
using netDxf.Shadow.Mcp;

namespace netDxf.Shadow.Studio;

public sealed record StudioAgentConfiguration
{
    public string Provider { get; init; } = "openai";
    public string Model { get; init; } = "";
    public string ApiKey { get; init; } = "";
    public string? Endpoint { get; init; }
    public string PermissionMode { get; init; } = "ask";
    public string DocumentId { get; init; } = "demo";
    public int MaxTurns { get; init; } = 40;
    public int MaxOutputTokens { get; init; } = 8192;
    public int ContextWindowTokens { get; init; } = 128000;
    public override string ToString() => $"{Provider}/{Model}, {PermissionMode}, document {DocumentId} (credential redacted)";
}

/// <summary>Identical lifecycle and permissions for JS interop and the local Studio host.</summary>
public sealed partial class StudioAgentController : IAsyncDisposable
{
    private readonly Func<string, McpToolRegistry> createTools;
    private readonly object lifecycle = new();
    private readonly MemoryAgentWorkspace workspace = new();
    private readonly IAgentCodeRunner? codeRunner;
    private readonly SwitchableExecutor executor;
    private string[] knownCredentials = [];
    private AgentSession session;
    private StudioAgentConfiguration configuration = new();
    private Task? work;
    private bool disposed;
    private bool transitioning;
    private string documentId = "demo";
    public string DocumentId => documentId;
    public AgentSession Session => session;
    public IMcpToolExecutor Tools => executor;

    public StudioAgentController(Func<string, McpToolRegistry> createTools,
        Func<string, CancellationToken, ValueTask>? changed = null, IAgentCodeRunner? codeRunner = null)
    {
        this.createTools = createTools;
        this.codeRunner = codeRunner;
        executor = new SwitchableExecutor(changed is null ? null : ct => changed(documentId, ct));
        session = new(new UnconfiguredChatClient(), executor, workspace: workspace, redactor: Redact);
        InstallTools("demo");
    }
    public static object Capabilities() => new
    {
        runtime = OperatingSystem.IsBrowser() ? "browser" : "native",
        providers = new[] { "openai", "anthropic", "google", "chatgpt" },
        directBrowserProviders = new[] { "openai", "anthropic", "google" },
        permissionModes = new[] { "readOnly", "ask", "fullAccess" },
        chatGpt = new { requiresCompanion = true, defaultEndpoint = "http://127.0.0.1:5091", signIn = "Sign in with ChatGPT",
            reason = "OpenAI's current ChatGPT plan authorization requires an HTTP 127.0.0.1 callback listener. A static browser tab cannot host this listener.",
            documentation = "https://developers.openai.com/siwc/token-sharing-open-source/sign-in" },
        contextEstimate = "Conservative UTF-8 estimate calibrated by provider usage",
        workspace = "Versioned transactional files with browser IndexedDB session persistence, folder import/publish and portable bundles.",
        code = new { browser = OperatingSystem.IsBrowser(), runtime = "JavaScript and WebAssembly in an isolated worker", nativeShell = false },
        mcp = new { inProcess = true, messagePort = true, webMcp = "Available when the browser exposes document.modelContext; enable in the MCP tab.",
            external = "Use the in-browser MessagePort connection or WebMCP. The optional companion exposes an HTTP /mcp endpoint for native clients." }
    };
    private void EnsureIdle()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (transitioning || session.IsRunning || work is { IsCompleted: false }) throw new InvalidOperationException("Stop the current agent operation first.");
    }
    public object Configure(StudioAgentConfiguration value)
    {
        lock (lifecycle) { EnsureIdle(); return ConfigureCore(value); }
    }
    private object ConfigureCore(StudioAgentConfiguration value, bool allowUnavailableDrawing = false, bool force = false)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (string.IsNullOrWhiteSpace(value.Provider) || string.IsNullOrWhiteSpace(value.DocumentId) || value.Model is null || value.ApiKey is null)
            throw new ArgumentException("Provider, drawing identifier, model and credential fields must be valid strings.", nameof(value));
        if (!force && !allowUnavailableDrawing && configuration == value) return new { configured = value.ApiKey.Length > 0, documentId };
        var mode = value.PermissionMode switch
        {
            "readOnly" or "ReadOnly" => AgentPermissionMode.ReadOnly,
            "ask" or "Ask" => AgentPermissionMode.Ask,
            "fullAccess" or "FullAccess" => AgentPermissionMode.FullAccess,
            _ => throw new ArgumentException("Unknown agent permission mode.")
        };
        var options = new AgentOptions { Model = value.Model, PermissionMode = mode, MaxTurns = value.MaxTurns,
            MaxOutputTokens = value.MaxOutputTokens, ContextWindowTokens = value.ContextWindowTokens };
        var providerOptions = new ShadowAgentProviderOptions
        { Provider = value.Provider, Model = value.Model, ApiKey = value.ApiKey, Endpoint = NormalizeEndpoint(value) };
        IChatClient client = value.ApiKey.Length == 0 ? new UnconfiguredChatClient() : ShadowAgentProviders.Create(providerOptions);
        AgentSession? replacement = null;
        try
        {
            // Resolve the drawing before importing a checkpoint into the shared file workspace.
            var tools = BuildTools(value.DocumentId, allowUnavailableDrawing);
            replacement = new(client, executor, options, workspace, Redact);
            var checkpoint = session.Export();
            replacement.Import(checkpoint);
            if (value.ApiKey.Length >= 8 && !knownCredentials.Contains(value.ApiKey, StringComparer.Ordinal))
                Volatile.Write(ref knownCredentials, [.. knownCredentials, value.ApiKey]);
            var previous = session; session = replacement; replacement = null;
            configuration = value; documentId = value.DocumentId; executor.Set(tools); previous.Dispose();
            return new { configured = value.ApiKey.Length > 0, provider = value.Provider, model = value.Model, permissionMode = value.PermissionMode, documentId };
        }
        catch { if (replacement is not null) replacement.Dispose(); else client.Dispose(); throw; }
    }
    private static string? NormalizeEndpoint(StudioAgentConfiguration value)
    {
        if (!string.Equals(value.Provider.Trim(), "chatgpt", StringComparison.OrdinalIgnoreCase)) return string.IsNullOrWhiteSpace(value.Endpoint) ? null : value.Endpoint;
        var endpoint = string.IsNullOrWhiteSpace(value.Endpoint) ? "http://127.0.0.1:5091" : value.Endpoint.TrimEnd('/');
        return endpoint.EndsWith("/v1", StringComparison.Ordinal) ? endpoint : endpoint + "/v1";
    }
    public object SetDocument(string id)
    {
        lock (lifecycle)
        {
        EnsureIdle();
        if (id != documentId) { InstallTools(id); configuration = configuration with { DocumentId = id }; }
        return new { documentId };
        }
    }
    private void InstallTools(string id)
    {
        var registry = BuildTools(id); documentId = id; executor.Set(registry);
    }
    private McpToolRegistry BuildTools(string id, bool allowUnavailableDrawing = false)
    {
        McpToolRegistry registry;
        try { registry = createTools(id); }
        catch (Exception error) when (allowUnavailableDrawing && error is (ArgumentException or InvalidOperationException or KeyNotFoundException))
        {
            // A closed drawing must not prevent removing provider and companion credentials.
            // The disconnected session still supports its local files and plan; selecting a
            // live drawing later resolves a fresh source-bound registry in SetDocument.
            registry = new McpToolRegistry();
        }
        AgentWorkspaceTools.Register(registry, workspace);
        if (codeRunner is not null) AgentCodeTools.Register(registry, workspace, codeRunner);
        companion?.Register(registry);
        registry.Register(new("agent_plan", "Publish or replace the task plan. States: pending, in_progress, completed; at most one in_progress.",
            McpJson.Parse("""{"type":"object","properties":{"steps":{"type":"array","maxItems":64,"items":{"type":"object","properties":{"step":{"type":"string"},"status":{"type":"string","enum":["pending","in_progress","completed"]}},"required":["step","status"],"additionalProperties":false}}},"required":["steps"],"additionalProperties":false}"""), true), (args, ct) =>
        { ct.ThrowIfCancellationRequested(); session.SetPlan(args.GetProperty("steps").Deserialize<AgentPlanStep[]>(McpJson.Options)!); return ValueTask.FromResult(McpToolResult.FromObject(session.Plan)); });
        return registry;
    }
    private string Redact(string text)
    {
        foreach (var credential in Volatile.Read(ref knownCredentials)) text = text.Replace(credential, "[credential redacted]", StringComparison.Ordinal);
        return text;
    }
    public object Start(string prompt)
    {
        lock (lifecycle)
        {
        EnsureIdle();
        ArgumentNullException.ThrowIfNull(prompt);
        if (prompt.Length > 256000) throw new ArgumentException("Prompt exceeds 256,000 characters.", nameof(prompt));
        if (configuration.ApiKey.Length == 0) throw new InvalidOperationException("Configure a provider API key or sign in with ChatGPT through the companion before starting.");
        var runId = Guid.NewGuid().ToString("N");
        work = session.RunAsync(prompt);
        return new { runId, sessionId = session.SessionId, status = "running", documentId };
        }
    }
    public object Poll(long after)
    {
        lock (lifecycle)
        {
        var result = session.Poll(after);
        var reset = after > result.LatestSequence;
        if (reset) result = session.Poll(0);
        return new { result.Events, result.Running, result.Status, result.ContextTokens, result.ContextLimit, result.SessionId,
            result.LatestSequence, result.EarliestSequence, pins = session.Pins, plan = session.Plan, documentId,
            reset,
            messages = !result.Running && (after == 0 || reset) ? session.Transcript() : null };
        }
    }
    public void Cancel() { lock (lifecycle) session.Cancel(); }
    public async Task<object> Disconnect()
    {
        Task? pending;
        lock (lifecycle)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (transitioning) throw new InvalidOperationException("An agent lifecycle operation is already in progress.");
            transitioning = true; session.Cancel(); pending = work;
        }
        try
        {
            await ObserveCompletion(pending).ConfigureAwait(false);
            lock (lifecycle)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                companion?.Dispose(); companion = null;
                var result = ConfigureCore(configuration with { ApiKey = "", DocumentId = documentId }, allowUnavailableDrawing: true);
                Volatile.Write(ref knownCredentials, []); return result;
            }
        }
        finally { lock (lifecycle) transitioning = false; }
    }
    public bool Approve(string id, bool allow) { lock (lifecycle) return session.Approve(id, allow); }
    public Task Compact()
    {
        lock (lifecycle) { EnsureIdle(); return work = session.CompactAsync(); }
    }
    public void Reset() { lock (lifecycle) { EnsureIdle(); session.Reset(); } }
    public string Export() { lock (lifecycle) return session.Export(); }
    public void Import(string json) { lock (lifecycle) { EnsureIdle(); session.Import(json); } }
    public IReadOnlyList<McpToolDefinition> Catalog() => executor.Tools;
    public async Task<McpToolResult> Call(string name, JsonElement args)
    {
        Task<McpToolResult> task;
        lock (lifecycle) { EnsureIdle(); task = session.ExecuteToolAsync(name, args); work = task; }
        return await task.ConfigureAwait(false);
    }
    public IReadOnlyList<AgentFileInfo> Files() => workspace.List();
    public IReadOnlyList<AgentFile> ExportFiles() { lock (lifecycle) return workspace.Export(); }
    public object ApplyFiles(IReadOnlyList<AgentFileChange> changes)
    {
        lock (lifecycle) { EnsureIdle(); var files = workspace.Apply(changes); return new { files, changed = changes.Count }; }
    }
    public AgentFileSlice Read(string path) => workspace.Read(path, 0, 1000000);
    public AgentFileInfo Write(string path, string content, long? expectedVersion)
    {
        lock (lifecycle)
        {
            EnsureIdle(); var file = workspace.Write(path, content, expectedVersion);
            return new(file.Path, file.Version, file.Content.Length, file.ModifiedAt);
        }
    }
    public AgentPinnedContext Pin(string text) { lock (lifecycle) return session.Pin(text); }
    public bool Unpin(string id) { lock (lifecycle) return session.Unpin(id); }
    public async ValueTask DisposeAsync()
    {
        Task? pending;
        lock (lifecycle)
        {
            if (disposed) return; disposed = true;
            session.Cancel(); pending = work;
        }
        await DisposeMcpSessionsAsync().ConfigureAwait(false);
        await ObserveCompletion(pending).ConfigureAwait(false);
        lock (lifecycle)
        {
            try { session.Dispose(); }
            finally
            {
                try { companion?.Dispose(); }
                finally
                {
                    companion = null; configuration = configuration with { ApiKey = "" };
                    Volatile.Write(ref knownCredentials, []);
                }
            }
        }
    }
    private static async Task ObserveCompletion(Task? pending)
    {
        if (pending is null) return;
        try { await pending.ConfigureAwait(false); }
        catch (Exception) { /* An earlier failed operation must not prevent credential cleanup. */ }
    }
    private sealed class SwitchableExecutor(Func<CancellationToken, ValueTask>? changed) : IMcpToolExecutor
    {
        private IMcpToolExecutor? current;
        public void Set(IMcpToolExecutor value) => Volatile.Write(ref current, value);
        public IReadOnlyList<McpToolDefinition> Tools => current?.Tools ?? [];
        public async ValueTask<McpToolResult> ExecuteAsync(string name, JsonElement arguments, CancellationToken cancellationToken = default)
        {
            var target = Volatile.Read(ref current) ?? throw new InvalidOperationException("Tool registry is not ready.");
            var result = await target.ExecuteAsync(name, arguments, cancellationToken).ConfigureAwait(false);
            if (!result.IsError && changed is not null && target.Tools.Any(t => t.Name == name && !t.ReadOnly))
                try { await changed(CancellationToken.None).ConfigureAwait(false); }
                catch (Exception) { /* A committed edit is still successful if the UI refresh is interrupted. */ }
            return result;
        }
    }
    private sealed class UnconfiguredChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.FromException<ChatResponse>(new InvalidOperationException("Choose a provider and supply a credential before inference."));
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        { await Task.Yield(); cancellationToken.ThrowIfCancellationRequested(); throw new InvalidOperationException("Choose a provider and supply a credential before inference.");
#pragma warning disable CS0162
            yield break;
#pragma warning restore CS0162
        }
        public object? GetService(Type serviceType, object? serviceKey = null) => serviceType.IsInstanceOfType(this) ? this : null;
        public void Dispose() { }
    }
}
