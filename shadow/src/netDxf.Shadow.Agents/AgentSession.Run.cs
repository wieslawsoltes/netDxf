using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using netDxf.Shadow.Mcp;

namespace netDxf.Shadow.Agents;

public sealed partial class AgentSession
{
    private readonly McpToolRegistry controlTools = new();
    private readonly HashSet<string> progressReceipts = new(StringComparer.Ordinal);
    private readonly Queue<string> progressReceiptOrder = new();
    private long progressVersion;
    private AgentRunState runState = new();
    private CompletionDecision? completionDecision;
    private bool acceptingCompletion;
    private sealed record CompletionDecision(string Outcome, string Summary, string[] Verification, string Blocker);

    public AgentRunState RunState
    {
        get
        {
            lock (gate) return runState with
            {
                SuccessCriteria = redact(runState.SuccessCriteria), Detail = redact(runState.Detail),
                CompletionSummary = redact(runState.CompletionSummary), Verification = runState.Verification.Select(redact).ToArray()
            };
        }
    }

    /// <summary>The current host catalog plus validated, session-owned plan and completion controls.</summary>
    public IReadOnlyList<McpToolDefinition> ToolDefinitions
    {
        get
        {
            var host = executor.Tools;
            return host.Where(tool => tool.Name is not ("agent_finish" or "agent_plan")).Concat(controlTools.Tools).ToArray();
        }
    }

    /// <summary>Continue the retained task and plan using the session's current execution policy.</summary>
    public Task ResumeAsync(CancellationToken cancellationToken = default) => RunAsync("", cancellationToken);

    private void InitializeRunControls()
    {
        runState = NewRunState();
        controlTools.Register(new("agent_plan", "Publish or update the task plan. Keep pending and in_progress steps until they are completed. Plan mode prepares steps without executing them.",
            McpJson.Parse("""{"type":"object","properties":{"steps":{"type":"array","maxItems":64,"items":{"type":"object","properties":{"step":{"type":"string","minLength":1,"maxLength":1024},"status":{"type":"string","enum":["pending","in_progress","completed"]}},"required":["step","status"],"additionalProperties":false}}},"required":["steps"],"additionalProperties":false}"""), true), (args, token) =>
        {
            token.ThrowIfCancellationRequested();
            SetPlan(args.GetProperty("steps").Deserialize<AgentPlanStep[]>(AgentJson.Options)!);
            return ValueTask.FromResult(McpToolResult.FromObject(new { steps = Plan }));
        });
        controlTools.Register(new("agent_finish", "End the current task explicitly. Call this tool alone after implementing and verifying all work. For completed, report the concrete result and verification; pending plan steps prevent completion except in Plan mode. Goal mode requires a complete plan and verification. For blocked, report the exact missing input, capability or permission in blocker. A progress report does not end a task.",
            McpJson.Parse("""{"type":"object","properties":{"outcome":{"type":"string","enum":["completed","blocked"]},"summary":{"type":"string","minLength":1,"maxLength":32000},"verification":{"type":"array","maxItems":64,"items":{"type":"string","minLength":1,"maxLength":2048}},"blocker":{"type":"string","maxLength":8000}},"required":["outcome","summary"],"additionalProperties":false}"""), true), (args, token) =>
        {
            token.ThrowIfCancellationRequested();
            if (!acceptingCompletion) return ValueTask.FromResult(McpToolResult.Error("completion_order",
                "Call agent_finish by itself after all other tool results have been observed. It is a model-run control, not a manual tool."));
            var outcome = args.GetProperty("outcome").GetString()!;
            var finalSummary = args.GetProperty("summary").GetString()!.Trim();
            var verification = args.TryGetProperty("verification", out var evidence)
                ? evidence.EnumerateArray().Select(item => item.GetString()!.Trim()).ToArray() : [];
            var blocker = args.TryGetProperty("blocker", out var blocked) ? blocked.GetString()!.Trim() : "";
            if (finalSummary.Length == 0 || verification.Any(string.IsNullOrWhiteSpace))
                return ValueTask.FromResult(McpToolResult.Error("invalid_completion", "Provide a meaningful summary and verification entries."));
            if (outcome == "blocked" && blocker.Length == 0)
                return ValueTask.FromResult(McpToolResult.Error("missing_blocker", "Explain exactly what prevents further progress and what input or capability is needed."));
            lock (gate)
            {
                if (outcome == "completed")
                {
                    if (Options.Mode == AgentRunMode.Plan && plan.Length == 0)
                        return ValueTask.FromResult(McpToolResult.Error("plan_required", "Publish the concrete implementation plan with agent_plan before finishing Plan mode."));
                    if (Options.Mode != AgentRunMode.Plan && plan.Any(step => step.Status != "completed"))
                        return ValueTask.FromResult(McpToolResult.Error("unfinished_plan", "The plan still has unfinished steps. Complete and verify them, or report the exact blocker."));
                    if (Options.Mode == AgentRunMode.Goal && (plan.Length == 0 || verification.Length == 0))
                        return ValueTask.FromResult(McpToolResult.Error("goal_verification_required", "Goal mode requires a completed plan and verification of the success criteria. Inspect results before claiming success."));
                }
                completionDecision = new(outcome, redact(finalSummary), verification.Select(redact).ToArray(), redact(blocker));
            }
            return ValueTask.FromResult(McpToolResult.FromObject(new { accepted = true, outcome }));
        });
    }

    private AgentRunState NewRunState() => new()
    {
        Mode = Options.Mode, SuccessCriteria = Options.SuccessCriteria, TurnLimit = Options.MaxTurns
    };

    private void BeginTask(AgentPrompt request)
    {
        var prompt = request.ToContextText();
        string? messageId = null;
        lock (gate)
        {
            completionDecision = null; acceptingCompletion = false;
            var hasPrompt = !string.IsNullOrWhiteSpace(prompt);
            var newTask = hasPrompt && !runState.Resumable;
            if (newTask)
            {
                activeTask = prompt; plan = []; runState = NewRunState();
                progressReceipts.Clear(); progressReceiptOrder.Clear(); progressVersion = 0;
            }
            else if (hasPrompt)
            {
                // Steering an interrupted task must retain its original objective through compaction.
                var combined = activeTask + "\n\nUser update:\n" + prompt;
                if (combined.Length > 256000) throw new ArgumentException("The task and its updates exceed 256,000 characters. Start a new task or shorten the update.");
                activeTask = combined;
            }
            if (hasPrompt)
            {
                var message = new ChatMessage(ChatRole.User, prompt);
                messageId = Guid.NewGuid().ToString("N");
                if (request.References is { Count: > 0 }) promptMetadata.Add(message, new(messageId, request));
                messages.Add(message);
            }
            else if (messages.Count == 0 && activeTask.Length == 0) throw new ArgumentException("Enter a task before continuing.");
            else messages.Add(new(ChatRole.User,
                "Resume the current task from the recorded state and plan. Inspect current state before repeating an interrupted mutation. Complete and verify remaining work, then call agent_finish."));
            runState = runState with
            {
                Mode = Options.Mode, SuccessCriteria = Options.SuccessCriteria, Phase = "running", TurnCount = 0,
                TurnLimit = Options.MaxTurns, NoProgressCount = 0, Resumable = true,
                StopReason = "", Detail = "Working on the current task.", CompletionSummary = "", Verification = []
            };
        }
        if (!string.IsNullOrWhiteSpace(prompt)) Emit("user", request.Text, new { references = request.References, messageId });
        EmitRun();
    }

    private void EmitRun() => Emit("run", RunState.Detail, RunState);

    private void SetRunPhase(string phase, string detail)
    {
        lock (gate) runState = runState with { Phase = phase, Detail = detail };
        EmitRun();
    }

    private void EndRun(string phase, string reason, string detail, bool resumable)
    {
        lock (gate)
        {
            status = phase;
            runState = runState with { Phase = phase, StopReason = reason, Detail = redact(detail), Resumable = resumable };
        }
        EmitRun();
        Emit(phase, detail, new { reason, resumable });
    }

    private void ContinueTask(string reason, string instruction, bool outputLimit = false)
    {
        lock (gate)
        {
            messages.Add(new(ChatRole.User, instruction));
            runState = runState with
            {
                Phase = "continuing", ContinuationCount = runState.ContinuationCount + 1,
                OutputLimitCount = runState.OutputLimitCount + (outputLimit ? 1 : 0),
                Detail = reason, Resumable = true
            };
        }
        Emit("continuing", reason, new { reason, outputLimit, automatic = true });
        EmitRun();
    }

    private void PreservePartialText(ChatResponse response)
    {
        var text = response.Text;
        if (string.IsNullOrWhiteSpace(text)) return;
        // Opaque reasoning and function-call fragments are not valid continuation items.
        // Keep only user-visible text; the next complete call is generated from real state.
        lock (gate) messages.Add(new(ChatRole.Assistant, text));
        Emit("assistant", text);
    }

    private static string? ResponseString(ChatResponse response, string key) =>
        response.AdditionalProperties?.TryGetValue(key, out var value) == true
            ? value as string ?? (value is JsonElement { ValueKind: JsonValueKind.String } json ? json.GetString() : null) : null;

    private static long? ResponseInteger(ChatResponse response, string key)
    {
        if (response.AdditionalProperties?.TryGetValue(key, out var value) != true) return null;
        return value switch
        {
            long number => number,
            int number => number,
            JsonElement { ValueKind: JsonValueKind.Number } json when json.TryGetInt64(out var number) => number,
            _ => null
        };
    }

    private static bool ResponseFlag(ChatResponse response, string key) =>
        response.AdditionalProperties?.TryGetValue(key, out var value) == true &&
        (value is true || value is JsonElement { ValueKind: JsonValueKind.True });

    private bool CompleteTask(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        CompletionDecision? decision;
        lock (gate) decision = completionDecision;
        if (decision is null) return false;
        var detail = decision.Outcome == "blocked" ? decision.Blocker : Options.Mode switch
        {
            AgentRunMode.Plan => "Plan ready for review. Switch to Normal or Goal to execute it.",
            AgentRunMode.Goal => "Goal completed with a finished plan and verification.",
            _ => "Task completed."
        };
        lock (gate)
        {
            runState = runState with { CompletionSummary = decision.Summary, Verification = decision.Verification };
            // The final answer is model-authored data inside agent_finish. Retain it as
            // ordinary assistant text as well, so reload/compaction does not hide it in
            // an old tool-argument card after the latest run state has changed.
            messages.Add(new(ChatRole.Assistant, decision.Summary));
        }
        Emit("assistant", decision.Summary);
        EndRun(decision.Outcome, decision.Outcome == "blocked" ? "model_blocked" : "explicit_completion", detail, decision.Outcome == "blocked");
        return true;
    }

    private void RecordProgress(string receipt)
    {
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(receipt)));
        lock (gate)
        {
            if (!progressReceipts.Add(fingerprint)) return;
            progressVersion++;
            progressReceiptOrder.Enqueue(fingerprint);
            if (progressReceiptOrder.Count > 128) progressReceipts.Remove(progressReceiptOrder.Dequeue());
        }
    }

    private bool CheckProgress(long before)
    {
        int count;
        lock (gate)
        {
            count = before == progressVersion ? runState.NoProgressCount + 1 : 0;
            runState = runState with { NoProgressCount = count };
        }
        if (count < Options.MaxNoProgressTurns) return true;
        EndRun("paused", "no_progress", $"Paused after {count} rounds without new tool results, plan progress or output continuation. Review the last error, adjust the task or output budget, then Resume.", true);
        return false;
    }

    private string RunInstructions()
    {
        var mode = Options.Mode switch
        {
            AgentRunMode.Plan => "PLAN MODE: inspect using read-only tools and publish a concrete plan with agent_plan. Do not apply edits or execute code. Finish with agent_finish after the plan is ready; its pending steps are for later execution.",
            AgentRunMode.Goal => "GOAL MODE: turn the task and success criteria into a concrete plan. Implement, inspect and verify each step. Keep working until every step and criterion is satisfied. Call agent_finish with the completed plan and verification, or report an exact blocker.",
            _ => "NORMAL MODE: perform the requested work, including implementation and verification. Continue after progress reports and tool results. Complete pending plan steps. End only by calling agent_finish with the result or a concrete blocker."
        };
        return mode + "\nThe per-response output limit is not a task-completion signal. If a response is truncated, generate smaller complete tool calls; incomplete calls were not executed. " +
            "Call agent_finish alone in a separate tool round after inspecting earlier results. Do not mark a plan complete without doing its work. " +
            "Verification must describe observed results; identify unavailable checks honestly. A blocked task needs an exact blocker and the next required input.";
    }

    private string TaskStateContext() => AgentJson.String(new
    {
        mode = Options.Mode, permissionMode = Options.PermissionMode, successCriteria = Options.SuccessCriteria, plan,
        turns = runState.TotalTurns, unfinishedSteps = plan.Count(step => step.Status != "completed")
    });

    private AgentRunState RestoreRunState(AgentRunState? value, bool hasTask)
    {
        if (value is null) return NewRunState() with { Resumable = hasTask, Detail = hasTask ? "Restored task ready to resume." : "" };
        if (!Enum.IsDefined(value.Mode) || value.Phase is not ("idle" or "running" or "continuing" or "compacting" or "approval" or "completed" or "blocked" or "paused" or "cancelled" or "error") ||
            value.TurnCount is < 0 or > 10000000 || value.TotalTurns is < 0 or > 10000000 || value.TurnCount > value.TotalTurns ||
            value.TurnLimit is < 0 or > 10000 || value.ContinuationCount is < 0 or > 10000000 || value.OutputLimitCount is < 0 or > 10000000 ||
            value.OutputLimitCount > value.ContinuationCount || value.ContinuationCount > value.TotalTurns ||
            value.NoProgressCount is < 0 or > 100 || value.NoProgressCount > value.TotalTurns || value.StopReason is null || value.StopReason.Length > 256 ||
            value.Detail is null || value.Detail.Length > 32000 || value.SuccessCriteria is null || value.SuccessCriteria.Length > 16000 ||
            value.CompletionSummary is null || value.CompletionSummary.Length > 32000 || value.Verification is null || value.Verification.Count > 64 ||
            value.Verification.Any(item => string.IsNullOrWhiteSpace(item) || item.Length > 2048))
            throw new ArgumentException("Invalid task lifecycle checkpoint.");
        if (!hasTask) return NewRunState();
        var changedMode = value.Mode != Options.Mode || value.SuccessCriteria != Options.SuccessCriteria;
        var interrupted = value.Phase is "running" or "continuing" or "compacting" or "approval";
        return value with
        {
            Mode = Options.Mode, SuccessCriteria = Options.SuccessCriteria, TurnLimit = Options.MaxTurns,
            Phase = changedMode || interrupted ? "paused" : value.Phase,
            StopReason = changedMode ? "mode_changed" : interrupted ? "restored" : value.StopReason,
            Detail = changedMode ? "Execution mode or success criteria changed. Resume the task under the selected policy." :
                interrupted ? "Restored interrupted task. Inspect state and resume remaining work." : value.Detail,
            Resumable = hasTask && (changedMode || interrupted || value.Resumable),
            CompletionSummary = changedMode ? "" : value.CompletionSummary,
            Verification = changedMode ? [] : value.Verification.ToArray()
        };
    }
}
