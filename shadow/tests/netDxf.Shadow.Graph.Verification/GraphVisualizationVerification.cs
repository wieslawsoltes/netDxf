using System.Collections.Immutable;
using System.Text.Json;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Shadow;
using netDxf.Shadow.Editor;
using netDxf.Shadow.Graph;
using netDxf.Shadow.Mcp;
using netDxf.Shadow.NetDxf;
using netDxf.Shadow.Studio;

internal static class GraphVisualizationVerification
{
    public static async Task<int> RunAsync()
    {
        var checks = 0;
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException("Graph visualization: " + message); checks++; }
        void Throws<T>(Action action, string message) where T : Exception
        { try { action(); } catch (T) { checks++; return; } throw new InvalidOperationException("Graph visualization expected " + typeof(T).Name + ": " + message); }
        var pairs = new[] { (0, 1), (1, 2), (2, 3), (3, 0), (2, 4), (5, 6), (6, 6) };
        var adjacency = Enumerable.Range(0, 8).Select(_ => new List<int>()).ToArray();
        var edges = pairs.Select((p, i) =>
        {
            adjacency[p.Item1].Add(i); adjacency[p.Item2].Add(i);
            return new GraphEdge(i, p.Item1, p.Item2, 1, new("entity:" + i), "occurrence:" + i);
        }).ToImmutableArray();
        var graph = new TopologyGraph(Guid.NewGuid(), 7,
            adjacency.Select((a, i) => new GraphNode(i, new(i, i * 2, i * 3), a.ToImmutableArray())).ToImmutableArray(), edges, []);
        var whole = graph.Visualize();
        Check(whole.NodeCount == 8 && whole.EdgeCount == 7 && whole.ComponentCount == 3 && whole.CycleRank == 2, "full graph counts preserve isolated nodes and self loops");
        Check(!whole.Truncated && whole.Nodes.Items.Length == 8 && whole.Edges.Items.Length == 7, "full page is complete");
        Check(whole.BridgeCount == graph.Bridges().Length && whole.ArticulationPointCount == graph.ArticulationPoints().Length, "existing feature algorithms drive annotations");
        Check(whole.Nodes.Items.Where(n => n.ArticulationPoint).Select(n => n.Id).SequenceEqual(graph.ArticulationPoints()), "articulation IDs match analysis");
        Check(whole.Edges.Items.Where(e => e.Bridge).Select(e => e.Id).Order().SequenceEqual(graph.Bridges().Order()), "bridge IDs match analysis");
        Check(whole.Nodes.Items.Single(n => n.Id == 7).Isolated && whole.IsolatedNodeCount == 1, "isolated node remains visualizable");
        Check(whole.Nodes.Items.Single(n => n.Id == 6).Junction, "self-loop contributes both incidence entries to degree");
        var first = graph.Visualize(new() { NodeLimit = 2, EdgeLimit = 1 });
        Check(first.Truncated && first.Nodes.NextOffset == 2 && first.Edges.NextOffset == 1 && first.Nodes.Total == 8 && first.Edges.Total == 7, "independent page continuation and full counts");
        var page = graph.Visualize(new() { NodeOffset = 7, NodeLimit = 1, EdgeOffset = 4, EdgeLimit = 1 });
        Check(page.Nodes.Items[0].Id == 7 && page.Edges.Items[0].Id == 4 && page.Edges.Items[0].Start == graph.Nodes[2].Position
            && page.Edges.Items[0].End == graph.Nodes[4].Position, "edge endpoint geometry is complete even outside the node page");
        Check(page.Nodes.NextOffset is null && page.Edges.NextOffset == 5, "node and edge page completion are independent");
        var beyond = graph.Visualize(new() { NodeOffset = int.MaxValue, EdgeOffset = int.MaxValue });
        Check(beyond.Nodes.Items.IsEmpty && beyond.Edges.Items.IsEmpty && beyond.Nodes.NextOffset is null && beyond.Edges.NextOffset is null && beyond.Truncated, "offset overflow cannot invent continuation");
        var connected = graph.Visualize(new() { Component = 1 });
        Check(connected.Nodes.Total == 2 && connected.Edges.Total == 2 && connected.Nodes.Items.All(n => n.Component == 1)
            && connected.EdgeCount == 7 && !connected.Truncated, "component filtering preserves global counts and classifications");
        var entity = graph.Visualize(new() { Entity = new("entity:4") });
        Check(entity.Nodes.Total == 2 && entity.Edges.Total == 1 && entity.Nodes.Items.Single(n => n.Id == 2).Degree == 3, "source-filtered nodes retain global topology degree");
        Check(graph.Visualize(new() { Occurrence = "absent" }).Nodes.Total == 0, "missing occurrence produces empty filtered geometry");
        foreach (var invalid in new GraphVisualizationOptions[] { new() { NodeLimit = 0 }, new() { EdgeLimit = 10001 }, new() { NodeOffset = -1 }, new() { EdgeOffset = -1 }, new() { Component = -1 }, new() { Occurrence = new('x', 8193) } })
            Throws<ArgumentOutOfRangeException>(() => graph.Visualize(invalid), "invalid resource budget");
        Throws<ArgumentException>(() => graph.Visualize(new() { Component = 3 }), "unknown component");
        using (var cancelled = new CancellationTokenSource()) { cancelled.Cancel(); Throws<OperationCanceledException>(() => graph.Visualize(cancellationToken: cancelled.Token), "cached projection honors cancellation"); }
        var empty = new TopologyGraph(Guid.NewGuid(), 0, [], [], []).Visualize();
        Check(empty.NodeCount == 0 && empty.ComponentCount == 0 && empty.Nodes.Total == 0 && !empty.Truncated, "empty graph is serializable and complete");
        Check(McpJson.Element(empty).GetProperty("totalLength").GetDouble() == 0, "empty visualization has finite numeric metadata");

        var leaf = new Line(Vector3.Zero, new Vector3(10, 0, 0));
        var inner = new Block("GraphInner"); inner.Entities.Add(leaf);
        var nested = new Insert(inner, new Vector3(5, 0, 0));
        var outer = new Block("GraphOuter"); outer.Entities.Add(nested);
        var insertA = new Insert(outer, new Vector3(0, 0, 0));
        var insertB = new Insert(outer, new Vector3(100, 0, 0));
        var document = new DxfDocument(); document.Entities.Add(insertA); document.Entities.Add(insertB);
        var source = new ShadowSession(document); var editor = new EditorSession(source);
        var workspace = "live"; long view = 0;
        var registry = ShadowMcpTools.Create(new(() => editor, () => source)
        { Identity = () => new(editor.Snapshot.DocumentId, workspace, view), RequireWorkspaceIdentity = true });
        async Task<JsonElement> Invoke(string name, object? arguments = null)
        {
            var result = await registry.ExecuteAsync(name, McpJson.Element(arguments ?? new { }));
            Check(!result.IsError, name + " failed: " + result.Text); return McpJson.Parse(result.Text);
        }
        async Task Error(string name, object arguments)
        { Check((await registry.ExecuteAsync(name, McpJson.Element(arguments))).IsError, name + " accepted stale or invalid input"); }
        var projectionReceipt = await Invoke("graph_visualization"); var projection = projectionReceipt.GetProperty("data");
        var projected = projection.GetProperty("edges").GetProperty("items").EnumerateArray().ToArray();
        Check(projected.Length == 2 && projected[0].GetProperty("entity").GetString() == projected[1].GetProperty("entity").GetString(), "repeated block definition retains shared leaf identity");
        Check(projected[0].GetProperty("rootEntity").GetString() != projected[1].GetProperty("rootEntity").GetString()
            && projected[0].GetProperty("occurrence").GetString() != projected[1].GetProperty("occurrence").GetString(), "repeated INSERT occurrences have distinct selectable roots");
        Check(projected.All(e => e.GetProperty("insertPath").GetArrayLength() == 2 && e.GetProperty("rootEntity").GetString() == e.GetProperty("insertPath")[0].GetString()), "nested INSERT source path preserves outer-to-inner order");
        Check(projected[0].GetProperty("start").GetProperty("x").GetDouble() == 5 && projected[1].GetProperty("start").GetProperty("x").GetDouble() == 105, "occurrence coordinates include source transforms");
        var key = projection.GetProperty("graphKey").GetString()!;
        var rootA = projected[0].GetProperty("rootEntity").GetString();
        var rootPage = (await Invoke("graph_visualization", new { graphKey = key, rootEntity = rootA })).GetProperty("data");
        Check(rootPage.GetProperty("nodes").GetProperty("total").GetInt32() == 2 && rootPage.GetProperty("edges").GetProperty("total").GetInt32() == 1, "root selection filters only its occurrence topology");
        var summary = (await Invoke("graph_summary", new { graphKey = key })).GetProperty("data");
        Check(summary.GetProperty("graphKey").GetString() == key, "all graph operations share the same build key");
        var graphEdges = (await Invoke("graph_edges", new { graphKey = key, rootEntity = rootA })).GetProperty("data");
        Check(graphEdges.GetProperty("items")[0].GetProperty("rootEntity").GetString() == rootA && graphEdges.GetProperty("items")[0].GetProperty("insertPath").GetArrayLength() == 2, "analysis results carry selectable source identity too");
        var originalHistory = editor.State.UndoCount; var originalSnapshot = editor.Snapshot;
        foreach (var (tool, arguments) in new (string, object)[]
        {
            ("graph_nodes", new { graphKey = key }), ("graph_components", new { graphKey = key }),
            ("graph_features", new { graphKey = key, kind = "bridges" }), ("graph_nearest", new { graphKey = key, x = 0, y = 0 }),
            ("graph_path", new { graphKey = key, source = 0, target = 1 }), ("graph_traverse", new { graphKey = key, source = 0 }),
            ("graph_cycles", new { graphKey = key }), ("graph_spanning_forest", new { graphKey = key })
        }) Check((await Invoke(tool, arguments)).GetProperty("data").GetProperty("graphKey").GetString() == key, tool + " receipt remains visualization-compatible");
        Check(editor.State.UndoCount == originalHistory && ReferenceEquals(editor.Snapshot, originalSnapshot), "projection and visual analyses are read-only");
        await Error("graph_visualization", new { nodeLimit = 5001 }); await Error("graph_visualization", new { edgeLimit = 10001 });
        await Error("graph_path", new { graphKey = "wrong", source = 0, target = 1 });
        await Error("graph_summary", new { graphKey = key, endpointTolerance = .02 });
        key = (await Invoke("graph_visualization")).GetProperty("data").GetProperty("graphKey").GetString()!;
        view++; await Error("graph_nodes", new { graphKey = key });
        key = (await Invoke("graph_visualization")).GetProperty("data").GetProperty("graphKey").GetString()!;
        workspace = "branch:fixture"; await Error("graph_visualization", new { graphKey = key });
        await Error("graph_visualization", new { workspaceId = "live" });
        key = (await Invoke("graph_visualization")).GetProperty("data").GetProperty("graphKey").GetString()!;
        editor.CreateEntity("netDxf.Entities.Line", new Dictionary<string, ShadowValue> { ["StartPoint"] = ShadowValue.From(new Vec3(200, 0)), ["EndPoint"] = ShadowValue.From(new Vec3(210, 0)) }, "Model");
        await Error("graph_visualization", new { graphKey = key });
        await Error("graph_visualization", new { revision = projectionReceipt.GetProperty("revision").GetInt64() });
        using var studio = new Studio(false);
        studio.Add(new(0, "line", 0, 0));
        var studioProjection = McpJson.Element(studio.GraphQuery(new("graph_visualization", McpJson.EmptyObject)));
        Check(!studioProjection.GetProperty("isError").GetBoolean() && studioProjection.GetProperty("result").GetProperty("data").GetProperty("edges").GetProperty("total").GetInt32() == 1,
            "shared Studio graph/query exposes projection without separate backend implementation");
        var studioKey = studioProjection.GetProperty("result").GetProperty("data").GetProperty("graphKey").GetString();
        var revision = McpJson.Element(studio.State()).GetProperty("revision").GetInt64();
        studio.Fork(new(revision, "Projection branch"));
        Check(McpJson.Element(studio.GraphQuery(new("graph_visualization", McpJson.Element(new { graphKey = studioKey })))).GetProperty("isError").GetBoolean(), "Studio branch changes invalidate visualization IDs");
        Console.WriteLine($"Graph visualization verification passed: {checks} checks.");
        return checks;
    }
}
