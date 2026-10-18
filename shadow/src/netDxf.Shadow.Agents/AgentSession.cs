using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using netDxf.Shadow.Mcp;

namespace netDxf.Shadow.Agents;

/// <summary>
/// One resumable conversation. Provider retries finish before tool execution; mutating
/// tools are serialized and never retried. A cancelled round still records every call result.
/// </summary>
public sealed partial class AgentSession : IDisposable
{
    private readonly IChatClient client;
    private readonly IMcpToolExecutor executor;
    private readonly object gate = new();
    private readonly SemaphoreSlim operation = new(1, 1);
    private readonly List<ChatMessage> messages = [];
    private readonly List<AgentEvent> events = [];
    private readonly List<AgentPinnedContext> pins = [];
    private readonly Dictionary<string, TaskCompletionSource<bool>> approvals = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string Signature, McpToolResult Result)> completedCalls = new(StringComparer.Ordinal);
    private readonly Func<string, string> redact;
    private readonly bool ownsClient;
    private CancellationTokenSource? activeRun;
    private long sequence;
    private int eventCharacters;
    private string status = "idle";
    private string summary = "";
    private string activeTask = "";
    private AgentPlanStep[] plan = [];
    private double tokenCalibration = 1;
    private bool disposed;
    public string SessionId { get; private set; } = Guid.NewGuid().ToString("N");
    public AgentOptions Options { get; }
    public IAgentWorkspace Workspace { get; }
    public bool IsRunning { get { lock (gate) return activeRun is not null; } }
    public int ContextTokens { get { lock (gate) return EstimateContext(); } }
    public event Action<AgentEvent>? EventEmitted;

    public AgentSession(IChatClient client, IMcpToolExecutor executor, AgentOptions? options = null,
        IAgentWorkspace? workspace = null, Func<string, string>? redactor = null, bool ownsClient = true)
    {
        this.client = client ?? throw new ArgumentNullException(nameof(client));
        this.executor = executor ?? throw new ArgumentNullException(nameof(executor));
        Options = options ?? new(); Options.Validate();
        Workspace = workspace ?? new MemoryAgentWorkspace();
        redact = redactor ?? (text => text); this.ownsClient = ownsClient;
    }

    public AgentPollResult Poll(long afterSequence = 0)
    {
        lock (gate) return new(events.Where(e => e.Sequence > afterSequence).Take(256).ToArray(), activeRun is not null,
            status, EstimateContext(), Options.ContextWindowTokens, SessionId, sequence, events.Count > 0 ? events[0].Sequence : sequence);
    }
    private void Emit(string type, string message = "", object? data = null)
    {
        AgentEvent item;
        lock (gate)
        {
            // Redact before an event enters memory, callbacks, polling, or a transcript export.
            var json = data is null ? (JsonElement?)null : JsonSerializer.Deserialize<JsonElement>(redact(AgentJson.String(data)));
            item = new(++sequence, type, redact(message), json, DateTimeOffset.UtcNow);
            events.Add(item); eventCharacters += item.Message.Length + (item.Data?.GetRawText().Length ?? 0);
            while (events.Count > 4096 || eventCharacters > 4 * 1024 * 1024 && events.Count > 1)
            { var old = events[0]; eventCharacters -= old.Message.Length + (old.Data?.GetRawText().Length ?? 0); events.RemoveAt(0); }
        }
        // A broken UI observer must not terminate model streaming or leave a mutation half-recorded.
        if (EventEmitted is { } callback)
            foreach (Action<AgentEvent> observer in callback.GetInvocationList())
                try { observer(item); } catch (Exception) { /* Host telemetry is observational. */ }
    }

    public async Task RunAsync(string prompt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        if (prompt.Length > 256000) throw new ArgumentException("Prompt exceeds 256,000 characters.", nameof(prompt));
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!await operation.WaitAsync(0, cancellationToken).ConfigureAwait(false)) throw new InvalidOperationException("The agent is already running.");
        using var run = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (gate) { activeRun = run; status = "running"; }
        try
        {
            if (!string.IsNullOrWhiteSpace(prompt))
            {
                lock (gate) { activeTask = prompt; messages.Add(new(ChatRole.User, prompt)); }
                Emit("user", prompt);
            }
            else if (messages.Count == 0) throw new ArgumentException("Enter a task before continuing.");
            for (var turn = 1; turn <= Options.MaxTurns; turn++)
            {
                run.Token.ThrowIfCancellationRequested();
                if (ContextTokens > InputBudget * Options.CompactionThreshold) await CompactCoreAsync(run.Token).ConfigureAwait(false);
                if (ContextTokens > InputBudget)
                    throw new InvalidOperationException("The current task, pinned context and tool schemas exceed the context budget. Increase the context window or remove a pin.");
                Emit("turn", $"Model turn {turn}", new { turn, maximum = Options.MaxTurns });
                int inputEstimate;
                lock (gate) inputEstimate = EstimateContext(false);
                var response = await InferAsync(BuildInput(), ToolOptions(), run.Token).ConfigureAwait(false);
                if (response.Messages.Count == 0) throw new InvalidOperationException("The provider returned no assistant message. Retry or choose another model.");
                NormalizeCalls(response);
                lock (gate) messages.AddRange(response.Messages);
                if (response.Usage is { } usage)
                {
                    Emit("usage", "Provider token usage", new { inputTokens = usage.InputTokenCount, outputTokens = usage.OutputTokenCount, totalTokens = usage.TotalTokenCount, cachedTokens = usage.CachedInputTokenCount });
                    if (usage.InputTokenCount is > 0)
                        lock (gate) tokenCalibration = Math.Max(tokenCalibration, Math.Min(4, (double)usage.InputTokenCount.Value / Math.Max(1, inputEstimate)));
                }
                var calls = response.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>().Where(c => !c.InformationalOnly).ToArray();
                if (calls.Length == 0)
                {
                    Emit("assistant", response.Text);
                    if (response.FinishReason == ChatFinishReason.Length)
                        Emit("notice", "The model reached its output limit. Continue to finish the response.");
                    lock (gate) status = "completed";
                    Emit("completed", "Agent turn completed."); return;
                }
                if (!string.IsNullOrWhiteSpace(response.Text)) Emit("assistant", response.Text);
                await ExecuteCallsAsync(calls, run.Token).ConfigureAwait(false);
                // Yield even when all tools and a fake/local provider complete synchronously (WebAssembly UI).
                await Task.Yield();
            }
            lock (gate) status = "paused";
            Emit("paused", "Turn budget reached. Continue to resume the same conversation.");
        }
        catch (OperationCanceledException) when (run.IsCancellationRequested)
        { lock (gate) status = "cancelled"; Emit("cancelled", "Stopped. Completed tool operations remain recorded; inspect the drawing before repeating a write."); }
        catch (Exception error)
        { lock (gate) status = "error"; Emit("error", error.Message, new { exception = error.GetType().Name, resumable = true }); }
        finally
        {
            lock (gate)
            {
                activeRun = null;
                foreach (var pending in approvals.Values) pending.TrySetCanceled();
                approvals.Clear();
            }
            operation.Release();
        }
    }
    public void Cancel() { lock (gate) activeRun?.Cancel(); }
    public bool Approve(string requestId, bool approved)
    {
        lock (gate) return approvals.TryGetValue(requestId, out var pending) && pending.TrySetResult(approved);
    }
    private ChatOptions ToolOptions() => new()
    {
        ModelId = string.IsNullOrWhiteSpace(Options.Model) ? null : Options.Model,
        MaxOutputTokens = Options.MaxOutputTokens,
        Tools = executor.Tools.Where(t => Options.PermissionMode != AgentPermissionMode.ReadOnly || t.ReadOnly)
            .Select(t => (AITool)new AgentFunction(t)).ToList(),
        ToolMode = ChatToolMode.Auto, AllowMultipleToolCalls = true
    };
    private async Task<ChatResponse> InferAsync(IReadOnlyList<ChatMessage> input, ChatOptions options, CancellationToken token, bool display = true)
    {
        for (var attempt = 0; ; attempt++)
        {
            var updates = new List<ChatResponseUpdate>();
            var outputCharacters = 0;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(Options.InferenceTimeout);
            try
            {
                await foreach (var update in client.GetStreamingResponseAsync(input, options, timeout.Token).ConfigureAwait(false))
                {
                    outputCharacters = checked(outputCharacters + update.Text.Length + update.Contents.OfType<FunctionCallContent>().Sum(c => AgentJson.String(c.Arguments).Length));
                    if (outputCharacters > Math.Max(1000000, Options.MaxOutputTokens * 32) || updates.Count >= 100000)
                        throw new InvalidOperationException("The provider exceeded the configured response buffer budget.");
                    updates.Add(update);
                    if (display && update.Text.Length > 0) Emit("assistantDelta", update.Text);
                }
                return updates.ToChatResponse();
            }
            catch (Exception error) when (!token.IsCancellationRequested && attempt < Options.MaxRetries && IsRetryable(error, out _))
            {
                IsRetryable(error, out var retryAfter);
                var delay = retryAfter ?? TimeSpan.FromMilliseconds(Math.Min(20000, 500 * Math.Pow(2, attempt)) + Random.Shared.Next(100, 400));
                if (delay > TimeSpan.FromMinutes(1)) delay = TimeSpan.FromMinutes(1);
                if (display) Emit("retrying", "Provider request interrupted; retrying inference before executing tools.", new { attempt = attempt + 1, delayMilliseconds = (int)delay.TotalMilliseconds, discardPartial = updates.Count > 0 });
                await Task.Delay(delay, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            { throw new TimeoutException("The provider request timed out. You can continue the conversation."); }
        }
    }
    private static bool IsRetryable(Exception error, out TimeSpan? delay)
    {
        delay = null;
        if (error is OperationCanceledException) return true;
        return ShadowAgentProviders.TryGetTransientFailure(error, out delay);
    }

    private async Task ExecuteCallsAsync(FunctionCallContent[] calls, CancellationToken token)
    {
        var results = new McpToolResult?[calls.Length];
        var callIds = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            for (var i = 0; i < calls.Length;)
            {
                token.ThrowIfCancellationRequested();
                // Only contiguous read operations can overlap; writes preserve model order.
                var end = i;
                while (end < calls.Length && end - i < Options.MaxParallelReadTools && executor.Tools.Any(t => t.Name == calls[end].Name && t.ReadOnly)) end++;
                if (end == i) end++;
                var tasks = new List<Task>();
                for (; i < end; i++)
                {
                    var index = i;
                    if (!callIds.Add(calls[index].CallId))
                    { results[index] = new("Duplicate tool call identifier in a provider response; operation was not repeated.", true); continue; }
                    tasks.Add(Execute(index));
                }
                await Task.WhenAll(tasks).ConfigureAwait(false);
                async Task Execute(int index) => results[index] = await ExecuteCallAsync(calls[index], token).ConfigureAwait(false);
            }
        }
        finally
        {
            // Preserve function-call/result pairs across stop, timeout and resumption.
            var contents = new List<AIContent>(calls.Length);
            for (var i = 0; i < calls.Length; i++)
            {
                var result = results[i] ?? new McpToolResult("Tool cancelled before a result was recorded. Inspect current state before repeating any mutation.", true);
                contents.Add(new FunctionResultContent(calls[i].CallId, result.Text) { AdditionalProperties = new() { ["shadow:isError"] = result.IsError } });
            }
            lock (gate) messages.Add(new(ChatRole.Tool, contents));
        }
    }
    private void NormalizeCalls(ChatResponse response)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var message in response.Messages)
        {
            for (var i = 0; i < message.Contents.Count; i++)
                if (message.Contents[i] is FunctionCallContent call && !call.InformationalOnly && !seen.Add(call.CallId))
                {
                    message.Contents.RemoveAt(i--);
                    Emit("notice", "The provider repeated a tool call identifier. The duplicate was removed before execution.");
                }
        }
    }
    private async Task<McpToolResult> ExecuteCallAsync(FunctionCallContent call, CancellationToken token)
    {
        if (call.Exception is not null) return new("Malformed tool arguments: " + call.Exception.Message, true);
        var arguments = AgentJson.Element(call.Arguments ?? new Dictionary<string, object?>());
        var signature = call.Name + "\n" + arguments.GetRawText();
        lock (gate)
            if (completedCalls.TryGetValue(call.CallId, out var previous))
                return previous.Signature == signature ? previous.Result : new("A completed call identifier was reused with different arguments. No operation was executed.", true);
        var result = await ExecuteToolWithPolicyAsync(call.Name, arguments, call.CallId, token).ConfigureAwait(false);
        lock (gate) completedCalls[call.CallId] = (signature, result);
        return result;
    }
    public async Task<McpToolResult> ExecuteToolAsync(string name, JsonElement arguments, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!await operation.WaitAsync(0, cancellationToken).ConfigureAwait(false)) throw new InvalidOperationException("Stop the agent before invoking a tool manually.");
        using var run = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (gate) { activeRun = run; status = "tool"; }
        try { return await ExecuteToolWithPolicyAsync(name, arguments, Guid.NewGuid().ToString("N"), run.Token).ConfigureAwait(false); }
        finally { lock (gate) { activeRun = null; status = "idle"; } operation.Release(); }
    }
    private async Task<McpToolResult> ExecuteToolWithPolicyAsync(string name, JsonElement arguments, string callId, CancellationToken token)
    {
        var definition = executor.Tools.FirstOrDefault(t => t.Name == name);
        if (definition is null) return new("Unknown tool. Inspect the current tool catalog.", true);
        if (!definition.ReadOnly && Options.PermissionMode == AgentPermissionMode.ReadOnly) return new("This session is read-only. The operation was not executed.", true);
        if (!definition.ReadOnly && Options.PermissionMode == AgentPermissionMode.Ask)
        {
            var request = new AgentApproval(callId, name, arguments, DateTimeOffset.UtcNow + Options.ApprovalTimeout);
            var decision = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (gate) { approvals.Add(request.RequestId, decision); status = "approval"; }
            Emit("approvalRequested", $"Allow {name}?", request);
            try
            {
                if (!await decision.Task.WaitAsync(Options.ApprovalTimeout, token).ConfigureAwait(false))
                { Emit("toolCompleted", "Permission denied.", new { callId, name, isError = true }); return new("Permission denied by the user. Do not attempt the operation through another tool.", true); }
            }
            catch (TimeoutException) { return new("Approval expired. The operation was not executed.", true); }
            finally { lock (gate) { approvals.Remove(request.RequestId); status = "running"; } }
        }
        token.ThrowIfCancellationRequested();
        Emit("toolStarted", name, new { callId, name, arguments, readOnly = definition.ReadOnly });
        var watch = Stopwatch.StartNew();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(Options.ToolTimeout);
        McpToolResult result;
        try
        {
            // Await cooperative cancellation. Abandoning an in-flight mutation could allow
            // a later command to race with a supposedly cancelled document transaction.
            result = await executor.ExecuteAsync(name, arguments, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        { result = new("Tool interrupted. Its mutation may already have committed; inspect current state before retrying.", true); }
        catch (Exception error) { result = new($"{error.GetType().Name}: {error.Message}", true); }
        result = result with { Text = redact(result.Text) };
        if (result.Text.Length > Options.MaxToolResultCharacters)
        {
            var full = result.Text;
            var path = $"agent-results/{Guid.NewGuid():N}.txt";
            try
            {
                Workspace.Write(path, full, 0);
                result = result with { Text = AgentJson.String(new { truncated = true, path, totalCharacters = full.Length, preview = full[..Options.MaxToolResultCharacters], instruction = "Use workspace_read with offset and length for the full result." }) };
            }
            catch (InvalidOperationException)
            { result = new("Tool completed, but its result exceeded the context and workspace capacity. Query a smaller range. " + full[..Options.MaxToolResultCharacters], result.IsError); }
        }
        Emit("toolCompleted", name, new { callId, name, result = result.Text, isError = result.IsError, milliseconds = watch.Elapsed.TotalMilliseconds });
        return result;
    }

    public AgentPinnedContext Pin(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        if (text.Length > 32000) throw new ArgumentException("Pinned context is limited to 32,000 characters per entry.");
        lock (gate)
        {
            if (pins.Count >= 32) throw new InvalidOperationException("The session already has 32 pins.");
            var pin = new AgentPinnedContext(Guid.NewGuid().ToString("N"), redact(text)); pins.Add(pin); return pin;
        }
    }
    public bool Unpin(string id) { lock (gate) return pins.RemoveAll(p => p.Id == id) > 0; }
    public IReadOnlyList<AgentPinnedContext> Pins { get { lock (gate) return pins.ToArray(); } }
    public IReadOnlyList<AgentPlanStep> Plan { get { lock (gate) return plan.ToArray(); } }
    public void SetPlan(IEnumerable<AgentPlanStep> steps)
    {
        var value = steps.ToArray();
        if (value.Length > 64 || value.Any(s => s.Step.Length is 0 or > 1024 || s.Status is not ("pending" or "in_progress" or "completed")) || value.Count(s => s.Status == "in_progress") > 1)
            throw new ArgumentException("Use at most 64 named steps, with pending/in_progress/completed states and at most one active step.");
        lock (gate) plan = value;
        Emit("plan", "Plan updated.", new { steps = value });
    }
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            if (activeRun is not null) throw new InvalidOperationException("Cancel and await the current operation before disposing the session.");
            disposed = true;
        }
        if (ownsClient) client.Dispose();
        operation.Dispose();
    }
}
