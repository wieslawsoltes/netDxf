using netDxf.Shadow.Agents;
using netDxf.Shadow.Mcp;
using System.Text.Json;

internal static class BrowserCodeVerification
{
    public static async Task<int> RunAsync()
    {
        var checks = 0;
        void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks++; }
        void Reject(Action action) { try { action(); } catch (ArgumentException) { checks++; return; } catch (InvalidOperationException) { checks++; return; } throw new InvalidOperationException("Expected an atomic file batch rejection."); }
        var files = new MemoryAgentWorkspace(3, 100);
        var a = files.Write("a.js", "initial", 0); var b = files.Write("b.txt", "delete me", 0);
        Reject(() => files.Apply([new("a.js", "should not commit", a.Version), new("b.txt", "conflict", b.Version + 1)]));
        Assert(files.Read("a.js").Content == "initial" && files.Read("a.js").Version == a.Version, "A conflicting file batch partially committed.");
        Reject(() => files.Apply([new("a.js", "bad", a.Version), new("../escape", "bad", 0)]));
        Reject(() => files.Apply([new("a.js", "bad", a.Version), new("a.js", "again", a.Version)]));
        Reject(() => files.Apply([new("a.js", new string('x', 101), a.Version)]));
        Assert(files.Export().Count == 2 && files.Read("a.js").Content == "initial", "Invalid file batch changed the workspace.");
        files.Apply([new("b.txt", null, b.Version), new("c.txt", "created", 0)]);
        Assert(files.List().All(file => file.Path != "b.txt") && files.Read("c.txt").Content == "created", "Atomic delete/create did not commit.");

        var registry = new McpToolRegistry(); var runner = new Runner();
        AgentCodeTools.Register(registry, files, runner);
        var catalog = await registry.ExecuteAsync("workspace_runtime", McpJson.EmptyObject);
        Assert(!catalog.IsError && catalog.Text.Contains("isolated-javascript-worker", StringComparison.Ordinal), "Browser code runtime discovery failed.");
        Assert(registry.Tools.Single(tool => tool.Name == "workspace_run_javascript").ReadOnly == false, "Code execution was classified as a read.");
        runner.Run = request =>
        {
            Assert(request.Code == "initial" && request.Files.Count == 2 && request.Arguments.Single() == "arg", "Code runner did not receive the exact versioned snapshot and source.");
            return Result([new("a.js", "after"), new("out.json", "42"), new("c.txt", null)]);
        };
        var executed = await registry.ExecuteAsync("workspace_run_javascript", McpJson.Element(new { path = "a.js", arguments = new[] { "arg" } }));
        Assert(!executed.IsError && files.Read("a.js").Content == "after" && files.Read("out.json").Content == "42" && files.List().Count == 2, "Successful code did not commit its complete file transaction.");
        Assert(files.Read("a.js").Version > a.Version, "Code reused an old file version.");
        runner.Run = _ => Result([new("a.js", "failed code mutation")]) with { ExitCode = 1 };
        var failed = await registry.ExecuteAsync("workspace_run_javascript", McpJson.Element(new { code = "throw new Error()" }));
        Assert(failed.IsError && files.Read("a.js").Content == "after", "Failed code committed staged files.");
        runner.Run = _ => Result([new("a.js", "timeout mutation")]) with { TimedOut = true };
        var timedOut = await registry.ExecuteAsync("workspace_run_javascript", McpJson.Element(new { code = "while(true){}" }));
        Assert(timedOut.IsError && files.Read("a.js").Content == "after", "Timed-out code committed staged files.");
        runner.Run = _ => { files.Write("a.js", "external change"); return Result([new("a.js", "stale"), new("new.txt", "must not commit")]); };
        var conflict = await registry.ExecuteAsync("workspace_run_javascript", McpJson.Element(new { code = "return 1" }));
        Assert(conflict.IsError && files.Read("a.js").Content == "external change" && files.List().All(f => f.Path != "new.txt"), "Code overwrote a concurrent edit or partially committed after a conflict.");
        using var disposedDocument = JsonDocument.Parse("{\"ready\":true}");
        var disposedValue = disposedDocument.RootElement;
        disposedDocument.Dispose();
        foreach (var invalid in new[]
        {
            Result([new("a.js", "must not commit")]) with { ElapsedMilliseconds = double.NaN },
            Result([new("a.js", "must not commit")]) with { ElapsedMilliseconds = double.PositiveInfinity },
            Result([new("a.js", "must not commit")]) with { Result = default(JsonElement) },
            Result([new("a.js", "must not commit")]) with { Result = disposedValue },
            Result([new("a.js", "must not commit")]) with { Result = McpJson.Element(new string('x', 200001)) },
            Result([new("a.js", "must not commit"), new("a.js", "duplicate")]),
            Result([new("a.js", new string('x', 16 * 1024 * 1024 + 1))]),
        })
        {
            runner.Run = _ => invalid;
            var rejected = await registry.ExecuteAsync("workspace_run_javascript", McpJson.Element(new { code = "return 1" }));
            Assert(rejected.IsError && files.Read("a.js").Content == "external change", "An invalid code receipt committed files before validation.");
        }
        Assert((await registry.ExecuteAsync("workspace_run_javascript", McpJson.EmptyObject)).IsError, "Missing code and path were accepted.");
        Assert((await registry.ExecuteAsync("workspace_run_javascript", McpJson.Element(new { code = "return 1", path = "a.js" }))).IsError, "Ambiguous code and path were accepted.");
        using var session = new AgentSession(new ScriptedChatClient(), registry, new() { PermissionMode = AgentPermissionMode.ReadOnly }, files);
        var denied = await session.ExecuteToolAsync("workspace_run_javascript", McpJson.Element(new { code = "return 1" }));
        Assert(denied.IsError, "Read-only agent permission allowed browser code execution.");
        return checks;
    }
    private static AgentCodeResult Result(IReadOnlyList<AgentCodeChange> changes) => new(0, "ok\n", "", false, false, 1, McpJson.Element(42), changes);
    private sealed class Runner : IAgentCodeRunner
    {
        public Func<AgentCodeRequest, AgentCodeResult> Run { get; set; } = _ => Result([]);
        public ValueTask<AgentCodeResult> ExecuteAsync(AgentCodeRequest request, CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); return ValueTask.FromResult(Run(request)); }
    }
}
