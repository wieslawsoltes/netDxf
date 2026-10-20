using System.Collections.Immutable;
using System.Text.Json;
using netDxf.Shadow.Mcp;
using netDxf.Shadow.Symbols;

namespace netDxf.Shadow.Studio;

public sealed record CadModuleConnectivityRequest(long Revision, string Module, string Operation = "network",
    string? Source = null, string? Target = null, string Direction = "both", int MaximumVisits = 10000, int MaximumDepth = 256,
    DomainConnectivityOptions? Options = null, string? GraphKey = null, int NodeOffset = 0, int EdgeOffset = 0,
    int IssueOffset = 0, int ComponentOffset = 0, int VisitOffset = 0, int Limit = 100);
public sealed record CadConnectivityPage<T>(int Total, int Offset, ImmutableArray<T> Items, int? NextOffset);
public sealed record CadConnectivityWalkSummary(string Source, string? Target, string Direction, string Status,
    bool Complete, bool ScopeComplete, bool VisitLimitReached, bool DepthLimitReached, int ExaminedEdges, int VisitedCount, int? PathLength);
public sealed record CadModuleConnectivityResult(string GraphKey, Guid DocumentId, long Revision, string WorkspaceId,
    long ViewVersion, string Layout, string Module, string Operation, bool Complete, bool DeclarationsComplete,
    bool IssuesComplete, int ObjectsVisited, int RecordCount, ImmutableArray<string> Limitations,
    DomainConnectivityOptions Options, CadConnectivityWalkSummary? Walk, CadConnectivityPage<DomainPortNode> Nodes,
    CadConnectivityPage<DomainConnection> Edges, CadConnectivityPage<DomainConnectivityIssue> Issues,
    CadConnectivityPage<DomainNetworkComponent> Components, CadConnectivityPage<DomainPortVisit> Visits);

public sealed partial class Studio
{
    private sealed record ConnectivityCache(ShadowSnapshot Snapshot, string Workspace, long View, long Configuration,
        string Layout, string Scope, string Key, DomainConnectivity Graph, DomainConnectivityWalk? Walk, ImmutableArray<DomainPortVisit> Visits);
    private ConnectivityCache? connectivityCache;

    /// <summary>Read-only, exact-scope, bounded discipline connectivity. Every continuation must retain its graph key.</summary>
    public CadModuleConnectivityResult CadModuleConnectivity(CadModuleConnectivityRequest request, CancellationToken token = default) =>
        ModuleConnectivity(request, 131072, token);
    private CadModuleConnectivityResult ModuleConnectivity(CadModuleConnectivityRequest request, int maximumCharacters, CancellationToken token)
    {
        lock (gate)
        {
            Check(request.Revision); token.ThrowIfCancellationRequested();
            if (!cadModules.IsEnabled(request.Module)) throw new InvalidOperationException("Enable this discipline module before analyzing its connectivity.");
            if (activeBranch?.BlockRoot is not null) throw new InvalidOperationException("Finish block editing before analyzing discipline connectivity in the active layout.");
            if (request.Operation is not ("network" or "path" or "trace") || request.Direction is not ("both" or "forward" or "reverse") ||
                request.Limit is < 1 or > 500 || request.NodeOffset < 0 || request.EdgeOffset < 0 || request.IssueOffset < 0 || request.ComponentOffset < 0 || request.VisitOffset < 0 ||
                request.Source?.Length > DomainConnectivity.MaximumPortIdLength || request.Target?.Length > DomainConnectivity.MaximumPortIdLength || request.GraphKey?.Length > 128 ||
                request.MaximumVisits is < 1 or > 50000 || request.MaximumDepth is < 0 or > 1000)
                throw new ArgumentException("Invalid connectivity operation, page, port ID or search bounds.");
            var options = request.Options ?? new DomainConnectivityOptions();
            var scope = JsonSerializer.Serialize(new { request.Module, request.Operation, request.Source, request.Target, request.Direction,
                request.MaximumVisits, request.MaximumDepth, options }, McpJson.Options);
            var cached = connectivityCache;
            var valid = cached is not null && ReferenceEquals(cached.Snapshot, Current) && cached.Workspace == WorkspaceId && cached.View == viewVersion &&
                cached.Configuration == moduleConfigurationVersion && cached.Layout == layout && cached.Scope == scope;
            if (request.GraphKey is not null && (!valid || cached!.Key != request.GraphKey)) throw new InvalidOperationException("This connectivity result is stale or belongs to another scope. Refresh the network.");
            if (request.GraphKey is null && (request.NodeOffset != 0 || request.EdgeOffset != 0 || request.IssueOffset != 0 || request.ComponentOffset != 0 || request.VisitOffset != 0))
                throw new ArgumentException("Continuation pages require the graph key from the first page.");
            if (!valid)
            {
                var graph = DomainConnectivity.Build(Current, request.Module, layout, options, token);
                var walk = request.Operation switch
                {
                    "path" => graph.ShortestPath(request.Source ?? "", request.Target ?? "", request.Direction, request.MaximumVisits, request.MaximumDepth, token),
                    "trace" => graph.Trace(request.Source ?? "", request.Direction, request.MaximumVisits, request.MaximumDepth, token),
                    _ => null
                };
                var visits = walk?.Visits ?? [];
                if (request.Operation == "path")
                {
                    var members = walk?.Path?.Nodes.ToHashSet(StringComparer.Ordinal) ?? [];
                    visits = visits.Where(v => members.Contains(v.Id)).ToImmutableArray();
                }
                cached = new(Current, WorkspaceId, viewVersion, moduleConfigurationVersion, layout, scope, Guid.NewGuid().ToString("N"), graph, walk, visits);
                connectivityCache = cached;
            }
            var g = cached!.Graph; var w = cached.Walk;
            var summary = w is null ? null : new CadConnectivityWalkSummary(w.Source, w.Target, w.Direction, w.Status, w.Complete,
                w.ScopeComplete, w.VisitLimitReached, w.DepthLimitReached, w.ExaminedEdges, w.Visits.Length, w.Path?.Edges.Length);
            var result = new CadModuleConnectivityResult(cached.Key, g.DocumentId, g.Revision, WorkspaceId, viewVersion, layout, request.Module,
                request.Operation, g.Complete, g.DeclarationsComplete, g.IssuesComplete, g.ObjectsVisited, g.RecordCount, g.Limitations, options, summary,
                Page(g.Nodes, request.NodeOffset, request.Limit), Page(g.Edges, request.EdgeOffset, request.Limit), Page(g.Issues, request.IssueOffset, request.Limit),
                Page(g.Components, request.ComponentOffset, request.Limit), Page(cached.Visits, request.VisitOffset, request.Limit));
            // Account for the exact serialized receipt. Short pages retain actual emitted cursors; never silently archive or lose an item.
            while (JsonSerializer.Serialize(result, McpJson.Options).Length > maximumCharacters)
            {
                token.ThrowIfCancellationRequested();
                if (result.Nodes.Items.Length <= 1 && result.Edges.Items.Length <= 1 && result.Issues.Items.Length <= 1 && result.Components.Items.Length <= 1 && result.Visits.Items.Length <= 1)
                    throw new InvalidOperationException("The minimum connectivity receipt exceeds this host's response budget.");
                result = result with { Nodes = Shrink(result.Nodes), Edges = Shrink(result.Edges), Issues = Shrink(result.Issues), Components = Shrink(result.Components), Visits = Shrink(result.Visits) };
            }
            return result;
        }
    }
    private static CadConnectivityPage<T> Page<T>(ImmutableArray<T> items, int offset, int limit)
    {
        if (offset > items.Length) throw new ArgumentException("Connectivity page offset exceeds the stream total.");
        var values = items.Skip(offset).Take(limit).ToImmutableArray();
        return new(items.Length, offset, values, offset + values.Length < items.Length ? offset + values.Length : null);
    }
    private static CadConnectivityPage<T> Shrink<T>(CadConnectivityPage<T> page)
    {
        if (page.Items.Length <= 1) return page;
        var items = page.Items.Take(Math.Max(1, page.Items.Length / 2)).ToImmutableArray();
        return page with { Items = items, NextOffset = page.Offset + items.Length < page.Total ? page.Offset + items.Length : null };
    }
    private void RegisterModuleConnectivityTool(McpToolRegistry registry)
    {
        registry.Register(new("studio_module_connectivity", "Inspect explicit discipline ports, internal passages, declared connections, weak components, minimum-link paths and bounded traces. No intersections or physical simulation are inferred. Disabled modules are rejected. Request CadModuleConnectivityRequest through studio_request_schema; retain the exact graphKey and options on every continuation. All five streams are paged; partial declarations and scan limits are reported.",
            McpJson.Parse("""{"type":"object","properties":{"identity":{"type":"object","properties":{"documentId":{"type":"string"},"workspaceId":{"type":"string"},"viewVersion":{"type":"integer","minimum":0}},"required":["documentId","workspaceId","viewVersion"],"additionalProperties":false},"request":{"type":"object"}},"required":["identity","request"],"additionalProperties":false}"""), true), (args, token) =>
        {
            var identity = args.GetProperty("identity").Deserialize<StudioRequestIdentity>(McpJson.Options) ?? throw new ArgumentException("Missing identity.");
            var request = args.GetProperty("request").Deserialize<CadModuleConnectivityRequest>(McpJson.Options) ?? throw new ArgumentException("Missing connectivity request.");
            return ValueTask.FromResult(McpToolResult.FromObject(ExecuteRequest(identity, () => ModuleConnectivity(request, registry.MaximumResultCharacters, token))));
        });
    }
}
