using Microsoft.Extensions.AI;

namespace netDxf.Shadow.Agents;

public sealed partial class AgentSession
{
    private AgentUsage usageTotals = new();
    private void RecordUsage(UsageDetails? usage, double milliseconds, double? firstTokenMilliseconds, string purpose)
    {
        long? Sum(long? previous, long? value) => value is null ? previous : (previous ?? 0) + Math.Max(0, value.Value);
        long? cacheWrites = null;
        if (usage?.AdditionalCounts is { } counts)
            foreach (var key in new[] { "cache_creation_input_tokens", "CacheCreationInputTokens", "InputTokenDetails.CacheWriteTokenCount", "input_tokens_details.cache_write_tokens" })
                if (counts.TryGetValue(key, out var value)) { cacheWrites = value; break; }
        AgentUsage totals;
        lock (gate)
        {
            totals = usageTotals = new(usageTotals.Inferences + 1,
                Sum(usageTotals.InputTokens, usage?.InputTokenCount), Sum(usageTotals.OutputTokens, usage?.OutputTokenCount),
                Sum(usageTotals.TotalTokens, usage?.TotalTokenCount), Sum(usageTotals.CachedTokens, usage?.CachedInputTokenCount),
                Sum(usageTotals.CacheWriteTokens, cacheWrites), Sum(usageTotals.ReasoningTokens, usage?.ReasoningTokenCount),
                usageTotals.InferenceMilliseconds + milliseconds, firstTokenMilliseconds);
        }
        Emit("usage", "Provider usage and latency", new
        {
            purpose, inputTokens = usage?.InputTokenCount, outputTokens = usage?.OutputTokenCount, totalTokens = usage?.TotalTokenCount,
            cachedTokens = usage?.CachedInputTokenCount, cacheWriteTokens = cacheWrites, reasoningTokens = usage?.ReasoningTokenCount,
            milliseconds, firstTokenMilliseconds, totals,
        });
    }
}
