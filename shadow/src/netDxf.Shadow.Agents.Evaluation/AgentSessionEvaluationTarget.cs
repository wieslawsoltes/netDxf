using Microsoft.Extensions.AI;
using System.Globalization;
using System.Text;
using System.Text.Json;
using netDxf.Shadow.Mcp;

namespace netDxf.Shadow.Agents.Evaluation;

/// <summary>Adapter for the shared streaming agent harness. Each run obtains and disposes a fresh injected client.</summary>
public sealed class AgentSessionEvaluationTarget : IAgentEvaluationTarget
{
    private readonly Func<IChatClient> createClient;
    private readonly AgentOptions options;
    private readonly Func<string, string>? redact;
    public AgentSessionEvaluationTarget(EvaluationTarget target, Func<IChatClient> createClient, AgentOptions options, Func<string, string>? redact = null)
    {
        ArgumentNullException.ThrowIfNull(target); ArgumentNullException.ThrowIfNull(createClient); ArgumentNullException.ThrowIfNull(options);
        if (options.MaxTurns is < 1 or > 100 || options.MaxOutputTokens is < 256 or > 8192 || options.MaxRetries is < 0 or > 4)
            throw new ArgumentException("Evaluation requires 1–100 logical rounds, 256–8192 output tokens and at most four explicit retries.");
        if (target.Model != options.Model) throw new ArgumentException("Target and harness model must match exactly.");
        var recorded = new Dictionary<string, string>(target.Settings ?? new Dictionary<string, string>(), StringComparer.Ordinal)
        {
            ["mode"] = options.Mode.ToString(), ["permissionMode"] = options.PermissionMode.ToString(),
            ["maxTurns"] = options.MaxTurns.ToString(CultureInfo.InvariantCulture), ["maxRetries"] = options.MaxRetries.ToString(CultureInfo.InvariantCulture),
            ["maxOutputTokens"] = options.MaxOutputTokens.ToString(CultureInfo.InvariantCulture), ["contextWindowTokens"] = options.ContextWindowTokens.ToString(CultureInfo.InvariantCulture),
            ["maxToolCallsPerTurn"] = options.MaxToolCallsPerTurn.ToString(CultureInfo.InvariantCulture),
            ["compactionThreshold"] = options.CompactionThreshold.ToString(CultureInfo.InvariantCulture),
            ["instructionsHash"] = EvaluationFixture.Hash(Encoding.UTF8.GetBytes(options.Instructions)),
        };
        Target = target with { Settings = recorded }; this.createClient = createClient; this.options = options; this.redact = redact;
    }
    public EvaluationTarget Target { get; }
    public async Task<EvaluationExecution> RunAsync(EvaluationContext context, CancellationToken cancellationToken = default)
    {
        var client = createClient();
        using var agent = new AgentSession(client, context.Tools, options, context.Workspace, redact);
        var events = new List<AgentEvent>(); agent.EventEmitted += entry => { lock (events) events.Add(entry); };
        await agent.RunAsync(context.Fixture.Scenario.Prompt, cancellationToken).ConfigureAwait(false);
        var usage = events.Where(e => e.Type == "usage" && e.Data is not null).Select(e => e.Data!.Value).ToArray();
        long? Sum(string name) => usage.Length > 0 && usage.All(e => e.TryGetProperty(name, out var n) && n.ValueKind == System.Text.Json.JsonValueKind.Number)
            ? usage.Sum(e => e.GetProperty(name).GetInt64()) : null;
        double? first = usage.Length > 0 && usage[0].TryGetProperty("firstTokenMilliseconds", out var f) && f.ValueKind == System.Text.Json.JsonValueKind.Number ? f.GetDouble() : null;
        var status = agent.Poll().Status;
        var retryCount = events.Count(e => e.Type == "retrying");
        var compacting = events.Count(e => e.Type == "compacting");
        var compactedUsage = usage.Count(e => e.TryGetProperty("purpose", out var purpose) && purpose.GetString() == "compaction");
        // Compaction retries are intentionally not surfaced as conversational retry events.
        // Mark their attempt count/usage completeness unknown rather than claiming no retries.
        int? reportedRetries = options.MaxRetries > 0 && compacting > 0 ? null : retryCount;
        bool? usageComplete = status != "completed" || retryCount > 0 || compacting != compactedUsage ? false :
            reportedRetries is null || Sum("inputTokens") is null || Sum("outputTokens") is null ? null : true;
        var telemetry = new EvaluationTelemetry(Sum("inputTokens"), Sum("outputTokens"), Sum("cachedTokens"), Sum("cacheWriteTokens"), Sum("reasoningTokens"),
            events.Count(e => e.Type == "turn") + usage.Count(e => e.TryGetProperty("purpose", out var purpose) && purpose.GetString() == "compaction"),
            reportedRetries, usage.Length == 0 ? null : usage.Sum(e => e.GetProperty("milliseconds").GetDouble()), first, usageComplete);
        var budget = client as BudgetChatClient;
        var catalogHash = EvaluationFixture.Hash(JsonSerializer.SerializeToUtf8Bytes(agent.ToolDefinitions.OrderBy(t => t.Name, StringComparer.Ordinal), McpJson.Options));
        return new(status, telemetry, events.ToArray(), agent.Export(), budget?.ObservedCost, catalogHash);
    }
}
