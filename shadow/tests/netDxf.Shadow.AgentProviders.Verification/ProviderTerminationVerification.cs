#pragma warning disable OPENAI001, MEAI001
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Google.GenAI.Types;
using Microsoft.Extensions.AI;
using netDxf.Shadow.Agents;

internal static class ProviderTerminationVerification
{
    public static async Task<int> RunAsync()
    {
        var checks = 0;
        foreach (var browser in new[] { false, true })
        foreach (var provider in new[] { "openai", "chatgpt", "anthropic", "google" })
        foreach (var empty in new[] { false, true })
        {
            var reason = provider switch { "anthropic" => "max_tokens", "google" => "MAX_TOKENS", _ => "max_output_tokens" };
            var payload = provider switch
            {
                "anthropic" => AnthropicStop(reason, empty),
                "google" => GoogleStop(reason, empty ? null : "done"),
                _ => OpenAIStop(reason, empty),
            };
            var requests = 0;
            using var http = new HttpClient(new TestHandler((_, _) => { requests++; return Task.FromResult(Sse(payload)); }));
            using var client = Create(provider, browser, http);
            var response = await client.GetResponseAsync([new(ChatRole.User, "Continue the coding task.")], new() { MaxOutputTokens = 256 });
            var label = $"{provider} {(browser ? "browser" : "native")} {(empty ? "reasoning-only" : "text")} output limit";
            Check(response.FinishReason == ChatFinishReason.Length, label + " is resumable Length");
            Check(response.Usage is { InputTokenCount: 11, OutputTokenCount: 4, TotalTokenCount: 15 }, label + " preserves terminal usage");
            Check(response.Text == (empty ? "" : "done") && Property(response, ShadowAgentProviders.StopReasonKey) == reason,
                label + " retains only visible text and exact response-level reason");
            Check(requests == 1, label + " never silently raises the configured output ceiling");
        }

        foreach (var provider in new[] { "openai", "chatgpt", "anthropic", "google" })
        {
            var reason = provider switch { "anthropic" => "refusal", "google" => "SAFETY", _ => "content_filter" };
            var payload = provider switch
            {
                "anthropic" => AnthropicStop(reason, empty: true),
                "google" => GoogleStop(reason),
                _ => OpenAIStop(reason, empty: true),
            };
            using var http = new HttpClient(new TestHandler((_, _) => Task.FromResult(Sse(payload))));
            using var client = Create(provider, true, http);
            var response = await client.GetResponseAsync([new(ChatRole.User, "test")]);
            Check(response.FinishReason == ChatFinishReason.ContentFilter && Property(response, ShadowAgentProviders.StopReasonKey) == reason,
                provider + " filter is a terminal non-length outcome even without content");
        }

        foreach (var provider in new[] { "openai", "chatgpt", "anthropic" })
        foreach (var length in new[] { false, true })
        {
            var payload = provider == "anthropic" ? AnthropicStop(length ? "max_tokens" : "tool_use", malformedTool: true)
                : OpenAIStop("max_output_tokens", malformedTool: true);
            if (provider != "anthropic" && !length)
            {
                var events = Events(payload);
                events[^1]["type"] = "response.completed";
                events[^1]["response"]!["status"] = "completed";
                events[^1]["response"]!.AsObject().Remove("incomplete_details");
                payload = Stream(events);
            }
            using var http = new HttpClient(new TestHandler((_, _) => Task.FromResult(Sse(payload))));
            using var client = Create(provider, true, http);
            var updates = new List<ChatResponseUpdate>();
            await foreach (var update in client.GetStreamingResponseAsync([new(ChatRole.User, "Write a file.")])) updates.Add(update);
            var response = updates.ToChatResponse();
            var calls = response.Messages.SelectMany(message => message.Contents).OfType<FunctionCallContent>().ToArray();
            Check(response.FinishReason == (length ? ChatFinishReason.Length : ChatFinishReason.ToolCalls) && calls.Length == 1 && calls[0].Exception is not null,
                provider + " preserves malformed tool arguments and the actual terminal reason");
            var bridged = updates.Select(update => JsonSerializer.Deserialize<ChatResponseUpdate>(JsonSerializer.Serialize(update))!).ToList().ToChatResponse();
            Check(Flag(response, ShadowAgentProviders.InvalidToolBatchKey) && Flag(bridged, ShadowAgentProviders.InvalidToolBatchKey),
                provider + " invalid tool batches retain a portable native-to-NDJSON marker on both completed and length-limited streams");
            Check(bridged.Messages.SelectMany(message => message.Contents).OfType<FunctionCallContent>().Single().Exception is null,
                provider + " bridge fixture confirms Exception itself is not serializable and requires the portable batch marker");
        }

        foreach (var bridge in new[] { false, true })
        {
            using var http = new HttpClient(new TestHandler((_, _) => Task.FromResult(Sse(AnthropicStop("model_context_window_exceeded", empty: true)))));
            using var client = Create("anthropic", true, http);
            var updates = new List<ChatResponseUpdate>();
            await foreach (var update in client.GetStreamingResponseAsync([new(ChatRole.User, "test")]))
                updates.Add(bridge ? JsonSerializer.Deserialize<ChatResponseUpdate>(JsonSerializer.Serialize(update))! : update);
            var response = updates.ToChatResponse();
            Check(response.FinishReason == ChatFinishReason.Length && Property(response, ShadowAgentProviders.StopReasonKey) == "model_context_window_exceeded",
                "Anthropic context-window reason survives " + (bridge ? "NDJSON" : "native") + " aggregation");
            Check(updates[^1].MessageId is null && updates[^1].Contents.Count == 0,
                "Stop metadata is scoped to the response without adding visible content");
        }

        foreach (var provider in new[] { "openai", "chatgpt" })
        foreach (var reason in new string?[] { null, "unsupported_incomplete_reason" })
        {
            using var http = new HttpClient(new TestHandler((_, _) => Task.FromResult(Sse(OpenAIStop(reason)))));
            using var client = Create(provider, true, http);
            await Failure(() => client.GetResponseAsync([new(ChatRole.User, "test")]), "response_incomplete", transient: false,
                provider + " does not pretend an unknown incomplete response is safely continuable");
        }

        foreach (var (provider, payload) in new[]
        {
            ("openai", WithoutTerminal(Streams.For("openai", false), "response.completed")),
            ("chatgpt", WithoutTerminal(Streams.For("chatgpt", false), "response.completed")),
            ("anthropic", WithoutTerminal(Streams.For("anthropic", false), "message_stop")),
            ("google", GoogleStop("FINISH_REASON_UNSPECIFIED", "partial")),
            ("google", GoogleStop(null, "partial")),
        })
        {
            using var http = new HttpClient(new TestHandler((_, _) => Task.FromResult(Sse(payload))));
            using var client = Create(provider, true, http);
            await Failure(() => client.GetResponseAsync([new(ChatRole.User, "test")]), "stream_interrupted", transient: true,
                provider + " rejects EOF without a real terminal event");
        }
        foreach (var reason in new[] { "MALFORMED_FUNCTION_CALL", "UNEXPECTED_TOOL_CALL", "TOO_MANY_TOOL_CALLS" })
        {
            using var http = new HttpClient(new TestHandler((_, _) => Task.FromResult(Sse(GoogleStop(reason)))));
            using var client = Create("google", true, http);
            var response = await client.GetResponseAsync([new(ChatRole.User, "test")]);
            Check(response.FinishReason == ChatFinishReason.ToolCalls && Property(response, ShadowAgentProviders.StopReasonKey) == reason
                && !response.Messages.SelectMany(message => message.Contents).OfType<FunctionCallContent>().Any(),
                "Google " + reason + " allows corrective inference without inventing a callable tool");
        }
        using (var http = new HttpClient(new TestHandler((_, _) => Task.FromResult(Sse(GoogleStop("OTHER"))))))
        using (var client = Create("google", true, http))
            await Failure(() => client.GetResponseAsync([new(ChatRole.User, "test")]), "google_finish_other", transient: false,
                "Google unknown terminal outcomes are explicit errors");

        foreach (var browser in new[] { false, true })
        foreach (var finalReason in new[] { "STOP", "MAX_TOKENS" })
        {
            var requests = new List<JsonObject>();
            var factoryCalls = 0;
            var raw = new GenerateContentConfig
            {
                AutomaticContinuation = true, Seed = 17, StopSequences = ["STOP_MARKER"],
                SystemInstruction = new() { Parts = [new() { Text = "Preset system instruction." }] },
                Tools = [new() { FunctionDeclarations = [new() { Name = "preset_tool", Description = "A preconfigured tool." }] }],
                ThinkingConfig = new(),
            };
            using var http = new HttpClient(new TestHandler(async (request, token) =>
            {
                requests.Add(JsonNode.Parse(await request.Content!.ReadAsStringAsync(token))!.AsObject());
                // Include an earlier cumulative usage chunk. Only the last count in each
                // hop is billable; adding all raw chunk counts would overstate usage.
                var stream = requests.Count switch
                {
                    1 => GoogleStop(null, "first ", input: 11, output: 1, responseId: "hop1")
                        + GoogleStop("CONTINUATION", token: "opaque-one", input: 11, output: 4, responseId: "hop1"),
                    2 => GoogleStop("CONTINUATION", "second ", "opaque-two", input: 18, output: 5, responseId: "hop2"),
                    _ => GoogleStop(finalReason, "third", input: 29, output: 6, responseId: "hop3"),
                };
                return Sse(stream);
            }));
            using var client = Create("google", browser, http);
            var options = new ChatOptions
            {
                MaxOutputTokens = 200000, Temperature = 0.25f, StopSequences = ["USER_STOP"],
                Instructions = "Additional caller instructions.", Reasoning = new() { Effort = ReasoningEffort.Low },
                Tools = [AIFunctionFactory.Create(() => "drawing", name: "document_get")],
                RawRepresentationFactory = _ => { factoryCalls++; return raw; },
            };
            var updates = new List<ChatResponseUpdate>();
            await foreach (var update in client.GetStreamingResponseAsync(
                [new(ChatRole.System, "Keep the exact tools and config."), new(ChatRole.User, "Write a long program.")], options))
                updates.Add(update);
            var response = updates.ToChatResponse();
            Check(requests.Count == 3 && factoryCalls == 1, "Google continuation is bounded within one logical inference");
            Check(response.FinishReason == (finalReason == "STOP" ? ChatFinishReason.Stop : ChatFinishReason.Length)
                && Property(response, ShadowAgentProviders.StopReasonKey) == finalReason
                && updates.Count(update => update.FinishReason is not null) == 1,
                "Google suppresses intermediate CONTINUATION and exposes only the real terminal reason");
            Check(response.Text == "first second third" && response.Messages.Count == 1,
                "Google joins different per-hop response IDs into one coherent assistant turn");
            Check(response.Usage is { InputTokenCount: 58, OutputTokenCount: 15, TotalTokenCount: 73 }
                && Property(response, ShadowAgentProviders.InitialInputTokensKey) == "11"
                && Property(response, ShadowAgentProviders.ContinuationHopsKey) == "3",
                "Google sums billable per-hop usage while preserving the first input count for context calibration");
            Check(requests[1].ToJsonString().Contains(Convert.ToBase64String(Encoding.UTF8.GetBytes("opaque-one")), StringComparison.Ordinal)
                && requests[2].ToJsonString().Contains(Convert.ToBase64String(Encoding.UTF8.GetBytes("opaque-two")), StringComparison.Ordinal),
                "Google sends each latest opaque token through the official SDK");
            foreach (var request in requests) RemoveContinuationToken(request);
            Check(JsonNode.DeepEquals(requests[0], requests[1]) && JsonNode.DeepEquals(requests[0], requests[2])
                && requests[0].ToJsonString().Contains("document_get", StringComparison.Ordinal),
                "Google continuation reuses the original input, system instruction, tools and output ceiling");
            Check(raw.AutomaticContinuation == true && raw.ContinuationToken is null && options.MaxOutputTokens == 200000,
                "Google never mutates a caller's continuation settings or raises the output ceiling");
            Check(raw.SystemInstruction.Parts!.Count == 1 && raw.Tools.Count == 1 && raw.StopSequences.SequenceEqual(["STOP_MARKER"])
                && raw.ThinkingConfig.ThinkingLevel is null && options.StopSequences.SequenceEqual(["USER_STOP"]),
                "Google leaves nested caller instructions, tools, stop sequences and thinking defaults unchanged");
            var bridged = updates.Select(update => JsonSerializer.Deserialize<ChatResponseUpdate>(JsonSerializer.Serialize(update))!).ToList().ToChatResponse();
            Check(Property(bridged, ShadowAgentProviders.InitialInputTokensKey) == "11" && bridged.Usage?.TotalTokenCount == 73
                && Property(bridged, ShadowAgentProviders.StopReasonKey) == finalReason,
                "Google continuation usage and response metadata survive NDJSON aggregation");
        }

        foreach (var scenario in new[] { "missing", "repeated", "limit", "interrupted" })
        {
            var requests = 0;
            using var http = new HttpClient(new TestHandler((_, _) =>
            {
                requests++;
                return Task.FromResult(Sse(scenario switch
                {
                    "missing" => GoogleStop("CONTINUATION"),
                    "repeated" => GoogleStop("CONTINUATION", token: "same-token"),
                    "limit" => GoogleStop("CONTINUATION", token: "token-" + requests),
                    _ => requests == 1 ? GoogleStop("CONTINUATION", "pending", "first-token") : GoogleStop(null, "uncommitted"),
                }));
            }));
            using var client = Create("google", true, http);
            var code = scenario switch { "missing" => "continuation_missing_token", "repeated" => "continuation_stalled", "limit" => "continuation_limit", _ => "stream_interrupted" };
            await Failure(() => client.GetResponseAsync([new(ChatRole.User, "test")]), code, transient: scenario == "interrupted",
                "Google " + scenario + " continuation cannot commit an incomplete response");
            Check(requests == (scenario switch { "missing" => 1, "limit" => ShadowAgentProviders.MaximumGoogleContinuationHops, _ => 2 }),
                "Google " + scenario + " continuation uses a bounded number of official requests");
        }
        using (var waiting = new WaitingStream())
        using (var cancellation = new CancellationTokenSource())
        {
            var requests = 0;
            using var http = new HttpClient(new TestHandler((_, _) =>
            {
                requests++;
                if (requests == 1) return Task.FromResult(Sse(GoogleStop("CONTINUATION", "pending", "first-token")));
                var content = new StreamContent(waiting);
                content.Headers.ContentType = new("text/event-stream");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
            }));
            using var client = Create("google", true, http);
            var running = client.GetResponseAsync([new(ChatRole.User, "test")], cancellationToken: cancellation.Token);
            await waiting.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();
            try { await running.WaitAsync(TimeSpan.FromSeconds(5)); throw new InvalidOperationException("FAIL: cancellation did not stop Google continuation"); }
            catch (OperationCanceledException) { checks++; }
            Check(requests == 2 && waiting.IsDisposed, "Cancelling a later Google hop disposes its body and sends no further requests");
        }
        return checks;

        void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException("FAIL: " + label);
            checks++;
        }
        async Task Failure(Func<Task> action, string code, bool transient, string label)
        {
            try { await action(); }
            catch (ShadowAgentProviderException error) when (error.Code == code)
            {
                Check(ShadowAgentProviders.TryGetTransientFailure(error, out _) == transient, label);
                return;
            }
            throw new InvalidOperationException("FAIL: " + label);
        }
    }

    private static IChatClient Create(string provider, bool browser, HttpClient http)
    {
        var options = new ShadowAgentProviderOptions { Provider = provider, Model = "verification-model", ApiKey = "test-secret", Endpoint = "https://provider.example/v1" };
        return browser ? ShadowAgentProviders.CreateBrowser(options, http) : ShadowAgentProviders.Create(options, http);
    }

    private static HttpResponseMessage Sse(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value, Encoding.UTF8, "text/event-stream") };

    private static string? Property(ChatResponse response, string key) =>
        response.AdditionalProperties?.TryGetValue(key, out var value) == true ? value?.ToString() : null;

    private static bool Flag(ChatResponse response, string key) =>
        response.AdditionalProperties?.TryGetValue(key, out var value) == true && (value is true || value is JsonElement { ValueKind: JsonValueKind.True });

    private static List<JsonObject> Events(string value) => value.Split('\n').Where(line => line.StartsWith("data: ", StringComparison.Ordinal))
        .Select(line => JsonNode.Parse(line[6..])!.AsObject()).ToList();

    private static string Stream(IEnumerable<JsonObject> events, bool anthropic = false) => string.Concat(events.Select(item =>
        (anthropic ? "event: " + item["type"]!.GetValue<string>() + "\n" : "") + "data: " + item.ToJsonString() + "\n\n"));

    private static string WithoutTerminal(string value, string terminal) => Stream(Events(value).Where(item => item["type"]?.GetValue<string>() != terminal), terminal == "message_stop");

    private static string OpenAIStop(string? reason, bool empty = false, bool malformedTool = false)
    {
        var events = Events(Streams.For("openai", malformedTool));
        var terminal = events[^1];
        terminal["type"] = "response.incomplete";
        terminal["response"]!["status"] = "incomplete";
        terminal["response"]!["incomplete_details"] = reason is null ? new JsonObject() : new JsonObject { ["reason"] = reason };
        terminal["response"]!["usage"]!["input_tokens_details"] = new JsonObject { ["cached_tokens"] = 3 };
        terminal["response"]!["usage"]!["output_tokens_details"] = new JsonObject { ["reasoning_tokens"] = 2 };
        if (empty)
        {
            terminal["response"]!["output"] = new JsonArray();
            events = [events[0], terminal];
        }
        if (malformedTool)
        {
            foreach (var item in events.Select(item => item["item"]).OfType<JsonObject>().Where(item => item["type"]?.GetValue<string>() == "function_call"))
                item["arguments"] = "{\"path\":";
            foreach (var item in terminal["response"]!["output"]!.AsArray().OfType<JsonObject>().Where(item => item["type"]?.GetValue<string>() == "function_call"))
                item["arguments"] = "{\"path\":";
        }
        return Stream(events);
    }

    private static string AnthropicStop(string reason, bool empty = false, bool malformedTool = false)
    {
        var events = Events(Streams.For("anthropic", malformedTool));
        events.Single(item => item["type"]!.GetValue<string>() == "message_delta")["delta"]!["stop_reason"] = reason;
        if (empty) events.RemoveAll(item => item["type"]!.GetValue<string>().StartsWith("content_block_", StringComparison.Ordinal));
        if (malformedTool)
            foreach (var item in events.Where(item => item["delta"]?["type"]?.GetValue<string>() == "input_json_delta"))
                item["delta"]!["partial_json"] = "{\"path\":";
        return Stream(events, anthropic: true);
    }

    private static string GoogleStop(string? reason, string? text = null, string? token = null, int input = 11, int output = 4, string responseId = "g1")
    {
        var candidate = new JsonObject { ["index"] = 0 };
        if (reason is not null) candidate["finishReason"] = reason;
        if (text is not null) candidate["content"] = new JsonObject { ["role"] = "model", ["parts"] = new JsonArray(new JsonObject { ["text"] = text }) };
        if (token is not null) candidate["continuationToken"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(token));
        return "data: " + new JsonObject
        {
            ["candidates"] = new JsonArray(candidate), ["modelVersion"] = "verification-model", ["responseId"] = responseId,
            ["usageMetadata"] = new JsonObject { ["promptTokenCount"] = input, ["candidatesTokenCount"] = output, ["totalTokenCount"] = input + output },
        }.ToJsonString() + "\n\n";
    }

    private static void RemoveContinuationToken(JsonNode node)
    {
        if (node is JsonObject value)
        {
            value.Remove("continuationToken");
            value.Remove("continuation_token");
            foreach (var item in value.ToArray()) if (item.Value is { } child) RemoveContinuationToken(child);
        }
        else if (node is JsonArray array)
            foreach (var child in array) if (child is not null) RemoveContinuationToken(child);
    }
}
