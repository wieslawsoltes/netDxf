using System.Collections.Immutable;

namespace netDxf.Shadow.Symbols;

public sealed record DomainPortVisit(string Id, int Depth, string? Predecessor, string? Connection);
public sealed record DomainConnectionProof(ImmutableArray<string> Nodes, ImmutableArray<string> Edges);
/// <summary>A BFS proof within the declared, scanned graph. Completeness never implies process or electrical simulation.</summary>
public sealed class DomainConnectivityWalk
{
    public string Source { get; }
    public string? Target { get; }
    public string Direction { get; }
    public string Status { get; }
    public bool Complete { get; }
    public bool ScopeComplete { get; }
    public bool VisitLimitReached { get; }
    public bool DepthLimitReached { get; }
    public int ExaminedEdges { get; }
    public ImmutableArray<DomainPortVisit> Visits { get; }
    public DomainConnectionProof? Path { get; }
    internal DomainConnectivityWalk(string source, string? target, string direction, string status, bool scopeComplete,
        bool visitLimit, bool depthLimit, int examined, ImmutableArray<DomainPortVisit> visits)
    {
        Source = source; Target = target; Direction = direction; Status = status; ScopeComplete = scopeComplete;
        VisitLimitReached = visitLimit; DepthLimitReached = depthLimit; ExaminedEdges = examined; Visits = visits;
        Complete = scopeComplete && !visitLimit && !depthLimit;
        Path = target is not null && status == "found" ? ProofTo(target) : null;
    }
    public DomainConnectionProof ProofTo(string id)
    {
        var map = Visits.ToDictionary(v => v.Id, StringComparer.Ordinal);
        if (!map.ContainsKey(id)) throw new ArgumentException("Port was not reached by this trace.", nameof(id));
        var nodes = new List<string>(); var edges = new List<string>(); var current = map[id];
        while (true)
        {
            nodes.Add(current.Id); if (current.Predecessor is null) break;
            edges.Add(current.Connection!); current = map[current.Predecessor];
        }
        nodes.Reverse(); edges.Reverse(); return new(nodes.ToImmutableArray(), edges.ToImmutableArray());
    }
}
public sealed partial class DomainConnectivity
{
    /// <summary>Minimum number of declared links. Direction "both" ignores passage direction; no geometric distance is optimized.</summary>
    public DomainConnectivityWalk ShortestPath(string source, string target, string direction = "both", int maximumVisits = 10000,
        int maximumDepth = 256, CancellationToken cancellationToken = default) => Walk(source, target, direction, maximumVisits, maximumDepth, cancellationToken);
    /// <summary>Reachable ports with a predecessor connection for every non-root port, suitable for exact connection inspection.</summary>
    public DomainConnectivityWalk Trace(string source, string direction = "forward", int maximumVisits = 10000,
        int maximumDepth = 256, CancellationToken cancellationToken = default) => Walk(source, null, direction, maximumVisits, maximumDepth, cancellationToken);
    private DomainConnectivityWalk Walk(string source, string? target, string direction, int maximumVisits, int maximumDepth, CancellationToken token)
    {
        if (direction is not ("both" or "forward" or "reverse") || maximumVisits is < 1 or > 50000 || maximumDepth is < 0 or > 1000)
            throw new ArgumentException("Choose both/forward/reverse direction, 1–50000 visits and depth 0–1000.");
        if (!nodesById.ContainsKey(source) || target is not null && !nodesById.ContainsKey(target)) throw new ArgumentException("Choose a port in this scanned discipline graph.");
        token.ThrowIfCancellationRequested(); var visits = new List<DomainPortVisit> { new(source, 0, null, null) };
        var known = new HashSet<string>(StringComparer.Ordinal) { source }; var queue = new Queue<DomainPortVisit>(); queue.Enqueue(visits[0]);
        var visitLimit = false; var depthLimit = false; var examined = 0; var found = target == source;
        while (!found && queue.TryDequeue(out var current))
        {
            token.ThrowIfCancellationRequested();
            foreach (var edge in adjacency[current.Id])
            {
                token.ThrowIfCancellationRequested(); examined++;
                var forward = edge.Source == current.Id;
                var allowed = direction == "both" || edge.Direction == "both" ||
                    (direction == "forward" ? forward : !forward) == (edge.Direction == "forward");
                if (!allowed) continue;
                var next = forward ? edge.Target : edge.Source; if (known.Contains(next)) continue;
                if (current.Depth == maximumDepth) { depthLimit = true; continue; }
                if (visits.Count == maximumVisits) { visitLimit = true; break; }
                var visit = new DomainPortVisit(next, current.Depth + 1, current.Id, edge.Id); known.Add(next); visits.Add(visit); queue.Enqueue(visit);
                if (next == target) { found = true; break; }
            }
            if (visitLimit) break;
        }
        var scope = Complete && DeclarationsComplete;
        var status = target is null ? "trace" : found ? "found" : scope && !visitLimit && !depthLimit ? "unreachable" : "incomplete";
        return new(source, target, direction, status, scope, visitLimit, depthLimit, examined, visits.ToImmutableArray());
    }
}
