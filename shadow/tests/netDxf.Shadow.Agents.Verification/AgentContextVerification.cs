using System.Collections;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.AI;
using netDxf.Shadow.Agents;
using netDxf.Shadow.Mcp;

internal static class AgentContextVerification
{
    public static async Task<int> RunAsync()
    {
        var checks = 0;
        void Check(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); checks++; }
        var tools = new CountingExecutor();
        var schema = McpJson.Parse("""{"type":"object","properties":{"query":{"type":"string"}},"additionalProperties":false}""");
        for (var i = 0; i < 256; i++) tools.Definitions.Add(new("fixture_" + i, new string('d', 256), schema, i % 2 == 0));
        using var agent = new AgentSession(new ScriptedChatClient(), tools,
            new() { MaxOutputTokens = 1024, ContextWindowTokens = 4_000_000, PermissionMode = AgentPermissionMode.FullAccess });
        for (var i = 0; i < 8; i++) agent.Pin(new string('p', 4000));
        agent.SetPlan(Enumerable.Range(0, 64).Select(i => new AgentPlanStep(i + new string('s', 400), "pending")));
        var expected = agent.ContextTokens;
        for (var i = 0; i < 10; i++) _ = agent.Poll(long.MaxValue);
        tools.Reads = tools.Enumerations = 0;
        const int polls = 1000;
        var allocation = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        var unchanged = true;
        for (var i = 0; i < polls; i++) unchanged &= agent.Poll(long.MaxValue).ContextTokens == expected;
        watch.Stop(); allocation = GC.GetAllocatedBytesForCurrentThread() - allocation;
        Check(unchanged, "Repeated polling changed the context estimate.");
        Check(tools.Reads == polls && tools.Enumerations == polls, "Unchanged polling must scan the live catalog once without rebuilding it.");
        Console.WriteLine($"AGENT_CONTEXT_METRIC polls={polls} tools=256 planSteps=64 pinCharacters=32000 catalogReads={tools.Reads} enumerations={tools.Enumerations} allocatedBytes={allocation} elapsedMilliseconds={watch.Elapsed.TotalMilliseconds:F3}");

        int FreshTokens(AgentSession value)
        {
            using var fresh = new AgentSession(new ScriptedChatClient(), tools, value.Options);
            fresh.Import(value.Export());
            return fresh.ContextTokens;
        }
        void Fresh(AgentSession value, string change) => Check(value.ContextTokens == FreshTokens(value), "Context cache differs from a fresh checkpoint after " + change + ".");
        var pin = agent.Pin(new string('x', 9000));
        Check(agent.ContextTokens > expected, "Pinning did not increase cached context."); Fresh(agent, "pin addition");
        agent.Unpin(pin.Id); Check(agent.ContextTokens == expected, "Unpinning did not restore cached context.");
        agent.SetPlan([new("Short replacement", "pending")]); Fresh(agent, "plan replacement");

        var beforeToolChange = agent.ContextTokens;
        tools.Definitions[0] = tools.Definitions[0] with { InputSchema = McpJson.Element(new {
            type = "object", properties = new { changed = new { type = "string", description = new string('x', 12000) } }, additionalProperties = false }) };
        Check(agent.ContextTokens > beforeToolChange + 3000, "Same-name same-count tool schema replacement was not detected."); Fresh(agent, "same-count schema replacement");
        tools.Definitions.Reverse(); Check(agent.ContextTokens == FreshTokens(agent), "Reordering a dynamic catalog changed its token estimate.");
        tools.FreshListOnRead = true;
        for (var i = 0; i < 5; i++) Check(agent.ContextTokens == FreshTokens(agent), "A newly allocated host catalog lost its cached definition semantics.");
        tools.Definitions.Add(new("shadow_state", "Inspect state", schema, true)); Fresh(agent, "tool addition and workflow guidance");
        tools.Definitions.RemoveAt(tools.Definitions.Count - 1); Fresh(agent, "tool removal and workflow guidance");

        var liveTools = new CountingExecutor();
        liveTools.Definitions.Add(new("initial", "Initial tool", schema, true));
        using (var live = new AgentSession(new ScriptedChatClient(), liveTools, agent.Options))
        {
            _ = live.ContextTokens;
            liveTools.ChangeOnCount = () => liveTools.Definitions.Add(new("added", "Added during comparison", schema, true));
            Check(live.ToolDefinitions.Any(tool => tool.Name == "added"), "A catalog growing during comparison was not refreshed.");
            liveTools.ChangeOnCount = () => liveTools.Definitions.RemoveAt(1);
            Check(live.ToolDefinitions.All(tool => tool.Name != "added"), "A catalog shrinking during comparison was not refreshed.");
        }

        AgentCheckpoint Seed(char text, int count = 8) => new(1, Guid.NewGuid().ToString("N"), "summary-" + text,
            Enumerable.Range(0, count).Select(i => new AgentMessageRecord(i % 2 == 0 ? "user" : "assistant",
                [new("text", new string(text, 4000))])).ToArray(), [new("seed-pin", new string(text, 1200))], [],
            [new(new string(text, 100), "pending")], "task-" + text);
        agent.Import(JsonSerializer.Serialize(Seed('a'), McpJson.Options)); _ = agent.ContextTokens;
        var replacement = Seed('b') with { Messages = Seed('b').Messages.Select(m => m with { Contents = [new("text", new string('b', 7000))] }).ToArray(),
            Pins = [new("seed-pin", new string('b', 4000))], Plan = [new(new string('b', 700), "pending")] };
        var beforeImport = agent.ContextTokens;
        agent.Import(JsonSerializer.Serialize(replacement, McpJson.Options));
        Check(agent.ContextTokens > beforeImport, "Same-count checkpoint import retained old messages, pins or plan sizes."); Fresh(agent, "same-count checkpoint replacement");
        agent.Reset(); Fresh(agent, "reset");
        Check(agent.ContextTokens < beforeImport, "Reset retained old context bytes.");

        using (var provider = new ScriptedChatClient())
        using (var compacting = new AgentSession(provider, tools, agent.Options, ownsClient: false))
        {
            compacting.Import(JsonSerializer.Serialize(Seed('c', 20), McpJson.Options));
            var before = compacting.ContextTokens;
            provider.EnqueueResponse(new TextContent("Retained compact notes."));
            await compacting.CompactAsync();
            Check(compacting.ContextTokens < before, "Compaction retained old message and summary sizes."); Fresh(compacting, "compaction summary replacement");
            provider.EnqueueResponse(new TextContent("Task verified.")); await compacting.RunAsync("Additional user instruction.");
            Fresh(compacting, "appended user, assistant and tool messages plus turn state");
        }

        var dynamicTools = new CountingExecutor();
        dynamicTools.Definitions.Add(new("shadow_state", "Inspect", schema, true));
        using (var provider = new ScriptedChatClient())
        using (var readOnly = new AgentSession(provider, dynamicTools, agent.Options with { PermissionMode = AgentPermissionMode.ReadOnly }, ownsClient: false))
        {
            provider.Enqueue((input, options, token) => {
                Check(options!.Tools!.Any(t => t.Name == "shadow_state") && input.Any(m => m.Text.Contains("CAD workflow:", StringComparison.Ordinal)), "Initial read-only declaration/guidance missing.");
                return ScriptedChatClient.Response([new TextContent("Inspected.")], token, finish: true);
            });
            await readOnly.RunAsync("Inspect."); _ = readOnly.ContextTokens;
            dynamicTools.Definitions[0] = dynamicTools.Definitions[0] with { ReadOnly = false };
            provider.Enqueue((input, options, token) => {
                Check(options!.Tools!.All(t => t.Name != "shadow_state") && input.All(m => !m.Text.Contains("CAD workflow:", StringComparison.Ordinal)), "Changed tool permission retained stale declarations or guidance.");
                return ScriptedChatClient.Response([new TextContent("No read available.")], token, finish: true);
            });
            await readOnly.RunAsync("Inspect again.");
            Check(readOnly.Poll().Status == "completed", "Read-only run failed after dynamic policy change.");
        }

        using (var provider = new ScriptedChatClient())
        using (var calibrated = new AgentSession(provider, tools, agent.Options, ownsClient: false))
        {
            _ = calibrated.ContextTokens;
            provider.EnqueueResponse(new UsageContent(new() { InputTokenCount = 4_000_000 }), new TextContent("Verified."));
            await calibrated.RunAsync("Inspect calibration.");
            var raw = FreshTokens(calibrated);
            Check(calibrated.ContextTokens >= raw * 4 - 3 && calibrated.ContextTokens <= raw * 4, "Usage calibration was not applied to cached raw context bytes.");
            var previous = calibrated.ContextTokens;
            for (var i = 0; i < 5; i++) Check(calibrated.Poll(long.MaxValue).ContextTokens == previous, "Polling changed calibrated context.");
            calibrated.Reset(); Fresh(calibrated, "calibration reset");
        }
        return checks;
    }

    private sealed class CountingExecutor : IMcpToolExecutor, IReadOnlyList<McpToolDefinition>
    {
        public readonly List<McpToolDefinition> Definitions = [];
        public int Reads, Enumerations;
        public bool FreshListOnRead;
        public Action? ChangeOnCount;
        public IReadOnlyList<McpToolDefinition> Tools { get { Reads++; return FreshListOnRead ? Definitions.ToArray() : this; } }
        public int Count { get { var count = Definitions.Count; var change = ChangeOnCount; ChangeOnCount = null; change?.Invoke(); return count; } }
        public McpToolDefinition this[int index] => Definitions[index];
        public IEnumerator<McpToolDefinition> GetEnumerator() { Enumerations++; return Definitions.GetEnumerator(); }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        public ValueTask<McpToolResult> ExecuteAsync(string name, JsonElement arguments, CancellationToken cancellationToken = default) => throw new InvalidOperationException("This fixture must not execute a tool.");
    }
}
