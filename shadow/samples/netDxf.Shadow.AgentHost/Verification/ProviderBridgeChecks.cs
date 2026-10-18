using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using netDxf.Shadow.AgentHost;
using netDxf.Shadow.Agents;

internal static class ProviderBridgeChecks
{
    public static async Task<int> RunAsync(string directory, string origin, string token)
    {
        var count = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("FAIL: " + message);
            count++;
        }
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(); var bridgePort = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        var options = new AgentHostOptions(bridgePort, [origin], token, Path.Combine(directory, "provider-host.json"));
        var requests = new List<JsonObject>();
        using var upstream = new HttpClient(new FixtureHandler(async (request, ct) =>
        {
            Check(request.RequestUri?.Host == "fixture.example" && request.RequestUri.AbsolutePath == "/v1/messages", "bridge preserves explicitly configured official-SDK endpoint");
            Check(request.Headers.GetValues("x-api-key").Single() == "anthropic-provider-key", "native official SDK receives provider key instead of bridge token");
            requests.Add(JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!.AsObject());
            var json = requests.Count switch
            {
                1 => AnthropicFixture(tool: true),
                2 => AnthropicFixture(tool: false),
                _ => "event: message_start\ndata: " + """{"type":"message_start","message":{"id":"m","type":"message","role":"assistant","model":"verification-model","content":[],"stop_reason":null,"stop_sequence":null,"usage":{"input_tokens":11,"output_tokens":1}}}"""
                    + "\n\nevent: error\ndata: " + """{"type":"error","error":{"type":"overloaded_error","message":"Temporary overload"}}""" + "\n\n",
            };
            return new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "text/event-stream") };
        }));
        await using var host = AgentHostApplication.Build(options, services => services.AddSingleton(upstream));
        await host.StartAsync();
        try
        {
            using var bridge = ShadowAgentProviders.CreateBrowserBridge(new()
            {
                Provider = "anthropic", Model = "verification-model", ApiKey = "anthropic-provider-key", Endpoint = "https://fixture.example",
            }, $"http://127.0.0.1:{bridgePort}", token);
            var chatOptions = new ChatOptions
            {
                MaxOutputTokens = 256, AllowMultipleToolCalls = true,
                Tools = [AIFunctionFactory.Create(() => "state", name: "inspect", description: "Inspect state")],
            };
            var messages = new List<ChatMessage> { new(ChatRole.System, "Inspect the document."), new(ChatRole.User, "Inspect.") };
            var first = await bridge.GetResponseAsync(messages, chatOptions);
            var call = first.Messages.SelectMany(message => message.Contents).OfType<FunctionCallContent>().Single();
            Check(call.Name == "inspect" && call.CallId == "tool-1", "official Anthropic tool call crosses native bridge");
            Check(first.Messages.SelectMany(message => message.Contents).OfType<TextReasoningContent>().Any(content => content.ProtectedData == "verified-signature"), "bridge preserves protected Anthropic thinking signature");
            Check(first.Usage?.InputTokenCount == 11, "bridge preserves provider usage details");
            messages.AddRange(first.Messages);
            messages.Add(new(ChatRole.Tool, [new FunctionResultContent(call.CallId, "{\"revision\":42}")]));
            var final = await bridge.GetResponseAsync(messages, chatOptions);
            Check(final.Text == "Bridge complete." && requests.Count == 2, "native official SDK completes multi-turn tool replay once per inference");
            Check(requests[1].ToJsonString().Contains("verified-signature", StringComparison.Ordinal)
                && requests[1].ToJsonString().Contains("revision", StringComparison.Ordinal), "bridge restores thinking and tool output in next official SDK request");
            try
            {
                await bridge.GetResponseAsync(messages, chatOptions);
                throw new InvalidOperationException("FAIL: Native provider failure became a successful response.");
            }
            catch (ShadowAgentProviderException error)
            { Check(ShadowAgentProviders.TryGetTransientFailure(error, out _), "typed Anthropic overload stays transient across native bridge"); }
            Check(requests.Count == 3, "native bridge never retries provider inference itself");
        }
        finally { await host.StopAsync(); }
        return count;
    }

    private static string AnthropicFixture(bool tool)
    {
        var events = new List<string>
        {
            """{"type":"message_start","message":{"id":"m1","type":"message","role":"assistant","model":"verification-model","content":[],"stop_reason":null,"stop_sequence":null,"usage":{"input_tokens":11,"output_tokens":1}}}""",
        };
        if (tool) events.AddRange(
        [
            """{"type":"content_block_start","index":0,"content_block":{"type":"thinking","thinking":"","signature":""}}""",
            """{"type":"content_block_delta","index":0,"delta":{"type":"thinking_delta","thinking":"Inspect the state"}}""",
            """{"type":"content_block_delta","index":0,"delta":{"type":"signature_delta","signature":"verified-signature"}}""",
            """{"type":"content_block_stop","index":0}""",
            """{"type":"content_block_start","index":1,"content_block":{"type":"tool_use","id":"tool-1","name":"inspect","input":{}}}""",
            """{"type":"content_block_delta","index":1,"delta":{"type":"input_json_delta","partial_json":"{}"}}""",
            """{"type":"content_block_stop","index":1}""",
        ]);
        else events.AddRange(
        [
            """{"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}""",
            """{"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"Bridge complete."}}""",
            """{"type":"content_block_stop","index":0}""",
        ]);
        events.Add("""{"type":"message_delta","delta":{"stop_reason":""" + JsonSerializer.Serialize(tool ? "tool_use" : "end_turn") + ""","stop_sequence":null},"usage":{"output_tokens":4}}""");
        events.Add("""{"type":"message_stop"}""");
        return string.Concat(events.Select(value => "event: " + JsonNode.Parse(value)!["type"]!.GetValue<string>() + "\ndata: " + value + "\n\n"));
    }

    private sealed class FixtureHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
