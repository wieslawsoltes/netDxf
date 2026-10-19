using System.Collections.Immutable;

namespace netDxf.Shadow.Graph;

/// <summary>A fundamental cycle closed by one non-tree edge. IDs are local to the immutable graph.</summary>
public sealed record GraphCycle(int Id, int ClosingEdge, int Start, int Ancestor, int EdgeCount);

/// <summary>
/// Linear-space index of an independent cycle basis, including self loops and parallel edges.
/// Building the index does not materialize every path: a dense graph can have a quadratic
/// number of cycle-edge memberships. Expand only the cycles required by the caller.
/// </summary>
public sealed class GraphCycleBasis
{
    private readonly TopologyGraph graph;
    private readonly ImmutableArray<int> parentEdges;
    public ImmutableArray<GraphCycle> Cycles { get; }

    internal GraphCycleBasis(TopologyGraph graph, ImmutableArray<int> parentEdges, ImmutableArray<GraphCycle> cycles)
    { this.graph = graph; this.parentEdges = parentEdges; Cycles = cycles; }

    /// <summary>Expand one ordered closed path, including its repeated final node. No recursion is used.</summary>
    public GraphPath Path(int cycle, CancellationToken cancellationToken = default)
    {
        if ((uint)cycle >= (uint)Cycles.Length) throw new ArgumentOutOfRangeException(nameof(cycle));
        cancellationToken.ThrowIfCancellationRequested();
        var descriptor = Cycles[cycle];
        var nodes = ImmutableArray.CreateBuilder<int>(descriptor.EdgeCount + 1);
        var edges = ImmutableArray.CreateBuilder<int>(descriptor.EdgeCount);
        var node = descriptor.Start; var length = 0.0; nodes.Add(node);
        while (node != descriptor.Ancestor)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var edge = graph.Edges[parentEdges[node]];
            edges.Add(edge.Id); length += edge.Length;
            node = edge.A == node ? edge.B : edge.A; nodes.Add(node);
        }
        var closing = graph.Edges[descriptor.ClosingEdge]; edges.Add(closing.Id); length += closing.Length;
        nodes.Add(descriptor.Start);
        return new(nodes.MoveToImmutable(), edges.MoveToImmutable(), length);
    }
}

public sealed partial record TopologyGraph
{
    /// <summary>
    /// Deterministic depth-first fundamental cycle basis in O(nodes + edges) time and memory.
    /// Each non-tree edge closes a path to a DFS ancestor; cycles are ordered by closing edge ID.
    /// This is an independent basis, not an enumeration of all simple cycles or a minimum basis.
    /// </summary>
    public GraphCycleBasis CycleBasis(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var seen = new bool[Nodes.Length]; var edgeSeen = new bool[Edges.Length];
        var parent = Enumerable.Repeat(-1, Nodes.Length).ToArray();
        var depth = new int[Nodes.Length]; var next = new int[Nodes.Length];
        var stack = new Stack<int>(); var closing = new GraphCycle?[Edges.Length];
        for (var root = 0; root < Nodes.Length; root++)
        {
            if (seen[root]) continue;
            seen[root] = true; stack.Push(root);
            while (stack.TryPeek(out var node))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (next[node] == Nodes[node].Edges.Length) { stack.Pop(); continue; }
                var edgeId = Nodes[node].Edges[next[node]++];
                if (edgeSeen[edgeId]) continue;
                edgeSeen[edgeId] = true;
                var edge = Edges[edgeId]; var other = edge.A == node ? edge.B : edge.A;
                if (!seen[other])
                {
                    seen[other] = true; parent[other] = edgeId; depth[other] = depth[node] + 1;
                    stack.Push(other);
                }
                else closing[edgeId] = new(0, edgeId, node, other, depth[node] - depth[other] + 1);
            }
        }
        var cycles = ImmutableArray.CreateBuilder<GraphCycle>();
        foreach (var cycle in closing)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (cycle is not null) cycles.Add(cycle with { Id = cycles.Count });
        }
        return new(this, parent.ToImmutableArray(), cycles.ToImmutable());
    }
}
