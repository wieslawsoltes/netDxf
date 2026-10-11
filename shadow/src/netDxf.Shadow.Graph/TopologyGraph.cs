using System.Collections.Immutable;

namespace netDxf.Shadow.Graph;

public sealed record GraphNode(int Id, Vec3 Position, ImmutableArray<int> Edges);
public sealed record GraphEdge(int Id, int A, int B, double Length, ObjectId Entity, string Occurrence);
public sealed record GraphPath(ImmutableArray<int> Nodes, ImmutableArray<int> Edges, double Length);
public sealed record TopologyGraph(Guid DocumentId, long Revision, ImmutableArray<GraphNode> Nodes,
    ImmutableArray<GraphEdge> Edges, ImmutableArray<ShadowDiagnostic> Diagnostics)
{
    public ImmutableArray<ImmutableArray<int>> ConnectedComponents()
    {
        var seen = new bool[Nodes.Length];
        var result = ImmutableArray.CreateBuilder<ImmutableArray<int>>();
        var stack = new Stack<int>();
        for (var start = 0; start < Nodes.Length; start++)
        {
            if (seen[start]) continue;
            var component = ImmutableArray.CreateBuilder<int>();
            seen[start] = true; stack.Push(start);
            while (stack.TryPop(out var node))
            {
                component.Add(node);
                foreach (var id in Nodes[node].Edges)
                {
                    var edge = Edges[id]; var next = edge.A == node ? edge.B : edge.A;
                    if (!seen[next]) { seen[next] = true; stack.Push(next); }
                }
            }
            result.Add(component.ToImmutable());
        }
        return result.ToImmutable();
    }
    public int CycleRank => Edges.Length - Nodes.Length + ConnectedComponents().Length;
    public IEnumerable<GraphNode> DeadEnds => Nodes.Where(n => n.Edges.Length == 1);
    public IEnumerable<GraphNode> Junctions => Nodes.Where(n => n.Edges.Length > 2);

    public GraphPath? ShortestPath(int source, int target)
    {
        if ((uint)source >= (uint)Nodes.Length || (uint)target >= (uint)Nodes.Length) throw new ArgumentOutOfRangeException(nameof(source));
        var distance = Enumerable.Repeat(double.PositiveInfinity, Nodes.Length).ToArray();
        var previous = Enumerable.Repeat(-1, Nodes.Length).ToArray();
        var queue = new PriorityQueue<int, double>(); distance[source] = 0; queue.Enqueue(source, 0);
        while (queue.TryDequeue(out var node, out var cost))
        {
            if (cost > distance[node]) continue;
            if (node == target) break;
            foreach (var id in Nodes[node].Edges)
            {
                var edge = Edges[id]; var next = edge.A == node ? edge.B : edge.A;
                var candidate = cost + edge.Length;
                if (candidate >= distance[next]) continue;
                distance[next] = candidate; previous[next] = id; queue.Enqueue(next, candidate);
            }
        }
        if (!double.IsFinite(distance[target])) return null;
        var path = new List<int> { target }; var edges = new List<int>();
        for (var node = target; node != source;)
        {
            var id = previous[node]; edges.Add(id); var edge = Edges[id]; node = edge.A == node ? edge.B : edge.A; path.Add(node);
        }
        path.Reverse(); edges.Reverse(); return new(path.ToImmutableArray(), edges.ToImmutableArray(), distance[target]);
    }

    /// <summary>Iterative Tarjan traversal, including correct handling of parallel edges.</summary>
    public ImmutableArray<int> Bridges()
    {
        var discovery = new int[Nodes.Length]; var low = new int[Nodes.Length]; var parent = Enumerable.Repeat(-1, Nodes.Length).ToArray();
        var nextEdge = new int[Nodes.Length]; var time = 0; var result = ImmutableArray.CreateBuilder<int>(); var stack = new Stack<int>();
        for (var root = 0; root < Nodes.Length; root++)
        {
            if (discovery[root] != 0) continue;
            discovery[root] = low[root] = ++time; stack.Push(root);
            while (stack.Count > 0)
            {
                var node = stack.Peek();
                if (nextEdge[node] < Nodes[node].Edges.Length)
                {
                    var id = Nodes[node].Edges[nextEdge[node]++];
                    if (id == parent[node]) continue;
                    var edge = Edges[id]; var other = edge.A == node ? edge.B : edge.A;
                    if (discovery[other] == 0) { parent[other] = id; discovery[other] = low[other] = ++time; stack.Push(other); }
                    else low[node] = Math.Min(low[node], discovery[other]);
                }
                else
                {
                    stack.Pop();
                    if (parent[node] >= 0)
                    {
                        var edge = Edges[parent[node]]; var other = edge.A == node ? edge.B : edge.A;
                        low[other] = Math.Min(low[other], low[node]);
                        if (low[node] > discovery[other]) result.Add(edge.Id);
                    }
                }
            }
        }
        return result.ToImmutable();
    }
}
