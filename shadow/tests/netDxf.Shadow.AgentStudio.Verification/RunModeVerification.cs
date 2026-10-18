using System.Text.Json;
using netDxf.Shadow.Agents;
using netDxf.Shadow.Mcp;
using netDxf.Shadow.Studio;

internal static class RunModeVerification
{
    internal static async Task<int> RunAsync()
    {
        var checks = 0; var mutations = 0;
        void Check(bool value, string description)
        {
            if (!value) throw new InvalidOperationException("Studio run-mode verification failed: " + description);
            checks++;
        }
        McpToolRegistry Tools(string document)
        {
            var registry = new McpToolRegistry();
            var schema = McpJson.Parse("""{"type":"object","properties":{},"additionalProperties":false}""");
            registry.Register(new("read", "Read drawing", schema, true), (_, _) => ValueTask.FromResult(McpToolResult.FromObject(new { document })));
            registry.Register(new("mutate", "Change drawing", schema), (_, _) =>
            { mutations++; return ValueTask.FromResult(new McpToolResult("Changed")); });
            return registry;
        }
        await using var controller = new StudioAgentController(Tools);
        controller.Configure(new() { PermissionMode = "fullAccess", Mode = "goal", SuccessCriteria = "Original goal criteria.", MaxTurns = 0, MaxNoProgressTurns = 6 });
        Check(controller.Session.Options.Mode == AgentRunMode.Goal && controller.Session.Options.MaxTurns == 0
            && controller.Session.Options.MaxNoProgressTurns == 6, "Studio forwards mode and persistent execution budgets to the harness");
        Check(controller.Session.RunState.Phase == "idle" && !controller.Session.RunState.Resumable,
            "choosing a mode before entering a task keeps the run idle");
        var checkpoint = controller.Export();
        controller.Configure(new() { PermissionMode = "fullAccess", Mode = "plan", SuccessCriteria = "Inspect without changes." });
        controller.Import(checkpoint);
        Check(controller.Session.Options.Mode == AgentRunMode.Plan && controller.Session.RunState.Mode == AgentRunMode.Plan
            && controller.Session.RunState.SuccessCriteria == "Inspect without changes.", "import cannot restore a less restrictive mode over the host's selection");
        Check((await controller.Call("mutate", McpJson.EmptyObject)).IsError && mutations == 0,
            "manual Studio tool execution enforces Plan mode even with FullAccess");
        Check((await controller.Call("workspace_write", McpJson.Element(new { path = "forbidden.txt", content = "write", expectedVersion = 0 }))).IsError
            && controller.Session.Workspace.List().Count == 0, "Plan mode blocks manual workspace changes through the controller");
        Check(!(await controller.Call("read", McpJson.EmptyObject)).IsError, "Plan mode still permits drawing inspection");
        var plan = await controller.Call("agent_plan", McpJson.Element(new { steps = new[] { new { step = "Implement after review", status = "pending" } } }));
        Check(!plan.IsError && controller.Session.Plan.Single().Status == "pending", "Plan mode can publish pending implementation steps");
        var poll = JsonSerializer.SerializeToElement(controller.Poll(0), McpJson.Options);
        Check(poll.TryGetProperty("run", out var run) && run.GetProperty("successCriteria").GetString() == "Inspect without changes.",
            "Studio poll exposes mode and run state for the browser visualization");
        controller.Configure(new() { PermissionMode = "fullAccess", Mode = "normal" });
        Check(!(await controller.Call("mutate", McpJson.EmptyObject)).IsError && mutations == 1,
            "switching explicitly from Plan to Normal restores the selected execution permission");
        var stable = controller.Session;
        try { controller.Configure(new() { Mode = "invalid" }); throw new InvalidOperationException("Invalid mode was accepted."); }
        catch (ArgumentException) { checks++; }
        Check(ReferenceEquals(stable, controller.Session), "invalid mode configuration cannot replace a working session");
        return checks;
    }
}
