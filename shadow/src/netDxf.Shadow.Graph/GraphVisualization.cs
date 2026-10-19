using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace netDxf.Shadow.Graph;

/// <summary>Independent bounded node/edge pages. Filters retain global graph IDs and feature classifications.</summary>
public sealed record GraphVisualizationOptions
{
    public int NodeOffset { get; init; }
    public int NodeLimit { get; init; } = 2000;
    public int EdgeOffset { get; init; }
    public int EdgeLimit { get; init; } = 4000;
    public int? Component { get; init; }
    public ObjectId? Entity { get; init; }
    public ObjectId? RootEntity { get; init; }
    public string? Occurrence { get; init; }
}

public sealed record GraphVisualizationNode(int Id, double X, double Y, double Z, int Degree, int Component,
    bool DeadEnd, bool Junction, bool Isolated, bool ArticulationPoint);
/// <summary>Endpoints are embedded so an edge can be rendered even when its nodes are outside the current node page.</summary>
public sealed record GraphVisualizationEdge(int Id, int A, int B, Vec3 Start, Vec3 End, double Length,
    string Entity, string RootEntity, string Occurrence, ImmutableArray<string> InsertPath, int Component, bool Bridge);
public sealed record GraphVisualizationPage<T>(int Total, int Offset, ImmutableArray<T> Items, int? NextOffset);
/// <summary>Counts/features describe the full graph; page totals describe the filtered subset. Truncated means a page omits matching geometry.</summary>
public sealed record GraphVisualization(int NodeCount, int EdgeCount, int ComponentCount, int CycleRank,
    int BridgeCount, int ArticulationPointCount, int DeadEndCount, int JunctionCount, int IsolatedNodeCount,
    int DiagnosticCount, double TotalLength, int NodeLimit, int EdgeLimit,
    GraphVisualizationPage<GraphVisualizationNode> Nodes, GraphVisualizationPage<GraphVisualizationEdge> Edges, bool Truncated);

public sealed partial record TopologyGraph
{
    private sealed class VisualizationIndex
    {
        public readonly int[] Components;
        public readonly HashSet<int> Bridges, ArticulationPoints;
        public readonly int ComponentCount, DeadEnds, Junctions, Isolated;
        public readonly double Length;
        public VisualizationIndex(TopologyGraph graph, CancellationToken token)
        {
            var components = graph.ConnectedComponents(token); ComponentCount = components.Length;
            Components = new int[graph.Nodes.Length];
            for (var i = 0; i < components.Length; i++) foreach (var node in components[i]) { token.ThrowIfCancellationRequested(); Components[node] = i; }
            Bridges = graph.Bridges(token).ToHashSet(); ArticulationPoints = graph.ArticulationPoints(token).ToHashSet();
            foreach (var node in graph.Nodes)
            {
                token.ThrowIfCancellationRequested();
                if (node.Edges.IsEmpty) Isolated++;
                if (node.Edges.Length == 1) DeadEnds++;
                if (node.Edges.Length > 2) Junctions++;
            }
            foreach (var edge in graph.Edges) { token.ThrowIfCancellationRequested(); Length += edge.Length; }
        }
    }
    private static readonly ConditionalWeakTable<TopologyGraph, VisualizationIndex> visualizationIndices = new();

    /// <summary>Produces source-linked visualization pages using the existing component, bridge and articulation algorithms.</summary>
    public GraphVisualization Visualize(GraphVisualizationOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new(); cancellationToken.ThrowIfCancellationRequested();
        if (options.NodeOffset < 0 || options.EdgeOffset < 0 || options.NodeLimit is < 1 or > 5000 || options.EdgeLimit is < 1 or > 10000
            || options.Component is < 0 || options.Occurrence?.Length > 8192)
            throw new ArgumentOutOfRangeException(nameof(options), "Visualization pages exceed supported bounds.");
        var index = visualizationIndices.GetValue(this, graph => new(graph, cancellationToken));
        if (options.Component is { } component && component >= index.ComponentCount) throw new ArgumentException("Unknown topology component.");
        var filteredSources = options.Entity is not null || options.RootEntity is not null || options.Occurrence is not null;
        HashSet<int>? sourceNodes = filteredSources ? [] : null;
        bool Matches(GraphEdge edge) => (options.Component is null || index.Components[edge.A] == options.Component)
            && (options.Entity is null || edge.Entity == options.Entity) && (options.RootEntity is null || edge.RootEntity == options.RootEntity)
            && (options.Occurrence is null || edge.Occurrence == options.Occurrence);
        var edgeItems = ImmutableArray.CreateBuilder<GraphVisualizationEdge>(); var edgeTotal = 0;
        foreach (var edge in Edges)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Matches(edge)) continue;
            sourceNodes?.Add(edge.A); sourceNodes?.Add(edge.B);
            if (edgeTotal++ < options.EdgeOffset || edgeItems.Count >= options.EdgeLimit) continue;
            edgeItems.Add(new(edge.Id, edge.A, edge.B, Nodes[edge.A].Position, Nodes[edge.B].Position, edge.Length,
                edge.Entity.Value, edge.RootEntity.Value, edge.Occurrence,
                edge.InsertPath.IsDefaultOrEmpty ? [] : edge.InsertPath.Select(id => id.Value).ToImmutableArray(),
                index.Components[edge.A], index.Bridges.Contains(edge.Id)));
        }
        var nodeItems = ImmutableArray.CreateBuilder<GraphVisualizationNode>(); var nodeTotal = 0;
        foreach (var node in Nodes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (options.Component is { } selected && index.Components[node.Id] != selected || sourceNodes is not null && !sourceNodes.Contains(node.Id)) continue;
            if (nodeTotal++ < options.NodeOffset || nodeItems.Count >= options.NodeLimit) continue;
            nodeItems.Add(new(node.Id, node.Position.X, node.Position.Y, node.Position.Z, node.Edges.Length, index.Components[node.Id],
                node.Edges.Length == 1, node.Edges.Length > 2, node.Edges.IsEmpty, index.ArticulationPoints.Contains(node.Id)));
        }
        return new(Nodes.Length, Edges.Length, index.ComponentCount, Edges.Length - Nodes.Length + index.ComponentCount,
            index.Bridges.Count, index.ArticulationPoints.Count, index.DeadEnds, index.Junctions, index.Isolated, Diagnostics.Length,
            index.Length, options.NodeLimit, options.EdgeLimit,
            new(nodeTotal, options.NodeOffset, nodeItems.ToImmutable(), Next(nodeTotal, options.NodeOffset, nodeItems.Count)),
            new(edgeTotal, options.EdgeOffset, edgeItems.ToImmutable(), Next(edgeTotal, options.EdgeOffset, edgeItems.Count)),
            nodeItems.Count < nodeTotal || edgeItems.Count < edgeTotal);
        static int? Next(int total, int offset, int count) => offset < total && count < total - offset ? offset + count : null;
    }
}
