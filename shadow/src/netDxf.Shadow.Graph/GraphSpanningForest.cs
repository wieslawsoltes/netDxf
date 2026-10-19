using System.Collections.Immutable;

namespace netDxf.Shadow.Graph;

public sealed record GraphSpanningForest(ImmutableArray<int> Edges, int ComponentCount, double Length);

public sealed partial record TopologyGraph
{
    /// <summary>
    /// Kruskal minimum spanning forest weighted by sampled length. Isolated nodes remain components.
    /// Equal weights are ordered by edge ID, parallel edges compete normally and self loops are excluded.
    /// </summary>
    public GraphSpanningForest MinimumSpanningForest(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var parents = Enumerable.Range(0, Nodes.Length).ToArray(); var ranks = new byte[Nodes.Length];
        var chosen = ImmutableArray.CreateBuilder<int>(); var components = Nodes.Length; var length = 0.0;
        int Root(int node)
        {
            while (parents[node] != node) { parents[node] = parents[parents[node]]; node = parents[node]; }
            return node;
        }
        // Validate weights before sorting, including edges that might otherwise be discarded.
        foreach (var edge in Edges)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!double.IsFinite(edge.Length) || edge.Length < 0)
                throw new ArgumentException("Topology edge lengths must be finite and nonnegative.");
        }
        foreach (var edge in Edges.OrderBy(e => e.Length).ThenBy(e => e.Id))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var a = Root(edge.A); var b = Root(edge.B); if (a == b) continue;
            if (ranks[a] < ranks[b]) (a, b) = (b, a);
            parents[b] = a; if (ranks[a] == ranks[b]) ranks[a]++;
            chosen.Add(edge.Id); components--; length += edge.Length;
        }
        return new(chosen.ToImmutable(), components, length);
    }
}
