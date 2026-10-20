using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using netDxf.Shadow.Graph;

namespace netDxf.Shadow.Mcp;

public sealed partial class ShadowMcpTools
{
    private sealed record ReferenceIndex(ReferenceGraphIndex Index, string Key);
    private static readonly ConditionalWeakTable<ShadowSnapshot, ReferenceIndex> referenceIndices = new();
    private ShadowSnapshot? referenceSnapshot;
    private string? referenceScope;
    private ReferenceGraph? referenceGraph;
    private sealed record ReferenceProjection(string ReferenceKey, string Direction, ImmutableArray<string> Roots,
        int NodeCount, int EdgeCount, int ObjectsIndexed, int MaximumDepth, int MaximumNodes, int MaximumEdges,
        bool NodeLimitReached, bool EdgeLimitReached, bool DepthLimitReached, bool ExplorationComplete,
        GraphVisualizationPage<ReferenceGraphNode> Nodes, GraphVisualizationPage<ReferenceGraphEdge> Edges, bool Truncated);

    private void InstallReferenceGraph(McpToolRegistry registry)
    {
        Add(registry, "graph_reference_graph",
            "Explore the captured source-object reference graph, distinct from geometric topology and domain connectivity. " +
            "Roots default to the document object. Follow incoming or outgoing references with exact JSON Pointer property/collection paths. " +
            "Edges always retain their source-to-target direction, including incoming exploration. Depth/node/edge limits are reported independently; " +
            "page totals describe the bounded exploration, not the full document graph. Reuse referenceKey and the same roots/direction/budgets " +
            "with every continuation. Pages adapt to the host reply budget. Object/value index budget exhaustion is an error. " +
            "Use shadow_read with an edge source/path for property details. This tool never selects or edits drawing objects.",
            true, Fields(("roots", Array(String(), 1, 200)), ("direction", Enum("outgoing", "incoming")),
                ("depth", Integer(0, 16)), ("maximumNodes", Integer(1, 1000)), ("maximumEdges", Integer(1, 4000)),
                ("nodeOffset", Integer(0)), ("nodeLimit", Integer(1, 200)), ("edgeOffset", Integer(0)), ("edgeLimit", Integer(1, 500)),
                ("referenceKey", String(128))), [], (args, _, snapshot, token) =>
            {
                var options = new ReferenceGraphOptions
                {
                    Roots = args.TryGetProperty("roots", out var roots) ? roots.EnumerateArray().Select(r => new ObjectId(r.GetString()!))
                        .Distinct().OrderBy(r => r.Value, StringComparer.Ordinal).ToImmutableArray() : [],
                    Direction = GetString(args, "direction") == "incoming" ? ReferenceGraphDirection.Incoming : ReferenceGraphDirection.Outgoing,
                    MaximumDepth = GetInt(args, "depth", 2), MaximumNodes = GetInt(args, "maximumNodes", 100),
                    MaximumEdges = GetInt(args, "maximumEdges", 500)
                };
                var scope = JsonSerializer.Serialize(options, McpJson.Options);
                var index = referenceIndices.GetValue(snapshot, s => new(new(s, cancellationToken: token), Guid.NewGuid().ToString("N")));
                var identity = context.GetIdentity(snapshot);
                var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
                { index.Key, identity.DocumentId, identity.WorkspaceId, identity.ViewVersion, snapshot.Revision, scope }, McpJson.Options))));
                if (GetString(args, "referenceKey") is { } expected && expected != key)
                    throw new ShadowConflictException("The source reference scope changed. Explore again before reusing the graph.");
                if (!ReferenceEquals(referenceSnapshot, snapshot) || referenceScope != scope)
                {
                    referenceGraph = index.Index.Explore(options, token);
                    referenceSnapshot = snapshot; referenceScope = scope;
                }
                var graph = referenceGraph!;
                var nodes = Page(graph.Nodes, GetInt(args, "nodeOffset", 0), GetInt(args, "nodeLimit", 100));
                var edges = Page(graph.Edges, GetInt(args, "edgeOffset", 0), GetInt(args, "edgeLimit", 200));
                var result = new ReferenceProjection(key, options.Direction == ReferenceGraphDirection.Incoming ? "incoming" : "outgoing",
                    graph.Roots, graph.Nodes.Length, graph.Edges.Length, graph.ObjectsIndexed,
                    options.MaximumDepth, options.MaximumNodes, options.MaximumEdges,
                    graph.NodeLimitReached, graph.EdgeLimitReached, graph.DepthLimitReached, !graph.Truncated,
                    nodes, edges, nodes.Items.Length < nodes.Total || edges.Items.Length < edges.Total);
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    if (McpToolResult.FromObject(CreateReceipt(snapshot, identity, result)).Text.Length <= registry.MaximumResultCharacters) return result;
                    if (result.Nodes.Items.Length <= 1 && result.Edges.Items.Length <= 1)
                        throw new McpResultTooLargeException("The minimum reference graph page exceeds this host's response budget.");
                    nodes = ShortenVisualizationPage(result.Nodes); edges = ShortenVisualizationPage(result.Edges);
                    result = result with { Nodes = nodes, Edges = edges, Truncated = nodes.Items.Length < nodes.Total || edges.Items.Length < edges.Total };
                }
            });
        static GraphVisualizationPage<T> Page<T>(ImmutableArray<T> items, int offset, int limit)
        {
            var count = offset >= items.Length ? 0 : Math.Min(limit, items.Length - offset);
            return new(items.Length, offset, items.Skip(offset).Take(count).ToImmutableArray(), offset < items.Length - count ? offset + count : null);
        }
    }
}
