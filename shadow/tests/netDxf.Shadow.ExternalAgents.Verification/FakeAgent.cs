using System.Text.Json;
using System.Text.Json.Nodes;

// A local protocol fixture, never an installed agent. It performs no network or file operations.
internal static class FakeAgent
{
    internal static async Task RunAsync(bool acp, bool stallInitialize = false)
    {
        var requests = new Dictionary<string, (JsonElement Id, JsonElement Parameters)>(); var serial = 0;
        async Task Send(object value) { await Console.Out.WriteLineAsync(JsonSerializer.Serialize(value)); await Console.Out.FlushAsync(); }
        async Task Result(JsonElement id, object result) => await Send(new { jsonrpc = acp ? "2.0" : null, id, result });
        async Task Notice(string method, object parameters) => await Send(new { jsonrpc = acp ? "2.0" : null, method, @params = parameters });
        while (await Console.In.ReadLineAsync() is { } line)
        {
            using var document = JsonDocument.Parse(line); var root = document.RootElement;
            if (!root.TryGetProperty("method", out var m))
            {
                if (root.TryGetProperty("id", out var serverId) && serverId.ValueKind == JsonValueKind.String && requests.Remove(serverId.GetString()!, out var original))
                {
                    var cancelled = acp && root.TryGetProperty("result", out var reply) && reply.TryGetProperty("outcome", out var outcome) && outcome.TryGetProperty("outcome", out var kind) && kind.GetString() == "cancelled";
                    await Result(original.Id, new { stopReason = cancelled ? "cancelled" : "end_turn", turn = new { id = "turn-fixture", status = "inProgress" }, approval = root.Clone() });
                    await Notice(acp ? "session/update" : "turn/completed", acp
                        ? new { sessionId = "session-fixture", update = (object)new { sessionUpdate = "agent_message_chunk", content = new { type = "text", text = "Approved fixture finished" } } }
                        : (object)new { threadId = "thread-fixture", turn = new { id = "turn-fixture", status = "completed" } });
                }
                continue;
            }
            var method = m.GetString()!; var p = root.TryGetProperty("params", out var parameters) ? parameters.Clone() : JsonSerializer.SerializeToElement(new { });
            if (!root.TryGetProperty("id", out var id))
            {
                if (method == "session/cancel")
                {
                    foreach (var item in requests.ToArray()) { await Result(item.Value.Id, new { stopReason = "cancelled" }); requests.Remove(item.Key); }
                }
                continue;
            }
            if (method == "initialize")
            {
                if (stallInitialize) { await Notice("fixture/initializing", new { fixtureProcessId = Environment.ProcessId }); continue; }
                await Result(id, acp ? new { protocolVersion = 1, agentCapabilities = new { loadSession = true, mcpCapabilities = new { http = true },
                    sessionCapabilities = new { list = new { }, resume = new { }, close = new { }, delete = new { }, additionalDirectories = new { } }, auth = new { logout = new { } } },
                    authMethods = new[] { new { id = "managed", name = "Managed fixture", type = "agent" } }, fixtureProcessId = Environment.ProcessId }
                    : (object)new { userAgent = "fake-codex", fixtureProcessId = Environment.ProcessId, cwd = Directory.GetCurrentDirectory(), startupArguments = Environment.GetCommandLineArgs() });
                continue;
            }
            if (method is "turn/start" or "session/prompt")
            {
                var text = p.GetProperty(acp ? "prompt" : "input")[0].GetProperty("text").GetString();
                if (text == "exit") return;
                if (text is "approval" or "expire" or "question" or "wait")
                {
                    var key = "request-" + ++serial;
                    requests.Add(key, (id.Clone(), p));
                    object request = acp ? new { sessionId = "session-fixture", toolCall = new { toolCallId = key, title = "Fixture file change", kind = "edit" },
                        options = new[] { new { optionId = "allow-once", name = "Allow once", kind = "allow_once" }, new { optionId = "reject-once", name = "Reject", kind = "reject_once" } } }
                        : text == "question" ? new { threadId = "thread-fixture", turnId = "turn-fixture", itemId = "item-fixture",
                            questions = new[] { new { id = "q1", header = "Choice", question = "Choose a fixture", isOther = false, options = new[] { new { label = "One", description = "First" }, new { label = "Two", description = "Second" } } } } }
                        : (object)new { threadId = "thread-fixture", turnId = "turn-fixture", itemId = "item-fixture", availableDecisions = new[] { "accept", "decline", "cancel" } };
                    await Send(new { jsonrpc = acp ? "2.0" : null, id = key,
                        method = acp ? "session/request_permission" : text == "question" ? "item/tool/requestUserInput" : "item/commandExecution/requestApproval", @params = request });
                    await Notice(acp ? "session/update" : "item/agentMessage/delta", new { text = "Notification while callback is pending" });
                    continue;
                }
                if (text == "flood") for (var i = 0; i < 32; i++) await Notice("fixture/event", new { text = new string('x', 1000) });
                if (text == "large-event") await Notice("fixture/large", new { text = new string('x', 4000) });
                if (text == "secret")
                {
                    var secret = Environment.GetEnvironmentVariable("SHADOW_STUDIO_MCP_TOKEN") ?? "none";
                    await Notice("fixture/scalar", secret);
                    await Notice("fixture/" + secret, new { text = "Credential in protocol method metadata" });
                    await Send(new { id, error = new { code = -32000, message = "synthetic " + secret } }); continue;
                }
            }
            if (method == "turn/interrupt")
            {
                foreach (var item in requests.ToArray())
                {
                    await Notice("serverRequest/resolved", new { threadId = "thread-fixture", requestId = item.Key });
                    await Result(item.Value.Id, new { turn = new { id = "turn-fixture", status = "interrupted" } }); requests.Remove(item.Key);
                }
            }
            if (method == "turn/steer" && p.GetProperty("expectedTurnId").GetString() != "turn-fixture")
            { await Send(new { id, error = new { code = -32602, message = "Expected turn does not match the active turn" } }); continue; }
            if (method == "thread/compact/start")
            {
                await Result(id, new { echo = p });
                await Notice("turn/started", new { threadId = "thread-fixture", turn = new { id = "compact-fixture", status = "inProgress" } });
                await Notice("item/started", new { threadId = "thread-fixture", turnId = "compact-fixture", item = new { id = "compact-item", type = "contextCompaction" } });
                await Notice("item/completed", new { threadId = "thread-fixture", turnId = "compact-fixture", item = new { id = "compact-item", type = "contextCompaction" } });
                await Notice("turn/completed", new { threadId = "thread-fixture", turn = new { id = "compact-fixture", status = "completed" } });
                continue;
            }
            var result = new JsonObject { ["echo"] = JsonNode.Parse(p.GetRawText()), ["method"] = method, ["cwd"] = Directory.GetCurrentDirectory() };
            if (method == "session/new") { result["sessionId"] = "session-fixture"; result["modes"] = new JsonObject { ["currentModeId"] = "default", ["availableModes"] = new JsonArray(new JsonObject { ["id"] = "default", ["name"] = "Default" }) }; }
            if (method is "thread/start" or "thread/resume" or "thread/read") result["thread"] = new JsonObject { ["id"] = "thread-fixture", ["cwd"] = Directory.GetCurrentDirectory() };
            if (method == "account/login/start") { result["loginId"] = "login-fixture"; result["authUrl"] = "https://example.invalid/fixture-login"; }
            if (method is "thread/list" or "thread/turns/list" or "thread/items/list") { result["data"] = new JsonArray(); result["nextCursor"] = null; }
            if (method == "turn/steer") result["turnId"] = "turn-fixture";
            await Result(id, result);
        }
    }
}
