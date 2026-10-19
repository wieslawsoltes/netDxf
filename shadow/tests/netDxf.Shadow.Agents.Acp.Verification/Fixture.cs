using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

internal static class Fixture
{
    internal static async Task Run(string scenario)
    {
        Console.InputEncoding = new UTF8Encoding(false); Console.OutputEncoding = new UTF8Encoding(false);
        var gate = new SemaphoreSlim(1); var replies = new ConcurrentDictionary<int, TaskCompletionSource<JsonElement>>();
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var nextRequest = 100; var authenticated = scenario != "auth";
        var tasks = new List<Task>();
        while (await Console.In.ReadLineAsync() is { } line)
        {
            using var doc = JsonDocument.Parse(line); var message = doc.RootElement.Clone();
            if (!message.TryGetProperty("method", out var methodElement))
            {
                if (message.GetProperty("id").TryGetInt32(out var id) && replies.TryRemove(id, out var result)) result.TrySetResult(message);
                continue;
            }
            var method = methodElement.GetString(); var p = message.GetProperty("params");
            if (method == "session/cancel") { cancelled.TrySetResult(); continue; }
            if (method == "$/cancel_request") continue;
            var requestId = message.GetProperty("id").Clone();
            switch (method)
            {
                case "initialize":
                    if (p.GetProperty("protocolVersion").GetInt32() != 1) throw new InvalidOperationException("Client must request ACP v1.");
                    if (p.GetProperty("clientCapabilities").TryGetProperty("auth", out var auth) && auth.TryGetProperty("terminal", out var terminal) && terminal.GetBoolean())
                        throw new InvalidOperationException("Client must not advertise terminal authentication.");
                    await Respond(requestId, new
                    {
                        protocolVersion = scenario == "version2" ? 2 : 1,
                        agentInfo = new { name = "fixture", version = "1" },
                        agentCapabilities = scenario == "minimal" ? J(new { }) : J(new { loadSession = true, auth = new { logout = new { } }, sessionCapabilities = new { list = new { }, delete = new { }, resume = new { }, close = new { } } }),
                        authMethods = new object[] { new { id = "login", name = "Agent login" }, new { id = "interactive", name = "External interactive login", type = "terminal", args = new[] { "--login" } } },
                        secretInherited = Environment.GetEnvironmentVariable("SHADOW_AGENT_BRIDGE_TOKEN") is not null,
                        runtimeEnvironment = new[] { "DOTNET_ROOT", "DOTNET_ROOT_X64", "DOTNET_ROOT_X86", "DOTNET_ROOT_ARM64", "DOTNET_ROOT(x86)", "DOTNET_ROLL_FORWARD" }
                            .ToDictionary(name => name, Environment.GetEnvironmentVariable),
                        clientCaps = p.GetProperty("clientCapabilities")
                    }); break;
                case "authenticate": authenticated = true; await Respond(requestId, new { }); break;
                case "logout": authenticated = false; await Respond(requestId, new { }); break;
                case "session/new":
                    if (!authenticated) await Send(new { jsonrpc = "2.0", id = requestId, error = new { code = -32000, message = "Authentication required", data = new { reason = "auth_required" } } });
                    else await Respond(requestId, Metadata()); break;
                case "session/load": case "session/resume": await Respond(requestId, Metadata()); break;
                case "session/list": if (scenario != "pending") await Respond(requestId, new { sessions = new[] { new { sessionId = "s1", cwd = Path.GetTempPath() } }, nextCursor = "next" }); break;
                case "session/delete": case "session/close": case "session/set_mode": await Respond(requestId, new { }); break;
                case "session/set_config_option": await Respond(requestId, new { configOptions = Config() }); break;
                case "session/prompt": tasks.Add(Task.Run(() => Turn(requestId, p))); break;
                default: throw new InvalidOperationException("Unexpected method: " + method);
            }
        }
        return;

        async Task Turn(JsonElement id, JsonElement parameters)
        {
            var text = parameters.GetProperty("prompt")[0].GetProperty("text").GetString();
            if (text == "hang") { await Task.Delay(Timeout.Infinite); return; }
            if (text == "eof") { Environment.Exit(0); return; }
            if (text == "cancel-elicitation")
            {
                var response = await Request("elicitation/create", new { sessionId = "s1", mode = "form", message = "Choose a value", requestedSchema = new { type = "object" } });
                await Respond(id, new { stopReason = "cancelled", elicitation = response.GetProperty("result") }); return;
            }
            if (text == "duplicate-request-id")
            {
                await Send(new { jsonrpc = "2.0", id = "same", method = "fs/read_text_file", @params = new { sessionId = "s1", path = Path.Combine(Path.GetTempPath(), "fixture.txt") } });
                await gate.WaitAsync();
                try
                {
                    var path = JsonSerializer.Serialize(Path.Combine(Path.GetTempPath(), "fixture.txt"));
                    await Console.Out.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"id\":\"sa\\u006de\",\"method\":\"fs/read_text_file\",\"params\":{\"sessionId\":\"s1\",\"path\":" + path + "}}");
                    await Console.Out.FlushAsync();
                }
                finally { gate.Release(); }
                return;
            }
            if (text is "malformed" or "oversized")
            {
                await gate.WaitAsync();
                try { await Console.Out.WriteLineAsync(text == "malformed" ? "not json" : new string('x', 5000)); await Console.Out.FlushAsync(); }
                finally { gate.Release(); }
                return;
            }
            if (text is "basic" or "permission" or "cancel" or "invalid-choice")
            {
                await Notify("session/update", new { sessionId = "s1", update = new { sessionUpdate = "agent_message_chunk", content = new { type = "text", text = "Hello 世界" } } });
                await Notify("session/update", new { sessionId = "s1", update = new { sessionUpdate = "tool_call", toolCallId = "edit1", title = "Edit drawing", kind = "edit", status = "pending", rawInput = new { entity = "line" } } });
                var permission = await Request("session/request_permission", new
                {
                    sessionId = "s1", toolCall = new { toolCallId = "edit1", title = "Edit drawing" },
                    options = new[] { new { optionId = "allow", name = "Allow once", kind = "allow_once" }, new { optionId = "deny", name = "Reject", kind = "reject_once" } }
                });
                if (text == "invalid-choice") { await Respond(id, new { stopReason = "end_turn", permissionError = permission.GetProperty("error").GetProperty("code").GetInt32() }); return; }
                var outcome = permission.GetProperty("result").GetProperty("outcome").GetProperty("outcome").GetString();
                if (text == "cancel")
                {
                    await cancelled.Task; if (outcome != "cancelled") throw new InvalidOperationException("Cancellation must cancel pending permission.");
                    await Respond(id, new { stopReason = "cancelled" }); return;
                }
                if (text == "permission") { await Respond(id, new { stopReason = "end_turn", permissionOutcome = outcome }); return; }
                if (outcome != "selected") throw new InvalidOperationException("Expected selection.");
                var read = await Request("fs/read_text_file", new { sessionId = "s1", path = Path.Combine(Path.GetTempPath(), "editor.txt"), line = 1, limit = 20 });
                if (read.GetProperty("result").GetProperty("content").GetString() != "file from editor") throw new InvalidOperationException("Read service failed.");
                await Request("fs/write_text_file", new { sessionId = "s1", path = Path.Combine(Path.GetTempPath(), "editor.txt"), content = "updated" });
                foreach (var terminalMethod in new[] { "create", "output", "wait_for_exit", "kill", "release" })
                    await Request("terminal/" + terminalMethod, new { sessionId = "s1", terminalId = "t1", command = "fixture", args = Array.Empty<string>() });
                var unknown = await Request("_fixture/unknown", new { });
                if (unknown.GetProperty("error").GetProperty("code").GetInt32() != -32601) throw new InvalidOperationException("Unknown request must be method-not-found.");
                await Request("elicitation/create", new { sessionId = "s1", mode = "form", message = "Choose a name", requestedSchema = new { type = "object", properties = new { name = new { type = "string" } } } });
                await Request("elicitation/create", new { sessionId = "s1", mode = "url", message = "Open sign-in", elicitationId = "login1", url = "https://example.invalid/sign-in" });
                await Notify("session/update", new { sessionId = "s1", update = new { sessionUpdate = "tool_call_update", toolCallId = "edit1", status = "completed", rawOutput = new { revision = 1 } } });
                await Notify("_fixture/metadata", new { custom = 7 });
            }
            await Respond(id, new { stopReason = "end_turn" });
        }
        Task Respond<T>(JsonElement id, T result) => Send(new { jsonrpc = "2.0", id, result });
        Task Notify<T>(string method, T parameters) => Send(new { jsonrpc = "2.0", method, @params = parameters });
        async Task<JsonElement> Request<T>(string method, T parameters)
        {
            var id = Interlocked.Increment(ref nextRequest); var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            replies[id] = completion; await Send(new { jsonrpc = "2.0", id, method, @params = parameters });
            return await completion.Task;
        }
        async Task Send<T>(T message)
        {
            await gate.WaitAsync();
            try { await Console.Out.WriteLineAsync(JsonSerializer.Serialize(message)); await Console.Out.FlushAsync(); }
            finally { gate.Release(); }
        }
    }
    private static object Metadata() => new { sessionId = "s1", modes = new { currentModeId = "ask", availableModes = new[] { new { id = "ask", name = "Ask" } } }, configOptions = Config() };
    private static object[] Config() => [new { id = "model", name = "Model", type = "select", currentValue = "model-a", options = new[] { new { value = "model-a", name = "A" }, new { value = "model-b", name = "B" } } }, new { id = "safe", name = "Safe", type = "boolean", currentValue = false }];
    private static JsonElement J<T>(T value) => JsonSerializer.SerializeToElement(value);
}
