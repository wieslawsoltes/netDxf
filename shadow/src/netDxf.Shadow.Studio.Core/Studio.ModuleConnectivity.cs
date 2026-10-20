using System.Collections.Immutable;
using System.Text.Json;
using netDxf.Shadow.Mcp;
using netDxf.Shadow.Symbols;

namespace netDxf.Shadow.Studio;

public sealed record CadModuleConnectivityRequest(long Revision, string Module, string Operation = "network",
    string? Source = null, string? Target = null, string Direction = "both", int MaximumVisits = 10000, int MaximumDepth = 256,
    DomainConnectivityOptions? Options = null, string? GraphKey = null, int NodeOffset = 0, int EdgeOffset = 0,
    int IssueOffset = 0, int ComponentOffset = 0, int VisitOffset = 0, int Limit = 100,
    DomainStructureOptions? StructureOptions = null, int StrongComponentOffset = 0, int PortStructureOffset = 0,
    int CondensationOffset = 0, int CriticalConnectionOffset = 0, int LoopOffset = 0, int LoopStepOffset = 0,
    ImmutableArray<string>? ExcludedConnections = null, ImmutableArray<string>? ExcludedPorts = null,
    DomainIsolationOptions? IsolationOptions = null, int AssessmentOffset = 0, int BeforeVisitOffset = 0,
    int AfterVisitOffset = 0, int ExclusionOffset = 0);
public sealed record CadConnectivityPage<T>(int Total, int Offset, ImmutableArray<T> Items, int? NextOffset);
public sealed record CadConnectivityWalkSummary(string Source, string? Target, string Direction, string Status,
    bool Complete, bool ScopeComplete, bool VisitLimitReached, bool DepthLimitReached, int ExaminedEdges, int VisitedCount, int? PathLength);
public sealed record CadModuleConnectivityResult(string GraphKey, Guid DocumentId, long Revision, string WorkspaceId,
    long ViewVersion, string Layout, string Module, string Operation, bool Complete, bool DeclarationsComplete,
    bool IssuesComplete, int ObjectsVisited, int RecordCount, ImmutableArray<string> Limitations,
    DomainConnectivityOptions Options, CadConnectivityWalkSummary? Walk, CadConnectivityPage<DomainPortNode> Nodes,
    CadConnectivityPage<DomainConnection> Edges, CadConnectivityPage<DomainConnectivityIssue> Issues,
    CadConnectivityPage<DomainNetworkComponent> Components, CadConnectivityPage<DomainPortVisit> Visits)
{
    public CadConnectivityStructureSummary? Structure { get; init; }
    public CadConnectivityPage<DomainStrongComponent> StrongComponents { get; init; } = new(0, 0, [], null);
    public CadConnectivityPage<DomainPortStructure> PortStructures { get; init; } = new(0, 0, [], null);
    public CadConnectivityPage<DomainCondensationLink> Condensation { get; init; } = new(0, 0, [], null);
    public CadConnectivityPage<DomainCriticalConnection> CriticalConnections { get; init; } = new(0, 0, [], null);
    public CadConnectivityPage<DomainLoop> Loops { get; init; } = new(0, 0, [], null);
    public CadConnectivityPage<DomainLoopStep> LoopSteps { get; init; } = new(0, 0, [], null);
    public CadConnectivityIsolationSummary? Isolation { get; init; }
    public CadConnectivityPage<DomainIsolationAssessment> Assessments { get; init; } = new(0, 0, [], null);
    public CadConnectivityPage<DomainPortVisit> BeforeVisits { get; init; } = new(0, 0, [], null);
    public CadConnectivityPage<DomainPortVisit> AfterVisits { get; init; } = new(0, 0, [], null);
    public CadConnectivityPage<DomainIsolationExclusion> Exclusions { get; init; } = new(0, 0, [], null);
}
public sealed record CadConnectivityStructureSummary(bool ScopeComplete, bool LoopSearchComplete, bool LoopLimitReached,
    bool ProofLimitReached, bool SearchLimitReached, int SearchWork, DomainStructureOptions Options);
public sealed record CadConnectivityIsolationSummary(string Source, string? Target, string Direction, bool ScopeComplete,
    DomainIsolationOptions Options, DomainIsolationTraversalSummary Before, DomainIsolationTraversalSummary After);

public sealed partial class Studio
{
    private sealed record ConnectivityCache(ShadowSnapshot Snapshot, string Workspace, long View, long Configuration,
        string Layout, string Scope, string Key, DomainConnectivity Graph, DomainConnectivityWalk? Walk, ImmutableArray<DomainPortVisit> Visits,
        DomainConnectivityStructure? Structure, DomainConnectivityIsolation? Isolation);
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
            if (request.Operation is not ("network" or "path" or "trace" or "structure" or "isolation") || request.Direction is not ("both" or "forward" or "reverse") ||
                request.Limit is < 1 or > 500 || request.NodeOffset < 0 || request.EdgeOffset < 0 || request.IssueOffset < 0 || request.ComponentOffset < 0 || request.VisitOffset < 0 ||
                request.StrongComponentOffset < 0 || request.PortStructureOffset < 0 || request.CondensationOffset < 0 || request.CriticalConnectionOffset < 0 || request.LoopOffset < 0 || request.LoopStepOffset < 0 ||
                request.AssessmentOffset < 0 || request.BeforeVisitOffset < 0 || request.AfterVisitOffset < 0 || request.ExclusionOffset < 0 ||
                request.Source?.Length > DomainConnectivity.MaximumPortIdLength || request.Target?.Length > DomainConnectivity.MaximumPortIdLength || request.GraphKey?.Length > 128 ||
                request.MaximumVisits is < 1 or > 50000 || request.MaximumDepth is < 0 or > 1000)
                throw new ArgumentException("Invalid connectivity operation, page, port ID or search bounds.");
            var options = request.Options ?? new DomainConnectivityOptions();
            // Normalize omitted arrays before serialization; default ImmutableArray values are not enumerable.
            var excludedConnections = request.ExcludedConnections is { IsDefault: false } connectionIds ? connectionIds : ImmutableArray<string>.Empty;
            var excludedPorts = request.ExcludedPorts is { IsDefault: false } portIds ? portIds : ImmutableArray<string>.Empty;
            if ((long)excludedConnections.Length + excludedPorts.Length > DomainConnectivity.MaximumIsolationExclusions ||
                excludedConnections.Any(id => string.IsNullOrEmpty(id) || id.Length > DomainConnectivity.MaximumConnectionIdLength) ||
                excludedPorts.Any(id => string.IsNullOrEmpty(id) || id.Length > DomainConnectivity.MaximumPortIdLength) ||
                excludedConnections.Sum(id => (long)id.Length) + excludedPorts.Sum(id => (long)id.Length) > DomainConnectivity.MaximumIsolationExclusionCharacters)
                throw new ArgumentException("Invalid isolation exclusion IDs or scenario budget.");
            var scope = JsonSerializer.Serialize(new { request.Module, request.Operation, request.Source, request.Target, request.Direction,
                request.MaximumVisits, request.MaximumDepth, options, request.StructureOptions, request.IsolationOptions, excludedConnections, excludedPorts }, McpJson.Options);
            var cached = connectivityCache;
            var valid = cached is not null && ReferenceEquals(cached.Snapshot, Current) && cached.Workspace == WorkspaceId && cached.View == viewVersion &&
                cached.Configuration == moduleConfigurationVersion && cached.Layout == layout && cached.Scope == scope;
            if (request.GraphKey is not null && (!valid || cached!.Key != request.GraphKey)) throw new InvalidOperationException("This connectivity result is stale or belongs to another scope. Refresh the network.");
            if (request.GraphKey is null && (request.NodeOffset != 0 || request.EdgeOffset != 0 || request.IssueOffset != 0 || request.ComponentOffset != 0 || request.VisitOffset != 0 ||
                request.StrongComponentOffset != 0 || request.PortStructureOffset != 0 || request.CondensationOffset != 0 || request.CriticalConnectionOffset != 0 || request.LoopOffset != 0 || request.LoopStepOffset != 0 ||
                request.AssessmentOffset != 0 || request.BeforeVisitOffset != 0 || request.AfterVisitOffset != 0 || request.ExclusionOffset != 0))
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
                var structure = request.Operation == "structure" ? graph.AnalyzeStructure(request.StructureOptions, token) : null;
                var isolation = request.Operation == "isolation" ? graph.AnalyzeIsolation(request.Source ?? "", request.Target,
                    excludedConnections, excludedPorts, request.Direction, request.IsolationOptions ??
                    new(request.MaximumVisits, request.MaximumDepth), token) : null;
                cached = new(Current, WorkspaceId, viewVersion, moduleConfigurationVersion, layout, scope, Guid.NewGuid().ToString("N"), graph, walk, visits, structure, isolation);
                connectivityCache = cached;
            }
            var g = cached!.Graph; var w = cached.Walk;
            var summary = w is null ? null : new CadConnectivityWalkSummary(w.Source, w.Target, w.Direction, w.Status, w.Complete,
                w.ScopeComplete, w.VisitLimitReached, w.DepthLimitReached, w.ExaminedEdges, w.Visits.Length, w.Path?.Edges.Length);
            var result = new CadModuleConnectivityResult(cached.Key, g.DocumentId, g.Revision, WorkspaceId, viewVersion, layout, request.Module,
                request.Operation, g.Complete, g.DeclarationsComplete, g.IssuesComplete, g.ObjectsVisited, g.RecordCount, g.Limitations, options, summary,
                Page(g.Nodes, request.NodeOffset, request.Limit), Page(g.Edges, request.EdgeOffset, request.Limit), Page(g.Issues, request.IssueOffset, request.Limit),
                Page(g.Components, request.ComponentOffset, request.Limit), Page(cached.Visits, request.VisitOffset, request.Limit));
            var structureResult = cached.Structure; var isolationResult = cached.Isolation;
            result = result with {
                Structure = structureResult is null ? null : new(structureResult.ScopeComplete, structureResult.LoopSearchComplete,
                    structureResult.LoopLimitReached, structureResult.ProofLimitReached, structureResult.SearchLimitReached, structureResult.SearchWork, structureResult.Options),
                StrongComponents = Page(structureResult?.StrongComponents ?? [], request.StrongComponentOffset, request.Limit),
                PortStructures = Page(structureResult?.PortStructures ?? [], request.PortStructureOffset, request.Limit),
                Condensation = Page(structureResult?.Condensation ?? [], request.CondensationOffset, request.Limit),
                CriticalConnections = Page(structureResult?.CriticalConnections ?? [], request.CriticalConnectionOffset, request.Limit),
                Loops = Page(structureResult?.Loops ?? [], request.LoopOffset, request.Limit),
                LoopSteps = Page(structureResult?.LoopSteps ?? [], request.LoopStepOffset, request.Limit),
                Isolation = isolationResult is null ? null : new(isolationResult.Source, isolationResult.Target, isolationResult.Direction,
                    isolationResult.ScopeComplete, isolationResult.Options, isolationResult.Before, isolationResult.After),
                Assessments = Page(isolationResult?.Assessments ?? [], request.AssessmentOffset, request.Limit),
                BeforeVisits = Page(isolationResult?.BeforeVisits ?? [], request.BeforeVisitOffset, request.Limit),
                AfterVisits = Page(isolationResult?.AfterVisits ?? [], request.AfterVisitOffset, request.Limit),
                Exclusions = Page(isolationResult?.Exclusions ?? [], request.ExclusionOffset, request.Limit)
            };
            // Account for the exact serialized receipt. Short pages retain actual emitted cursors; never silently archive or lose an item.
            while (JsonSerializer.Serialize(result, McpJson.Options).Length > maximumCharacters)
            {
                token.ThrowIfCancellationRequested();
                if (result.Nodes.Items.Length <= 1 && result.Edges.Items.Length <= 1 && result.Issues.Items.Length <= 1 && result.Components.Items.Length <= 1 && result.Visits.Items.Length <= 1 &&
                    result.StrongComponents.Items.Length <= 1 && result.PortStructures.Items.Length <= 1 && result.Condensation.Items.Length <= 1 && result.CriticalConnections.Items.Length <= 1 && result.Loops.Items.Length <= 1 && result.LoopSteps.Items.Length <= 1 &&
                    result.Assessments.Items.Length <= 1 && result.BeforeVisits.Items.Length <= 1 && result.AfterVisits.Items.Length <= 1 && result.Exclusions.Items.Length <= 1)
                    throw new InvalidOperationException("The minimum connectivity receipt exceeds this host's response budget.");
                result = result with { Nodes = Shrink(result.Nodes), Edges = Shrink(result.Edges), Issues = Shrink(result.Issues), Components = Shrink(result.Components), Visits = Shrink(result.Visits),
                    StrongComponents = Shrink(result.StrongComponents), PortStructures = Shrink(result.PortStructures), Condensation = Shrink(result.Condensation), CriticalConnections = Shrink(result.CriticalConnections), Loops = Shrink(result.Loops), LoopSteps = Shrink(result.LoopSteps),
                    Assessments = Shrink(result.Assessments), BeforeVisits = Shrink(result.BeforeVisits), AfterVisits = Shrink(result.AfterVisits), Exclusions = Shrink(result.Exclusions) };
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
        registry.Register(new("studio_module_connectivity", "Inspect explicit discipline ports, passages, connections, paths/traces and structure: directed SCC/condensation, weak cut links/ports and bounded connection-distinct loop proofs (one witness per SCC, not all cycles). Isolation compares before/after reachability under explicitly excluded port/connection IDs, with independent traversal budgets and exact predecessor proofs; incomplete absence is unknown. No intersections, operational valve states or physical simulation are inferred. Disabled modules are rejected. Request CadModuleConnectivityRequest through studio_request_schema; retain exact graphKey and options on continuation. All fifteen streams are paged; incomplete scope and evidence limits are explicit.",
            McpJson.Parse("""{"type":"object","properties":{"identity":{"type":"object","properties":{"documentId":{"type":"string"},"workspaceId":{"type":"string"},"viewVersion":{"type":"integer","minimum":0}},"required":["documentId","workspaceId","viewVersion"],"additionalProperties":false},"request":{"type":"object"}},"required":["identity","request"],"additionalProperties":false}"""), true), (args, token) =>
        {
            var identity = args.GetProperty("identity").Deserialize<StudioRequestIdentity>(McpJson.Options) ?? throw new ArgumentException("Missing identity.");
            var request = args.GetProperty("request").Deserialize<CadModuleConnectivityRequest>(McpJson.Options) ?? throw new ArgumentException("Missing connectivity request.");
            return ValueTask.FromResult(McpToolResult.FromObject(ExecuteRequest(identity, () => ModuleConnectivity(request, registry.MaximumResultCharacters, token))));
        });
    }
}
