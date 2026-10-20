using System.Collections.Immutable;

namespace netDxf.Shadow.Symbols;

public sealed record DomainConnectivityOptions(int MaximumObjects = 10000, int MaximumNodes = 20000,
    int MaximumEdges = 40000, int MaximumIssues = 500, double ConnectionTolerance = .01);
public sealed record DomainPortNode(string Id, string Key, string Port, ObjectId Entity, string Module, string Class,
    string Tag, string Kind, Vec3 Position, string? NominalSize, bool Open, bool PassagesDeclared, int Component = 0, int Degree = 0);
public sealed record DomainConnection(string Id, string Source, string Target, string Kind, string Direction,
    bool Reciprocal, double Span);
public sealed record DomainConnectivityIssue(string Code, string Severity, string Message, ImmutableArray<ObjectId> Entities,
    ImmutableArray<string> Nodes);
public sealed record DomainNetworkComponent(int Id, int NodeCount, int EdgeCount, int EntityCount, int OpenPortCount);

/// <summary>
/// A bounded graph of declared ports and passages in one active layout and discipline. Symbol strokes,
/// coincident coordinates and geometric crossings never create links. Declared connectivity is not process simulation.
/// </summary>
public sealed partial class DomainConnectivity
{
    public const int MaximumPortIdLength = 2048;
    public Guid DocumentId { get; }
    public long Revision { get; }
    public string Module { get; }
    public string Layout { get; }
    public ImmutableArray<DomainPortNode> Nodes { get; }
    public ImmutableArray<DomainConnection> Edges { get; }
    public ImmutableArray<DomainNetworkComponent> Components { get; }
    public ImmutableArray<DomainConnectivityIssue> Issues { get; }
    public int ObjectsVisited { get; }
    public int RecordCount { get; }
    public bool Complete { get; }
    public bool DeclarationsComplete { get; }
    public bool IssuesComplete { get; }
    public ImmutableArray<string> Limitations { get; }
    private readonly ImmutableDictionary<string, DomainPortNode> nodesById;
    private readonly ImmutableDictionary<string, ImmutableArray<DomainConnection>> adjacency;
    private DomainConnectivity(ShadowSnapshot snapshot, string module, string layout, IEnumerable<DomainPortNode> nodes,
        IEnumerable<DomainConnection> edges, IEnumerable<DomainConnectivityIssue> issues, int visited, int records,
        bool complete, bool declarationsComplete, bool issuesComplete, IEnumerable<string> limitations, CancellationToken token)
    {
        DocumentId = snapshot.DocumentId; Revision = snapshot.Revision; Module = module; Layout = layout;
        Edges = edges.OrderBy(e => e.Id, StringComparer.Ordinal).ToImmutableArray();
        var map = nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        var links = map.Keys.ToDictionary(id => id, _ => new List<DomainConnection>(), StringComparer.Ordinal);
        foreach (var edge in Edges) { token.ThrowIfCancellationRequested(); links[edge.Source].Add(edge); links[edge.Target].Add(edge); }
        var components = ImmutableArray.CreateBuilder<DomainNetworkComponent>(); var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in map.Keys.OrderBy(id => id, StringComparer.Ordinal).ToArray())
        {
            token.ThrowIfCancellationRequested(); if (!seen.Add(id)) continue;
            var number = components.Count; var queue = new Queue<string>(); queue.Enqueue(id);
            var members = new List<string>(); var sourceIds = new HashSet<ObjectId>(); var degree = 0; var open = 0;
            while (queue.TryDequeue(out var current))
            {
                token.ThrowIfCancellationRequested(); var node = map[current]; members.Add(current); sourceIds.Add(node.Entity);
                degree += links[current].Count; if (node.Open) open++;
                foreach (var edge in links[current]) { var next = edge.Source == current ? edge.Target : edge.Source; if (seen.Add(next)) queue.Enqueue(next); }
            }
            foreach (var member in members) map[member] = map[member] with { Component = number, Degree = links[member].Count };
            components.Add(new(number, members.Count, degree / 2, sourceIds.Count, open));
        }
        Nodes = map.Values.OrderBy(n => n.Id, StringComparer.Ordinal).ToImmutableArray(); Components = components.ToImmutable();
        nodesById = map.ToImmutableDictionary(StringComparer.Ordinal);
        adjacency = links.ToImmutableDictionary(p => p.Key, p => p.Value.OrderBy(e => e.Id, StringComparer.Ordinal).ToImmutableArray(), StringComparer.Ordinal);
        Issues = issues.ToImmutableArray(); ObjectsVisited = visited; RecordCount = records; Complete = complete;
        DeclarationsComplete = declarationsComplete; IssuesComplete = issuesComplete; Limitations = limitations.Distinct().ToImmutableArray();
    }
    public static string PortId(string key, string port) => Uri.EscapeDataString(key) + "/" + Uri.EscapeDataString(port);
    public static DomainConnectivity Build(ShadowSnapshot snapshot, string module, string layout = "Model",
        DomainConnectivityOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot); options ??= new();
        if (string.IsNullOrWhiteSpace(module) || module.Length > 32) throw new ArgumentException("A bounded discipline ID is required.");
        if (options.MaximumObjects is < 1 or > 100000 || options.MaximumNodes is < 1 or > 50000 || options.MaximumEdges is < 1 or > 100000 ||
            options.MaximumIssues is < 1 or > 5000 || !double.IsFinite(options.ConnectionTolerance) || options.ConnectionTolerance is <= 0 or > 1e6)
            throw new ArgumentOutOfRangeException(nameof(options));
        if (!snapshot.Layout(layout).Exists) throw new ArgumentException("Unknown layout.");
        var token = cancellationToken; token.ThrowIfCancellationRequested();
        var records = new List<DomainEntity>(); var keyCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var issues = new List<DomainConnectivityIssue>(); var limits = new List<string>();
        var edgeLimited = false; var complete = true; var declarations = true; var issuesComplete = true; var visited = 0; var portCount = 0; var metadataCost = 0L;
        void Issue(string code, string severity, string message, IEnumerable<ObjectId> entities, params string[] nodeIds)
        {
            if (issues.Count < options.MaximumIssues) issues.Add(new(code, severity, message, entities.Distinct().ToImmutableArray(), nodeIds.ToImmutableArray()));
            else issuesComplete = false;
        }
        foreach (var entity in snapshot.DrawingEntities(layout))
        {
            token.ThrowIfCancellationRequested();
            if (visited == options.MaximumObjects) { complete = false; limits.Add("Object scan limit reached; unscanned objects and relations are absent."); break; }
            visited++;
            try
            {
                var data = DomainMetadata.Read(entity); if (data is null) continue;
                keyCounts[data.Key] = keyCounts.GetValueOrDefault(data.Key) + 1;
                if (data.Module != module) continue;
                // Bound retained declarations as well as emitted graph nodes. This prevents zero-port records and
                // long imported properties from defeating the node cap before projection begins.
                metadataCost += data.Key.Length + data.Tag.Length + (data.Properties?.Sum(p => p.Key.Length + p.Value.Length) ?? 0) +
                    data.Ports.Sum(p => p.Id.Length + p.Kind.Length + 128) + data.References.Sum(r => r.PortId.Length + r.TargetKey.Length + r.TargetPortId.Length + 32);
                if (metadataCost > 8_000_000 || portCount + data.Ports.Length > options.MaximumNodes)
                { complete = false; limits.Add("Port or metadata retention limit reached; remaining records are absent."); break; }
                portCount += data.Ports.Length;
                var worldPorts = DomainMetadata.WorldPorts(snapshot, entity, data);
                if (worldPorts.Any(p => !p.Position.IsFinite || p.Position.Length > 1e12 || !p.Direction.IsFinite))
                { declarations = false; Issue("invalid-world-port", "error", "Transformed port coordinates exceed the supported finite drawing bounds.", [entity.Id!.Value]); continue; }
                records.Add(new(entity.Id!.Value, data, worldPorts));
            }
            catch (Exception error) when (error is FormatException or NotSupportedException or InvalidOperationException)
            { declarations = false; Issue("invalid-metadata", "error", error.Message, entity.Id is { } id ? [id] : []); }
        }
        var unique = records.Where(r => keyCounts[r.Data.Key] == 1).ToDictionary(r => r.Data.Key, StringComparer.Ordinal);
        var nodes = new List<DomainPortNode>(); var edges = new Dictionary<string, DomainConnection>(StringComparer.Ordinal);
        foreach (var record in records)
        {
            token.ThrowIfCancellationRequested(); var data = record.Data;
            if (keyCounts[data.Key] != 1) { declarations = false; Issue("duplicate-key", "error", "Ambiguous domain key; its ports and connections are excluded.", [record.Entity]); continue; }
            var open = (data.Properties?.GetValueOrDefault("openPorts") ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
            foreach (var port in record.WorldPorts)
                nodes.Add(new(PortId(data.Key, port.Id), data.Key, port.Id, record.Entity, data.Module, data.Class, data.Tag, port.Kind, port.Position, port.NominalSize, open.Contains(port.Id), data.Passages is not null));
            if (data.Passages is null && data.Ports.Length > 1)
            { declarations = false; Issue("unspecified-passages", "warning", "Internal passages are unspecified. Terminals remain separate; author or import explicit passage declarations to trace through this object.", [record.Entity]); }
        }
        var nodeMap = nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        bool AddEdge(DomainConnection edge)
        {
            if (edges.ContainsKey(edge.Id)) return true;
            if (edges.Count == options.MaximumEdges) { edgeLimited = true; complete = false; return false; }
            edges.Add(edge.Id, edge); return true;
        }
        foreach (var record in unique.Values.OrderBy(r => r.Data.Key, StringComparer.Ordinal))
        {
            token.ThrowIfCancellationRequested(); var data = record.Data;
            foreach (var passage in data.Passages ?? [])
            {
                var a = PortId(data.Key, passage.FromPort); var b = PortId(data.Key, passage.ToPort);
                var direction = passage.Direction;
                if (string.CompareOrdinal(a, b) > 0) { (a, b) = (b, a); direction = direction == "forward" ? "reverse" : direction == "reverse" ? "forward" : direction; }
                if (!AddEdge(new("passage:" + a + "|" + b + ":" + direction, a, b, "passage", direction, true, Vec3.Distance(nodeMap[a].Position, nodeMap[b].Position)))) break;
            }
            foreach (var relation in data.References)
            {
                token.ThrowIfCancellationRequested(); var a = PortId(data.Key, relation.PortId);
                if (relation.Role == "association") { Issue("association", "info", "An explicit association is recorded but excluded from physical connectivity.", [record.Entity], a); continue; }
                if (!unique.TryGetValue(relation.TargetKey, out var target))
                {
                    declarations = false;
                    var known = keyCounts.ContainsKey(relation.TargetKey);
                    Issue(known ? "scope-boundary" : complete ? "missing-target" : "unscanned-target", "warning",
                        known ? "Target is outside this discipline or has an ambiguous key; no cross-module connection is traversed." : complete ? "Referenced target is missing from this layout." : "Target was not found in the bounded scan; existence is unknown.", [record.Entity], a); continue;
                }
                var b = PortId(relation.TargetKey, relation.TargetPortId);
                if (!nodeMap.TryGetValue(b, out var targetPort)) { declarations = false; Issue("missing-port", "error", "Target port is not declared.", [record.Entity, target.Entity], a); continue; }
                if (a == b) { declarations = false; Issue("self-connection", "error", "A port cannot connect to itself.", [record.Entity], a); continue; }
                var sourcePort = nodeMap[a];
                if (sourcePort.Kind != targetPort.Kind) { declarations = false; Issue("port-kind-mismatch", "error", "Incompatible port kinds are not linked.", [record.Entity, target.Entity], a, b); continue; }
                var reciprocal = target.Data.References.Any(r => r.Role == "connection" && r.PortId == relation.TargetPortId && r.TargetKey == data.Key && r.TargetPortId == relation.PortId);
                var span = Vec3.Distance(sourcePort.Position, targetPort.Position);
                if (!reciprocal) Issue("one-sided-connection", "warning", "Only one endpoint declares this connection; the assertion remains visible in the graph.", [record.Entity, target.Entity], a, b);
                // Report each undirected relation once even when both endpoints declare it.
                if (string.CompareOrdinal(a, b) > 0 && reciprocal) continue;
                if (span > options.ConnectionTolerance) Issue("connection-gap", "warning", "Declared endpoints are separated by " + span.ToString("G6", System.Globalization.CultureInfo.InvariantCulture) + " drawing units; coordinates do not change connectivity.", [record.Entity, target.Entity], a, b);
                if (!string.IsNullOrWhiteSpace(sourcePort.NominalSize) && !string.IsNullOrWhiteSpace(targetPort.NominalSize) && sourcePort.NominalSize != targetPort.NominalSize)
                    Issue("size-mismatch", "warning", "Connected ports declare different nominal sizes.", [record.Entity, target.Entity], a, b);
                if (string.CompareOrdinal(a, b) > 0) (a, b) = (b, a);
                if (!AddEdge(new("connection:" + a + "|" + b, a, b, "connection", "both", reciprocal, span))) break;
            }
        }
        if (edgeLimited) limits.Add("Connection limit reached; component membership and searches cover only emitted links.");
        var connected = edges.Values.Where(e => e.Kind == "connection").SelectMany(e => new[] { e.Source, e.Target }).ToHashSet(StringComparer.Ordinal);
        foreach (var node in nodes) { token.ThrowIfCancellationRequested(); if (!node.Open && !connected.Contains(node.Id)) Issue("unconnected-port", "info", "Port has no explicit external connection and is not declared open.", [node.Entity], node.Id); }
        if (!declarations) limits.Add("Missing, ambiguous or unspecified declarations prevent a complete connectivity interpretation. No geometry or component behavior is inferred.");
        if (!issuesComplete) limits.Add("Finding limit reached; exported findings are a subset.");
        return new(snapshot, module, layout, nodes, edges.Values, issues, visited, records.Count, complete, declarations, issuesComplete, limits, token);
    }
}
