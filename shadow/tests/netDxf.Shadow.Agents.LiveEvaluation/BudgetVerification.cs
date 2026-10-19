using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.AI;
using netDxf.Shadow.Agents.Evaluation;

internal static class BudgetVerification
{
    public static async Task RunAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), "shadow-agent-budget-" + Guid.NewGuid().ToString("N") + ".json");
        var settings = new EvaluationBudgetOptions { CacheTokensAdditionalToInput = false, Provider = "openai", Model = "fixture-model", InputRate = 1, OutputRate = 1, MaxCostUsd = .2m, BudgetLedger = path };
        try
        {
            foreach (var provider in new[] { "openai", "anthropic" })
            {
                using var fixture = new FixtureClient();
                using var budget = new BudgetChatClient(fixture, settings with { Provider = provider });
                await budget.GetResponseAsync([new(ChatRole.User, "fixture")], new() { MaxOutputTokens = 2048 });
                if (fixture.Calls != 1) throw new InvalidOperationException("Budget wrapper did not dispatch exactly one request.");
            }
            var ledger = JsonSerializer.Deserialize<BudgetLedger>(File.ReadAllText(path))!;
            if (ledger.ChargedUsd != .00003m || ledger.Requests.Count != 2 || ledger.Requests.Any(request => request.Status != "usage_reported"))
                throw new InvalidOperationException("Shared provider ledger did not reconcile reported input and output usage.");
            using (var fixture = new FixtureClient())
            using (var budget = new BudgetChatClient(fixture, settings with { OutputRate = 1000 }))
            {
                try
                {
                    await budget.GetResponseAsync([new(ChatRole.User, "fixture")], new() { MaxOutputTokens = 2048 });
                    throw new InvalidOperationException("Expected budget denial.");
                }
                catch (InvalidOperationException error) when (error.Message.StartsWith("Budget prevents", StringComparison.Ordinal)) { }
                if (fixture.Calls != 0) throw new InvalidOperationException("Budget denial dispatched a network request.");
            }
            using (var fixture = new FixtureClient { Fail = true })
            using (var budget = new BudgetChatClient(fixture, settings))
            {
                try { await budget.GetResponseAsync([new(ChatRole.User, "fixture")], new() { MaxOutputTokens = 2048 }); }
                catch (IOException) { }
            }
            ledger = JsonSerializer.Deserialize<BudgetLedger>(File.ReadAllText(path))!;
            if (ledger.Requests.Count != 3 || ledger.Requests[^1].Status != "reserved_or_interrupted" || ledger.ChargedUsd != .130078m)
                throw new InvalidOperationException("Interrupted requests must retain their entire durable cost reservation.");
        }
        finally { File.Delete(path); }
    }

    private sealed class FixtureClient : IChatClient
    {
        public int Calls { get; private set; }
        public bool Fail { get; init; }
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Calls++; await Task.Yield(); cancellationToken.ThrowIfCancellationRequested();
            if (Fail) throw new IOException("Fixture interruption.");
            yield return new(ChatRole.Assistant, [new TextContent("done"), new UsageContent(new() { InputTokenCount = 10, OutputTokenCount = 5 })])
                { FinishReason = ChatFinishReason.Stop };
        }
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            GetStreamingResponseAsync(messages, options, cancellationToken).ToChatResponseAsync(cancellationToken);
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
