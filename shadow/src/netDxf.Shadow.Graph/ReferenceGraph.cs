using System.Collections.Immutable;
using System.Globalization;

namespace netDxf.Shadow.Graph;

public enum ReferenceGraphDirection { Outgoing, Incoming }
public sealed record ReferenceGraphOptions
{
    public ImmutableArray<ObjectId> Roots { get; init; } = [];
    public ReferenceGraphDirection Direction { get; init; }
    public int MaximumDepth { get; init; } = 2;
    public int MaximumNodes { get; init; } = 100;
    public int MaximumEdges { get; init; } = 500;
}
public sealed record ReferenceGraphNode(string Id, string Type, string Kind, string? Name,
    int Depth, bool Root, bool Exists, int Incoming, int Outgoing);
/// <summary>A directed captured reference with its exact JSON Pointer path, including collection indices.</summary>
public sealed record ReferenceGraphEdge(string Source, string Path, string Target);
public sealed record ReferenceGraph(ImmutableArray<string> Roots, ReferenceGraphDirection Direction,
    ImmutableArray<ReferenceGraphNode> Nodes, ImmutableArray<ReferenceGraphEdge> Edges,
    bool NodeLimitReached, bool EdgeLimitReached, bool DepthLimitReached, int ObjectsIndexed)
{
    public bool Truncated => NodeLimitReached || EdgeLimitReached || DepthLimitReached;
}

/// <summary>
/// Immutable source-reference index. It describes captured object relationships, never drawing
/// geometry or inferred process connectivity. Build limits fail explicitly instead of omitting edges.
/// </summary>
public sealed partial class ReferenceGraphIndex
{
    private readonly ShadowSnapshot snapshot;
    private readonly Dictionary<string, List<ReferenceGraphEdge>> outgoing = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<ReferenceGraphEdge>> incoming = new(StringComparer.Ordinal);

    public ReferenceGraphIndex(ShadowSnapshot snapshot, int maximumObjects = 100000,
        int maximumValues = 2000000, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (maximumObjects is < 1 or > 1000000 || maximumValues is < 1 or > 10000000)
            throw new ArgumentOutOfRangeException(nameof(maximumObjects));
        cancellationToken.ThrowIfCancellationRequested();
        if (snapshot.Objects.Count > maximumObjects)
            throw new InvalidOperationException("Reference graph object-index budget exceeded.");
        this.snapshot = snapshot;
        var scheduled = 0;
        long pathCharacters = 0;
        foreach (var node in snapshot.Objects.Values.OrderBy(n => n.Id.Value, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pending = new Stack<(string Path, ShadowValue Value)>();
            if (node.Properties.Count > maximumValues - scheduled)
                throw new InvalidOperationException("Reference graph value-index budget exceeded.");
            foreach (var property in node.Properties.OrderByDescending(p => p.Key, StringComparer.Ordinal))
                Schedule("", property.Key, property.Value);
            while (pending.TryPop(out var item))
            {
                cancellationToken.ThrowIfCancellationRequested();
                switch (item.Value)
                {
                    case ShadowValue.Reference reference:
                        var edge = new ReferenceGraphEdge(node.Id.Value, item.Path, reference.Id.Value);
                        Add(outgoing, edge.Source, edge); Add(incoming, edge.Target, edge);
                        break;
                    case ShadowValue.Sequence sequence:
                        if (sequence.Items.Length > maximumValues - scheduled)
                            throw new InvalidOperationException("Reference graph value-index budget exceeded.");
                        for (var i = sequence.Items.Length - 1; i >= 0; i--)
                            Schedule(item.Path, i.ToString(CultureInfo.InvariantCulture), sequence.Items[i]);
                        break;
                    case ShadowValue.Structure structure:
                        if (structure.Fields.Count > maximumValues - scheduled)
                            throw new InvalidOperationException("Reference graph value-index budget exceeded.");
                        foreach (var field in structure.Fields.OrderByDescending(p => p.Key, StringComparer.Ordinal))
                            Schedule(item.Path, field.Key, field.Value);
                        break;
                }
            }
            void Schedule(string parent, string segment, ShadowValue value)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++scheduled > maximumValues) throw new InvalidOperationException("Reference graph value-index budget exceeded.");
                if (parent.Length + 1L + segment.Length > 8192)
                    throw new InvalidOperationException("Reference graph property path exceeds 8192 characters.");
                var escaped = Escape(segment);
                var length = parent.Length + 1 + escaped.Length;
                if (length > 8192) throw new InvalidOperationException("Reference graph property path exceeds 8192 characters.");
                // Bound queued and retained path text together, before allocating the joined path.
                // A value-count limit alone permits millions of long repeated prefixes.
                pathCharacters += length;
                if (pathCharacters > 16 * 1024 * 1024)
                    throw new InvalidOperationException("Reference graph path-character index budget exceeded.");
                pending.Push((parent + "/" + escaped, value));
            }
        }
        static void Add(Dictionary<string, List<ReferenceGraphEdge>> map, string key, ReferenceGraphEdge edge)
        { if (!map.TryGetValue(key, out var list)) map[key] = list = []; list.Add(edge); }
    }

    public ReferenceGraph Explore(ReferenceGraphOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new();
        cancellationToken.ThrowIfCancellationRequested();
        if (options.MaximumDepth is < 0 or > 16 || options.MaximumNodes is < 1 or > 1000
            || options.MaximumEdges is < 1 or > 4000 || !Enum.IsDefined(options.Direction))
            throw new ArgumentOutOfRangeException(nameof(options));
        var roots = (options.Roots.IsDefaultOrEmpty ? [snapshot.Root] : options.Roots)
            .Distinct().OrderBy(id => id.Value, StringComparer.Ordinal).ToArray();
        if (roots.Length > options.MaximumNodes || roots.Length > 200)
            throw new ArgumentException("Root count exceeds the reference graph node budget.");
        foreach (var id in roots)
            if (!snapshot.Objects.ContainsKey(id)) throw new ArgumentException("Unknown reference graph root: " + id.Value);
        var depth = roots.ToDictionary(id => id.Value, _ => 0, StringComparer.Ordinal);
        var rootSet = roots.Select(id => id.Value).ToHashSet(StringComparer.Ordinal);
        var queue = new Queue<string>(rootSet.Order(StringComparer.Ordinal));
        var edges = ImmutableArray.CreateBuilder<ReferenceGraphEdge>();
        var seenEdges = new HashSet<ReferenceGraphEdge>();
        var nodeLimit = false; var edgeLimit = false; var depthLimit = false;
        var adjacency = options.Direction == ReferenceGraphDirection.Incoming ? incoming : outgoing;
        while (queue.TryDequeue(out var id))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!adjacency.TryGetValue(id, out var adjacent)) continue;
            foreach (var edge in adjacent)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var next = options.Direction == ReferenceGraphDirection.Incoming ? edge.Source : edge.Target;
                if (!depth.ContainsKey(next))
                {
                    if (depth[id] >= options.MaximumDepth) { depthLimit = true; continue; }
                    if (depth.Count >= options.MaximumNodes) { nodeLimit = true; continue; }
                    depth.Add(next, depth[id] + 1); queue.Enqueue(next);
                }
                if (!seenEdges.Add(edge)) continue;
                if (edges.Count >= options.MaximumEdges) { edgeLimit = true; continue; }
                edges.Add(edge);
            }
        }
        var nodes = depth.Select(item =>
        {
            snapshot.Objects.TryGetValue(new(item.Key), out var node);
            var name = (node?["Name"] as ShadowValue.Atom)?.Text;
            if (name?.Length > 180) name = name[..180];
            return new ReferenceGraphNode(item.Key, node?.TypeName ?? "Missing object", node?.Kind.ToString() ?? "Missing",
                name, item.Value, rootSet.Contains(item.Key), node is not null,
                incoming.GetValueOrDefault(item.Key)?.Count ?? 0, outgoing.GetValueOrDefault(item.Key)?.Count ?? 0);
        }).ToImmutableArray();
        return new(roots.Select(id => id.Value).ToImmutableArray(), options.Direction, nodes, edges.ToImmutable(),
            nodeLimit, edgeLimit, depthLimit, snapshot.Objects.Count);
    }

    private static string Escape(string value) => value.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
}
