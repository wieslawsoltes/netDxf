using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using netDxf.Shadow.Graph;

namespace netDxf.Shadow.Mcp;

public sealed partial class ShadowMcpTools
{
    private sealed record ReferenceAnalysisProjection(string ReferenceKey, string Analysis, string Direction,
        ImmutableArray<string> Roots, int NodeCount, int EdgeCount, int ObjectsIndexed,
        bool NodeLimitReached, bool EdgeLimitReached, bool DepthLimitReached, bool ExplorationComplete,
        string? Status, int VisitedNodes, int ExaminedEdges,
        GraphVisualizationPage<object> Nodes, GraphVisualizationPage<ReferenceGraphEdge> Edges,
        GraphVisualizationPage<object> Results, GraphVisualizationPage<ReferenceCondensationEdge> Links, bool Truncated);
    private ShadowSnapshot? referenceAnalysisSnapshot;
    private string? referenceAnalysisScope;
    private ReferenceAnalysisProjection? referenceAnalysis;

    private void InstallReferenceAnalysis(McpToolRegistry registry)
    {
        foreach (var analysis in new[] { "components", "path", "impact" })
        {
            var kind = analysis;
            var description = kind switch
            {
                "components" => "Strongly connected components and condensation DAG of a bounded source-reference scope. Cycles are informational object relationships, not drawing defects. Partial scopes may split larger components and cannot prove global acyclicity. Nodes include component IDs; results page component summaries; links page aggregated directed condensation edges.",
                "path" => "Find a deterministic fewest-reference dependency path between source and target. Status is found, unreachable, or incomplete. Budget exhaustion never claims unreachable. Nodes/edges are ordered from search source to target; edges retain their original orientation for incoming searches. Source equals target returns a zero-edge path.",
                _ => "Explore incoming structural impact, with a shortest predecessor proof forest. This is reference reachability, not a simulation of edit consequences. Each node includes predecessor and via; follow those toward a seed to explain why it is affected. Shared proofs avoid expanding every complete path."
            };
            Add(registry, "graph_reference_" + kind, description +
                " Exact JSON Pointer properties identify links. No geometry or discipline connectivity is inferred. Preserve identity, inputs and referenceKey for all continuations. Node/edge/result/link pages adapt to serialized host budgets. Counts describe this bounded analysis; all reads preserve source, selection and undo history.", true,
                Fields(("roots", Array(String(), 1, 200)), ("direction", Enum("outgoing", "incoming")),
                    ("source", String(512)), ("target", String(512)), ("depth", Integer(0, 16)),
                    ("maximumNodes", Integer(1, 1000)), ("maximumEdges", Integer(1, 4000)),
                    ("nodeOffset", Integer(0)), ("nodeLimit", Integer(1, 200)), ("edgeOffset", Integer(0)), ("edgeLimit", Integer(1, 500)),
                    ("resultOffset", Integer(0)), ("resultLimit", Integer(1, 200)), ("linkOffset", Integer(0)), ("linkLimit", Integer(1, 500)),
                    ("referenceKey", String(128))), kind == "path" ? ["source", "target"] : [], (args, _, snapshot, token) =>
                {
                    var options = new ReferenceGraphOptions
                    {
                        Roots = args.TryGetProperty("roots", out var roots) ? roots.EnumerateArray().Select(r => new ObjectId(r.GetString()!)).Distinct()
                            .OrderBy(r => r.Value, StringComparer.Ordinal).ToImmutableArray() : [],
                        Direction = kind == "impact" || GetString(args, "direction") == "incoming" ? ReferenceGraphDirection.Incoming : ReferenceGraphDirection.Outgoing,
                        MaximumDepth = GetInt(args, "depth", kind == "path" ? 16 : 2), MaximumNodes = GetInt(args, "maximumNodes", kind == "path" ? 1000 : 100),
                        MaximumEdges = GetInt(args, "maximumEdges", kind == "path" ? 4000 : 500)
                    };
                    var source = GetString(args, "source"); var target = GetString(args, "target");
                    if (kind == "path") options = options with { Roots = [new(source!)] };
                    var scope = JsonSerializer.Serialize(new { kind, options, source = kind == "path" ? source : null, target = kind == "path" ? target : null }, McpJson.Options);
                    var index = referenceIndices.GetValue(snapshot, s => new(new(s, cancellationToken: token), Guid.NewGuid().ToString("N")));
                    var identity = context.GetIdentity(snapshot);
                    var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
                    { index.Key, identity.DocumentId, identity.WorkspaceId, identity.ViewVersion, snapshot.Revision, scope }, McpJson.Options))));
                    if (GetString(args, "referenceKey") is { } expected && expected != key)
                        throw new ShadowConflictException("The source reference analysis changed. Run it again before continuing.");
                    if (!ReferenceEquals(referenceAnalysisSnapshot, snapshot) || referenceAnalysisScope != scope)
                    {
                        ReferenceGraph graph; ImmutableArray<object> nodes; ImmutableArray<object> results = [];
                        ImmutableArray<ReferenceCondensationEdge> links = []; string? status = null; var visited = 0; var examined = 0;
                        if (kind == "components")
                        {
                            var result = index.Index.StronglyConnectedComponents(options, token); graph = result.Scope;
                            var membership = result.Components.SelectMany(c => c.Nodes.Select(id => (id, c))).ToDictionary(p => p.id, p => p.c);
                            nodes = graph.Nodes.Select(n => Node(n, membership[n.Id])).ToImmutableArray();
                            results = result.Components.Select(c => (object)new { c.Id, NodeCount = c.Nodes.Length, c.InternalEdgeCount, c.Cyclic }).ToImmutableArray();
                            links = result.CondensationEdges;
                        }
                        else if (kind == "path")
                        {
                            var result = index.Index.ShortestPath(new(source!), new(target!), new() { Direction = options.Direction,
                                MaximumDepth = options.MaximumDepth, MaximumNodes = options.MaximumNodes, MaximumEdges = options.MaximumEdges }, token);
                            status = result.Status.ToString().ToLowerInvariant(); visited = result.VisitedNodes; examined = result.ExaminedEdges;
                            graph = new(options.Roots.Select(r => r.Value).ToImmutableArray(), options.Direction, result.Nodes, result.Edges,
                                result.NodeLimitReached, result.EdgeLimitReached, result.DepthLimitReached, snapshot.Objects.Count);
                            nodes = graph.Nodes.Select(n => Node(n)).ToImmutableArray();
                        }
                        else
                        {
                            var result = index.Index.Impact(options, token); graph = result.Scope; examined = result.ExaminedEdges;
                            var visits = result.Visits.ToDictionary(v => v.Id, StringComparer.Ordinal);
                            nodes = graph.Nodes.Select(n => Node(n, visit: visits[n.Id])).ToImmutableArray();
                        }
                        if (kind != "path") visited = graph.Nodes.Length;
                        referenceAnalysis = new(key, kind, options.Direction == ReferenceGraphDirection.Incoming ? "incoming" : "outgoing", graph.Roots,
                            graph.Nodes.Length, graph.Edges.Length, snapshot.Objects.Count, graph.NodeLimitReached, graph.EdgeLimitReached, graph.DepthLimitReached,
                            !graph.Truncated, status, visited, examined, AnalysisPage(nodes, 0, nodes.Length), AnalysisPage(graph.Edges, 0, graph.Edges.Length),
                            AnalysisPage(results, 0, results.Length), AnalysisPage(links, 0, links.Length), false);
                        referenceAnalysisSnapshot = snapshot; referenceAnalysisScope = scope;
                    }
                    var full = referenceAnalysis!;
                    // Identity can change without replacing a snapshot (workspace/view switches).
                    var page = full with { ReferenceKey = key,
                        Nodes = AnalysisPage(full.Nodes.Items, GetInt(args, "nodeOffset", 0), GetInt(args, "nodeLimit", 100)),
                        Edges = AnalysisPage(full.Edges.Items, GetInt(args, "edgeOffset", 0), GetInt(args, "edgeLimit", 200)),
                        Results = AnalysisPage(full.Results.Items, GetInt(args, "resultOffset", 0), GetInt(args, "resultLimit", 100)),
                        Links = AnalysisPage(full.Links.Items, GetInt(args, "linkOffset", 0), GetInt(args, "linkLimit", 200)) };
                    while (true)
                    {
                        token.ThrowIfCancellationRequested();
                        page = page with { Truncated = page.Nodes.Items.Length < page.Nodes.Total || page.Edges.Items.Length < page.Edges.Total ||
                            page.Results.Items.Length < page.Results.Total || page.Links.Items.Length < page.Links.Total };
                        if (McpToolResult.FromObject(CreateReceipt(snapshot, identity, page)).Text.Length <= registry.MaximumResultCharacters) return page;
                        if (page.Nodes.Items.Length <= 1 && page.Edges.Items.Length <= 1 && page.Results.Items.Length <= 1 && page.Links.Items.Length <= 1)
                            throw new McpResultTooLargeException("The minimum reference analysis page exceeds this host's response budget.");
                        page = page with { Nodes = ShortenVisualizationPage(page.Nodes), Edges = ShortenVisualizationPage(page.Edges),
                            Results = ShortenVisualizationPage(page.Results), Links = ShortenVisualizationPage(page.Links) };
                    }
                });
        }
        static object Node(ReferenceGraphNode n, ReferenceComponent? component = null, ReferenceImpactVisit? visit = null) => new
        { n.Id, n.Type, n.Kind, n.Name, n.Depth, n.Root, n.Exists, n.Incoming, n.Outgoing, Component = component?.Id, Cyclic = component?.Cyclic,
            Predecessor = visit?.Predecessor, Via = visit?.Via };
    }
    private static GraphVisualizationPage<T> AnalysisPage<T>(ImmutableArray<T> items, int offset, int limit)
    {
        var count = offset >= items.Length ? 0 : Math.Min(limit, items.Length - offset);
        return new(items.Length, offset, items.Skip(offset).Take(count).ToImmutableArray(), offset < items.Length - count ? offset + count : null);
    }
}
