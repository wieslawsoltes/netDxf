#pragma warning disable OPENAI001, MEAI001 // Pinned official Responses API and its Microsoft adapter.
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Google.GenAI.Types;
using Microsoft.Extensions.AI;
using OpenAI.Responses;

namespace netDxf.Shadow.Agents;

public static partial class ShadowAgentProviders
{
    internal const string StopReasonKey = "shadow:stop-reason";
    internal const string InvalidToolBatchKey = "shadow:invalid-tool-batch";
    internal const string InitialInputTokensKey = "shadow:initial-input-tokens";
    internal const string ContinuationHopsKey = "shadow:continuation-hops";
    internal const int MaximumGoogleContinuationHops = 32;

    private sealed partial class ProviderChatClient
    {
        public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            var input = CompatibleHistory(messages).ToArray();
            var configured = options?.Clone() ?? new ChatOptions();
            var originalFactory = configured.RawRepresentationFactory;
            var google = provider is "google" or "gemini";
            GenerateContentConfig? googleTemplate = null;
            byte[]? continuationToken = null;
            var seenTokens = new HashSet<string>(StringComparer.Ordinal);
            if (provider is "openai" or "chatgpt")
            {
                configured.ConversationId = null;
                configured.RawRepresentationFactory = client =>
                {
                    var raw = originalFactory?.Invoke(client) as CreateResponseOptions ?? new CreateResponseOptions();
                    raw.StoredOutputEnabled = false;
                    raw.PreviousResponseId = null;
                    return raw;
                };
            }
            else if (google)
            {
                configured.RawRepresentationFactory = client =>
                {
                    if (googleTemplate is null)
                    {
                        // Resolve a caller's factory once: every continuation must have the
                        // same contents, system instruction, tools and generation settings.
                        googleTemplate = (originalFactory?.Invoke(client) as GenerateContentConfig ?? new())
                            with { AutomaticContinuation = false };
                        if (googleTemplate.ContinuationToken is { Length: > 0 } initialToken)
                            seenTokens.Add(TokenFingerprint(initialToken));
                    }
                    // Keep the caller's config intact. The official SDK builds and parses each
                    // request; this adapter owns only the bounded logical continuation loop.
                    return googleTemplate with
                    {
                        AutomaticContinuation = false,
                        ContinuationToken = continuationToken ?? googleTemplate.ContinuationToken,
                        // M.E.AI appends messages/options to these collections and fills in
                        // thinking defaults. A record's shallow copy would otherwise repeat
                        // instructions/tools on later hops and mutate the caller's config.
                        SystemInstruction = googleTemplate.SystemInstruction is { } system
                            ? system with { Parts = system.Parts?.ToList() } : null,
                        StopSequences = googleTemplate.StopSequences?.ToList(),
                        Tools = googleTemplate.Tools?.ToList(),
                        ThinkingConfig = googleTemplate.ThinkingConfig is { } thinking ? thinking with { } : null,
                    };
                };
            }

            string? logicalGoogleMessageId = null;
            long? initialInputTokens = null;
            string? stopReason = null;
            var invalidToolBatch = false;
            for (var hop = 1; ; hop++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var completed = false;
                FinishReason? googleFinish = null;
                byte[]? nextToken = null;
                await foreach (var update in InnerClient.GetStreamingResponseAsync(input, configured, cancellationToken).ConfigureAwait(false))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    switch (update.RawRepresentation)
                    {
                        case StreamingResponseFailedUpdate failed:
                            throw new ShadowAgentProviderException(failed.Response.Error?.Code.ToString() ?? "response_failed",
                                failed.Response.Error?.Message ?? "The provider failed the response.");
                        case StreamingResponseIncompleteUpdate incomplete:
                            // M.E.AI does not currently translate this terminal event's reason
                            // or usage. A model output limit is resumable, even if all of the
                            // budget was spent on reasoning and no visible content arrived.
                            var reason = incomplete.Response.IncompleteStatusDetails?.Reason;
                            if (reason == ResponseIncompleteStatusReason.MaxOutputTokens)
                            {
                                update.FinishReason = ChatFinishReason.Length;
                                stopReason = "max_output_tokens";
                            }
                            else if (reason == ResponseIncompleteStatusReason.ContentFilter)
                            {
                                update.FinishReason = ChatFinishReason.ContentFilter;
                                stopReason = "content_filter";
                            }
                            else
                                throw new ShadowAgentProviderException("response_incomplete", "The provider returned an incomplete response without a supported continuation reason.");
                            if (!update.Contents.OfType<UsageContent>().Any() && incomplete.Response.Usage is { } usage)
                                update.Contents.Add(new UsageContent(new UsageDetails
                                {
                                    InputTokenCount = usage.InputTokenCount,
                                    OutputTokenCount = usage.OutputTokenCount,
                                    TotalTokenCount = usage.TotalTokenCount,
                                    CachedInputTokenCount = usage.InputTokenDetails?.CachedTokenCount,
                                    ReasoningTokenCount = usage.OutputTokenDetails?.ReasoningTokenCount,
                                }));
                            completed = true;
                            break;
                        case StreamingResponseCompletedUpdate complete:
                            completed = true;
                            stopReason = complete.Response.IncompleteStatusDetails?.Reason?.ToString() ?? "completed";
                            break;
                        case Anthropic.Models.Messages.RawMessageStreamEvent { Value: Anthropic.Models.Messages.RawMessageDeltaEvent delta }:
                            stopReason = delta.Delta.StopReason?.Raw() ?? stopReason;
                            break;
                        case Anthropic.Models.Messages.RawMessageStreamEvent { Value: Anthropic.Models.Messages.RawMessageStopEvent }:
                            // message_delta precedes message_stop; losing the connection
                            // between them must not commit a partial assistant/tool batch.
                            completed = stopReason is not null;
                            break;
                        case GenerateContentResponse generated when google:
                            if (hop == 1 && generated.UsageMetadata?.PromptTokenCount is { } firstPromptTokens)
                                initialInputTokens = firstPromptTokens;
                            if (!string.IsNullOrEmpty(update.MessageId) || update.Contents.Count > 0)
                                update.MessageId = logicalGoogleMessageId ??= update.MessageId ?? "google_" + Guid.NewGuid().ToString("N");
                            // The SDK maps only candidates that include content. Terminal
                            // MAX_TOKENS and safety chunks may legitimately have no content.
                            update.FinishReason = null;
                            if (generated.Candidates is { Count: > 0 })
                            {
                                var candidate = generated.Candidates[0];
                                if (candidate.ContinuationToken is { Length: > 0 } token) nextToken = token;
                                if (candidate.FinishReason is { } finish && finish != FinishReason.FinishReasonUnspecified)
                                {
                                    googleFinish = finish;
                                    stopReason = finish.ToString();
                                    completed = true;
                                    update.FinishReason = GoogleFinishReason(finish);
                                }
                            }
                            break;
                    }
                    foreach (var error in update.Contents.OfType<ErrorContent>())
                        throw new ShadowAgentProviderException(error.ErrorCode ?? "provider_error", error.Message ?? "The provider reported a streaming error.");
                    for (var i = 0; i < update.Contents.Count; i++)
                    {
                        if (update.Contents[i] is TextReasoningContent reasoning)
                            (reasoning.AdditionalProperties ??= new())[ReasoningProviderKey] = reasoningProvider;
                        if (update.Contents[i] is FunctionCallContent { Exception: not null }) invalidToolBatch = true;
                        // Gemini can omit IDs. Preserve the SDK's tool errors and protected
                        // reasoning while assigning a stable ID for checkpoints and results.
                        if (update.Contents[i] is FunctionCallContent call && string.IsNullOrWhiteSpace(call.CallId))
                            update.Contents[i] = new FunctionCallContent("call_" + Guid.NewGuid().ToString("N"), call.Name, call.Arguments)
                            { AdditionalProperties = call.AdditionalProperties, Exception = call.Exception, InformationalOnly = call.InformationalOnly };
                    }
                    yield return update;
                }
                cancellationToken.ThrowIfCancellationRequested();
                if (!completed)
                    throw new ShadowAgentProviderException("stream_interrupted", "The provider stream ended without a terminal completion event.", isTransient: true);
                if (googleFinish == FinishReason.Continuation)
                {
                    if (nextToken is not { Length: > 0 })
                        throw new ShadowAgentProviderException("continuation_missing_token", "Google requested continuation without a continuation token.");
                    if (!seenTokens.Add(TokenFingerprint(nextToken)))
                        throw new ShadowAgentProviderException("continuation_stalled", "Google repeated a continuation token without making progress.");
                    if (hop >= MaximumGoogleContinuationHops)
                        throw new ShadowAgentProviderException("continuation_limit", $"Google exceeded the limit of {MaximumGoogleContinuationHops} requests for one response.");
                    continuationToken = nextToken.ToArray();
                    continue;
                }

                // A null MessageId places these properties on ChatResponse, not the last
                // assistant message, when M.E.AI aggregates native or NDJSON bridge updates.
                var properties = new AdditionalPropertiesDictionary { [StopReasonKey] = stopReason };
                // FunctionCallContent.Exception is intentionally excluded from JSON. Keep a
                // portable batch-level marker so the companion bridge cannot turn malformed
                // arguments into a seemingly valid empty argument object on deserialization.
                if (invalidToolBatch) properties[InvalidToolBatchKey] = true;
                if (google)
                {
                    // Per-hop usage is cumulative and the official adapter emits its final
                    // delta once. Summing those deltas measures billing; the first prompt
                    // count, not that sum, measures the input's context-window occupancy.
                    if (initialInputTokens is { } tokens) properties[InitialInputTokensKey] = tokens;
                    properties[ContinuationHopsKey] = hop;
                }
                yield return new ChatResponseUpdate { AdditionalProperties = properties };
                yield break;
            }
        }

        private static string TokenFingerprint(byte[] token) => Convert.ToHexString(SHA256.HashData(token));

        private static ChatFinishReason? GoogleFinishReason(FinishReason finish)
        {
            if (finish == FinishReason.Continuation) return null;
            if (finish == FinishReason.Stop) return ChatFinishReason.Stop;
            if (finish == FinishReason.MaxTokens) return ChatFinishReason.Length;
            if (finish == FinishReason.MalformedFunctionCall || finish == FinishReason.UnexpectedToolCall || finish == FinishReason.TooManyToolCalls)
                return ChatFinishReason.ToolCalls;
            if (finish == FinishReason.Safety || finish == FinishReason.Recitation || finish == FinishReason.Blocklist
                || finish == FinishReason.ProhibitedContent || finish == FinishReason.Spii || finish == FinishReason.ImageSafety
                || finish == FinishReason.ImageProhibitedContent || finish == FinishReason.ImageRecitation)
                return ChatFinishReason.ContentFilter;
            throw new ShadowAgentProviderException("google_finish_" + finish.ToString().ToLowerInvariant(),
                $"Google stopped generation with the unsupported finish reason '{finish}'.");
        }
    }
}
