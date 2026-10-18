using System.Text.Json;
using netDxf.Shadow.Mcp;
using netDxf.Shadow.Studio;

internal static class BrowserMcpVerification
{
    internal static async Task<int> RunAsync(string companionEndpoint, string companionToken, Func<int> nativeExecutions)
    {
        var checks = 0;
        var mutations = 0;
        var includeLateTool = false;
        var waitStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var waitCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Assert(bool value, string description)
        {
            if (!value) throw new InvalidOperationException("Browser MCP verification failed: " + description);
            checks++;
        }
        McpToolRegistry Tools(string document)
        {
            var registry = new McpToolRegistry();
            registry.Register(new("shadow_state", "Read the bound drawing", McpJson.Parse("""{"type":"object","properties":{},"additionalProperties":false}"""), true), (_, token) =>
            { token.ThrowIfCancellationRequested(); return ValueTask.FromResult(McpToolResult.FromObject(new { document })); });
            registry.Register(new("fixture_change", "Commit one change", McpJson.Parse("""{"type":"object","properties":{},"additionalProperties":false}""")), (_, token) =>
            { token.ThrowIfCancellationRequested(); mutations++; return ValueTask.FromResult(new McpToolResult("Committed.")); });
            registry.Register(new("fixture_wait", "Wait for transport cancellation", McpJson.Parse("""{"type":"object","properties":{},"additionalProperties":false}"""), true), async (_, token) =>
            {
                waitStarted.TrySetResult();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
                catch (OperationCanceledException) { waitCancelled.TrySetResult(); throw; }
                return new("Wait ended.");
            });
            if (includeLateTool)
                registry.Register(new("fixture_late", "Added after the browser grant", McpJson.Parse("""{"type":"object","properties":{},"additionalProperties":false}"""), true), (_, _) => ValueTask.FromResult(new McpToolResult("Late tool executed.")));
            return registry;
        }
        await using var controller = new StudioAgentController(Tools);
        controller.Configure(new() { PermissionMode = "fullAccess" });
        var connection = JsonSerializer.SerializeToElement(controller.OpenMcpSession("demo"), McpJson.Options);
        var session = connection.GetProperty("sessionId").GetString()!;
        string Request(object id, string method, object? parameters = null) => JsonSerializer.Serialize(new { jsonrpc = "2.0", id, method, @params = parameters ?? new { } }, McpJson.Options);
        async Task<JsonElement> Call(object id, string method, object? parameters = null) =>
            McpJson.Parse((await controller.ProcessMcpSession(session, Request(id, method, parameters)).WaitAsync(TimeSpan.FromSeconds(10)))!);
        object Tool(string name) => new { name, arguments = new { } };
        async Task Notify(string method, object? parameters = null) =>
            await controller.ProcessMcpSession(session, JsonSerializer.Serialize(new { jsonrpc = "2.0", method, @params = parameters ?? new { } }, McpJson.Options));
        bool ToolError(JsonElement value) => value.TryGetProperty("error", out _) || value.GetProperty("result").TryGetProperty("isError", out var error) && error.GetBoolean();

        var initialize = await Call(1, "initialize", new { protocolVersion = "2025-11-25", capabilities = new { }, clientInfo = new { name = "Browser transport verification", version = "1.0.0" } });
        Assert(initialize.GetProperty("result").GetProperty("serverInfo").GetProperty("name").GetString() == "netDxf.Shadow.Mcp", "initialization is answered by the official SDK server");
        await Notify("notifications/initialized");
        var catalog = await Call(2, "tools/list");
        Assert(catalog.GetProperty("result").GetProperty("tools").EnumerateArray().Any(tool => tool.GetProperty("name").GetString() == "fixture_change"), "the complete registered tool catalog crosses the protocol transport");
        var state = await Call(3, "tools/call", Tool("shadow_state"));
        Assert(!ToolError(state) && state.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!.Contains("demo", StringComparison.Ordinal), "MCP reads use the bound drawing");

        var request = Request(4, "tools/call", Tool("fixture_change"));
        var receipt = await controller.ProcessMcpSession(session, request);
        Assert(!ToolError(McpJson.Parse(receipt!)) && mutations == 1, "full access executes the shared agent mutation once");
        Assert(await controller.ProcessMcpSession(session, request) == receipt && mutations == 1, "a duplicate completed request returns its receipt without replaying the mutation");
        Assert((await Call(4, "tools/call", Tool("shadow_state"))).GetProperty("error").GetProperty("code").GetInt32() == -32600 && mutations == 1, "request IDs cannot be reused with different content");

        controller.Configure(new() { PermissionMode = "readOnly" });
        Assert(ToolError(await Call(5, "tools/call", Tool("fixture_change"))) && mutations == 1, "MCP cannot bypass read-only agent permissions");
        controller.Configure(new() { PermissionMode = "fullAccess", Mode = "plan" });
        Assert(ToolError(await Call("plan-write", "tools/call", Tool("fixture_change"))) && mutations == 1,
            "an existing browser MCP grant cannot bypass Plan mode through FullAccess");
        Assert(!ToolError(await Call("plan-read", "tools/call", Tool("shadow_state"))), "Plan mode retains browser MCP inspection tools");
        Assert(!ToolError(await Call("plan-metadata", "tools/call", new { name = "agent_plan", arguments = new { steps = new[] { new { step = "Review before implementation", status = "pending" } } } }))
            && controller.Session.Plan.Count == 1, "Plan metadata remains editable through the same browser MCP policy");
        controller.Configure(new() { PermissionMode = "ask" });
        var approval = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        controller.Session.EventEmitted += value =>
        {
            if (value.Type == "approvalRequested") approval.TrySetResult(value.Data!.Value.GetProperty("requestId").GetString()!);
        };
        var pendingApproval = Call(6, "tools/call", Tool("fixture_change"));
        await approval.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert(!pendingApproval.IsCompleted && mutations == 1, "MCP mutations wait for the shared approval panel");
        await Notify("notifications/cancelled", new { requestId = 6, reason = "verification cancellation" });
        Assert(ToolError(await pendingApproval) && mutations == 1, "protocol cancellation ends a pending approval without editing");
        approval = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var approvedCall = Call(7, "tools/call", Tool("fixture_change"));
        Assert(controller.Approve(await approval.Task.WaitAsync(TimeSpan.FromSeconds(5)), true), "the ordinary approval API resolves MCP requests");
        Assert(!ToolError(await approvedCall) && mutations == 2, "an approved MCP mutation commits once");

        var resources = await Call(8, "resources/list");
        Assert(resources.GetProperty("result").GetProperty("resources").GetArrayLength() >= 3, "official SDK resources remain available over MessagePort transport");
        var resource = await Call(9, "resources/read", new { uri = "shadow://workspace/state" });
        Assert(resource.GetProperty("result").GetProperty("contents")[0].GetProperty("text").GetString()!.Contains("demo", StringComparison.Ordinal), "resource reads enter the same bound tool executor");
        var prompts = await Call(10, "prompts/list");
        Assert(prompts.GetProperty("result").GetProperty("prompts").GetArrayLength() == 2, "official SDK prompts remain available");
        Assert((await Call(11, "ping")).TryGetProperty("result", out _), "protocol ping works without inference or a companion");

        var waiting = Call(12, "tools/call", Tool("fixture_wait"));
        await waitStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Notify("notifications/cancelled", new { requestId = 12 });
        await waitCancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert(ToolError(await waiting), "MCP cancellation reaches the actual running .NET tool");
        Assert(!ToolError(await Call(13, "tools/call", Tool("shadow_state"))), "a cancelled call releases the shared tool lane");

        includeLateTool = true;
        controller.Configure(new() { PermissionMode = "ask", Model = "catalog changed" });
        Assert(ToolError(await Call("late", "tools/call", Tool("fixture_late"))), "new tools require refreshing the explicit browser MCP catalog grant");

        controller.SetDocument("second");
        Assert(ToolError(await Call(14, "tools/call", Tool("shadow_state"))), "an old connection cannot follow a newly selected drawing");
        try { controller.OpenMcpSession("demo"); throw new InvalidOperationException("Expected a stale drawing rejection."); }
        catch (InvalidOperationException error) when (error.Message.Contains("active drawing changed", StringComparison.Ordinal)) { checks++; }
        var extraSessions = new List<string>();
        for (var index = 0; index < 7; index++)
            extraSessions.Add(JsonSerializer.SerializeToElement(controller.OpenMcpSession("second"), McpJson.Options).GetProperty("sessionId").GetString()!);
        try { controller.OpenMcpSession("second"); throw new InvalidOperationException("Expected a session capacity rejection."); }
        catch (InvalidOperationException error) when (error.Message.Contains("At most eight", StringComparison.Ordinal)) { checks++; }
        foreach (var extra in extraSessions) await controller.CloseMcpSession(extra);
        Assert(McpJson.Parse((await controller.ProcessMcpSession(session, new string('x', 1_048_577)))!).GetProperty("error").GetProperty("code").GetInt32() == -32600, "oversized messages are rejected before protocol dispatch");
        Assert((await Call(new string('x', 257), "ping")).GetProperty("error").GetProperty("code").GetInt32() == -32600, "request identifiers cannot exhaust receipt memory");
        await controller.CloseMcpSession(session);
        await controller.CloseMcpSession(session);
        try { await controller.ProcessMcpSession(session, Request(20, "ping")); throw new InvalidOperationException("Expected a closed session rejection."); }
        catch (InvalidOperationException error) when (error.Message.Contains("session is closed", StringComparison.Ordinal)) { checks++; }

        await using var native = new StudioAgentController(Tools);
        native.Configure(new() { PermissionMode = "fullAccess" });
        await native.CompanionConnect(companionEndpoint, companionToken);
        var nativeSession = JsonSerializer.SerializeToElement(native.OpenMcpSession("demo"), McpJson.Options).GetProperty("sessionId").GetString()!;
        async Task<JsonElement> NativeCall(object id, string method, object? parameters = null) =>
            McpJson.Parse((await native.ProcessMcpSession(nativeSession, Request(id, method, parameters)).WaitAsync(TimeSpan.FromSeconds(10)))!);
        await NativeCall(1, "initialize", new { protocolVersion = "2025-11-25", capabilities = new { }, clientInfo = new { name = "Native grant verification", version = "1.0.0" } });
        await native.ProcessMcpSession(nativeSession, """{"jsonrpc":"2.0","method":"notifications/initialized"}""");
        var beforeNative = nativeExecutions();
        var nativeReceipt = await NativeCall(2, "tools/call", Tool("companion_probe"));
        Assert(ToolError(nativeReceipt) && nativeExecutions() == beforeNative + 1,
            "browser MCP calls the granted companion exactly once even when the native tool returns an error");
        // Reconnecting the same endpoint is a new grant and may identify a different workspace.
        // Tool names and schemas alone must not authorize an old external agent to follow it.
        await native.CompanionConnect(companionEndpoint, companionToken);
        var replacedGrant = await NativeCall(3, "tools/call", Tool("companion_probe"));
        Assert(ToolError(replacedGrant) && replacedGrant.GetRawText().Contains("companion_changed", StringComparison.Ordinal)
            && nativeExecutions() == beforeNative + 1, "an existing browser MCP session cannot follow a replacement companion grant");
        return checks;
    }
}
