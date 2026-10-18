using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using netDxf.Shadow.Agents;
using netDxf.Shadow.Mcp;

internal static class AgentRunVerification
{
    internal static async Task<int> RunAsync()
    {
        var checks = 0;
        void Check(bool value, string description)
        {
            if (!value) throw new InvalidOperationException("Persistent agent verification failed: " + description);
            checks++;
        }
        var options = new AgentOptions { PermissionMode = AgentPermissionMode.FullAccess, MaxRetries = 0, MaxOutputTokens = 1024 };
        var empty = McpJson.Parse("""{"type":"object","properties":{},"additionalProperties":false}""");
        Check(options.Mode == AgentRunMode.Normal && options.MaxTurns == 0, "Normal mode has no fixed default turn cutoff");

        // Output exhaustion is continuation, including a reasoning-only/empty terminal update.
        foreach (var opaqueOnly in new[] { false, true })
        {
            using var client = new ScriptedChatClient();
            using var agent = new AgentSession(client, new McpToolRegistry(), options, ownsClient: false);
            Enqueue(client, ChatFinishReason.Length, opaqueOnly ? [] : [new TextContent("First portion of the answer.")]);
            Enqueue(client, ChatFinishReason.Length, new TextReasoningContent("partial thinking") { ProtectedData = "discard-this-incomplete-signature" },
                new TextContent("Second portion of the answer."));
            Enqueue(client, ChatFinishReason.Stop, new TextContent("The work is complete."));
            client.Enqueue((input, _, token) =>
            {
                Check(input.Any(message => message.Text.Contains("Second portion", StringComparison.Ordinal)), "length continuation preserves usable partial text");
                Check(!input.SelectMany(message => message.Contents).OfType<TextReasoningContent>().Any(), "incomplete protected reasoning never enters continuation input");
                Check(agent.Poll().Events.All(item => item.Type != "completed"), "plain Stop text cannot falsely complete a task");
                return Raw(ChatFinishReason.ToolCalls, [Finish("output-done")], token);
            });
            await agent.RunAsync("Write the complete answer without stopping at an output limit.");
            Check(agent.Poll().Status == "completed" && client.Calls == 4, "consecutive output limits and ordinary text automatically reach explicit completion");
            Check(agent.RunState.OutputLimitCount == 2 && agent.RunState.ContinuationCount >= 2,
                "output limits and continuation are visible in persistent run state");
            Check(!agent.Export().Contains("discard-this-incomplete-signature", StringComparison.Ordinal), "a truncated reasoning signature cannot poison a checkpoint");
        }

        // A length-truncated batch may contain a syntactically valid mutation before a partial
        // call. Nothing in that uncommitted response may execute or enter the call cache.
        {
            var mutations = 0;
            var registry = new McpToolRegistry();
            registry.Register(new("mutate", "One observable mutation", empty), (_, _) =>
            { mutations++; return ValueTask.FromResult(new McpToolResult("Mutation " + mutations)); });
            using var client = new ScriptedChatClient();
            using var agent = new AgentSession(client, registry, options, ownsClient: false);
            Enqueue(client, ChatFinishReason.Length, Call("write-once", "mutate"),
                new FunctionCallContent("unfinished", "mutate", new Dictionary<string, object?>()) { Exception = new JsonException("Incomplete arguments") });
            client.Enqueue((input, _, token) =>
            {
                Check(mutations == 0, "a truncated response executes neither complete-looking nor malformed calls");
                Check(!input.SelectMany(message => message.Contents).OfType<FunctionCallContent>().Any(), "discarded partial calls leave no orphaned transcript entries");
                return Raw(ChatFinishReason.ToolCalls, [Call("write-once", "mutate")], token);
            });
            Enqueue(client, ChatFinishReason.ToolCalls, Call("write-once", "mutate"));
            Enqueue(client, ChatFinishReason.ToolCalls, Finish("write-finished"));
            await agent.RunAsync("Apply one complete mutation.");
            Check(mutations == 1 && agent.Poll().Status == "completed", "a regenerated and then repeated call ID executes exactly once");
            using var imported = new AgentSession(new ScriptedChatClient(), registry, options);
            imported.Import(agent.Export());
            Check(imported.Transcript().SelectMany(message => message.Contents).All(content => content.CallId != "unfinished"),
                "truncated call discard preserves an importable checkpoint");
        }

        // Provider termination metadata and portable bridge flags must reject a whole invalid
        // batch, including any complete-looking mutation that preceded the malformed call.
        foreach (var invalidBatch in new[] { "provider-stop", "parsed-call-error", "portable-bool", "portable-json" })
        {
            var mutations = 0;
            var registry = new McpToolRegistry();
            registry.Register(new("mutate", "Commit a corrected batch once", empty), (_, _) =>
            { mutations++; return ValueTask.FromResult(new McpToolResult("Mutation " + mutations)); });
            using var client = new ScriptedChatClient();
            using var agent = new AgentSession(client, registry, options, ownsClient: false);
            var metadata = new AdditionalPropertiesDictionary();
            if (invalidBatch == "provider-stop") metadata["shadow:stop-reason"] = "MALFORMED_FUNCTION_CALL";
            if (invalidBatch == "portable-bool") metadata["shadow:invalid-tool-batch"] = true;
            if (invalidBatch == "portable-json") metadata["shadow:invalid-tool-batch"] = McpJson.Element(true);
            var contents = new List<AIContent> { new TextContent("Regenerate this invalid batch."), Call("corrected-write", "mutate") };
            if (invalidBatch == "parsed-call-error")
                contents.Add(new FunctionCallContent("malformed", "mutate", new Dictionary<string, object?>()) { Exception = new JsonException("Incomplete arguments") });
            client.Enqueue((_, _, token) => Raw(ChatFinishReason.ToolCalls, contents, token, metadata));
            client.Enqueue((input, _, token) =>
            {
                Check(mutations == 0, invalidBatch + " prevents every mutation in the rejected batch");
                Check(!input.SelectMany(message => message.Contents).OfType<FunctionCallContent>().Any(),
                    invalidBatch + " leaves no partial calls or receipts in continuation input");
                return Raw(ChatFinishReason.ToolCalls, [Call("corrected-write", "mutate")], token);
            });
            Enqueue(client, ChatFinishReason.ToolCalls, Finish("corrected-batch-finished"));
            await agent.RunAsync("Correct the malformed tool batch before committing.");
            Check(mutations == 1 && agent.Poll().Status == "completed" && agent.RunState.OutputLimitCount == 0,
                invalidBatch + " regenerates a non-length response and commits exactly once");
        }

        // The harness owns completion; free-form prose and unfinished structured plans do not.
        {
            using var client = new ScriptedChatClient();
            using var agent = new AgentSession(client, new McpToolRegistry(), options, ownsClient: false);
            Enqueue(client, ChatFinishReason.ToolCalls, Plan("plan-start", new AgentPlanStep("Implement and verify", "in_progress")));
            Enqueue(client, ChatFinishReason.ToolCalls, Finish("premature-finish"));
            client.Enqueue((input, _, token) =>
            {
                Check(Failed(input, "premature-finish"), "Normal mode rejects completion while a plan step remains unfinished");
                Check(input.Any(message => message.Text.Contains("Implement and verify", StringComparison.Ordinal)), "current plan state is supplied explicitly to the model");
                return Raw(ChatFinishReason.ToolCalls, [Plan("plan-done", new AgentPlanStep("Implement and verify", "completed"))], token);
            });
            Enqueue(client, ChatFinishReason.ToolCalls, Finish("normal-finished"));
            await agent.RunAsync("Implement and verify the task.");
            Check(agent.Poll().Status == "completed" && agent.Plan.All(step => step.Status == "completed"), "Normal mode continues until its plan and explicit finish agree");
            Check(agent.RunState.CompletionSummary.Length > 0 && !agent.RunState.Resumable, "completed runs expose a completion receipt rather than a resumable pause");
            Check(agent.Transcript().Last().Role == "assistant" && agent.Transcript().Last().Contents.Any(content => content.Kind == "text"
                && content.Text == agent.RunState.CompletionSummary), "accepted completion remains ordinary assistant text in the conversation");
            using var restored = new AgentSession(new ScriptedChatClient(), new McpToolRegistry(), options);
            restored.Import(agent.Export());
            Check(restored.Transcript().Last().Contents.Any(content => content.Kind == "text" && content.Text == agent.RunState.CompletionSummary),
                "the final answer survives checkpoint export, import and reload");
        }

        // Goal mode requires a concrete finished plan and verification, independent of prose.
        {
            const string criteria = "The probe reports one verified result.";
            using var client = new ScriptedChatClient();
            using var agent = new AgentSession(client, new McpToolRegistry(), options with { Mode = AgentRunMode.Goal, SuccessCriteria = criteria }, ownsClient: false);
            Enqueue(client, ChatFinishReason.ToolCalls, Finish("goal-no-plan"));
            client.Enqueue((input, _, token) =>
            {
                Check(Failed(input, "goal-no-plan"), "Goal mode rejects completion without a plan");
                Check(input.Any(message => message.Text.Contains(criteria, StringComparison.Ordinal)), "Goal success criteria reach model context");
                return Raw(ChatFinishReason.ToolCalls, [Plan("goal-plan", new AgentPlanStep("Verify the probe", "completed"))], token);
            });
            Enqueue(client, ChatFinishReason.ToolCalls, Finish("goal-no-proof", verification: []));
            client.Enqueue((input, _, token) =>
            {
                Check(Failed(input, "goal-no-proof"), "Goal mode rejects completion without verification evidence");
                return Raw(ChatFinishReason.ToolCalls, [Finish("goal-done", verification: [criteria])], token);
            });
            await agent.RunAsync("Meet the supplied success criteria.");
            Check(agent.Poll().Status == "completed" && agent.RunState.Verification.Contains(criteria), "Goal completion records its verification evidence");
            var checkpoint = agent.Export();
            using var imported = new AgentSession(new ScriptedChatClient(), new McpToolRegistry(), options with { Mode = AgentRunMode.Plan, SuccessCriteria = "Review only." });
            imported.Import(checkpoint);
            Check(imported.Options.Mode == AgentRunMode.Plan && imported.RunState.Mode == AgentRunMode.Plan && imported.RunState.SuccessCriteria == "Review only.",
                "host mode and success criteria take precedence over imported run metadata");
        }

        // Plan mode can inspect and publish plan metadata, but FullAccess cannot authorize writes.
        {
            var mutations = 0; var reads = 0;
            var registry = new McpToolRegistry();
            registry.Register(new("read", "Inspect current state", empty, true), (_, _) =>
            { reads++; return ValueTask.FromResult(new McpToolResult("Read " + reads)); });
            registry.Register(new("mutate", "Change current state", empty), (_, _) =>
            { mutations++; return ValueTask.FromResult(new McpToolResult("Changed")); });
            var workspace = new MemoryAgentWorkspace();
            AgentWorkspaceTools.Register(registry, workspace);
            using var client = new ScriptedChatClient();
            using var agent = new AgentSession(client, registry, options with { Mode = AgentRunMode.Plan }, workspace, ownsClient: false);
            Check((await agent.ExecuteToolAsync("mutate", McpJson.EmptyObject)).IsError && mutations == 0,
                "Plan mode blocks manual mutations even with FullAccess");
            Check(agent.Poll().Events.Any(item => item.Type == "toolCompleted" && item.Data is { } data
                && data.TryGetProperty("name", out var name) && name.GetString() == "mutate"
                && data.TryGetProperty("isError", out var error) && error.ValueKind == JsonValueKind.True
                && data.TryGetProperty("executed", out var executed) && executed.ValueKind == JsonValueKind.False
                && (item.Message + data.GetRawText()).Contains("Plan mode", StringComparison.Ordinal)),
                "a Plan-rejected mutation is visible to polling with its reason and executed=false");
            Check((await agent.ExecuteToolAsync("workspace_write", McpJson.Element(new { path = "forbidden.txt", content = "write", expectedVersion = 0 }))).IsError && workspace.List().Count == 0,
                "Plan mode also blocks workspace writes through direct execution");
            client.Enqueue((_, advertised, token) =>
            {
                var names = advertised!.Tools!.OfType<AIFunctionDeclaration>().Select(tool => tool.Name).ToArray();
                Check(names.Contains("read") && names.Contains("agent_plan") && names.Contains("agent_finish") && !names.Contains("mutate") && !names.Contains("workspace_write"),
                    "Plan mode advertises inspection and task controls without write capabilities");
                return Raw(ChatFinishReason.ToolCalls, [Call("invented-write", "mutate")], token);
            });
            client.Enqueue((input, _, token) =>
            {
                Check(Failed(input, "invented-write") && mutations == 0, "an invented model write cannot bypass Plan mode");
                return Raw(ChatFinishReason.ToolCalls, [Call("inspect", "read")], token);
            });
            Enqueue(client, ChatFinishReason.ToolCalls, Plan("review-plan", new("Implement after review", "pending"), new("Verify after implementation", "pending")));
            Enqueue(client, ChatFinishReason.ToolCalls, Finish("plan-ready", verification: []));
            await agent.RunAsync("Inspect and prepare a plan without implementing it.");
            Check(agent.Poll().Status == "completed" && reads == 1 && mutations == 0 && agent.Plan.All(step => step.Status == "pending"),
                "Plan mode completes a reviewable plan while leaving implementation steps pending");
        }

        // Explicit blocking is a resumable terminal outcome, not completion or an automatic retry.
        foreach (var mode in new[] { AgentRunMode.Normal, AgentRunMode.Plan, AgentRunMode.Goal })
        {
            using var client = new ScriptedChatClient();
            using var agent = new AgentSession(client, new McpToolRegistry(), options with { Mode = mode }, ownsClient: false);
            Enqueue(client, ChatFinishReason.ToolCalls, Call("missing-blocker", "agent_finish", new { outcome = "blocked", summary = "Need input." }));
            client.Enqueue((input, _, token) =>
            {
                Check(Failed(input, "missing-blocker"), mode + " requires a concrete blocker before pausing");
                return Raw(ChatFinishReason.ToolCalls, [Call("blocked", "agent_finish", new { outcome = "blocked", summary = "Awaiting a required drawing.", blocker = "Attach the missing drawing." })], token);
            });
            await agent.RunAsync("Work on the missing drawing.");
            Check(client.Calls == 2 && !agent.IsRunning && agent.Poll().Status != "completed" && agent.RunState.Resumable
                && agent.RunState.Detail.Contains("missing drawing", StringComparison.OrdinalIgnoreCase), mode + " stops with an actionable, resumable blocker");
            using var restoredClient = new ScriptedChatClient();
            using var restored = new AgentSession(restoredClient, new McpToolRegistry(), options with { Mode = mode }, ownsClient: false);
            restored.Import(agent.Export());
            if (mode != AgentRunMode.Normal)
                Enqueue(restoredClient, ChatFinishReason.ToolCalls, Plan("unblocked-plan", new AgentPlanStep("Review or verify supplied drawing", mode == AgentRunMode.Plan ? "pending" : "completed")));
            Enqueue(restoredClient, ChatFinishReason.ToolCalls, Finish("unblocked"));
            await restored.RunAsync("The missing drawing is now available.");
            Check(restored.Poll().Status == "completed", mode + " can resume after the user resolves its blocker");
        }

        // A content filter may accompany a parsed call, but neither it nor a no-progress loop can mutate.
        {
            var mutations = 0;
            var registry = new McpToolRegistry();
            registry.Register(new("mutate", "Change current state", empty), (_, _) =>
            { mutations++; return ValueTask.FromResult(new McpToolResult("Changed")); });
            using var client = new ScriptedChatClient();
            using var agent = new AgentSession(client, registry, options, ownsClient: false);
            Enqueue(client, ChatFinishReason.ContentFilter, Call("filtered-write", "mutate"));
            await agent.RunAsync("Handle the provider's blocked response.");
            Check(mutations == 0 && client.Calls == 1 && agent.Poll().Status != "completed" && !agent.IsRunning,
                "content-filtered responses stop without execution, false completion or repeated inference");
        }
        {
            using var client = new ScriptedChatClient();
            using var agent = new AgentSession(client, new McpToolRegistry(), options with { MaxNoProgressTurns = 3 }, ownsClient: false);
            for (var index = 0; index < 12; index++) Enqueue(client, ChatFinishReason.Stop, new TextContent("I am still working."));
            await agent.RunAsync("Do not spin forever without progress.");
            Check(client.Calls <= 4 && agent.Poll().Status == "paused" && agent.RunState.Resumable && agent.RunState.NoProgressCount >= 3,
                "repeated unproductive responses pause observably rather than silently completing or spinning");
        }
        {
            using var client = new ScriptedChatClient();
            using var agent = new AgentSession(client, new McpToolRegistry(), options with { MaxNoProgressTurns = 3 }, ownsClient: false);
            for (var index = 0; index < 12; index++)
                Enqueue(client, ChatFinishReason.ToolCalls, Plan("oscillating-plan-" + index,
                    new AgentPlanStep("Implement and verify", index % 2 == 0 ? "pending" : "in_progress")));
            await agent.RunAsync("Make concrete progress instead of cycling the same plan states.");
            Check(client.Calls == 5 && agent.Poll().Status == "paused" && agent.RunState.StopReason == "no_progress"
                && agent.RunState.Resumable && agent.RunState.NoProgressCount == 3,
                "alternating previously observed plan states cannot reset the no-progress guard indefinitely");
        }

        // Work can exceed the former default turn cap; an explicitly configured cap still pauses.
        {
            var reads = 0;
            var registry = new McpToolRegistry();
            registry.Register(new("inspect_next", "Inspect the next object", empty, true), (_, _) =>
            { reads++; return ValueTask.FromResult(new McpToolResult("Object " + reads)); });
            using var client = new ScriptedChatClient();
            using var agent = new AgentSession(client, registry, options, ownsClient: false);
            for (var index = 0; index < 42; index++) Enqueue(client, ChatFinishReason.ToolCalls, Call("object-" + index, "inspect_next"));
            Enqueue(client, ChatFinishReason.ToolCalls, Finish("all-objects-inspected"));
            await agent.RunAsync("Inspect all 42 objects before completing.");
            Check(reads == 42 && client.Calls == 43 && agent.Poll().Status == "completed", "default runs continue beyond forty productive turns");
        }
        {
            var reads = 0;
            var registry = new McpToolRegistry();
            registry.Register(new("read", "Inspect next item", empty, true), (_, _) =>
            { reads++; return ValueTask.FromResult(new McpToolResult("Item " + reads)); });
            var capped = options with { MaxTurns = 2 };
            using var client = new ScriptedChatClient();
            using var agent = new AgentSession(client, registry, capped, ownsClient: false);
            Enqueue(client, ChatFinishReason.ToolCalls, Call("first", "read"));
            Enqueue(client, ChatFinishReason.ToolCalls, Call("second", "read"));
            await agent.RunAsync("Continue this task across an explicit turn budget.");
            Check(agent.Poll().Status == "paused" && agent.RunState.Resumable && agent.RunState.TurnCount == 2 && agent.RunState.TotalTurns == 2,
                "an explicit turn limit pauses and records a resumable run");
            using var restoredClient = new ScriptedChatClient();
            using var restored = new AgentSession(restoredClient, registry, capped, ownsClient: false);
            restored.Import(agent.Export());
            Enqueue(restoredClient, ChatFinishReason.ToolCalls, Finish("resumed-completion"));
            await restored.ResumeAsync();
            Check(restored.Poll().Status == "completed" && restored.RunState.TotalTurns == 3 && restored.RunState.TurnCount == 1,
                "checkpoint resume retains total turns and starts a fresh per-invocation budget");
        }

        // Finishing must be a separate call batch. A repeated write ID remains deduplicated
        // whether the invalid mixed batch rejects the whole batch or just its finish call.
        {
            var mutations = 0;
            var registry = new McpToolRegistry();
            registry.Register(new("mutate", "One change", empty), (_, _) =>
            { mutations++; return ValueTask.FromResult(new McpToolResult("Changed")); });
            using var client = new ScriptedChatClient();
            using var agent = new AgentSession(client, registry, options, ownsClient: false);
            Enqueue(client, ChatFinishReason.ToolCalls, Finish("mixed-finish"), Call("write-once", "mutate"));
            client.Enqueue((input, _, token) =>
            {
                Check(Failed(input, "mixed-finish") && agent.Poll().Events.All(item => item.Type != "completed"), "a mixed completion batch cannot end the task prematurely");
                return Raw(ChatFinishReason.ToolCalls, [Call("write-once", "mutate")], token);
            });
            Enqueue(client, ChatFinishReason.ToolCalls, Finish("separate-finish"));
            await agent.RunAsync("Commit once and then report completion separately.");
            Check(mutations == 1 && agent.Poll().Status == "completed", "completion regeneration never duplicates an already executed write");
        }

        // Stop remains responsive at an output boundary and through a blocked continuation.
        {
            using var client = new ScriptedChatClient();
            using var agent = new AgentSession(client, new McpToolRegistry(), options, ownsClient: false);
            agent.EventEmitted += item => { if (item.Type == "assistantDelta") agent.Cancel(); };
            Enqueue(client, ChatFinishReason.Length, new TextContent("Stop during this partial answer."));
            await agent.RunAsync("Exercise stop at an output boundary.");
            Check(client.Calls == 1 && agent.Poll().Status == "cancelled" && !agent.IsRunning,
                "cancellation at an output boundary wins over automatic continuation");
        }
        {
            var mutations = 0;
            var registry = new McpToolRegistry();
            registry.Register(new("mutate", "Commit exactly once", empty), (_, _) =>
            { mutations++; return ValueTask.FromResult(new McpToolResult("Committed")); });
            using var client = new ScriptedChatClient();
            using var agent = new AgentSession(client, registry, options, ownsClient: false);
            var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Enqueue(client, ChatFinishReason.ToolCalls, Call("committed", "mutate"));
            Enqueue(client, ChatFinishReason.Length, new TextContent("Partial verification."));
            client.Enqueue((_, _, token) => Wait(waiting, token));
            var run = agent.RunAsync("Commit and keep checking until finished.");
            await waiting.Task.WaitAsync(TimeSpan.FromSeconds(5));
            agent.Cancel(); await run.WaitAsync(TimeSpan.FromSeconds(5));
            Check(mutations == 1 && agent.Poll().Status == "cancelled" && agent.RunState.Resumable,
                "stop cancels a stalled continuation while preserving committed work");
            using var restoredClient = new ScriptedChatClient();
            using var restored = new AgentSession(restoredClient, registry, options, ownsClient: false);
            restored.Import(agent.Export());
            Check(restored.RunState.OutputLimitCount == 1, "output-limit history survives cancellation and checkpoint restore");
            Enqueue(restoredClient, ChatFinishReason.ToolCalls, Call("committed", "mutate"));
            Enqueue(restoredClient, ChatFinishReason.ToolCalls, Finish("checked-after-resume"));
            await restored.RunAsync("");
            Check(mutations == 1 && restored.Poll().Status == "completed", "resuming after output exhaustion and stop does not replay a committed mutation");
        }

        // Untrusted checkpoint mode metadata cannot expand the importing host's execution mode.
        {
            using var client = new ScriptedChatClient();
            using var agent = new AgentSession(client, new McpToolRegistry(), options with { Mode = AgentRunMode.Plan }, ownsClient: false);
            var checkpoint = JsonNode.Parse(agent.Export())!.AsObject();
            checkpoint.Remove("run");
            agent.Import(checkpoint.ToJsonString());
            Check(agent.RunState.Mode == AgentRunMode.Plan && agent.Options.Mode == AgentRunMode.Plan,
                "legacy checkpoints without run metadata retain the host-selected mode");
        }

        // Completion receipts are revalidated against the current task, even when a provider
        // reuses the same call identifier after a completed run or a rejected finish attempt.
        {
            using var client = new ScriptedChatClient();
            using var agent = new AgentSession(client, new McpToolRegistry(), options, ownsClient: false);
            Enqueue(client, ChatFinishReason.ToolCalls, Finish("reused-finish"));
            await agent.RunAsync("Complete the first task.");
            Enqueue(client, ChatFinishReason.ToolCalls, Plan("second-task-plan", new AgentPlanStep("Complete second task", "in_progress")));
            Enqueue(client, ChatFinishReason.ToolCalls, Finish("reused-finish"));
            client.Enqueue((input, _, token) =>
            {
                Check(Failed(input, "reused-finish") && agent.Plan.Single().Status == "in_progress",
                    "an old completion receipt cannot satisfy a new task's unfinished plan");
                return Raw(ChatFinishReason.ToolCalls, [Plan("second-task-plan-done", new AgentPlanStep("Complete second task", "completed"))], token);
            });
            Enqueue(client, ChatFinishReason.ToolCalls, Finish("reused-finish"));
            await agent.RunAsync("Complete a different task with its own plan.");
            Check(client.Calls == 5 && agent.Poll().Status == "completed", "a reused finish identifier is evaluated again after the task becomes complete");
        }

        // Reapplying task metadata is safe and necessary after a new task clears the old plan.
        {
            using var client = new ScriptedChatClient();
            using var agent = new AgentSession(client, new McpToolRegistry(), options with { Mode = AgentRunMode.Goal }, ownsClient: false);
            for (var task = 1; task <= 2; task++)
            {
                Enqueue(client, ChatFinishReason.ToolCalls, Plan("reused-plan", new AgentPlanStep("Inspect and verify this task", "completed")));
                client.Enqueue((_, _, token) =>
                {
                    Check(agent.Plan.Count == 1 && agent.Plan[0].Status == "completed", "a reused plan ID publishes metadata for the current Goal task");
                    return Raw(ChatFinishReason.ToolCalls, [Finish("reused-goal-finish")], token);
                });
                await agent.RunAsync("Complete distinct Goal task " + task + ".");
                Check(agent.Poll().Status == "completed", "Goal completion revalidates reused plan and finish IDs for a new task");
            }
            Check(client.Calls == 4, "reusing metadata-control identifiers cannot strand a second Goal task");
        }

        // Manual approvals are operations on the current workspace, not a replacement task.
        {
            var mutations = 0;
            var registry = new McpToolRegistry();
            registry.Register(new("mutate", "Approved manual change", empty), (_, _) =>
            { mutations++; return ValueTask.FromResult(new McpToolResult("Changed")); });
            using var client = new ScriptedChatClient();
            using var agent = new AgentSession(client, registry, options with { PermissionMode = AgentPermissionMode.Ask }, ownsClient: false);
            Enqueue(client, ChatFinishReason.ToolCalls, Finish("completed-before-manual"));
            await agent.RunAsync("Complete before the user's manual operation.");
            var before = agent.RunState;
            agent.EventEmitted += item =>
            {
                if (item.Type == "approvalRequested") agent.Approve(item.Data!.Value.GetProperty("requestId").GetString()!, true);
            };
            var manual = await agent.ExecuteToolAsync("mutate", McpJson.EmptyObject);
            Check(!manual.IsError && mutations == 1 && agent.RunState.Phase == "completed" && agent.Poll().Status == "completed"
                && agent.RunState.CompletionSummary == before.CompletionSummary, "a manual approval restores the prior completed lifecycle after execution");
        }

        // Invalid run metadata must fail before any transcript or workspace is replaced.
        {
            using var client = new ScriptedChatClient();
            using var agent = new AgentSession(client, new McpToolRegistry(), options, ownsClient: false);
            Enqueue(client, ChatFinishReason.ToolCalls, Finish("checkpoint-ready"));
            await agent.RunAsync("Retain a valid task lifecycle.");
            agent.Workspace.Write("keep.txt", "existing workspace", 0);
            var stable = agent.Export();
            (string Description, Action<JsonObject> Apply)[] corruptions =
            [
                ("negative turn count", value => value["turnCount"] = -1),
                ("negative total turns", value => value["totalTurns"] = -1),
                ("turn count exceeds total", value => { value["turnCount"] = 2; value["totalTurns"] = 1; }),
                ("turn limit exceeds its bound", value => value["turnLimit"] = 10001),
                ("negative continuation count", value => value["continuationCount"] = -1),
                ("negative output-limit count", value => value["outputLimitCount"] = -1),
                ("output-limit count exceeds continuations", value => { value["outputLimitCount"] = 2; value["continuationCount"] = 1; }),
                ("no-progress count exceeds its bound", value => value["noProgressCount"] = 101),
                ("counter is a string", value => value["turnCount"] = "not a counter"),
            ];
            foreach (var corrupt in corruptions)
            {
                var checkpoint = JsonNode.Parse(stable)!.AsObject();
                corrupt.Apply(checkpoint["run"]!.AsObject());
                var rejected = false;
                try { agent.Import(checkpoint.ToJsonString()); }
                catch (Exception error) when (error is ArgumentException or JsonException) { rejected = true; }
                Check(rejected && agent.Export() == stable, "malformed lifecycle counters are rejected atomically: " + corrupt.Description);
            }
        }
        return checks;
    }

    private static FunctionCallContent Call(string id, string name, object? arguments = null) => new(id, name,
        McpJson.Element(arguments ?? new { }).Deserialize<Dictionary<string, object?>>(McpJson.Options));
    private static FunctionCallContent Plan(string id, params AgentPlanStep[] steps) => Call(id, "agent_plan", new { steps });
    private static FunctionCallContent Finish(string id, string[]? verification = null) => Call(id, "agent_finish", new
    {
        outcome = "completed", summary = "The requested fixture task is complete.",
        verification = verification ?? ["Verified the fixture's expected final state."]
    });
    private static bool Failed(IReadOnlyList<ChatMessage> messages, string callId) => messages.SelectMany(message => message.Contents)
        .OfType<FunctionResultContent>().Any(result => result.CallId == callId
            && result.AdditionalProperties?.TryGetValue("shadow:isError", out var error) == true
            && (error is true || error is JsonElement { ValueKind: JsonValueKind.True }));
    private static void Enqueue(ScriptedChatClient client, ChatFinishReason reason, params AIContent[] contents) =>
        client.Enqueue((_, _, token) => Raw(reason, contents, token));
    private static async IAsyncEnumerable<ChatResponseUpdate> Raw(ChatFinishReason reason, IList<AIContent> contents,
        [EnumeratorCancellation] CancellationToken token, AdditionalPropertiesDictionary? metadata = null)
    {
        token.ThrowIfCancellationRequested(); await Task.Yield();
        var update = contents.Count == 0 ? new ChatResponseUpdate() : new ChatResponseUpdate(ChatRole.Assistant, contents);
        update.FinishReason = reason;
        update.MessageId = Guid.NewGuid().ToString("N"); update.ResponseId = Guid.NewGuid().ToString("N");
        yield return update;
        // Official adapters attach batch metadata to a terminal update without MessageId,
        // so aggregation retains it on ChatResponse through both native and NDJSON paths.
        if (metadata is { Count: > 0 }) yield return new ChatResponseUpdate { AdditionalProperties = metadata };
    }
    private static async IAsyncEnumerable<ChatResponseUpdate> Wait(TaskCompletionSource started,
        [EnumeratorCancellation] CancellationToken token)
    {
        started.TrySetResult();
        await Task.Delay(Timeout.InfiniteTimeSpan, token);
        yield break;
    }
}
