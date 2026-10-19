using System.Collections.Immutable;

namespace netDxf.Shadow.Graph;

public sealed record GraphVisit(int Node, int Depth, int? ViaEdge);
public sealed record GraphTraversal(ImmutableArray<GraphVisit> Visits, bool NodeLimitReached, bool DepthLimitReached);

public sealed partial record TopologyGraph
{
    /// <summary>Bounded breadth-first traversal. Each node is queued once; both limits are reported independently.</summary>
    public GraphTraversal Traverse(int source, int maximumDepth = 8, int maximumNodes = 200, CancellationToken cancellationToken = default)
    {
        if ((uint)source >= (uint)Nodes.Length) throw new ArgumentOutOfRangeException(nameof(source));
        if (maximumDepth < 0) throw new ArgumentOutOfRangeException(nameof(maximumDepth));
        if (maximumNodes < 1) throw new ArgumentOutOfRangeException(nameof(maximumNodes));
        var visited = new HashSet<int> { source };
        var pending = new Queue<GraphVisit>(); pending.Enqueue(new(source, 0, null));
        var result = ImmutableArray.CreateBuilder<GraphVisit>();
        var nodeLimit = false; var depthLimit = false;
        while (pending.TryDequeue(out var visit))
        {
            cancellationToken.ThrowIfCancellationRequested(); result.Add(visit);
            foreach (var id in Nodes[visit.Node].Edges)
            {
                var edge = Edges[id]; var next = edge.A == visit.Node ? edge.B : edge.A;
                if (visited.Contains(next)) continue;
                if (visit.Depth == maximumDepth) { depthLimit = true; continue; }
                if (visited.Count == maximumNodes) { nodeLimit = true; continue; }
                visited.Add(next); pending.Enqueue(new(next, visit.Depth + 1, id));
            }
        }
        return new(result.ToImmutable(), nodeLimit, depthLimit);
    }

    /// <summary>Iterative articulation-point search. Parallel edges and self loops retain their multigraph semantics.</summary>
    public ImmutableArray<int> ArticulationPoints(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var discovery = new int[Nodes.Length]; var low = new int[Nodes.Length];
        var parentEdge = Enumerable.Repeat(-1, Nodes.Length).ToArray();
        var nextEdge = new int[Nodes.Length]; var children = new int[Nodes.Length]; var cuts = new bool[Nodes.Length];
        var stack = new Stack<int>(); var time = 0;
        for (var root = 0; root < Nodes.Length; root++)
        {
            if (discovery[root] != 0) continue;
            discovery[root] = low[root] = ++time; stack.Push(root);
            while (stack.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested(); var node = stack.Peek();
                if (nextEdge[node] < Nodes[node].Edges.Length)
                {
                    var id = Nodes[node].Edges[nextEdge[node]++]; if (id == parentEdge[node]) continue;
                    var edge = Edges[id]; var next = edge.A == node ? edge.B : edge.A;
                    if (discovery[next] != 0) { low[node] = Math.Min(low[node], discovery[next]); continue; }
                    children[node]++; parentEdge[next] = id; discovery[next] = low[next] = ++time; stack.Push(next);
                }
                else
                {
                    stack.Pop();
                    if (parentEdge[node] < 0) { cuts[node] = children[node] > 1; continue; }
                    var edge = Edges[parentEdge[node]]; var parent = edge.A == node ? edge.B : edge.A;
                    low[parent] = Math.Min(low[parent], low[node]);
                    if (parentEdge[parent] >= 0 && low[node] >= discovery[parent]) cuts[parent] = true;
                }
            }
        }
        return Enumerable.Range(0, Nodes.Length).Where(n => cuts[n]).ToImmutableArray();
    }
}
