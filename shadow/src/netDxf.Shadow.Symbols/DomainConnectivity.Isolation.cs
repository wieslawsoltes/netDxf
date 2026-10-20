using System.Collections.Immutable;

namespace netDxf.Shadow.Symbols;

public sealed record DomainIsolationOptions(int MaximumVisits = 10000, int MaximumDepth = 256, int MaximumExaminedEdges = 200000);
public sealed record DomainIsolationExclusion(string Id, string Kind, string Value);
public sealed record DomainIsolationAssessment(string Id, string Before, string After, string Status);
public sealed record DomainIsolationTraversalSummary(bool Complete, bool ScopeComplete, bool VisitLimitReached,
    bool DepthLimitReached, bool EdgeLimitReached, int ExaminedEdges, int VisitedCount, string? TargetStatus, int? TargetPathLength);

/// <summary>Reachability under explicit removal assumptions; no operational equipment state or physics is inferred.</summary>
public sealed class DomainConnectivityIsolation
{
    public string Source { get; }
    public string? Target { get; }
    public string Direction { get; }
    public bool ScopeComplete { get; }
    public DomainIsolationOptions Options { get; }
    public DomainIsolationTraversalSummary Before { get; }
    public DomainIsolationTraversalSummary After { get; }
    public ImmutableArray<DomainIsolationExclusion> Exclusions { get; }
    public ImmutableArray<DomainIsolationAssessment> Assessments { get; }
    public ImmutableArray<DomainPortVisit> BeforeVisits { get; }
    public ImmutableArray<DomainPortVisit> AfterVisits { get; }
    private readonly ImmutableDictionary<string, DomainPortVisit> beforeIndex, afterIndex;

    internal DomainConnectivityIsolation(string source, string? target, string direction, bool scopeComplete,
        DomainIsolationOptions options, DomainIsolationTraversalSummary before, DomainIsolationTraversalSummary after,
        ImmutableArray<DomainIsolationExclusion> exclusions, ImmutableArray<DomainIsolationAssessment> assessments,
        ImmutableArray<DomainPortVisit> beforeVisits, ImmutableArray<DomainPortVisit> afterVisits)
    {
        Source = source; Target = target; Direction = direction; ScopeComplete = scopeComplete; Options = options;
        Before = before; After = after; Exclusions = exclusions; Assessments = assessments;
        BeforeVisits = beforeVisits; AfterVisits = afterVisits;
        beforeIndex = beforeVisits.ToImmutableDictionary(v => v.Id, StringComparer.Ordinal);
        afterIndex = afterVisits.ToImmutableDictionary(v => v.Id, StringComparer.Ordinal);
    }
    /// <summary>Exact minimum-link predecessor proof in the baseline or the exclusion scenario. Only reached ports have proofs.</summary>
    public DomainConnectionProof ProofTo(string port, bool after = true, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var index = after ? afterIndex : beforeIndex;
        if (!index.TryGetValue(port, out var visit)) throw new ArgumentException("The port has no proven path in this traversal.", nameof(port));
        var nodes = ImmutableArray.CreateBuilder<string>(); var edges = ImmutableArray.CreateBuilder<string>();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested(); nodes.Add(visit.Id);
            if (visit.Predecessor is null) break;
            edges.Add(visit.Connection!); visit = index[visit.Predecessor];
        }
        nodes.Reverse(); edges.Reverse();
        return new(nodes.ToImmutable(), edges.ToImmutable());
    }
}

public sealed partial class DomainConnectivity
{
    public const int MaximumConnectionIdLength = MaximumPortIdLength * 2 + 32;
    public const int MaximumIsolationExclusions = 2000;
    public const int MaximumIsolationExclusionCharacters = 128000;

    /// <summary>
    /// Compare reachability before and after removing explicitly named ports and connections.
    /// A source cannot be excluded. A missing after-path proves loss only when the source scope and
    /// after traversal are complete; otherwise absence is unknown. Positive paths remain valid in partial scope.
    /// </summary>
    public DomainConnectivityIsolation AnalyzeIsolation(string source, string? target = null,
        ImmutableArray<string> excludedConnections = default, ImmutableArray<string> excludedPorts = default,
        string direction = "forward", DomainIsolationOptions? options = null, CancellationToken cancellationToken = default)
    {
        var token = cancellationToken; token.ThrowIfCancellationRequested(); options ??= new();
        if (direction is not ("forward" or "reverse" or "both") || options.MaximumVisits is < 1 or > 50000 ||
            options.MaximumDepth is < 0 or > 1000 || options.MaximumExaminedEdges is < 1 or > 400000)
            throw new ArgumentException("Choose a direction, 1–50000 visits, depth 0–1000 and 1–400000 edge examinations per traversal.");
        if (source is null || !nodesById.ContainsKey(source) || target is not null && !nodesById.ContainsKey(target))
            throw new ArgumentException("Choose source and target ports in the scanned discipline graph.");
        excludedConnections = excludedConnections.IsDefault ? [] : excludedConnections;
        excludedPorts = excludedPorts.IsDefault ? [] : excludedPorts;
        if ((long)excludedConnections.Length + excludedPorts.Length > MaximumIsolationExclusions)
            throw new ArgumentException("The scenario accepts at most 2000 declared exclusions.");
        var edgeIds = Edges.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        var removedEdges = new HashSet<string>(StringComparer.Ordinal); var removedPorts = new HashSet<string>(StringComparer.Ordinal); var characters = 0L;
        void Add(ImmutableArray<string> values, HashSet<string> into, Func<string, bool> exists, int maximumLength)
        {
            foreach (var value in values)
            {
                token.ThrowIfCancellationRequested();
                if (string.IsNullOrEmpty(value) || value.Length > maximumLength || (characters += value.Length) > MaximumIsolationExclusionCharacters || !exists(value))
                    throw new ArgumentException("Exclusions must name existing declared IDs within the 128000-character scenario budget.");
                into.Add(value);
            }
        }
        Add(excludedConnections, removedEdges, edgeIds.Contains, MaximumConnectionIdLength);
        Add(excludedPorts, removedPorts, nodesById.ContainsKey, MaximumPortIdLength);
        if (removedPorts.Contains(source)) throw new ArgumentException("The source port cannot be excluded. Choose a retained source.");
        var scope = Complete && DeclarationsComplete;
        var (before, beforeVisits) = Search(false); var (after, afterVisits) = Search(true);
        var baseline = beforeVisits.Select(v => v.Id).ToHashSet(StringComparer.Ordinal);
        var scenario = afterVisits.Select(v => v.Id).ToHashSet(StringComparer.Ordinal);
        var assessments = ImmutableArray.CreateBuilder<DomainIsolationAssessment>(Nodes.Length);
        foreach (var node in Nodes)
        {
            token.ThrowIfCancellationRequested();
            var b = baseline.Contains(node.Id) ? "reachable" : before.Complete ? "unreachable" : "unknown";
            var a = removedPorts.Contains(node.Id) ? "excluded" : scenario.Contains(node.Id) ? "reachable" : after.Complete ? "unreachable" : "unknown";
            var status = a == "excluded" ? "excluded" : a == "reachable" ? "reachable" :
                b == "reachable" && a == "unreachable" ? "lost" : b == "unreachable" && a == "unreachable" ? "unreachable" : "unknown";
            assessments.Add(new(node.Id, b, a, status));
        }
        var exclusions = removedPorts.Order(StringComparer.Ordinal).Select(id => new DomainIsolationExclusion("port:" + id, "port", id))
            .Concat(removedEdges.Order(StringComparer.Ordinal).Select(id => new DomainIsolationExclusion("connection:" + id, "connection", id))).ToImmutableArray();
        return new(source, target, direction, scope, options, before, after, exclusions, assessments.ToImmutable(), beforeVisits, afterVisits);

        (DomainIsolationTraversalSummary Summary, ImmutableArray<DomainPortVisit> Visits) Search(bool apply)
        {
            var visits = new List<DomainPortVisit> { new(source, 0, null, null) };
            var known = new HashSet<string>(StringComparer.Ordinal) { source }; var head = 0;
            var visitLimit = false; var depthLimit = false; var edgeLimit = false; var examined = 0;
            while (head < visits.Count && !visitLimit && !edgeLimit)
            {
                token.ThrowIfCancellationRequested(); var current = visits[head++];
                foreach (var edge in adjacency[current.Id])
                {
                    token.ThrowIfCancellationRequested();
                    if (examined == options.MaximumExaminedEdges) { edgeLimit = true; break; }
                    examined++;
                    if (apply && removedEdges.Contains(edge.Id)) continue;
                    var forward = edge.Source == current.Id;
                    var allowed = direction == "both" || edge.Direction == "both" ||
                        (direction == "forward" ? forward : !forward) == (edge.Direction == "forward");
                    if (!allowed) continue;
                    var next = forward ? edge.Target : edge.Source;
                    if (apply && removedPorts.Contains(next) || known.Contains(next)) continue;
                    if (current.Depth == options.MaximumDepth) { depthLimit = true; continue; }
                    if (visits.Count == options.MaximumVisits) { visitLimit = true; break; }
                    known.Add(next); visits.Add(new(next, current.Depth + 1, current.Id, edge.Id));
                }
            }
            var complete = scope && !visitLimit && !depthLimit && !edgeLimit;
            var found = target is null ? null : visits.FirstOrDefault(v => v.Id == target);
            var targetStatus = target is null ? null : apply && removedPorts.Contains(target) ? "excluded" :
                found is not null ? "reachable" : complete ? "unreachable" : "unknown";
            return (new(complete, scope, visitLimit, depthLimit, edgeLimit, examined, visits.Count, targetStatus, found?.Depth), visits.ToImmutableArray());
        }
    }
}
