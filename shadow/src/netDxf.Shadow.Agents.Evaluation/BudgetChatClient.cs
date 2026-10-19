using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace netDxf.Shadow.Agents.Evaluation;

/// <summary>A durable fail-closed reservation for the single-request OpenAI/Anthropic API adapters. Rates are caller-verified upper bounds, not a live pricing catalog.</summary>
/// <remarks>The injected client must have SDK retries and hidden continuations disabled, as ShadowAgentProviders does for these two APIs.
/// Google, subscription and unknown providers are rejected: reserving one request cannot bound their logical continuation costs.</remarks>
public sealed class BudgetChatClient : DelegatingChatClient
{
    private readonly EvaluationBudgetOptions settings;
    private decimal estimated, retained;
    private bool unknown;
    public BudgetChatClient(IChatClient inner, EvaluationBudgetOptions settings) : base(inner)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (string.IsNullOrWhiteSpace(settings.Provider) || string.IsNullOrWhiteSpace(settings.Model) || string.IsNullOrWhiteSpace(settings.BudgetLedger)
            || settings.InputRate is not > 0 or > 1000 || settings.OutputRate is not > 0 or > 1000 || settings.MaxCostUsd is <= 0 or > 20
            || settings.ContextTokens is < 4096 or > 4000000 || settings.MaxOutputTokens is < 256 or > 8192)
            throw new ArgumentException("Explicit provider, model, ledger, conservative positive rates and bounded tokens/cost are required.");
        if (settings.Provider is not ("openai" or "anthropic"))
            throw new NotSupportedException("The budget guard supports the single-request OpenAI and Anthropic API adapters only. Google logical continuations and subscription/unknown providers require a per-request budget transport and are rejected before dispatch.");
        this.settings = settings;
    }
    public EvaluationCost ObservedCost => new("caller-verified conservative token estimate; not an invoice", unknown ? null : estimated, retained, settings.RateSource);
    public override Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        CancellationToken cancellationToken = default) => GetStreamingResponseAsync(messages, options, cancellationToken).ToChatResponseAsync(cancellationToken);

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var input = messages.ToArray();
        if (options?.MaxOutputTokens is not > 0 || options.MaxOutputTokens > settings.MaxOutputTokens)
            throw new InvalidOperationException("A bounded output token limit is required before billing a request.");
        var maximumOutputTokens = options.MaxOutputTokens.Value;
        if (options.ModelId is { Length: > 0 } model && model != settings.Model)
            throw new InvalidOperationException("The evaluation model cannot change during a run.");
        if (options.RawRepresentationFactory is not null || options.AdditionalProperties is { Count: > 0 }
            || !string.IsNullOrEmpty(options.ConversationId) || options.ContinuationToken is not null || options.AllowBackgroundResponses == true)
            throw new InvalidOperationException("The budget guard cannot bound provider-specific overrides, hidden conversation context or background continuations.");
        // A missing ModelId must not fall back to an injected client's differently priced default.
        options = options.Clone(); options.ModelId = settings.Model;
        // Reserve at least the full configured context. Counting UTF-8 bytes as tokens,
        // with generous per-item framing, also covers underestimated tokenizer usage.
        // These text-only cases expose no billable hosted tools or image/audio inputs.
        var inputUpperBound = Math.Max(settings.ContextTokens, InputBytes(input, options));
        var reserve = Cost(inputUpperBound, maximumOutputTokens);
        var path = Path.GetFullPath(settings.BudgetLedger!);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var existed = File.Exists(path);
        using var ledgerFile = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        BudgetLedger ledger;
        if (ledgerFile.Length == 0)
        {
            if (existed) throw new InvalidOperationException("The existing budget ledger is empty or damaged; refusing to reset spent usage.");
            ledger = new() { MaximumUsd = settings.MaxCostUsd };
        }
        else if (ledgerFile.Length > 16_777_216) throw new InvalidOperationException("Budget ledger exceeds its inspection limit.");
        else ledger = JsonSerializer.Deserialize<BudgetLedger>(ledgerFile) ?? throw new InvalidOperationException("Invalid budget ledger.");
        if (ledger.SchemaVersion != 1 || ledger.MaximumUsd != settings.MaxCostUsd || ledger.ChargedUsd < 0 || ledger.Requests is null
            || ledger.Requests.Any(r => r.ChargedUsd < 0 || r.ReservedUsd < 0 || r.ChargedUsd > r.ReservedUsd
                || r.Status == "usage_exceeded_reservation" || r.InputRate <= 0 || r.OutputRate <= 0)
            || ledger.Requests.Sum(r => r.ChargedUsd) != ledger.ChargedUsd)
            throw new InvalidOperationException("Budget ledger schema or ceiling differs. Preserve and inspect the ledger before continuing.");
        // A rejected first request must leave a valid zero-spend ledger, rather than an
        // empty file that the next request must treat as damaged accounting history.
        if (!existed && ledgerFile.Length == 0) Save();
        if (ledger.ChargedUsd + reserve > ledger.MaximumUsd)
            throw new InvalidOperationException($"Budget prevents this inference: reserved upper bound ${reserve:F4}, remaining ${Math.Max(0, ledger.MaximumUsd - ledger.ChargedUsd):F4}. No API request was sent.");
        var request = new BudgetRequest
        {
            Provider = settings.Provider, Model = settings.Model, StartedAt = DateTimeOffset.UtcNow,
            ReservedUsd = reserve, ChargedUsd = reserve, InputRate = settings.InputRate!.Value, OutputRate = settings.OutputRate!.Value,
            InputTokenUpperBound = inputUpperBound, OutputTokenUpperBound = maximumOutputTokens,
        };
        ledger.Requests.Add(request); ledger.ChargedUsd += reserve;
        retained += reserve; unknown = true;
        Save(); // Reserve durably before network dispatch; failures retain the full reserve.
        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in InnerClient.GetStreamingResponseAsync(input, options, cancellationToken).ConfigureAwait(false))
        {
            updates.Add(update);
            yield return update;
        }
        var usage = updates.ToChatResponse().Usage;
        long writes = 0;
        if (usage?.AdditionalCounts is { } counts)
            foreach (var key in new[] { "cache_creation_input_tokens", "CacheCreationInputTokens", "InputTokenDetails.CacheWriteTokenCount", "input_tokens_details.cache_write_tokens" })
                if (counts.TryGetValue(key, out var value)) { writes = Math.Max(0, value); break; }
        if (settings.CacheTokensAdditionalToInput is not null && usage?.InputTokenCount is >= 0 && usage.OutputTokenCount is >= 0
            && (usage.InputTokenCount > 0 || usage.OutputTokenCount > 0)
            && usage.CachedInputTokenCount is not < 0
            && !(usage.AdditionalCounts?.Values.Any(value => value < 0) ?? false))
        {
            var inputTokens = checked(usage.InputTokenCount.Value + (settings.CacheTokensAdditionalToInput == true ? (usage.CachedInputTokenCount ?? 0) + writes : 0));
            var charge = Cost(inputTokens, usage.OutputTokenCount.Value);
            request.InputTokens = usage.InputTokenCount; request.OutputTokens = usage.OutputTokenCount;
            request.ChargedUsd = charge; request.Status = charge > reserve ? "usage_exceeded_reservation" : "usage_reported";
            retained -= reserve; estimated += charge; unknown = retained > 0;
            ledger.ChargedUsd += charge - reserve;
            Save();
            if (charge > reserve) throw new InvalidOperationException("Provider usage exceeded the conservative reservation; stop and audit token rates and bounds before another run.");
        }
        else { request.Status = "completed_without_usage_reserved_in_full"; Save(); }

        void Save()
        {
            ledgerFile.Position = 0;
            JsonSerializer.Serialize(ledgerFile, ledger, new JsonSerializerOptions { WriteIndented = true });
            ledgerFile.SetLength(ledgerFile.Position); ledgerFile.Flush(flushToDisk: true);
        }
    }

    private decimal Cost(long input, long output) =>
        (input * settings.InputRate!.Value + output * settings.OutputRate!.Value) / 1_000_000m;

    private static long InputBytes(IEnumerable<ChatMessage> messages, ChatOptions options)
    {
        long bytes = 16384 + Encoding.UTF8.GetByteCount(options.Instructions ?? "");
        if (options.ResponseFormat is ChatResponseFormatJson json)
            bytes += Encoding.UTF8.GetByteCount(json.Schema?.GetRawText() ?? "")
                + Encoding.UTF8.GetByteCount(json.SchemaName ?? "") + Encoding.UTF8.GetByteCount(json.SchemaDescription ?? "");
        foreach (var message in messages)
        {
            bytes += 1024;
            foreach (var content in message.Contents)
                bytes += 1024 + content switch
                {
                    TextContent text => Encoding.UTF8.GetByteCount(text.Text),
                    TextReasoningContent reasoning => Encoding.UTF8.GetByteCount(reasoning.Text ?? "") + Encoding.UTF8.GetByteCount(reasoning.ProtectedData ?? ""),
                    FunctionCallContent call => Encoding.UTF8.GetByteCount(call.Name) + Encoding.UTF8.GetByteCount(call.CallId) + JsonSerializer.SerializeToUtf8Bytes(call.Arguments).Length,
                    FunctionResultContent result => Encoding.UTF8.GetByteCount(result.CallId) + JsonSerializer.SerializeToUtf8Bytes(result.Result).Length,
                    _ => throw new InvalidOperationException("The evaluator budget supports text, reasoning and local function calls only."),
                };
        }
        foreach (var tool in options.Tools ?? [])
        {
            if (tool is not AIFunctionDeclaration function) throw new InvalidOperationException("Hosted tools have no declared cost in this evaluator.");
            bytes += 1024 + Encoding.UTF8.GetByteCount(function.Name) + Encoding.UTF8.GetByteCount(function.Description ?? "") + Encoding.UTF8.GetByteCount(function.JsonSchema.GetRawText());
        }
        return bytes;
    }
}

public sealed class BudgetLedger
{
    public int SchemaVersion { get; set; } = 1;
    public decimal MaximumUsd { get; set; }
    public decimal ChargedUsd { get; set; }
    public List<BudgetRequest> Requests { get; set; } = [];
}
public sealed class BudgetRequest
{
    public string Provider { get; set; } = "";
    public string Model { get; set; } = "";
    public DateTimeOffset StartedAt { get; set; }
    public decimal InputRate { get; set; }
    public decimal OutputRate { get; set; }
    public long InputTokenUpperBound { get; set; }
    public long OutputTokenUpperBound { get; set; }
    public long? InputTokens { get; set; }
    public long? OutputTokens { get; set; }
    public decimal ReservedUsd { get; set; }
    public decimal ChargedUsd { get; set; }
    public string Status { get; set; } = "reserved_or_interrupted";
}

/// <summary>Text-only budget assumptions. Unknown cache-token accounting keeps the full reservation; no price lookup or credentials occur here.</summary>
public sealed record EvaluationBudgetOptions
{
    public string Provider { get; init; } = "";
    public string Model { get; init; } = "";
    public string BudgetLedger { get; init; } = "";
    public decimal? InputRate { get; init; }
    public decimal? OutputRate { get; init; }
    public decimal MaxCostUsd { get; init; } = 20;
    public int ContextTokens { get; init; } = 128000;
    public int MaxOutputTokens { get; init; } = 2048;
    /// <summary>False means input includes cache tokens, true means cache counts must be added, null retains the full reservation.</summary>
    public bool? CacheTokensAdditionalToInput { get; init; }
    public string? RateSource { get; init; }
}
