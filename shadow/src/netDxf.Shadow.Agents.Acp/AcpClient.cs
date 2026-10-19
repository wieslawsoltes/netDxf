using System.Collections.Concurrent;
using System.Text.Json;

namespace netDxf.Shadow.Agents.Acp;

/// <summary>Stable ACP v1 session client. The connected coding agent owns inference and tool execution.</summary>
public sealed partial class AcpClient : IAsyncDisposable
{
    private readonly AcpConnectionOptions options;
    private readonly AcpClientServices services;
    private readonly AcpJsonRpcConnection rpc;
    private readonly ConcurrentDictionary<string, Session> sessions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, (string Session, CancellationTokenSource Cancellation)> permissions = new();
    private readonly SemaphoreSlim initializeGate = new(1);
    private Func<ValueTask>? disposeProcess;
    private int initialized, disposed;
    public JsonElement Initialization { get; private set; }
    public Task Completion => rpc.Completion;
    /// <summary>The first transport failure, if any, including unexpected EOF or a partial frame write.</summary>
    public Exception? ConnectionError => rpc.ConnectionError;
    public bool IsConnected => Volatile.Read(ref disposed) == 0 && !rpc.Lifetime.IsCancellationRequested;

    /// <summary>Connects readable agent stdout and writable agent stdin. Call InitializeAsync before session methods.</summary>
    public AcpClient(Stream agentOutput, Stream agentInput, AcpClientServices? services = null,
        AcpConnectionOptions? options = null, bool leaveOpen = false)
    {
        this.options = options ?? new(); this.options.Validate(); this.services = services ?? new();
        if (!agentOutput.CanRead || !agentInput.CanWrite) throw new ArgumentException("ACP requires readable output and writable input streams.");
        rpc = new(agentOutput, agentInput, this.options, HandleRequestAsync, OnNotification, leaveOpen);
    }

    public async Task<JsonElement> InitializeAsync(CancellationToken cancellationToken = default)
    {
        await initializeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (initialized != 0) return Initialization;
            var response = await TimedRequestAsync("initialize", AcpJson.Element(new
            {
                protocolVersion = 1,
                clientInfo = new { name = "netdxf-shadow", title = "netDxf Shadow Studio", version = "1.0.0" },
                clientCapabilities = new
                {
                    fs = new { readTextFile = services.ReadTextFileAsync is not null, writeTextFile = services.WriteTextFileAsync is not null },
                    terminal = services.TerminalRequestAsync is not null,
                    elicitation = new { form = services.ElicitFormAsync is null ? null : new { }, url = services.ElicitUrlAsync is null ? null : new { } },
                    session = new { configOptions = new { boolean = new { } }, notices = new { } }
                }
            }), cancellationToken).ConfigureAwait(false);
            if (response.ValueKind != JsonValueKind.Object || !response.TryGetProperty("protocolVersion", out var version) || version.GetInt32() != 1)
            {
                await DisposeAsync().ConfigureAwait(false);
                throw new AcpException(-32000, "The agent selected an unsupported ACP version. This client implements stable ACP v1.");
            }
            Initialization = response; Volatile.Write(ref initialized, 1); return response;
        }
        finally { initializeGate.Release(); }
    }

    /// <summary>Calls a supported ACP method, preserving its protocol-shaped result and metadata.</summary>
    public async Task<JsonElement> RequestAsync(string method, JsonElement parameters, CancellationToken cancellationToken = default)
    {
        if (initialized == 0) throw new InvalidOperationException("Initialize the ACP connection first.");
        if (parameters.ValueKind != JsonValueKind.Object) throw new AcpException(-32602, "ACP parameters must be an object.");
        var capabilities = Initialization.TryGetProperty("agentCapabilities", out var caps) ? caps : AcpJson.Empty;
        Session? session = null; string? sessionId = null;
        switch (method)
        {
            case "authenticate":
                var methodId = AcpJson.RequiredString(parameters, "methodId");
                var advertised = Initialization.TryGetProperty("authMethods", out var methods) && methods.ValueKind == JsonValueKind.Array
                    ? methods.EnumerateArray().FirstOrDefault(m => AcpJson.RequiredString(m, "id") == methodId) : default;
                if (advertised.ValueKind == JsonValueKind.Undefined) throw new AcpException(-32602, "Authentication method was not advertised by this agent.");
                if (advertised.TryGetProperty("type", out var authType) && authType.ValueKind != JsonValueKind.Null && authType.GetString() != "agent")
                    throw new AcpException(-32602, "This authentication method requires an external interactive flow; it cannot be sent to authenticate.");
                break;
            case "logout":
                RequireCapability(capabilities, "auth", "logout");
                if (sessions.Values.Any(s => Volatile.Read(ref s.Running) != 0)) throw new AcpException(-32602, "Cancel active turns before logging out.");
                break;
            case "session/new": ValidateSetup(parameters, capabilities); break;
            case "session/load":
            case "session/resume":
                if (method == "session/load") RequireCapability(capabilities, "loadSession");
                else RequireCapability(capabilities, "sessionCapabilities", "resume");
                ValidateSetup(parameters, capabilities, method == "session/resume");
                sessionId = AcpJson.RequiredString(parameters, "sessionId");
                if (sessions.TryGetValue(sessionId, out var loading) && Volatile.Read(ref loading.Running) != 0)
                    throw new AcpException(-32602, "Cancel the active turn before loading its session.");
                break;
            case "session/close":
                RequireCapability(capabilities, "sessionCapabilities", "close");
                sessionId = AcpJson.RequiredString(parameters, "sessionId");
                if (!sessions.ContainsKey(sessionId)) throw new AcpException(-32602, "Unknown ACP session.");
                await CancelSessionAsync(sessionId, cancellationToken).ConfigureAwait(false);
                break;
            case "session/list":
                RequireCapability(capabilities, "sessionCapabilities", "list");
                if (parameters.TryGetProperty("cwd", out var cwd) && cwd.ValueKind != JsonValueKind.Null) AbsolutePath(cwd.GetString(), "cwd");
                break;
            case "session/delete":
                RequireCapability(capabilities, "sessionCapabilities", "delete");
                sessionId = AcpJson.RequiredString(parameters, "sessionId");
                if (sessions.TryGetValue(sessionId, out var deleting) && Volatile.Read(ref deleting.Running) != 0)
                    throw new AcpException(-32602, "Cancel the active turn before deleting its session.");
                break;
            case "session/prompt":
            case "session/set_mode":
            case "session/set_config_option":
                sessionId = AcpJson.RequiredString(parameters, "sessionId");
                if (!sessions.TryGetValue(sessionId, out session)) throw new AcpException(-32602, "Create or load the ACP session before using it.");
                if (method == "session/prompt") { ValidatePrompt(parameters, capabilities); return await PromptAsync(sessionId, session, parameters, cancellationToken).ConfigureAwait(false); }
                if (method == "session/set_mode") ValidateMode(session.Metadata, AcpJson.RequiredString(parameters, "modeId"));
                else ValidateConfig(session.Metadata, parameters);
                break;
            default: throw new AcpException(-32601, "Unsupported ACP client method: " + method);
        }
        var result = await TimedRequestAsync(method, parameters, cancellationToken).ConfigureAwait(false);
        if (method == "session/new")
        {
            sessionId = AcpJson.RequiredString(result, "sessionId");
            if (!sessions.TryAdd(sessionId, new(result))) throw new AcpException(-32600, "Agent reused an active session ID.");
        }
        else if (method is "session/load" or "session/resume") sessions[sessionId!] = new(result);
        else if (method is "session/delete" or "session/close") sessions.TryRemove(sessionId!, out _);
        else if (method == "session/set_config_option" && result.TryGetProperty("configOptions", out var config))
            session!.Metadata = Merge(session.Metadata, "configOptions", config);
        else if (method == "logout") sessions.Clear();
        return result;
    }

    public async Task CancelSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (!sessions.TryGetValue(sessionId, out var session)) throw new AcpException(-32602, "Unknown ACP session.");
        Volatile.Write(ref session.Cancelling, 1);
        // ACP mandates answering pending permissions with the cancelled outcome immediately, independently of agent request cancellation.
        foreach (var request in permissions.Values) if (request.Session == sessionId) CancelSafely(request.Cancellation);
        await rpc.NotifyAsync("session/cancel", AcpJson.Element(new { sessionId }), cancellationToken).ConfigureAwait(false);
    }

    private async Task<JsonElement> PromptAsync(string sessionId, Session session, JsonElement parameters, CancellationToken ct)
    {
        if (Interlocked.CompareExchange(ref session.Running, 1, 0) != 0) throw new AcpException(-32602, "This ACP session already has a running prompt.");
        Volatile.Write(ref session.Cancelling, 0);
        try
        {
            ct.ThrowIfCancellationRequested();
            // A prompt response closes the turn. Keep the request alive while cancellation is acknowledged.
            var response = rpc.RequestAsync("session/prompt", parameters, rpc.Lifetime);
            try { return await response.WaitAsync(ct).ConfigureAwait(false); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested && IsConnected)
            {
                await CancelSessionAsync(sessionId, rpc.Lifetime).ConfigureAwait(false);
                try { await response.WaitAsync(options.CancellationGrace).ConfigureAwait(false); }
                catch (TimeoutException) { await DisposeAsync().ConfigureAwait(false); }
                catch (AcpException) { /* A cancelled request may finish with the protocol cancellation error. */ }
                throw;
            }
        }
        finally { Volatile.Write(ref session.Running, 0); }
    }

    private async Task<JsonElement> TimedRequestAsync(string method, JsonElement parameters, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct, rpc.Lifetime);
        timeout.CancelAfter(options.RequestTimeout);
        return await rpc.RequestAsync(method, parameters, timeout.Token).ConfigureAwait(false);
    }

    private async Task<JsonElement> HandleRequestAsync(string id, string method, JsonElement parameters, CancellationToken ct)
    {
        if (method == "elicitation/create")
        {
            var mode = AcpJson.RequiredString(parameters, "mode");
            var handler = mode switch { "form" => services.ElicitFormAsync, "url" => services.ElicitUrlAsync, _ => null };
            if (handler is null) throw new AcpException(-32601, "This elicitation mode was not advertised by the client.");
            string? sessionId = null;
            Session? elicitationSession = null;
            if (parameters.TryGetProperty("sessionId", out var s))
            {
                sessionId = s.GetString();
                if (sessionId is null || !sessions.TryGetValue(sessionId, out elicitationSession)) throw new AcpException(-32602, "Unknown elicitation session.");
                if (Volatile.Read(ref elicitationSession.Cancelling) != 0) return AcpJson.Element(new { action = "cancel" });
            }
            else if (!parameters.TryGetProperty("requestId", out var requestId) || !rpc.HasPending(requestId))
                throw new AcpException(-32602, "Elicitation must reference an active session or request.");
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cancellation.CancelAfter(options.PermissionTimeout);
            if (sessionId is not null) permissions[id] = (sessionId, cancellation);
            try
            {
                bool Cancelled() => cancellation.IsCancellationRequested || elicitationSession is not null && Volatile.Read(ref elicitationSession.Cancelling) != 0;
                // Cancellation may race registration or a synchronous callback. WaitAsync
                // returns an already-completed task even when its token was cancelled.
                if (Cancelled()) return AcpJson.Element(new { action = "cancel" });
                var response = await handler(parameters, cancellation.Token).WaitAsync(cancellation.Token).ConfigureAwait(false);
                if (Cancelled()) return AcpJson.Element(new { action = "cancel" });
                if (AcpJson.RequiredString(response, "action") is not ("accept" or "decline" or "cancel")) throw new AcpException(-32602, "Invalid elicitation action.");
                if (mode == "url" && response.TryGetProperty("content", out var content) && content.ValueKind != JsonValueKind.Null)
                    throw new AcpException(-32602, "URL elicitation responses must not carry form content.");
                return response;
            }
            catch (OperationCanceledException) { return AcpJson.Element(new { action = "cancel" }); }
            finally { permissions.TryRemove(id, out _); }
        }
        if (method == "session/request_permission")
        {
            var sessionId = AcpJson.RequiredString(parameters, "sessionId");
            var cancelled = AcpJson.Element(new { outcome = new { outcome = "cancelled" } });
            if (!sessions.TryGetValue(sessionId, out var session) || Volatile.Read(ref session.Cancelling) != 0 || services.RequestPermissionAsync is null) return cancelled;
            if (!parameters.TryGetProperty("options", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() is < 1 or > 128
                || !parameters.TryGetProperty("toolCall", out var tool) || tool.ValueKind != JsonValueKind.Object)
                throw new AcpException(-32602, "Permission request requires a toolCall and bounded options array.");
            var optionIds = choices.EnumerateArray().Select(p => AcpJson.RequiredString(p, "optionId")).ToHashSet(StringComparer.Ordinal);
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cancellation.CancelAfter(options.PermissionTimeout);
            permissions[id] = (sessionId, cancellation);
            try
            {
                if (Volatile.Read(ref session.Cancelling) != 0) return cancelled;
                var decision = await services.RequestPermissionAsync(new(id, sessionId, tool.Clone(), choices.Clone(), parameters), cancellation.Token)
                    .WaitAsync(cancellation.Token).ConfigureAwait(false);
                if (decision is null || cancellation.IsCancellationRequested || Volatile.Read(ref session.Cancelling) != 0) return cancelled;
                if (!optionIds.Contains(decision)) throw new AcpException(-32602, "The permission choice was not offered by the agent.");
                return AcpJson.Element(new { outcome = new { outcome = "selected", optionId = decision } });
            }
            catch (OperationCanceledException) { return cancelled; }
            finally { permissions.TryRemove(id, out _); }
        }
        // Optional methods are unreachable unless the host supplied the corresponding service and advertised it.
        if (method is not ("fs/read_text_file" or "fs/write_text_file" or "terminal/create" or "terminal/output" or "terminal/wait_for_exit" or "terminal/kill" or "terminal/release"))
            throw new AcpException(-32601, "Client method is not available: " + method);
        var idForSession = AcpJson.RequiredString(parameters, "sessionId");
        if (!sessions.ContainsKey(idForSession)) throw new AcpException(-32602, "Unknown ACP session.");
        if (method == "fs/read_text_file" && services.ReadTextFileAsync is { } read)
        {
            AbsolutePath(AcpJson.RequiredString(parameters, "path"), "path");
            return await read(parameters, ct).ConfigureAwait(false);
        }
        if (method == "fs/write_text_file" && services.WriteTextFileAsync is { } write)
        {
            AbsolutePath(AcpJson.RequiredString(parameters, "path"), "path");
            return await write(parameters, ct).ConfigureAwait(false);
        }
        if (method is "terminal/create" or "terminal/output" or "terminal/wait_for_exit" or "terminal/kill" or "terminal/release"
            && services.TerminalRequestAsync is { } terminal) return await terminal(method, parameters, ct).ConfigureAwait(false);
        throw new AcpException(-32601, "Client method is not available: " + method);
    }

    private void OnNotification(string method, JsonElement parameters)
    {
        if (method == "session/update" && parameters.TryGetProperty("sessionId", out var sessionId) && sessionId.ValueKind == JsonValueKind.String
            && sessions.TryGetValue(sessionId.GetString()!, out var session) && parameters.TryGetProperty("update", out var update)
            && update.TryGetProperty("sessionUpdate", out var kind) && kind.GetString() == "config_option_update"
            && update.TryGetProperty("configOptions", out var config)) session.Metadata = Merge(session.Metadata, "configOptions", config);
        // Preserve all notification variants (text, tool creation/patches, plans, modes, commands, usage, notices and extensions).
        try { services.Notification?.Invoke(method, parameters); } catch (Exception) { /* A UI observer cannot break the protocol reader. */ }
    }

    private static void ValidateSetup(JsonElement parameters, JsonElement capabilities, bool optionalServers = false)
    {
        AbsolutePath(AcpJson.RequiredString(parameters, "cwd"), "cwd");
        var hasServers = parameters.TryGetProperty("mcpServers", out var servers);
        if (!hasServers && optionalServers) servers = AcpJson.Element(Array.Empty<object>());
        if ((!hasServers && !optionalServers) || servers.ValueKind != JsonValueKind.Array || servers.GetArrayLength() > 128)
            throw new AcpException(-32602, "Session setup requires a bounded mcpServers array.");
        foreach (var server in servers.EnumerateArray())
        {
            if (server.TryGetProperty("type", out var type))
            {
                var transport = type.GetString();
                if (transport is "http" or "sse") RequireCapability(capabilities, "mcpCapabilities", transport);
                else throw new AcpException(-32602, "ACP v1 stdio MCP servers omit type; unknown transports are not enabled.");
            }
            else AbsolutePath(AcpJson.RequiredString(server, "command"), "command");
        }
        if (parameters.TryGetProperty("additionalDirectories", out var directories) && directories.ValueKind != JsonValueKind.Null)
        {
            RequireCapability(capabilities, "sessionCapabilities", "additionalDirectories");
            if (directories.ValueKind != JsonValueKind.Array) throw new AcpException(-32602, "additionalDirectories must be an array.");
            foreach (var directory in directories.EnumerateArray()) AbsolutePath(directory.GetString(), "additionalDirectories");
        }
    }
    private static void ValidatePrompt(JsonElement parameters, JsonElement capabilities)
    {
        if (!parameters.TryGetProperty("prompt", out var prompt) || prompt.ValueKind != JsonValueKind.Array || prompt.GetArrayLength() is < 1 or > 1024)
            throw new AcpException(-32602, "A prompt requires a nonempty bounded content array.");
        foreach (var content in prompt.EnumerateArray())
        {
            var type = AcpJson.RequiredString(content, "type");
            switch (type)
            {
                case "text": case "resource_link": break;
                case "image": case "audio": RequireCapability(capabilities, "promptCapabilities", type); break;
                case "resource": RequireCapability(capabilities, "promptCapabilities", "embeddedContext"); break;
                default: throw new AcpException(-32602, "Unsupported ACP prompt content type: " + type);
            }
        }
    }
    private static void ValidateMode(JsonElement metadata, string modeId)
    {
        if (!metadata.TryGetProperty("modes", out var modes) || !modes.TryGetProperty("availableModes", out var available)
            || !available.EnumerateArray().Any(m => AcpJson.RequiredString(m, "id") == modeId))
            throw new AcpException(-32602, "Mode was not advertised for this session.");
    }
    private static void ValidateConfig(JsonElement metadata, JsonElement parameters)
    {
        var id = AcpJson.RequiredString(parameters, "configId");
        var config = metadata.TryGetProperty("configOptions", out var options) && options.ValueKind == JsonValueKind.Array
            ? options.EnumerateArray().FirstOrDefault(o => AcpJson.RequiredString(o, "id") == id) : default;
        if (config.ValueKind == JsonValueKind.Undefined || !parameters.TryGetProperty("value", out var value))
            throw new AcpException(-32602, "Config option was not advertised, or value is missing.");
        if (AcpJson.RequiredString(config, "type") == "boolean")
        {
            if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False) || !parameters.TryGetProperty("type", out var type) || type.GetString() != "boolean")
                throw new AcpException(-32602, "Boolean config requires type:boolean and a boolean value.");
        }
        else if (value.ValueKind != JsonValueKind.String || !config.TryGetProperty("options", out var choices) || !Values(choices).Contains(value.GetString()))
            throw new AcpException(-32602, "Config value is not an advertised choice.");
        static IEnumerable<string?> Values(JsonElement choices)
        {
            foreach (var choice in choices.EnumerateArray())
                if (choice.TryGetProperty("value", out var v)) yield return v.GetString();
                else if (choice.TryGetProperty("options", out var group)) foreach (var item in Values(group)) yield return item;
        }
    }
    private static void RequireCapability(JsonElement capabilities, params string[] path)
    { if (!AcpJson.Has(capabilities, path)) throw new AcpException(-32601, "Agent capability is not advertised: " + string.Join('.', path)); }
    private static void AbsolutePath(string? path, string name)
    { if (string.IsNullOrEmpty(path) || !Path.IsPathFullyQualified(path)) throw new AcpException(-32602, name + " must be an absolute path."); }
    private static JsonElement Merge(JsonElement source, string key, JsonElement value)
    {
        var properties = source.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.Ordinal); properties[key] = value.Clone();
        return AcpJson.Element(properties);
    }
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        foreach (var request in permissions.Values) CancelSafely(request.Cancellation);
        await rpc.DisposeAsync().ConfigureAwait(false);
        if (disposeProcess is { } dispose) await dispose().ConfigureAwait(false);
    }
    private sealed class Session(JsonElement metadata)
    {
        private readonly object gate = new();
        private JsonElement metadata = metadata;
        internal JsonElement Metadata { get { lock (gate) return metadata; } set { lock (gate) metadata = value; } }
        internal int Running, Cancelling;
    }
    private static void CancelSafely(CancellationTokenSource source) { try { source.Cancel(); } catch (ObjectDisposedException) { } }
}
