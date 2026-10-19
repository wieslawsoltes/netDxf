using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.AI;
using netDxf.Shadow.Agents;
using netDxf.Shadow.Agents.Evaluation;
using netDxf.Shadow.Mcp;

var checks = 0;
EvaluationCases.SelfTest(); Check(true, "native graders accept exact expected drawings and reject negative controls");
var scenario = EvaluationCases.All.Single(c => c.Id == "edit");
var fixture = EvaluationFixture.Freeze(scenario); var identical = EvaluationFixture.Freeze(scenario);
Check(fixture.Fingerprint == identical.Fingerprint && fixture.ExportSeed().SequenceEqual(identical.ExportSeed()), "fixed seed bytes reproduce across independent fixture construction");
Check(new EvaluationFixture(scenario with { Prompt = scenario.Prompt + " changed" }, fixture.ExportSeed()).Fingerprint != fixture.Fingerprint, "prompt changes invalidate task comparison");
Check(new EvaluationFixture(scenario with { GraderVersion = "2" }, fixture.ExportSeed()).Fingerprint != fixture.Fingerprint, "grader changes invalidate task comparison");
var exported = fixture.ExportSeed(); exported[0] ^= 1; Check(exported[0] != fixture.ExportSeed()[0], "seed bytes are immutable after construction");
var runner = new EvaluationRunner(); var correct = new ScriptedTarget();
var first = await runner.RunAsync(fixture, correct); var second = await runner.RunAsync(fixture, correct, repeat: 2);
Check(first.Passed && second.Passed && first.UndoTransactions == 1 && second.UndoTransactions == 1, "actual MCP edits pass grader and fresh isolated drawings repeat");
Check(first.Fingerprint == second.Fingerprint && first.ToolCatalogHash == second.ToolCatalogHash && first.Repeat != second.Repeat, "paired runs expose task/catalog identity and repeat");
Check(first.ToolCalls == 1 && first.ToolErrors == 0 && first.ToolMilliseconds >= 0 && first.ElapsedMilliseconds >= first.ToolMilliseconds, "actual tool execution latency and counts measured");
Check(first.DrawingDxf is { Length: > 0 } && !JsonSerializer.Serialize(first).Contains("DrawingDxf", StringComparison.Ordinal), "drawing artifact exported separately from report JSON");
Check(first.Telemetry.InputTokens is null && first.Cost is null, "unknown protocol token usage and subscription costs remain unknown");
var noMutation = await runner.RunAsync(fixture, new ScriptedTarget { Skip = true });
Check(!noMutation.Passed && noMutation.ToolCalls == 0 && noMutation.Checks.Any(c => !c.Passed), "completed language response without real edits fails");
var failure = await runner.RunAsync(fixture, new ScriptedTarget { Throw = true });
Check(failure.Status == "error" && !failure.Passed && !JsonSerializer.Serialize(failure).Contains("synthetic-secret"), "failed target retained without leaking exception credential");
using var stop = new CancellationTokenSource(); stop.Cancel();
var cancelled = await runner.RunAsync(fixture, new ScriptedTarget(), cancellationToken: stop.Token);
Check(cancelled.Status == "cancelled" && !cancelled.Passed, "cancelled target retained as failed observation");
var adapter = new AgentSessionEvaluationTarget(new("fixture", "agent-session", "fixture", "explicit-model", "none"), () => new FixtureClient(),
    new() { Model = "explicit-model", MaxTurns = 1, MaxRetries = 0 });
var observed = await runner.RunAsync(fixture, adapter);
Check(observed.Telemetry.InputTokens == 20 && observed.Telemetry.OutputTokens == 7 && observed.Telemetry.CacheReadTokens == 4 && observed.Telemetry.CacheWriteTokens is null, "harness adapter records reported usage without inventing absent cache writes");
Check(observed.Target.Settings?["maxTurns"] == "1" && observed.Target.Settings?["instructionsHash"].Length == 64 && observed.Telemetry.UsageComplete == true, "actual harness budgets and instruction identity recorded with usage completeness");
Check(observed.Telemetry.Inferences == 1 && observed.Telemetry.FirstTokenMilliseconds >= 0, "harness inference and first token latency observed");
try { _ = new AgentSessionEvaluationTarget(adapter.Target, () => new FixtureClient(), new() { Model = "other", MaxTurns = 1 }); throw new Exception("not rejected"); }
catch (ArgumentException) { Check(true, "model mismatch rejected without fallback"); }
try { _ = new AgentSessionEvaluationTarget(adapter.Target, () => new FixtureClient(), new() { Model = "explicit-model", MaxTurns = 0 }); throw new Exception("not rejected"); }
catch (ArgumentException) { Check(true, "unbounded default rounds rejected before creating an evaluation client"); }
var path = Path.Combine(Path.GetTempPath(), "shadow-evaluation-budget-" + Guid.NewGuid().ToString("N") + ".json");
try
{
    var budget = new EvaluationBudgetOptions { Provider = "openai", Model = "explicit-model", BudgetLedger = path, InputRate = 1, OutputRate = 1, MaxCostUsd = .3m, CacheTokensAdditionalToInput = false };
    foreach (var provider in new[] { "google", "gemini", "chatgpt", "unknown" })
    {
        using var probe = new FixtureClient();
        try { using var rejected = new BudgetChatClient(probe, budget with { Provider = provider }); throw new Exception("not rejected"); }
        catch (NotSupportedException) { Check(probe.Calls == 0 && !File.Exists(path), provider + " logical/unknown billing rejected before any underlying inference or ledger reservation"); }
    }
    foreach (var unsupported in new ChatOptions[]
    {
        new() { RawRepresentationFactory = _ => throw new Exception("The raw factory must never execute.") },
        new() { AdditionalProperties = new() { ["model"] = "unbudgeted-model" } },
        new() { ConversationId = "unobserved-context" },
        new() { AllowBackgroundResponses = true },
    })
    {
        unsupported.MaxOutputTokens = 2048;
        using var probe = new FixtureClient(); using var client = new BudgetChatClient(probe, budget);
        try { await client.GetResponseAsync([new(ChatRole.User, "fixture")], unsupported); throw new Exception("not rejected"); }
        catch (InvalidOperationException) { Check(probe.Calls == 0 && !File.Exists(path), "unbounded provider overrides fail before dispatch or reservation"); }
    }
    foreach (var oversized in new ChatOptions[]
    {
        new() { Instructions = new string('x', 350000) },
        new() { ResponseFormat = new ChatResponseFormatJson(JsonSerializer.SerializeToElement(new { type = "object", description = new string('x', 350000) })) },
    })
    {
        oversized.MaxOutputTokens = 2048;
        using var probe = new FixtureClient(); using var client = new BudgetChatClient(probe, budget);
        try { await client.GetResponseAsync([new(ChatRole.User, "fixture")], oversized); throw new Exception("not rejected"); }
        catch (InvalidOperationException error) when (error.Message.StartsWith("Budget prevents", StringComparison.Ordinal))
        { Check(probe.Calls == 0, "request instructions and response schemas are included in the reservation"); }
    }
    var initialLedger = JsonSerializer.Deserialize<BudgetLedger>(File.ReadAllText(path))!;
    Check(initialLedger.ChargedUsd == 0 && initialLedger.Requests.Count == 0,
        "a rejected first request preserves a valid zero-spend ledger for later bounded requests");
    using (var probe = new FixtureClient())
    using (var client = new BudgetChatClient(probe, budget))
    {
        var supplied = new ChatOptions { MaxOutputTokens = 2048 };
        await client.GetResponseAsync([new(ChatRole.User, "fixture")], supplied);
        Check(client.ObservedCost.EstimatedUsd == .000027m && client.ObservedCost.ReservedUsd == 0, "known inclusive cache accounting reconciles conservative estimate");
        Check(probe.ReceivedModel == budget.Model && supplied.ModelId is null, "budget pins the priced model without mutating caller options");
    }
    using (var client = new BudgetChatClient(new FixtureClient(), budget with { CacheTokensAdditionalToInput = true }))
    {
        await client.GetResponseAsync([new(ChatRole.User, "fixture")], new() { MaxOutputTokens = 2048 });
        Check(client.ObservedCost.EstimatedUsd == .000031m, "explicit additive cache accounting includes read tokens");
    }
    using (var client = new BudgetChatClient(new FixtureClient(), budget with { CacheTokensAdditionalToInput = null }))
    {
        await client.GetResponseAsync([new(ChatRole.User, "fixture")], new() { MaxOutputTokens = 2048 });
        Check(client.ObservedCost.EstimatedUsd is null && client.ObservedCost.ReservedUsd == .130048m, "unknown accounting keeps full durable reservation");
    }
    using (var client = new BudgetChatClient(new FixtureClient { Usage = new() { InputTokenCount = 20, OutputTokenCount = 7, CachedInputTokenCount = -1000 } },
        budget with { CacheTokensAdditionalToInput = true }))
    {
        await client.GetResponseAsync([new(ChatRole.User, "fixture")], new() { MaxOutputTokens = 2048 });
        Check(client.ObservedCost.EstimatedUsd is null && client.ObservedCost.ReservedUsd == .130048m,
            "invalid negative cache usage cannot refund the durable reservation");
    }
    using (var fixtureClient = new FixtureClient())
    using (var client = new BudgetChatClient(fixtureClient, budget with { OutputRate = 1000 }))
    {
        try { await client.GetResponseAsync([new(ChatRole.User, "fixture")], new() { MaxOutputTokens = 2048 }); throw new Exception("not denied"); }
        catch (InvalidOperationException) { Check(fixtureClient.Calls == 0, "budget ceiling prevents underlying dispatch"); }
    }
    var ledger = JsonSerializer.Deserialize<BudgetLedger>(File.ReadAllText(path))!;
    ledger.ChargedUsd = 0; File.WriteAllText(path, JsonSerializer.Serialize(ledger));
    using (var fixtureClient = new FixtureClient())
    using (var client = new BudgetChatClient(fixtureClient, budget))
    {
        try { await client.GetResponseAsync([new(ChatRole.User, "fixture")], new() { MaxOutputTokens = 2048 }); throw new Exception("not denied"); }
        catch (InvalidOperationException) { Check(fixtureClient.Calls == 0, "inconsistent ledger fails closed before dispatch"); }
    }
}
finally { File.Delete(path); }
var overrunPath = path + ".overrun";
try
{
    var settings = new EvaluationBudgetOptions { Provider = "openai", Model = "explicit-model", BudgetLedger = overrunPath,
        InputRate = 1, OutputRate = 1, MaxCostUsd = .3m, CacheTokensAdditionalToInput = false };
    using (var client = new BudgetChatClient(new FixtureClient { Usage = new() { InputTokenCount = 140000, OutputTokenCount = 7 } }, settings))
    {
        try { await client.GetResponseAsync([new(ChatRole.User, "fixture")], new() { MaxOutputTokens = 2048 }); throw new Exception("not rejected"); }
        catch (InvalidOperationException error) when (error.Message.StartsWith("Provider usage exceeded", StringComparison.Ordinal))
        { Check(true, "usage above the declared bound is recorded as an accounting failure"); }
    }
    using var probe = new FixtureClient(); using var next = new BudgetChatClient(probe, settings);
    try { await next.GetResponseAsync([new(ChatRole.User, "fixture")], new() { MaxOutputTokens = 2048 }); throw new Exception("not rejected"); }
    catch (InvalidOperationException)
    { Check(probe.Calls == 0, "a prior reservation overrun blocks later requests even while nominal budget remains"); }
}
finally { File.Delete(overrunPath); }
if (args is ["--write-fixture-report", var reportPath])
    File.WriteAllText(reportPath, JsonSerializer.Serialize(new { schemaVersion = 1, synthetic = true, results = new[] { first, second, noMutation, failure, observed } }, McpJson.Options));
else if (args.Length > 0) throw new ArgumentException("Use --write-fixture-report PATH only for an explicitly labeled local scripted report.");
Console.WriteLine($"PASS {checks} credential-free evaluation checks");
void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); checks++; }

sealed class ScriptedTarget : IAgentEvaluationTarget
{
    public bool Skip { get; init; } public bool Throw { get; init; }
    public EvaluationTarget Target => new("scripted", "fixture", "local", "explicit-fixture", "not-applicable");
    public async Task<EvaluationExecution> RunAsync(EvaluationContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Throw) throw new IOException("synthetic-secret");
        if (!Skip)
        {
            var snapshot = context.Editor.Snapshot;
            var edits = snapshot.Objects.Values.Where(o => o.TypeName == "netDxf.Entities.Circle")
                .Where(o => snapshot.Read(o.Id)["Layer"]["Name"].Text == "Target")
                .SelectMany(o => new object[] { new { id = o.Id.Value, path = "Radius", value = 3 }, new { id = o.Id.Value, path = "Center/Y", value = 15 } }).ToArray();
            var result = await context.Tools.ExecuteAsync("shadow_edit", McpJson.Element(new { revision = snapshot.Revision, edits }), cancellationToken);
            if (result.IsError) throw new InvalidOperationException(result.Text);
        }
        return new("completed");
    }
}
sealed class FixtureClient : IChatClient
{
    public int Calls;
    public string? ReceivedModel;
    public UsageDetails Usage { get; init; } = new() { InputTokenCount = 20, OutputTokenCount = 7, CachedInputTokenCount = 4 };
    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Calls++; ReceivedModel = options?.ModelId; await Task.Yield(); cancellationToken.ThrowIfCancellationRequested();
        var content = new List<AIContent> { new TextContent("Fixture only"), new UsageContent(Usage) };
        var finish = options?.Tools?.OfType<AIFunctionDeclaration>().Any(t => t.Name == "agent_finish") == true;
        if (finish) content.Add(new FunctionCallContent("fixture-finish", "agent_finish", new Dictionary<string, object?> { ["outcome"] = "completed", ["summary"] = "Fixture only", ["verification"] = new[] { "Synthetic fixture" } }));
        yield return new(ChatRole.Assistant, content) { FinishReason = finish ? ChatFinishReason.ToolCalls : ChatFinishReason.Stop };
    }
    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) => GetStreamingResponseAsync(messages, options, cancellationToken).ToChatResponseAsync(cancellationToken);
    public object? GetService(Type type, object? key = null) => null;
    public void Dispose() { }
}
