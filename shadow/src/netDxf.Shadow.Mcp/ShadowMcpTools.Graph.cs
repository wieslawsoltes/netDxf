using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using netDxf.Shadow.Graph;

namespace netDxf.Shadow.Mcp;

public sealed partial class ShadowMcpTools
{
    private ShadowSnapshot? topologySnapshot;
    private string? topologyLayout;
    private TopologyOptions? topologyOptions;
    private TopologyGraph? topology;
    private sealed record GraphBuildKey(string Value);
    private static readonly ConditionalWeakTable<TopologyGraph, GraphBuildKey> graphBuildKeys = new();

    private void InstallGraph(McpToolRegistry registry)
    {
        GraphTool("graph_visualization", "Get bounded node and edge geometry pages with global component/feature annotations and exact source roots, definition entities and INSERT occurrences. Edge endpoints are embedded independently of node pagination. Page totals describe filters; summary counts describe the entire graph. Reuse graphKey and identity/options for every continuation or analysis highlight; refresh after a stale-key error.",
            Fields(("nodeOffset", Integer(0)), ("nodeLimit", Integer(1, 5000)), ("edgeOffset", Integer(0)), ("edgeLimit", Integer(1, 10000)),
                ("component", Integer(0)), ("entity", String()), ("rootEntity", String()), ("occurrence", String(8192))), [],
            (g, a, ct) => g.Visualize(new()
            {
                NodeOffset = GetInt(a, "nodeOffset", 0), NodeLimit = GetInt(a, "nodeLimit", 2000),
                EdgeOffset = GetInt(a, "edgeOffset", 0), EdgeLimit = GetInt(a, "edgeLimit", 4000), Component = OptionalInt(a, "component"),
                Entity = GetString(a, "entity") is { } entity ? new ObjectId(entity) : null,
                RootEntity = GetString(a, "rootEntity") is { } root ? new ObjectId(root) : null, Occurrence = GetString(a, "occurrence")
            }, ct));
        GraphTool("graph_summary", "Analyze semantic drawing topology: connected components, independent cycle rank, bridges, articulation points, dead ends, junctions and total sampled length. Graph IDs are local to this receipt, layout and options; reuse those options and revision for follow-up queries.", [], [], (g, a, ct) =>
        {
            var components = g.ConnectedComponents(ct);
            return new { nodeCount = g.Nodes.Length, edgeCount = g.Edges.Length, componentCount = components.Length,
                cycleRank = g.Edges.Length - g.Nodes.Length + components.Length, bridgeCount = g.Bridges(ct).Length,
                articulationPointCount = g.ArticulationPoints(ct).Length, deadEndCount = g.DeadEnds.Count(), junctionCount = g.Junctions.Count(),
                isolatedNodeCount = g.Nodes.Count(n => n.Edges.IsEmpty), totalLength = g.Edges.Sum(e => e.Length), diagnosticCount = g.Diagnostics.Length };
        });
        GraphTool("graph_nodes", "Page through topology node coordinates and degrees. Filter by node ID, component ID or degree. Use graph_edges with node to inspect adjacency, graph_nearest for coordinates and graph_features for special nodes.",
            Paging(("node", Integer(0)), ("component", Integer(0)), ("minimumDegree", Integer(0)), ("maximumDegree", Integer(0))), [], (g, a, ct) =>
            {
                var node = OptionalInt(a, "node"); if (node is { } n) CheckNode(g, n);
                var members = ComponentMembers(g, a, ct); var minimum = GetInt(a, "minimumDegree", 0); var maximum = GetInt(a, "maximumDegree", int.MaxValue);
                if (minimum > maximum) throw new ArgumentException("minimumDegree must not exceed maximumDegree.");
                return GraphPage(g.Nodes.Where(n => (node is null || n.Id == node) && (members is null || members.Contains(n.Id)) && n.Edges.Length >= minimum && n.Edges.Length <= maximum), a, DescribeNode, ct);
            });
        GraphTool("graph_edges", "Page through topology edges, endpoint coordinates, sampled lengths, source entity IDs and occurrence keys. Filter by edge, adjacent node, component, source entity or exact occurrence. Parallel edges and self loops are retained.",
            Paging(("edge", Integer(0)), ("node", Integer(0)), ("component", Integer(0)), ("entity", String()), ("rootEntity", String()), ("occurrence", String(8192))), [], (g, a, ct) =>
            {
                var edge = OptionalInt(a, "edge"); if (edge is { } e && (uint)e >= (uint)g.Edges.Length) throw new ArgumentException("Unknown topology edge.");
                var node = OptionalInt(a, "node"); if (node is { } n) CheckNode(g, n);
                var members = ComponentMembers(g, a, ct); var entity = GetString(a, "entity"); var rootEntity = GetString(a, "rootEntity"); var occurrence = GetString(a, "occurrence");
                return GraphPage(g.Edges.Where(e => (edge is null || e.Id == edge) && (node is null || e.A == node || e.B == node) &&
                    (members is null || members.Contains(e.A)) && (entity is null || e.Entity.Value == entity) && (rootEntity is null || e.RootEntity.Value == rootEntity) && (occurrence is null || e.Occurrence == occurrence)), a, e => DescribeEdge(g, e), ct);
            });
        GraphTool("graph_components", "Page through connected component summaries, node/edge counts, lengths and cycle ranks. Component IDs follow deterministic node order; query graph_nodes or graph_edges with component to page through members.", Paging(), [], (g, a, ct) =>
        {
            var components = g.ConnectedComponents(ct); var membership = new int[g.Nodes.Length];
            for (var i = 0; i < components.Length; i++) foreach (var node in components[i]) membership[node] = i;
            var counts = new int[components.Length]; var lengths = new double[components.Length];
            foreach (var edge in g.Edges) { ct.ThrowIfCancellationRequested(); counts[membership[edge.A]]++; lengths[membership[edge.A]] += edge.Length; }
            return GraphPage(Enumerable.Range(0, components.Length), a, i => new { id = i, nodeCount = components[i].Length, edgeCount = counts[i], totalLength = lengths[i], cycleRank = counts[i] - components[i].Length + 1 }, ct);
        });
        GraphTool("graph_cycles", "Page an independent fundamental cycle basis. Each closing edge identifies one cycle, including self loops and parallel-edge loops. Expand an individual cycle with graph_cycle. This does not enumerate every simple cycle or compute a minimum cycle basis.", Paging(), [], (g, a, ct) =>
            GraphPage(g.CycleBasis(ct).Cycles, a, cycle => new { cycle.Id, cycle.ClosingEdge, cycle.Start, cycle.Ancestor, cycle.EdgeCount }, ct));
        GraphTool("graph_cycle", "Inspect one fundamental cycle as a paged ordered closed path with source entities and occurrences. The final node repeats the start; its incoming edge closes the loop. Cycle IDs come from graph_cycles with the same identity and topology options.",
            Paging(("cycle", Integer(0))), ["cycle"], (g, a, ct) =>
            {
                var cycle = a.GetProperty("cycle").GetInt32(); var path = g.CycleBasis(ct).Path(cycle, ct);
                return new { cycle, path.Length, nodeCount = path.Nodes.Length, edgeCount = path.Edges.Length,
                    steps = GraphPage(Enumerable.Range(0, path.Nodes.Length), a, i => new { index = i, node = DescribeNode(g.Nodes[path.Nodes[i]]), viaEdge = i == 0 ? null : DescribeEdge(g, g.Edges[path.Edges[i - 1]]) }, ct) };
            });
        GraphTool("graph_spanning_forest", "Find a minimum spanning forest weighted by sampled length and page its source-linked edges. Retains disconnected and isolated components, excludes self loops and breaks equal-weight ties by edge ID. Read-only analysis; no drawing edges are removed.", Paging(), [], (g, a, ct) =>
        {
            var forest = g.MinimumSpanningForest(ct);
            return new { forest.ComponentCount, forest.Length, edgeCount = forest.Edges.Length,
                steps = GraphPage(forest.Edges, a, id => DescribeEdge(g, g.Edges[id]), ct) };
        });
        GraphTool("graph_features", "Page through dead ends, junctions, isolated nodes, articulation points (cut vertices), bridge edges, or topology/geometry diagnostics. Bridges identify single-edge disconnections; cycleRank is the number of independent cycles, not a list of simple cycles.",
            Paging(("kind", Enum("deadEnds", "junctions", "isolated", "bridges", "articulationPoints", "diagnostics"))), ["kind"], (g, a, ct) => RequiredString(a, "kind") switch
            {
                "bridges" => GraphPage(g.Bridges(ct).Order(), a, id => DescribeEdge(g, g.Edges[id]), ct),
                "articulationPoints" => GraphPage(g.ArticulationPoints(ct), a, id => DescribeNode(g.Nodes[id]), ct),
                "deadEnds" => GraphPage(g.DeadEnds, a, DescribeNode, ct),
                "junctions" => GraphPage(g.Junctions, a, DescribeNode, ct),
                "isolated" => GraphPage(g.Nodes.Where(n => n.Edges.IsEmpty), a, DescribeNode, ct),
                "diagnostics" => GraphPage(g.Diagnostics, a, d => new { d.Code, d.Message, entity = d.Object?.Value, severity = d.Severity.ToString() }, ct),
                _ => throw new ArgumentException("Unknown graph feature.")
            });
        GraphTool("graph_nearest", "Find the nearest topology node to a drawing coordinate; optionally limit the distance. Uses XYZ when respectElevation is true and XY otherwise. Ties choose the lowest node ID. This finds nodes, not nearest points on edges.",
            Fields(("x", GraphNumber()), ("y", GraphNumber()), ("z", GraphNumber()), ("maximumDistance", GraphNumber(0))), ["x", "y"], (g, a, ct) =>
            {
                var point = new Vec3(a.GetProperty("x").GetDouble(), a.GetProperty("y").GetDouble(), GraphDouble(a, "z", 0));
                var elevation = !a.TryGetProperty("respectElevation", out var respect) || respect.GetBoolean();
                var distance = GraphDouble(a, "maximumDistance", double.MaxValue); GraphNode? nearest = null;
                foreach (var node in g.Nodes)
                {
                    ct.ThrowIfCancellationRequested();
                    var delta = node.Position - point; var candidate = (elevation ? delta : delta with { Z = 0 }).Length;
                    if (candidate <= distance && (nearest is null || candidate < distance)) { distance = candidate; nearest = node; }
                }
                return new { found = nearest is not null, distance = nearest is null ? (double?)null : distance, node = nearest is null ? null : DescribeNode(nearest) };
            });
        GraphTool("graph_path", "Find the shortest undirected path weighted by sampled drawing length. Returns reachable=false for disconnected nodes. Page the ordered path nodes and incoming edges using offset/limit; complete path length and step count remain available on every page.",
            Paging(("source", Integer(0)), ("target", Integer(0))), ["source", "target"], (g, a, ct) =>
            {
                var path = g.ShortestPath(a.GetProperty("source").GetInt32(), a.GetProperty("target").GetInt32(), ct);
                return new { reachable = path is not null, length = path?.Length, nodeCount = path?.Nodes.Length ?? 0, edgeCount = path?.Edges.Length ?? 0,
                    steps = GraphPage(Enumerable.Range(0, path?.Nodes.Length ?? 0), a, i => new { index = i, node = DescribeNode(g.Nodes[path!.Nodes[i]]), viaEdge = i == 0 ? null : DescribeEdge(g, g.Edges[path.Edges[i - 1]]) }, ct) };
            });
        GraphTool("graph_traverse", "Traverse topology breadth-first from a node with explicit hop-depth and node budgets. Returns predecessor edges with source entity/occurrence identity and independent nodeLimitReached/depthLimitReached flags. No graph or drawing state is changed.",
            Fields(("source", Integer(0)), ("depth", Integer(0, 128)), ("limit", Integer(1, 200))), ["source"], (g, a, ct) =>
            {
                var result = g.Traverse(a.GetProperty("source").GetInt32(), GetInt(a, "depth", 8), GetInt(a, "limit", 100), ct);
                return new { nodes = result.Visits.Select(v => new { node = DescribeNode(g.Nodes[v.Node]), v.Depth, viaEdge = v.ViaEdge is { } edge ? DescribeEdge(g, g.Edges[edge]) : null }).ToArray(),
                    result.NodeLimitReached, result.DepthLimitReached, truncated = result.NodeLimitReached || result.DepthLimitReached };
            });

        void GraphTool(string name, string description, Dictionary<string, object> fields, string[] required, Func<TopologyGraph, JsonElement, CancellationToken, object> operation)
        {
            fields.Add("graphKey", String(128));
            fields.Add("layout", String()); fields.Add("endpointTolerance", GraphNumber(0, true)); fields.Add("curveTolerance", GraphNumber(0, true));
            fields.Add("splitIntersections", Boolean()); fields.Add("respectElevation", Boolean());
            fields.Add("maximumSegments", Integer(1, 250000)); fields.Add("maximumCandidatePairs", Integer(1, 20000000));
            Add(registry, name, description + " Curves are approximated using curveTolerance; endpointTolerance controls merging. Invisible and presentation-only geometry is excluded. Budget exhaustion is an error, never a partial graph.", true, fields, required,
                (args, _, snapshot, ct) =>
                {
                    var layout = GetString(args, "layout") ?? context.Layout?.Invoke() ?? "Model";
                    var options = new TopologyOptions(GraphDouble(args, "endpointTolerance", .01), GraphDouble(args, "curveTolerance", .05),
                        !args.TryGetProperty("splitIntersections", out var split) || split.GetBoolean(), !args.TryGetProperty("respectElevation", out var elevation) || elevation.GetBoolean(),
                        GetInt(args, "maximumSegments", 100000), GetInt(args, "maximumCandidatePairs", 2000000));
                    TopologyGraph graph;
                    if (context.Topology is { } builder) graph = builder(layout, options, ct);
                    else
                    {
                        if (!snapshot.Layout(layout).Exists) throw new ArgumentException("Unknown topology layout.");
                        if (!ReferenceEquals(snapshot, topologySnapshot) || layout != topologyLayout || options != topologyOptions)
                        {
                            graph = new TopologyBuilder().Build(snapshot, layout, options, ct);
                            topology = graph; topologySnapshot = snapshot; topologyLayout = layout; topologyOptions = options;
                        }
                        else graph = topology!;
                    }
                    // A graph key identifies this exact cached build as well as its workspace/view/options.
                    // Competing topology builds may expire it; never silently reinterpret numeric IDs.
                    var identity = context.GetIdentity(snapshot);
                    var graphKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
                    {
                        build = graphBuildKeys.GetValue(graph, _ => new(Guid.NewGuid().ToString("N"))).Value,
                        identity.DocumentId, identity.WorkspaceId, identity.ViewVersion, snapshot.Revision, layout, options
                    }, McpJson.Options))));
                    if (GetString(args, "graphKey") is { } expectedKey && expectedKey != graphKey)
                        throw new ShadowConflictException("The topology scope changed. Refresh graph visualization before reusing graph IDs.");
                    var result = McpJson.Element(operation(graph, args, ct));
                    var data = result.EnumerateObject().ToDictionary(p => p.Name, p => (object)p.Value, StringComparer.Ordinal);
                    data.Add("layout", layout); data.Add("options", options); data.Add("graphKey", graphKey);
                    return data;
                });
        }
    }

    private static Dictionary<string, object> Paging(params (string Name, object Schema)[] fields)
        => Fields([..fields, ("offset", Integer(0)), ("limit", Integer(1, 200))]);
    private static object GraphNumber(double? minimum = null, bool exclusive = false)
    {
        var schema = new Dictionary<string, object> { ["type"] = "number" };
        if (minimum is { } value) schema[exclusive ? "exclusiveMinimum" : "minimum"] = value;
        return schema;
    }
    private static double GraphDouble(JsonElement args, string name, double fallback) => args.TryGetProperty(name, out var value) ? value.GetDouble() : fallback;
    private static int? OptionalInt(JsonElement args, string name) => args.TryGetProperty(name, out var value) ? value.GetInt32() : null;
    private static void CheckNode(TopologyGraph graph, int node) { if ((uint)node >= (uint)graph.Nodes.Length) throw new ArgumentException("Unknown topology node."); }
    private static object DescribeNode(GraphNode node) => new { node.Id, node.Position.X, node.Position.Y, node.Position.Z, degree = node.Edges.Length };
    private static object DescribeEdge(TopologyGraph graph, GraphEdge edge) => new { edge.Id, edge.A, edge.B, edge.Length, entity = edge.Entity.Value, rootEntity = edge.RootEntity.Value, insertPath = edge.InsertPath.IsDefaultOrEmpty ? System.Array.Empty<string>() : edge.InsertPath.Select(id => id.Value).ToArray(), edge.Occurrence, start = graph.Nodes[edge.A].Position, end = graph.Nodes[edge.B].Position };
    private static HashSet<int>? ComponentMembers(TopologyGraph graph, JsonElement args, CancellationToken token)
    {
        if (OptionalInt(args, "component") is not { } component) return null;
        var components = graph.ConnectedComponents(token);
        if ((uint)component >= (uint)components.Length) throw new ArgumentException("Unknown topology component.");
        return components[component].ToHashSet();
    }
    private static object GraphPage<T, TResult>(IEnumerable<T> source, JsonElement args, Func<T, TResult> describe, CancellationToken token)
    {
        IEnumerable<T> Checked() { foreach (var value in source) { token.ThrowIfCancellationRequested(); yield return value; } }
        return Page(Checked(), GetInt(args, "offset", 0), GetInt(args, "limit", 50), describe);
    }
}
