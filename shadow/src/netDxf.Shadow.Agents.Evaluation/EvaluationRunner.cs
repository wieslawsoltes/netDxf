using System.Diagnostics;
using System.Text.Json;
using netDxf.Shadow.Editor;
using netDxf.Shadow.Mcp;
using netDxf.Shadow.NetDxf;

namespace netDxf.Shadow.Agents.Evaluation;

/// <summary>Runs a frozen task against an injected target with a fresh drawing, tools and workspace on every invocation.</summary>
public sealed class EvaluationRunner
{
    public async Task<EvaluationResult> RunAsync(EvaluationFixture fixture, IAgentEvaluationTarget target,
        int repeat = 1, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fixture); ArgumentNullException.ThrowIfNull(target);
        if (repeat < 1 || target.Target is not { Id.Length: > 0, Protocol.Length: > 0, Provider.Length: > 0, Model.Length: > 0, CacheMode.Length: > 0 })
            throw new ArgumentException("Explicit target, provider, protocol, model and cache mode are required.");
        var drawing = fixture.CreateDrawing(); var source = new ShadowSession(drawing); var editor = new EditorSession(source);
        var workspace = new MemoryAgentWorkspace(); var registry = ShadowMcpTools.Create(new(() => editor, () => source));
        AgentWorkspaceTools.Register(registry, workspace);
        var measured = new MeasuredTools(registry);
        var catalogHash = EvaluationFixture.Hash(JsonSerializer.SerializeToUtf8Bytes(registry.Tools.OrderBy(t => t.Name, StringComparer.Ordinal), McpJson.Options));
        var watch = Stopwatch.StartNew(); EvaluationExecution execution;
        try { execution = await target.RunAsync(new(fixture, drawing, editor, workspace, measured), cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) { execution = new("cancelled"); }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException and not AccessViolationException)
        { execution = new("error"); } // Exception messages can contain credentials; targets may expose sanitized events explicitly.
        watch.Stop();
        var checks = new List<EvaluationCheck>();
        try { checks.AddRange(fixture.Scenario.Validate(drawing, workspace)); }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException and not AccessViolationException)
        { checks.Add(new("native grader completed", false)); }
        checks.Add(new("agent completed within budgets", execution.Status == "completed"));
        var calls = measured.Snapshot();
        bool Called(string name) => calls.Any(c => c.Name == name && !c.IsError);
        checks.Add(new("used actual MCP drawing mutations", Called("shadow_create_entities") || Called("shadow_graph_edit") || Called("shadow_edit")));
        if (fixture.Scenario.Id == "graph") checks.Add(new("used graph summary and shortest path", Called("graph_summary") && Called("graph_path")));
        if (fixture.Scenario.Id == "edit") checks.Add(new("used bulk property editing", Called("shadow_edit")));
        checks.Add(new("at most three coherent undo transactions", editor.State.UndoCount is > 0 and <= 3));
        byte[]? finalDrawing = null;
        try
        {
            using var output = new MemoryStream(); var saved = drawing.Save(output); finalDrawing = saved ? output.ToArray() : null; output.Position = 0;
            checks.Add(new("DXF round trip preserves requested geometry", saved && fixture.Scenario.Validate(DxfDocument.Load(output), workspace).All(c => c.Passed)));
        }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException and not AccessViolationException)
        { checks.Add(new("DXF round trip preserves requested geometry", false)); }
        return new(1, fixture.Scenario.Id, fixture.Scenario.Description, fixture.Fingerprint, fixture.SeedHash,
            fixture.Scenario.GraderVersion, execution.ToolCatalogHash ?? catalogHash, target.Target, repeat, execution.Status, checks.All(c => c.Passed), watch.Elapsed.TotalMilliseconds,
            calls.Count, calls.Count(c => c.IsError), calls.Sum(c => c.Milliseconds), editor.State.UndoCount,
            execution.Telemetry ?? new(), execution.Cost, checks, calls, execution.Events ?? [], execution.Checkpoint) { DrawingDxf = finalDrawing };
    }
    private sealed class MeasuredTools(IMcpToolExecutor inner) : IMcpToolExecutor
    {
        private readonly List<EvaluationToolCall> calls = [];
        public IReadOnlyList<McpToolDefinition> Tools => inner.Tools;
        public IReadOnlyList<EvaluationToolCall> Snapshot() { lock (calls) return calls.ToArray(); }
        public async ValueTask<McpToolResult> ExecuteAsync(string name, JsonElement args, CancellationToken cancellationToken = default)
        {
            var watch = Stopwatch.StartNew(); var failed = true;
            try { var result = await inner.ExecuteAsync(name, args, cancellationToken).ConfigureAwait(false); failed = result.IsError; return result; }
            finally { lock (calls) calls.Add(new(name, failed, watch.Elapsed.TotalMilliseconds)); }
        }
    }
}
