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
        InitializeRunControls();
    }

    public AgentPollResult Poll(long afterSequence = 0)
    {
        lock (gate) return new(events.Where(e => e.Sequence > afterSequence).Take(256).ToArray(), activeRun is not null,
            status, EstimateContext(), Options.ContextWindowTokens, SessionId, sequence, events.Count > 0 ? events[0].Sequence : sequence, RunState, usageTotals);
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

    public Task RunAsync(string prompt, CancellationToken cancellationToken = default) => RunCoreAsync(new AgentPrompt(prompt).Validate(), cancellationToken);

    private async Task RunCoreAsync(AgentPrompt request, CancellationToken cancellationToken, bool validateReferences = false)
    {
        var prompt = request.ToContextText();
        ArgumentNullException.ThrowIfNull(prompt);
        if (prompt.Length > 256000) throw new ArgumentException("Prompt exceeds 256,000 characters.", nameof(prompt));
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!await operation.WaitAsync(0, cancellationToken).ConfigureAwait(false)) throw new InvalidOperationException("The agent is already running.");
        using var run = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (gate) { activeRun = run; status = "running"; }
        try
        {
            if (validateReferences)
            {
                request = request with { References = await DrawingReferences.ValidateCurrentAsync(executor, request.References, run.Token).ConfigureAwait(false) };
                prompt = request.ToContextText();
            }
            BeginTask(request);
            for (var turn = 1; Options.MaxTurns == 0 || turn <= Options.MaxTurns; turn++)
            {
                run.Token.ThrowIfCancellationRequested();
                if (ContextTokens > InputBudget * Options.CompactionThreshold) await CompactCoreAsync(run.Token).ConfigureAwait(false);
                if (ContextTokens > InputBudget)
                {
                    await CompactCoreAsync(run.Token, force: true).ConfigureAwait(false);
                    if (ContextTokens > InputBudget)
                    {
                        EndRun("paused", "context_budget", "The current task, pinned context and tool schemas exceed the context budget. Increase the context window or remove a pin, then Resume.", true);
                        return;
                    }
                }
                long progressBefore;
                lock (gate)
                {
                    progressBefore = progressVersion;
                    runState = runState with { Phase = "running", TurnCount = turn, TotalTurns = runState.TotalTurns + 1, Detail = "Generating the next task step." };
                }
                Emit("turn", $"Model turn {turn}", new { turn, maximum = Options.MaxTurns });
                EmitRun();
                int inputEstimate;
                lock (gate) inputEstimate = EstimateContext(false);
                var response = await InferAsync(BuildInput(), ToolOptions(), run.Token).ConfigureAwait(false);
                run.Token.ThrowIfCancellationRequested();
                if (response.Usage is { } usage)
                {
                    var calibrationInput = ResponseInteger(response, "shadow:initial-input-tokens") ?? usage.InputTokenCount;
                    if (calibrationInput is > 0)
                        lock (gate) tokenCalibration = Math.Max(tokenCalibration, Math.Min(4, (double)calibrationInput.Value / Math.Max(1, inputEstimate)));
                }
                // A terminal token limit can contain only reasoning/usage, or partially parsed
                // function arguments. Never append or execute that incomplete tool batch.
                if (response.FinishReason == ChatFinishReason.Length)
                {
                    PreservePartialText(response);
                    if (!response.Messages.SelectMany(message => message.Contents).OfType<FunctionCallContent>().Any() && !string.IsNullOrWhiteSpace(response.Text))
                        RecordProgress("output-continuation\n" + response.Text);
                    var contextLimit = string.Equals(ResponseString(response, "shadow:stop-reason"), "model_context_window_exceeded", StringComparison.OrdinalIgnoreCase);
                    if (contextLimit) await CompactCoreAsync(run.Token, force: true).ConfigureAwait(false);
                    ContinueTask(contextLimit ? "The provider context limit was reached; compacting and continuing automatically." :
                        "The response output limit was reached; continuing automatically.",
                        "The previous response was truncated. No tool calls from that partial response were executed. Continue the current task from recorded state without repeating earlier completed work. " +
                        "Regenerate any unfinished tool request as a smaller, valid, complete call matching its schema. Use incremental workspace edits instead of one oversized write. " +
                        "If the response contained only reasoning, reduce the reasoning needed for the next concrete step. Finish using agent_finish only after completion and verification.", outputLimit: true);
                }
                else if (response.FinishReason == ChatFinishReason.ContentFilter)
                {
                    PreservePartialText(response);
                    EndRun("blocked", "provider_refusal", "The provider declined this response. Review the request or supply the required input before resuming.", true);
                    return;
                }
                else if (ResponseString(response, "shadow:stop-reason")?.ToUpperInvariant() is "MALFORMED_FUNCTION_CALL" or "UNEXPECTED_TOOL_CALL" or "TOO_MANY_TOOL_CALLS" ||
                    ResponseFlag(response, "shadow:invalid-tool-batch") ||
                    response.Messages.SelectMany(message => message.Contents).OfType<FunctionCallContent>().Any(call => call.Exception is not null))
                {
                    PreservePartialText(response);
                    ContinueTask("The provider returned an invalid tool batch; regenerating it before execution.",
                        "No tools from the previous invalid batch were executed. Regenerate the next step using a smaller set of complete function calls with valid JSON matching the advertised schemas. Do not repeat earlier successful mutations.");
                }
                else if (response.Messages.Count == 0)
                {
                    ContinueTask("The provider returned no usable step; requesting a corrected response.",
                        "The last response contained no usable assistant message or complete tool call. Generate the next concrete step with valid compact arguments matching the tool schema. If the task is complete, call agent_finish; if blocked, report the exact blocker.");
                }
                else
                {
                    NormalizeCalls(response);
                    var calls = response.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>().Where(c => !c.InformationalOnly).ToArray();
                    if (calls.Length > Options.MaxToolCallsPerTurn)
                        throw new InvalidOperationException($"The provider returned more than {Options.MaxToolCallsPerTurn} tool calls in one turn. No tools were executed; request a smaller batch before continuing.");
                    lock (gate) messages.AddRange(response.Messages);
                    if (!string.IsNullOrWhiteSpace(response.Text)) Emit("assistant", response.Text);
                    if (calls.Length == 0)
                    {
                        ContinueTask("Continuing until the task has an explicit completion or blocker.",
                            "Continue the requested task. A text-only response does not complete this run. Perform and verify remaining work and update the plan. If everything is complete, call agent_finish alone with the result and verification. If unable to proceed, call it with outcome blocked and the exact blocker.");
                    }
                    else
                    {
                        acceptingCompletion = calls.Length == 1 && calls[0].Name == "agent_finish";
                        try { await ExecuteCallsAsync(calls, run.Token).ConfigureAwait(false); }
                        finally { acceptingCompletion = false; }
                        if (CompleteTask(run.Token)) return;
                    }
                }
                run.Token.ThrowIfCancellationRequested();
                if (!CheckProgress(progressBefore)) return;
                // Yield even when all tools and a fake/local provider complete synchronously (WebAssembly UI).
                await Task.Yield();
            }
            EndRun("paused", "turn_budget", "The configured round budget was reached. Resume the same task, or set the round budget to 0 to continue until completion.", true);
        }
        catch (OperationCanceledException) when (run.IsCancellationRequested)
        { EndRun("cancelled", "cancelled", "Stopped. Completed tool operations remain recorded; inspect current state before repeating a write. Resume continues the task.", true); }
        catch (Exception error)
        { EndRun("error", "execution_error", error.Message, true); }
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
        Tools = PrepareTools().Functions.ToList(),
        ToolMode = ChatToolMode.Auto, AllowMultipleToolCalls = true
    };
    private async Task<ChatResponse> InferAsync(IReadOnlyList<ChatMessage> input, ChatOptions options, CancellationToken token, bool display = true)
    {
        for (var attempt = 0; ; attempt++)
        {
            var watch = Stopwatch.StartNew();
            double? firstTokenMilliseconds = null;
            var updates = new List<ChatResponseUpdate>();
            var outputCharacters = 0;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(Options.InferenceTimeout);
            try
            {
                await foreach (var update in client.GetStreamingResponseAsync(input, options, timeout.Token).ConfigureAwait(false))
                {
                    outputCharacters = checked(outputCharacters + update.Text.Length
                        + update.Contents.OfType<TextReasoningContent>().Sum(c => checked((c.Text?.Length ?? 0) + (c.ProtectedData?.Length ?? 0)))
                        + update.Contents.OfType<FunctionCallContent>().Sum(c => AgentJson.String(c.Arguments).Length));
                    if (outputCharacters > Math.Max(1000000, Options.MaxOutputTokens * 32) || updates.Count >= 100000)
                        throw new InvalidOperationException("The provider exceeded the configured response buffer budget.");
                    updates.Add(update);
                    if (firstTokenMilliseconds is null && (update.Text.Length > 0 || update.Contents.OfType<FunctionCallContent>().Any())) firstTokenMilliseconds = watch.Elapsed.TotalMilliseconds;
                    if (display && update.Text.Length > 0) Emit("assistantDelta", update.Text);
                }
                var response = updates.ToChatResponse();
                RecordUsage(response.Usage, watch.Elapsed.TotalMilliseconds, firstTokenMilliseconds, display ? "turn" : "compaction");
                return response;
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
                while (end < calls.Length && end - i < Options.MaxParallelReadTools && ToolDefinitions.Any(t => t.Name == calls[end].Name && t.ReadOnly)) end++;
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
        // Completion is evaluated against the current task and plan. A receipt from a
        // previous task must never complete a new task or bypass its verification gate.
        if (call.Name is "agent_finish" or "agent_plan") return await ExecuteToolWithPolicyAsync(call.Name, arguments, call.CallId, token).ConfigureAwait(false);
        if (!ValidReceiptId(call.CallId)) return ReportReceiptResult(call,
            McpToolResult.Error("invalid_call_id", "The tool call identifier is invalid. No operation was executed."));
        var signature = CallSignature(call.Name, arguments);
        var receipt = new AgentCallReceipt(call.CallId, signature, true, RedactedCallSignature(call.Name, arguments, signature));
        McpToolResult? recorded = null;
        var cached = false; var historical = false;
        lock (gate)
        {
            if (callReceipts.TryGetValue(call.CallId, out var previous))
            {
                cached = previous.Signature == signature;
                historical = cached && !completedCalls.ContainsKey(call.CallId);
                recorded = cached
                    ? completedCalls.TryGetValue(call.CallId, out var exact) ? exact.Result : HistoricalCallResult(previous)
                    : McpToolResult.Error("call_id_reused", "A recorded call identifier was reused with different arguments. No operation was executed.");
            }
            else
            {
                var budget = ReceiptBudget(receipt);
                if (callReceipts.Count >= MaximumCallReceipts || callReceiptBudget + budget > MaximumCallReceiptBudget)
                    recorded = McpToolResult.Error("receipt_capacity", "The session's tool receipt ledger is full. No operation was executed. Export this session and start a new one before further work.");
                else
                {
                    // Reserve before dispatch, including parallel reads. A cancelled/uncertain
                    // attempt must retain its ID instead of becoming eligible for blind replay.
                    callReceipts.Add(call.CallId, receipt); callReceiptBudget += budget;
                }
            }
        }
        if (recorded is not null) return ReportReceiptResult(call, recorded, cached, historical);
        var result = await ExecuteToolWithPolicyAsync(call.Name, arguments, call.CallId, token).ConfigureAwait(false);
        lock (gate)
        {
            callReceipts[call.CallId] = receipt with { IsError = result.IsError };
            completedCalls[call.CallId] = (signature, result);
        }
        return result;
    }
    public async Task<McpToolResult> ExecuteToolAsync(string name, JsonElement arguments, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!await operation.WaitAsync(0, cancellationToken).ConfigureAwait(false)) throw new InvalidOperationException("Stop the agent before invoking a tool manually.");
        using var run = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        AgentRunState previousRun; string previousStatus;
        lock (gate) { previousRun = runState; previousStatus = status; }
        lock (gate) { activeRun = run; status = "tool"; }
        try { return await ExecuteToolWithPolicyAsync(name, arguments, Guid.NewGuid().ToString("N"), run.Token).ConfigureAwait(false); }
        finally { lock (gate) { activeRun = null; status = previousStatus; runState = previousRun; } operation.Release(); }
    }
    private async Task<McpToolResult> ExecuteToolWithPolicyAsync(string name, JsonElement arguments, string callId, CancellationToken token)
    {
        McpToolResult Reject(string message)
        {
            var rejected = new McpToolResult(redact(message), true);
            Emit("toolCompleted", name, new { callId, name, arguments, result = rejected.Text, isError = true, executed = false, milliseconds = 0 });
            return rejected;
        }
        var definition = ToolDefinitions.FirstOrDefault(t => t.Name == name);
        if (definition is null) return Reject("Unknown tool. Inspect the current tool catalog.");
        if (!definition.ReadOnly && Options.Mode == AgentRunMode.Plan) return Reject("Plan mode permits inspection and planning only. Switch to Normal or Goal before applying changes or executing code.");
        if (!definition.ReadOnly && Options.PermissionMode == AgentPermissionMode.ReadOnly) return Reject("This session is read-only. The operation was not executed.");
        if (!definition.ReadOnly && Options.PermissionMode == AgentPermissionMode.Ask)
        {
            var request = new AgentApproval(callId, name, arguments, DateTimeOffset.UtcNow + Options.ApprovalTimeout);
            var decision = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (gate) { approvals.Add(request.RequestId, decision); status = "approval"; }
            SetRunPhase("approval", $"Waiting for permission to execute {name}.");
            Emit("approvalRequested", $"Allow {name}?", request);
            try
            {
                if (!await decision.Task.WaitAsync(Options.ApprovalTimeout, token).ConfigureAwait(false))
                    return Reject("Permission denied by the user. Do not attempt the operation through another tool.");
            }
            catch (TimeoutException) { return Reject("Approval expired. The operation was not executed."); }
            finally { lock (gate) { approvals.Remove(request.RequestId); status = "running"; } SetRunPhase("running", "Continuing after the permission decision."); }
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
            result = await (name is "agent_finish" or "agent_plan"
                ? controlTools : executor).ExecuteAsync(name, arguments, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        { result = new("Tool interrupted. Its mutation may already have committed; inspect current state before retrying.", true); }
        catch (Exception error) { result = new($"{error.GetType().Name}: {error.Message}", true); }
        result = result with { Text = redact(result.Text) };
        if (!result.IsError && name is not ("agent_finish" or "agent_plan")) RecordProgress(name + "\n" + arguments.GetRawText() + "\n" + result.Text);
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
            var pin = new AgentPinnedContext(Guid.NewGuid().ToString("N"), redact(text)); pins.Add(pin); retainedContextRevision++; return pin;
        }
    }
    public bool Unpin(string id) { lock (gate) { var removed = pins.RemoveAll(p => p.Id == id) > 0; if (removed) retainedContextRevision++; return removed; } }
    public IReadOnlyList<AgentPinnedContext> Pins { get { lock (gate) return pins.ToArray(); } }
    public IReadOnlyList<AgentPlanStep> Plan { get { lock (gate) return plan.ToArray(); } }
    public void SetPlan(IEnumerable<AgentPlanStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        var value = steps.ToArray();
        if (value.Length > 64 || value.Any(s => s is null || s.Step is null || s.Step.Length is 0 or > 1024 || s.Status is not ("pending" or "in_progress" or "completed")) || value.Count(s => s.Status == "in_progress") > 1)
            throw new ArgumentException("Use at most 64 named steps, with pending/in_progress/completed states and at most one active step.");
        lock (gate) plan = value;
        RecordProgress("plan\n" + AgentJson.String(value));
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
