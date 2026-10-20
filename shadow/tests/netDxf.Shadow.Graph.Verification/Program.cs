using System.Collections.Immutable;
using System.Text.Json;
using netDxf;
using netDxf.Entities;
using netDxf.Shadow;
using netDxf.Shadow.Editor;
using netDxf.Shadow.Geometry;
using netDxf.Shadow.Graph;
using netDxf.Shadow.Mcp;
using netDxf.Shadow.NetDxf;
using netDxf.Shadow.Studio;

var checks = 0;
void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); checks++; }
void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { checks++; return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
int Number(JsonElement value, string name) => value.GetProperty(name).GetInt32();
var document = new DxfDocument();
void Line(double x1, double y1, double x2, double y2) => document.Entities.Add(new Line(new Vector3(x1, y1, 0), new Vector3(x2, y2, 0)));
Line(0, 0, 10, 0); Line(10, 0, 10, 10); Line(10, 10, 0, 10); Line(0, 10, 0, 0);
Line(10, 10, 20, 10); Line(15, 5, 15, 15); Line(30, 30, 40, 30);
var source = new ShadowSession(document); var editor = new EditorSession(source);
var registry = ShadowMcpTools.Create(new(() => editor, () => source));
async Task<JsonElement> Data(string name, object? args = null)
{
    var result = await registry.ExecuteAsync(name, McpJson.Element(args ?? new { }));
    Check(!result.IsError, name + ": " + result.Text);
    return McpJson.Parse(result.Text).GetProperty("data");
}
async Task Error(string name, object args)
{
    var result = await registry.ExecuteAsync(name, McpJson.Element(args)); Check(result.IsError, name + " accepted invalid arguments.");
}
Check(registry.Tools.Count(t => t.Name.StartsWith("graph_", StringComparison.Ordinal) && t.ReadOnly) == 16, "Graph catalog incomplete.");
var summary = await Data("graph_summary");
Check(Number(summary, "nodeCount") == 10 && Number(summary, "edgeCount") == 9 && Number(summary, "componentCount") == 2, "Crossing/component counts incorrect.");
Check(Number(summary, "cycleRank") == 1 && Number(summary, "bridgeCount") == 5 && Number(summary, "articulationPointCount") == 2 && Number(summary, "deadEndCount") == 5, "Feature summary incorrect.");
Check(summary.GetProperty("totalLength").GetDouble() == 70, "Length summary incorrect.");
var nodes = await Data("graph_nodes", new { limit = 3 });
Check(Number(nodes, "total") == 10 && Number(nodes, "nextOffset") == 3, "Node pagination lost continuation.");
var second = await Data("graph_nodes", new { offset = 3, limit = 3 });
Check(Number(second.GetProperty("items")[0], "id") == 3, "Page IDs were renumbered.");
Check(Number(await Data("graph_nodes", new { minimumDegree = 4 }), "total") == 1, "Degree filter failed.");
var features = await Data("graph_features", new { kind = "articulationPoints", limit = 1 });
Check(Number(features, "total") == 2 && Number(features, "nextOffset") == 1, "Articulation pagination failed.");
Check(Number(await Data("graph_features", new { kind = "bridges" }), "total") == 5, "Bridge feature listing failed.");
var near = await Data("graph_nearest", new { x = 15, y = 10 });
var junction = Number(near.GetProperty("node"), "id");
Check(near.GetProperty("found").GetBoolean() && near.GetProperty("distance").GetDouble() == 0, "Nearest node incorrect.");
Check(!((await Data("graph_nearest", new { x = 999, y = 999, maximumDistance = 1 })).GetProperty("found").GetBoolean()), "Nearest maximum distance ignored.");
Check(Number(await Data("graph_edges", new { node = junction }), "total") == 4, "Node adjacency query incorrect.");
var edges = await Data("graph_edges", new { limit = 1 }); var firstEdge = edges.GetProperty("items")[0];
Check(firstEdge.GetProperty("entity").GetString()!.StartsWith("H:", StringComparison.Ordinal) && firstEdge.GetProperty("occurrence").GetString()!.Length > 0, "Source linkage omitted.");
Check(Number(await Data("graph_edges", new { entity = firstEdge.GetProperty("entity").GetString() }), "total") == 1, "Entity filter incorrect.");
var components = await Data("graph_components", new { limit = 1 }); var component = components.GetProperty("items")[0];
Check(Number(component, "nodeCount") == 8 && Number(component, "cycleRank") == 1, "Component analysis incorrect.");
Check(Number(await Data("graph_nodes", new { component = 1 }), "total") == 2, "Component member filter incorrect.");
var cyclePage = await Data("graph_cycles", new { limit = 1 });
Check(Number(cyclePage, "total") == 1 && Number(cyclePage.GetProperty("items")[0], "edgeCount") == 4, "Cycle basis did not recover the square.");
var cyclePath = await Data("graph_cycle", new { cycle = 0, limit = 2 });
Check(cyclePath.GetProperty("length").GetDouble() == 40 && Number(cyclePath, "nodeCount") == 5 && Number(cyclePath.GetProperty("steps"), "nextOffset") == 2, "Cycle geometry or pagination incorrect.");
var cycleEnd = (await Data("graph_cycle", new { cycle = 0, offset = 4 })).GetProperty("steps").GetProperty("items")[0];
Check(Number(cycleEnd.GetProperty("node"), "id") == Number(cyclePath.GetProperty("steps").GetProperty("items")[0].GetProperty("node"), "id") && cycleEnd.GetProperty("viaEdge").ValueKind == JsonValueKind.Object, "Cycle did not close with its incoming edge.");
var forestPage = await Data("graph_spanning_forest", new { limit = 2 });
Check(Number(forestPage, "edgeCount") == 8 && Number(forestPage, "componentCount") == 2 && forestPage.GetProperty("length").GetDouble() == 60 && Number(forestPage.GetProperty("steps"), "nextOffset") == 2, "Minimum spanning forest result incorrect.");
await Error("graph_cycle", new { cycle = 1 }); await Error("graph_cycle", new { cycle = -1 });
var start = Number((await Data("graph_nearest", new { x = 0, y = 0 })).GetProperty("node"), "id");
var end = Number((await Data("graph_nearest", new { x = 20, y = 10 })).GetProperty("node"), "id");
var path = await Data("graph_path", new { source = start, target = end, limit = 2 });
Check(path.GetProperty("reachable").GetBoolean() && path.GetProperty("length").GetDouble() == 30 && Number(path.GetProperty("steps"), "nextOffset") == 2, "Weighted path or pagination incorrect.");
var pathNext = await Data("graph_path", new { source = start, target = end, offset = 2, limit = 20 });
Check(pathNext.GetProperty("steps").GetProperty("items")[0].GetProperty("viaEdge").ValueKind == JsonValueKind.Object, "Path continuation lost predecessor edge.");
var samePath = await Data("graph_path", new { source = start, target = start });
Check(Number(samePath, "nodeCount") == 1 && Number(samePath, "edgeCount") == 0 && samePath.GetProperty("length").GetDouble() == 0, "Zero-length path incorrect.");
var disconnected = Number((await Data("graph_nearest", new { x = 30, y = 30 })).GetProperty("node"), "id");
Check(!(await Data("graph_path", new { source = start, target = disconnected })).GetProperty("reachable").GetBoolean(), "Disconnected path was invented.");
var traversal = await Data("graph_traverse", new { source = junction, limit = 2 });
Check(traversal.GetProperty("nodes").GetArrayLength() == 2 && traversal.GetProperty("nodeLimitReached").GetBoolean(), "Traversal node budget ignored.");
traversal = await Data("graph_traverse", new { source = junction, depth = 0 });
Check(traversal.GetProperty("nodes").GetArrayLength() == 1 && traversal.GetProperty("depthLimitReached").GetBoolean(), "Traversal depth budget ignored.");
traversal = await Data("graph_traverse", new { source = disconnected, limit = 2 });
Check(!traversal.GetProperty("truncated").GetBoolean(), "Exact traversal limit falsely reported truncation.");
Check(Number(await Data("graph_summary", new { splitIntersections = false }), "edgeCount") == 7, "Build options were ignored or cached incorrectly.");
Check(Number(await Data("graph_summary"), "edgeCount") == 9, "Default options were not restored.");
await Error("graph_summary", new { endpointTolerance = 0 }); await Error("graph_summary", new { curveTolerance = -1 });
await Error("graph_summary", new { maximumSegments = 1 }); await Error("graph_summary", new { maximumCandidatePairs = 1 });
await Error("graph_summary", new { layout = "missing" }); await Error("graph_summary", new { revision = source.Current.Revision + 1 });
await Error("graph_nodes", new { limit = 201 }); await Error("graph_nodes", new { component = 999 }); await Error("graph_path", new { source = 0, target = 999 });
await Error("graph_nodes", new { minimumDegree = 4, maximumDegree = 1 });
var before = editor.Snapshot; var history = editor.State.UndoCount;
await Data("graph_features", new { kind = "diagnostics" });
Check(ReferenceEquals(before, editor.Snapshot) && history == editor.State.UndoCount, "Read-only graph tools mutated history.");
editor.CreateEntity("netDxf.Entities.Line", new Dictionary<string, ShadowValue>() { ["StartPoint"] = ShadowValue.From(new Vec3(100, 100)), ["EndPoint"] = ShadowValue.From(new Vec3(110, 100)) }, "Model");
Check(Number(await Data("graph_summary"), "edgeCount") == 10, "Graph cache was stale after edit.");
editor.Undo(); Check(Number(await Data("graph_summary"), "edgeCount") == 9, "Graph cache was stale after undo.");
var live = editor; editor = new(new ShadowMemoryStore(source.Current));
editor.CreateEntity("netDxf.Entities.Line", new Dictionary<string, ShadowValue>() { ["StartPoint"] = ShadowValue.From(new Vec3(100, 100)), ["EndPoint"] = ShadowValue.From(new Vec3(110, 100)) }, "Model");
Check(Number(await Data("graph_summary"), "edgeCount") == 10 && document.Entities.Lines.Count() == 7, "Graph did not follow detached branch.");
editor = live; Check(Number(await Data("graph_summary"), "edgeCount") == 9, "Graph retained previous branch binding.");

TopologyGraph Graph(int count, params (int A, int B)[] pairs)
{
    var adjacent = Enumerable.Range(0, count).Select(_ => ImmutableArray.CreateBuilder<int>()).ToArray();
    var graphEdges = pairs.Select((p, i) => { adjacent[p.A].Add(i); adjacent[p.B].Add(i); return new GraphEdge(i, p.A, p.B, 1, new("E:" + i), "E:" + i); }).ToImmutableArray();
    return new(Guid.NewGuid(), 0, Enumerable.Range(0, count).Select(i => new GraphNode(i, new(i, 0), adjacent[i].ToImmutable())).ToImmutableArray(), graphEdges, []);
}
int ComponentsWithout(TopologyGraph graph, int removed)
{
    var seen = new HashSet<int> { removed }; var count = 0;
    foreach (var node in graph.Nodes)
    {
        if (!seen.Add(node.Id)) continue; count++; var queue = new Queue<int>(); queue.Enqueue(node.Id);
        while (queue.TryDequeue(out var n)) foreach (var id in graph.Nodes[n].Edges)
        { var e = graph.Edges[id]; var other = e.A == n ? e.B : e.A; if (seen.Add(other)) queue.Enqueue(other); }
    }
    return count;
}
var random = new Random(1031);
for (var attempt = 0; attempt < 100; attempt++)
{
    var graph = Graph(12, Enumerable.Range(0, 18).Select(_ => (random.Next(12), random.Next(12))).ToArray());
    var count = graph.ConnectedComponents().Length;
    var expected = Enumerable.Range(0, 12).Where(n => ComponentsWithout(graph, n) > count);
    Check(graph.ArticulationPoints().SequenceEqual(expected), "Articulation points differ from independent removal oracle.");
    var basis = graph.CycleBasis();
    Check(basis.Cycles.Length == graph.Edges.Length - graph.Nodes.Length + count, "Fundamental basis rank is incorrect.");
    var pivots = new Dictionary<int, ulong>();
    foreach (var descriptor in basis.Cycles)
    {
        var cycle = basis.Path(descriptor.Id);
        Check(cycle.Nodes[0] == cycle.Nodes[^1] && cycle.Nodes.Length == cycle.Edges.Length + 1 && cycle.Edges.Length == descriptor.EdgeCount, "Invalid closed cycle dimensions.");
        Check(cycle.Nodes.SkipLast(1).Distinct().Count() == cycle.Nodes.Length - 1 && cycle.Edges.Distinct().Count() == cycle.Edges.Length, "Cycle repeats an interior node or edge.");
        ulong vector = 0;
        for (var i = 0; i < cycle.Edges.Length; i++)
        {
            var e = graph.Edges[cycle.Edges[i]]; var a = cycle.Nodes[i]; var b = cycle.Nodes[i + 1];
            Check(e.A == a && e.B == b || e.A == b && e.B == a, "Cycle contains a nonadjacent edge.");
            vector ^= 1UL << e.Id;
        }
        while (vector != 0)
        {
            var pivot = System.Numerics.BitOperations.Log2(vector);
            if (pivots.TryGetValue(pivot, out var previous)) vector ^= previous;
            else { pivots.Add(pivot, vector); break; }
        }
        Check(vector != 0, "Cycle basis is linearly dependent over GF(2).");
    }
}
var parallel = Graph(3, (0, 1), (0, 1), (1, 2), (1, 1));
Check(parallel.Bridges().SequenceEqual([2]) && parallel.ArticulationPoints().SequenceEqual([1]), "Parallel edges/self loops broke cut analysis.");
Check(parallel.CycleBasis().Cycles.Select(c => c.EdgeCount).SequenceEqual([2, 1]), "Parallel edges/self loops lost their independent cycles.");
Check(parallel.MinimumSpanningForest().Edges.SequenceEqual([0, 2]), "Minimum forest tie order or self-loop exclusion is incorrect.");
Throws<ArgumentOutOfRangeException>(() => parallel.CycleBasis().Path(-1));
Throws<ArgumentOutOfRangeException>(() => parallel.CycleBasis().Path(2));
for (var attempt = 0; attempt < 30; attempt++)
{
    var g = Graph(5, Enumerable.Range(0, 8).Select(_ => (random.Next(5), random.Next(5))).ToArray());
    g = g with { Edges = g.Edges.Select(e => e with { Length = random.Next(1, 20) }).ToImmutableArray() };
    var componentsCount = g.ConnectedComponents().Length; var minimum = double.PositiveInfinity;
    for (var mask = 0; mask < 1 << g.Edges.Length; mask++)
    {
        if (System.Numerics.BitOperations.PopCount((uint)mask) != g.Nodes.Length - componentsCount) continue;
        var subset = g.Edges.Where(e => (mask & 1 << e.Id) != 0).ToArray();
        var subgraph = Graph(5, subset.Select(e => (e.A, e.B)).ToArray());
        if (subgraph.ConnectedComponents().Length == componentsCount) minimum = Math.Min(minimum, subset.Sum(e => e.Length));
    }
    var forest = g.MinimumSpanningForest();
    Check(forest.ComponentCount == componentsCount && forest.Length == minimum && forest.Edges.Length == 5 - componentsCount, "Minimum spanning forest differs from exhaustive subset oracle.");
}
var deep = Graph(20000, Enumerable.Range(0, 19999).Select(i => (i, i + 1)).ToArray());
Check(deep.ArticulationPoints().Length == 19998 && deep.Bridges().Length == 19999, "Iterative deep graph traversal failed.");
var deepCycle = Graph(20000, Enumerable.Range(0, 20000).Select(i => (i, (i + 1) % 20000)).ToArray()).CycleBasis();
Check(deepCycle.Cycles.Length == 1 && deepCycle.Path(0).Edges.Length == 20000, "Deep cycle expansion is not stack safe.");
Check(deep.CycleBasis().Cycles.IsEmpty && deep.MinimumSpanningForest().Edges.Length == 19999, "Deep tree forest/basis is incorrect.");
using (var cancellation = new CancellationTokenSource())
{
    cancellation.Cancel(); var token = cancellation.Token;
    Throws<OperationCanceledException>(() => deep.ArticulationPoints(token)); Throws<OperationCanceledException>(() => deep.Bridges(token));
    Throws<OperationCanceledException>(() => deep.ConnectedComponents(token)); Throws<OperationCanceledException>(() => deep.ShortestPath(0, 3, token));
    Throws<OperationCanceledException>(() => deep.Traverse(0, cancellationToken: token));
    Throws<OperationCanceledException>(() => deep.CycleBasis(token)); Throws<OperationCanceledException>(() => deep.MinimumSpanningForest(token));
    Throws<OperationCanceledException>(() => deepCycle.Path(0, token));
}
var occurrence = new CadOccurrence("presentation", new("E:1"), [], CadStyle.Default, new([new([new LineCurve(Vec3.Zero, Vec3.UnitX)])])) { IsPresentationOnly = true };
Check(new TopologyBuilder().Build(new CadScene(Guid.NewGuid(), 0, "Model", [occurrence], [])).Edges.IsEmpty, "Presentation-only geometry leaked into topology.");

using var studio = new Studio(false);
JsonElement StudioData(string tool, object? arguments = null)
{
    var response = McpJson.Element(studio.GraphQuery(new(tool, McpJson.Element(arguments ?? new { }))));
    Check(!response.GetProperty("isError").GetBoolean(), "Studio graph query failed: " + response);
    return response.GetProperty("result").GetProperty("data");
}
Check(Number(StudioData("graph_summary"), "edgeCount") == 0, "Empty Studio graph was not empty.");
Check(Number(StudioData("graph_cycles"), "total") == 0 && Number(StudioData("graph_spanning_forest"), "componentCount") == 0, "Empty graph cycle/forest result is incorrect.");
studio.Add(new(0, "line", 0, 0));
Check(Number(StudioData("graph_summary"), "edgeCount") == 1, "Studio graph query did not follow source.");
var studioState = McpJson.Element(studio.State()); var revision = studioState.GetProperty("revision").GetInt64();
Check(Number(McpJson.Element(studio.Graph(default)), "edgeCount") == 1, "Studio overlay and graph analysis disagree.");
studio.Fork(new(revision, "Topology branch")); studio.Add(new(revision, "line", 0, 100));
Check(Number(StudioData("graph_summary"), "edgeCount") == 2, "Studio graph did not follow branch.");
var branchState = McpJson.Element(studio.State()); studio.SwitchWorkspace(new(branchState.GetProperty("revision").GetInt64(), "live"));
Check(Number(StudioData("graph_summary"), "edgeCount") == 1, "Studio graph branch cache leaked.");
Throws<ArgumentException>(() => studio.GraphQuery(new("shadow_undo", McpJson.EmptyObject)));
Check(McpJson.Element(studio.GraphQuery(new("graph_summary", McpJson.Element(new { revision = 999 })))).GetProperty("isError").GetBoolean(), "Studio graph endpoint ignored stale revision.");
checks += await GraphVisualizationVerification.RunAsync();
checks += await ReferenceGraphVerification.RunAsync();
checks += await ReferenceAnalysisVerification.RunAsync();
Console.WriteLine($"Graph verification passed: {checks} checks covering algorithms, pagination, options, source linkage, budgets, cancellation, branch isolation and Studio integration.");
