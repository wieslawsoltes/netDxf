using System.Collections.Immutable;

namespace netDxf.Shadow.Graph;

public readonly record struct IndexedGraphArc(int Source, int Target);
public readonly record struct IndexedGraphConnection(int A, int B);
public sealed record IndexedStrongComponents(ImmutableArray<int> Membership, int ComponentCount);
public sealed record IndexedLowLinks(ImmutableArray<int> Bridges, ImmutableArray<int> ArticulationPoints);

/// <summary>Geometry-independent, iterative graph kernels. Node and connection identities are their input indices.</summary>
public static class IndexedGraphAnalysis
{
    public const int MaximumNodes = 50000;
    public const int MaximumConnections = 100000;
    public const int MaximumArcs = 200000;

    /// <summary>Exact SCC membership, with component numbers ordered by their smallest node index.</summary>
    public static IndexedStrongComponents StrongComponents(int nodeCount, ImmutableArray<IndexedGraphArc> arcs,
        CancellationToken cancellationToken = default)
    {
        Validate(nodeCount, arcs.IsDefault ? -1 : arcs.Length, MaximumArcs); var token = cancellationToken;
        token.ThrowIfCancellationRequested();
        var outgoing = Adjacency(nodeCount); var incoming = Adjacency(nodeCount);
        foreach (var arc in arcs)
        {
            token.ThrowIfCancellationRequested(); Endpoint(arc.Source, nodeCount); Endpoint(arc.Target, nodeCount);
            outgoing[arc.Source].Add(arc.Target); incoming[arc.Target].Add(arc.Source);
        }
        var seen = new bool[nodeCount]; var finished = new List<int>(nodeCount);
        for (var start = 0; start < nodeCount; start++)
        {
            token.ThrowIfCancellationRequested(); if (seen[start]) continue;
            seen[start] = true; var stack = new Stack<(int Node, int Next)>(); stack.Push((start, 0));
            while (stack.TryPop(out var frame))
            {
                token.ThrowIfCancellationRequested();
                if (frame.Next == outgoing[frame.Node].Count) { finished.Add(frame.Node); continue; }
                stack.Push((frame.Node, frame.Next + 1)); var target = outgoing[frame.Node][frame.Next];
                if (!seen[target]) { seen[target] = true; stack.Push((target, 0)); }
            }
        }
        Array.Clear(seen); var groups = new List<List<int>>();
        for (var i = finished.Count - 1; i >= 0; i--)
        {
            token.ThrowIfCancellationRequested(); if (seen[finished[i]]) continue;
            seen[finished[i]] = true; var members = new List<int>(); var pending = new Stack<int>(); pending.Push(finished[i]);
            while (pending.TryPop(out var node))
            {
                token.ThrowIfCancellationRequested(); members.Add(node);
                foreach (var source in incoming[node]) if (!seen[source]) { seen[source] = true; pending.Push(source); }
            }
            members.Sort(); groups.Add(members);
        }
        groups.Sort((a, b) => a[0].CompareTo(b[0])); var membership = new int[nodeCount];
        for (var group = 0; group < groups.Count; group++) foreach (var node in groups[group]) membership[node] = group;
        return new(membership.ToImmutableArray(), groups.Count);
    }

    /// <summary>Weak bridges and articulation nodes. Parallel connections and self-loops retain multigraph semantics.</summary>
    public static IndexedLowLinks LowLinks(int nodeCount, ImmutableArray<IndexedGraphConnection> connections,
        CancellationToken cancellationToken = default)
    {
        Validate(nodeCount, connections.IsDefault ? -1 : connections.Length, MaximumConnections); var token = cancellationToken;
        token.ThrowIfCancellationRequested(); var adjacent = Adjacency(nodeCount);
        for (var id = 0; id < connections.Length; id++)
        {
            token.ThrowIfCancellationRequested(); var edge = connections[id]; Endpoint(edge.A, nodeCount); Endpoint(edge.B, nodeCount);
            adjacent[edge.A].Add(id); adjacent[edge.B].Add(id);
        }
        var discovery = new int[nodeCount]; var low = new int[nodeCount]; var parent = Enumerable.Repeat(-1, nodeCount).ToArray();
        var nextEdge = new int[nodeCount]; var children = new int[nodeCount]; var cuts = new bool[nodeCount];
        var stack = new Stack<int>(); var bridges = ImmutableArray.CreateBuilder<int>(); var time = 0;
        for (var root = 0; root < nodeCount; root++)
        {
            token.ThrowIfCancellationRequested(); if (discovery[root] != 0) continue;
            discovery[root] = low[root] = ++time; stack.Push(root);
            while (stack.Count > 0)
            {
                token.ThrowIfCancellationRequested(); var node = stack.Peek();
                if (nextEdge[node] < adjacent[node].Count)
                {
                    var id = adjacent[node][nextEdge[node]++]; if (id == parent[node]) continue;
                    var edge = connections[id]; var next = edge.A == node ? edge.B : edge.A;
                    if (discovery[next] != 0) { low[node] = Math.Min(low[node], discovery[next]); continue; }
                    children[node]++; parent[next] = id; discovery[next] = low[next] = ++time; stack.Push(next);
                }
                else
                {
                    stack.Pop();
                    if (parent[node] < 0) { cuts[node] = children[node] > 1; continue; }
                    var edge = connections[parent[node]]; var ancestor = edge.A == node ? edge.B : edge.A;
                    low[ancestor] = Math.Min(low[ancestor], low[node]);
                    if (low[node] > discovery[ancestor]) bridges.Add(parent[node]);
                    if (parent[ancestor] >= 0 && low[node] >= discovery[ancestor]) cuts[ancestor] = true;
                }
            }
        }
        return new(bridges.ToImmutable(), Enumerable.Range(0, nodeCount).Where(n => cuts[n]).ToImmutableArray());
    }
    private static List<int>[] Adjacency(int count) => Enumerable.Range(0, count).Select(_ => new List<int>()).ToArray();
    private static void Endpoint(int id, int count)
    {
        if ((uint)id >= (uint)count) throw new ArgumentException("Graph endpoint is outside the indexed node range.");
    }
    private static void Validate(int nodes, int edges, int maximum)
    {
        if (nodes is < 0 or > MaximumNodes || edges < 0 || edges > maximum)
            throw new ArgumentOutOfRangeException(nameof(nodes), "Indexed graph exceeds the bounded node or edge range.");
    }
}
