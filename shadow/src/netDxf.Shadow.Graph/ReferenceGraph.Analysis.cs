using System.Collections.Immutable;

namespace netDxf.Shadow.Graph;

public sealed record ReferenceComponent(int Id, ImmutableArray<string> Nodes, int InternalEdgeCount, bool Cyclic);
public sealed record ReferenceCondensationEdge(int Source, int Target, int ReferenceCount);
/// <summary>Components of the returned bounded scope, not a claim about omitted objects or references.</summary>
public sealed record ReferenceComponentAnalysis(ReferenceGraph Scope, ImmutableArray<ReferenceComponent> Components,
    ImmutableArray<ReferenceCondensationEdge> CondensationEdges)
{
    public bool Complete => !Scope.Truncated;
}
public enum ReferencePathStatus { Found, Unreachable, Incomplete }
public sealed record ReferencePathOptions
{
    public ReferenceGraphDirection Direction { get; init; }
    public int MaximumDepth { get; init; } = 16;
    public int MaximumNodes { get; init; } = 1000;
    public int MaximumEdges { get; init; } = 4000;
}
/// <summary>Edges retain stored source-to-target orientation, even for an incoming search.</summary>
public sealed record ReferencePathResult(ReferencePathStatus Status, ReferenceGraphDirection Direction,
    ImmutableArray<ReferenceGraphNode> Nodes, ImmutableArray<ReferenceGraphEdge> Edges,
    int VisitedNodes, int ExaminedEdges, bool NodeLimitReached, bool EdgeLimitReached, bool DepthLimitReached);
public sealed record ReferenceImpactVisit(string Id, int Depth, string? Predecessor, ReferenceGraphEdge? Via);
public sealed record ReferenceImpactResult(ReferenceGraph Scope, ImmutableArray<ReferenceImpactVisit> Visits, int ExaminedEdges)
{
    public bool Complete => !Scope.Truncated;
    /// <summary>One shortest incoming proof, from the referrer back to a seed, in original reference direction.</summary>
    public ImmutableArray<ReferenceGraphEdge> PathToRoot(string id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Visits.IsDefault || Visits.Length > 1000 || Scope is null || Scope.Direction != ReferenceGraphDirection.Incoming ||
            Scope.Roots.IsDefault || Scope.Roots.Length > 200 || Scope.Nodes.IsDefault || Scope.Nodes.Length > 1000 ||
            Scope.Edges.IsDefault || Scope.Edges.Length > 4000) throw new InvalidOperationException("Invalid bounded impact proof forest.");
        var roots = Scope.Roots.ToHashSet(StringComparer.Ordinal);
        var objects = Scope.Nodes.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
        var references = Scope.Edges.ToHashSet();
        var visits = new Dictionary<string, ReferenceImpactVisit>(StringComparer.Ordinal);
        foreach (var visit in Visits)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (visit is null || string.IsNullOrEmpty(visit.Id) || !objects.Contains(visit.Id) || !visits.TryAdd(visit.Id, visit))
                throw new InvalidOperationException("Impact proof forest contains an invalid or duplicate object.");
        }
        if (!visits.ContainsKey(id)) throw new ArgumentException("Object is outside the impact exploration.", nameof(id));
        var path = ImmutableArray.CreateBuilder<ReferenceGraphEdge>(); var seen = new HashSet<string>(StringComparer.Ordinal);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!seen.Add(id) || !visits.TryGetValue(id, out var visit)) throw new InvalidOperationException("Impact proof contains a cycle or missing predecessor.");
            if (visit.Predecessor is not { } predecessor)
            {
                if (visit.Depth != 0 || visit.Via is not null || !roots.Contains(id)) throw new InvalidOperationException("Impact proof has an invalid root.");
                break;
            }
            if (visit.Via is not { } edge || edge.Source != id || edge.Target != predecessor || !references.Contains(edge) ||
                !visits.TryGetValue(predecessor, out var parent) || parent.Depth != visit.Depth - 1)
                throw new InvalidOperationException("Impact proof link does not connect adjacent predecessor depths.");
            path.Add(edge); id = predecessor;
        }
        return path.ToImmutable();
    }
}

public sealed partial class ReferenceGraphIndex
{
    /// <summary>Iterative Kosaraju analysis. Partial scopes retain real cycles but can split a larger component.</summary>
    public ReferenceComponentAnalysis StronglyConnectedComponents(ReferenceGraphOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var graph = Explore(options, cancellationToken);
        var ids = graph.Nodes.Select(n => n.Id).Order(StringComparer.Ordinal).ToArray();
        var positions = ids.Select((id, index) => (id, index)).ToDictionary(p => p.id, p => p.index, StringComparer.Ordinal);
        var indexed = IndexedGraphAnalysis.StrongComponents(ids.Length,
            graph.Edges.Select(edge => new IndexedGraphArc(positions[edge.Source], positions[edge.Target])).ToImmutableArray(), cancellationToken);
        var groups = Enumerable.Range(0, indexed.ComponentCount).Select(_ => new List<string>()).ToList();
        var membership = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < ids.Length; i++) { groups[indexed.Membership[i]].Add(ids[i]); membership.Add(ids[i], indexed.Membership[i]); }
        var internalEdges = new int[groups.Count]; var links = new Dictionary<(int Source, int Target), int>();
        foreach (var edge in graph.Edges)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var a = membership[edge.Source]; var b = membership[edge.Target];
            if (a == b) internalEdges[a]++;
            else links[(a, b)] = links.GetValueOrDefault((a, b)) + 1;
        }
        return new(graph, groups.Select((group, id) => new ReferenceComponent(id, group.ToImmutableArray(), internalEdges[id],
                group.Count > 1 || internalEdges[id] > 0)).ToImmutableArray(),
            links.OrderBy(p => p.Key.Source).ThenBy(p => p.Key.Target)
                .Select(p => new ReferenceCondensationEdge(p.Key.Source, p.Key.Target, p.Value)).ToImmutableArray());
    }

    /// <summary>Fewest-reference path. Exhaustion stops search instead of skipping candidates and claiming a longer shortest path.</summary>
    public ReferencePathResult ShortestPath(ObjectId source, ObjectId target, ReferencePathOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new();
        if (!snapshot.Objects.ContainsKey(target) && !incoming.ContainsKey(target.Value))
            throw new ArgumentException("Unknown reference path target.", nameof(target));
        var walk = Walk(new() { Roots = [source], Direction = options.Direction, MaximumDepth = options.MaximumDepth,
            MaximumNodes = options.MaximumNodes, MaximumEdges = options.MaximumEdges }, target.Value, cancellationToken);
        var found = walk.Visits.Any(v => v.Id == target.Value);
        if (!found) return new(walk.Scope.Truncated ? ReferencePathStatus.Incomplete : ReferencePathStatus.Unreachable,
            options.Direction, [], [], walk.Visits.Length, walk.ExaminedEdges, walk.Scope.NodeLimitReached,
            walk.Scope.EdgeLimitReached, walk.Scope.DepthLimitReached);
        var byId = walk.Visits.ToDictionary(v => v.Id, StringComparer.Ordinal);
        var ids = new List<string>(); var edges = new List<ReferenceGraphEdge>(); var current = target.Value;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested(); ids.Add(current);
            var visit = byId[current]; if (visit.Predecessor is null) break;
            edges.Add(visit.Via!); current = visit.Predecessor;
        }
        ids.Reverse(); edges.Reverse();
        var nodes = walk.Scope.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        return new(ReferencePathStatus.Found, options.Direction, ids.Select(id => nodes[id]).ToImmutableArray(), edges.ToImmutableArray(),
            walk.Visits.Length, walk.ExaminedEdges, false, false, false);
    }

    /// <summary>Incoming reachability with a compact shortest-proof forest. This is structural impact, not simulated edit effects.</summary>
    public ReferenceImpactResult Impact(ReferenceGraphOptions? options = null, CancellationToken cancellationToken = default) =>
        Walk((options ?? new()) with { Direction = ReferenceGraphDirection.Incoming }, null, cancellationToken);

    private ReferenceImpactResult Walk(ReferenceGraphOptions options, string? target, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (options.MaximumDepth is < 0 or > 16 || options.MaximumNodes is < 1 or > 1000 ||
            options.MaximumEdges is < 1 or > 4000 || !Enum.IsDefined(options.Direction)) throw new ArgumentOutOfRangeException(nameof(options));
        var roots = (options.Roots.IsDefaultOrEmpty ? [snapshot.Root] : options.Roots).Distinct().OrderBy(id => id.Value, StringComparer.Ordinal).ToArray();
        if (roots.Length > options.MaximumNodes || roots.Length > 200) throw new ArgumentException("Root count exceeds the reference graph node budget.");
        foreach (var root in roots) if (!snapshot.Objects.ContainsKey(root)) throw new ArgumentException("Unknown reference graph root: " + root.Value);
        var visits = roots.ToDictionary(id => id.Value, id => new ReferenceImpactVisit(id.Value, 0, null, null), StringComparer.Ordinal);
        var queue = new Queue<string>(visits.Keys); var edges = ImmutableArray.CreateBuilder<ReferenceGraphEdge>();
        var adjacency = options.Direction == ReferenceGraphDirection.Incoming ? incoming : outgoing;
        var examined = 0; var nodeLimit = false; var edgeLimit = false; var depthLimit = false;
        var found = target is not null && visits.ContainsKey(target);
        while (!found && queue.TryDequeue(out var id))
        {
            token.ThrowIfCancellationRequested();
            if (!adjacency.TryGetValue(id, out var adjacent)) continue;
            foreach (var edge in adjacent)
            {
                token.ThrowIfCancellationRequested();
                if (examined >= options.MaximumEdges) { edgeLimit = true; goto Finish; }
                examined++;
                var next = options.Direction == ReferenceGraphDirection.Incoming ? edge.Source : edge.Target;
                if (!visits.ContainsKey(next))
                {
                    if (visits[id].Depth >= options.MaximumDepth) { depthLimit = true; continue; }
                    if (visits.Count >= options.MaximumNodes) { nodeLimit = true; goto Finish; }
                    visits.Add(next, new(next, visits[id].Depth + 1, id, edge)); queue.Enqueue(next);
                }
                edges.Add(edge);
                if (target == next) { found = true; break; }
            }
        }
    Finish:
        var rootSet = roots.Select(id => id.Value).ToHashSet(StringComparer.Ordinal);
        var nodes = visits.Values.Select(visit =>
        {
            snapshot.Objects.TryGetValue(new(visit.Id), out var node);
            var name = (node?["Name"] as ShadowValue.Atom)?.Text; if (name?.Length > 180) name = name[..180];
            return new ReferenceGraphNode(visit.Id, node?.TypeName ?? "Missing object", node?.Kind.ToString() ?? "Missing", name,
                visit.Depth, rootSet.Contains(visit.Id), node is not null, incoming.GetValueOrDefault(visit.Id)?.Count ?? 0, outgoing.GetValueOrDefault(visit.Id)?.Count ?? 0);
        }).ToImmutableArray();
        return new(new(roots.Select(id => id.Value).ToImmutableArray(), options.Direction, nodes, edges.ToImmutable(),
            nodeLimit, edgeLimit, depthLimit, snapshot.Objects.Count), visits.Values.ToImmutableArray(), examined);
    }
}
