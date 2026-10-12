using System.Collections.Immutable;
using netDxf.Shadow.Geometry;

namespace netDxf.Shadow.Graph;

public sealed record TopologyOptions(double EndpointTolerance = 0.01, double CurveTolerance = 0.05,
    bool SplitIntersections = true, bool RespectElevation = true, int MaximumSegments = 1_000_000,
    long MaximumCandidatePairs = 20_000_000);

/// <summary>Spatially indexed segment topology. Curve intersections use the explicit approximation tolerance.</summary>
public sealed class TopologyBuilder
{
    private sealed record Segment(Vec3 A, Vec3 B, ObjectId Entity, string Occurrence)
    {
        public Bounds2 Bounds => Bounds2.Empty.Include(A).Include(B);
    }
    private readonly record struct Cell(long X, long Y, long Z);

    public TopologyGraph Build(ShadowSnapshot snapshot, string layout = "Model", TopologyOptions? options = null, CancellationToken cancellationToken = default)
        => Build(new CadSceneBuilder().Build(snapshot, new(layout), cancellationToken), options, cancellationToken);

    public TopologyGraph Build(CadScene scene, TopologyOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new();
        if (options.EndpointTolerance <= 0 || !double.IsFinite(options.EndpointTolerance) || options.CurveTolerance <= 0 || !double.IsFinite(options.CurveTolerance) || options.MaximumSegments < 1 || options.MaximumCandidatePairs < 1)
            throw new ArgumentOutOfRangeException(nameof(options));
        var segments = new List<Segment>();
        var diagnostics = scene.Diagnostics.ToBuilder();
        foreach (var occurrence in scene.Occurrences)
        {
            if (!occurrence.Style.Visible) continue;
            foreach (var contour in occurrence.Geometry.Contours)
                foreach (var curve in contour.Curves)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var points = CurveSampler.Sample(curve, new(options.CurveTolerance));
                    for (var i = 1; i < points.Length; i++)
                    {
                        if (segments.Count >= options.MaximumSegments) throw new InvalidOperationException("Topology segment budget exceeded.");
                        segments.Add(new(points[i - 1], points[i], occurrence.Entity, occurrence.Key));
                    }
                }
        }
        if (segments.Count == 0) return new(scene.DocumentId, scene.Revision, [], [], diagnostics.ToImmutable());
        var cuts = Enumerable.Range(0, segments.Count).Select(_ => new List<double> { 0, 1 }).ToArray();
        if (options.SplitIntersections)
        {
            var index = new SpatialIndex(segments.Select(s => s.Bounds.Inflate(options.EndpointTolerance)));
            long candidates = 0;
            for (var i = 0; i < segments.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var j in index.Query(segments[i].Bounds.Inflate(options.EndpointTolerance)))
                {
                    if (j <= i) continue;
                    if (++candidates > options.MaximumCandidatePairs) throw new InvalidOperationException("Topology intersection candidate budget exceeded.");
                    Intersections(segments[i], segments[j], cuts[i], cuts[j], options);
                }
            }
        }
        var pointsList = new List<Vec3>(); var parents = new List<int>();
        var cells = new Dictionary<Cell, List<int>>();
        var origin = segments[0].A;
        var rawEdges = new List<(int A, int B, double Length, Segment Source)>();
        long proximityCandidates = 0;
        int Root(int id) { while (parents[id] != id) { parents[id] = parents[parents[id]]; id = parents[id]; } return id; }
        void Union(int a, int b) { a = Root(a); b = Root(b); if (a != b) parents[Math.Max(a, b)] = Math.Min(a, b); }
        int AddPoint(Vec3 point)
        {
            if (!options.RespectElevation) point = point with { Z = 0 };
            var relative = (point - origin) / options.EndpointTolerance;
            long Coordinate(double value)
            {
                if (!double.IsFinite(value) || Math.Abs(value) >= long.MaxValue - 1024) throw new ArithmeticException("Topology extent/tolerance exceeds spatial hash range.");
                return (long)Math.Floor(value);
            }
            var cell = new Cell(Coordinate(relative.X), Coordinate(relative.Y), options.RespectElevation ? Coordinate(relative.Z) : 0);
            var id = pointsList.Count; pointsList.Add(point); parents.Add(id);
            for (var x = -1; x <= 1; x++) for (var y = -1; y <= 1; y++) for (var z = options.RespectElevation ? -1 : 0; z <= (options.RespectElevation ? 1 : 0); z++)
                if (cells.TryGetValue(new(cell.X + x, cell.Y + y, cell.Z + z), out var candidates))
                    foreach (var candidate in candidates)
                    {
                        if (++proximityCandidates > options.MaximumCandidatePairs) throw new InvalidOperationException("Topology proximity candidate budget exceeded.");
                        if (Vec3.Distance(point, pointsList[candidate]) <= options.EndpointTolerance) Union(id, candidate);
                    }
            if (!cells.TryGetValue(cell, out var bucket)) cells[cell] = bucket = new();
            bucket.Add(id); return id;
        }
        for (var i = 0; i < segments.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = segments[i]; var ordered = cuts[i].Order().Distinct().ToArray();
            for (var j = 1; j < ordered.Length; j++)
            {
                var a = source.A * (1 - ordered[j - 1]) + source.B * ordered[j - 1];
                var b = source.A * (1 - ordered[j]) + source.B * ordered[j];
                rawEdges.Add((AddPoint(a), AddPoint(b), Vec3.Distance(a, b), source));
                if (rawEdges.Count > options.MaximumSegments * 4L) throw new InvalidOperationException("Split topology edge budget exceeded.");
            }
        }
        var roots = Enumerable.Range(0, pointsList.Count).Select(Root).Distinct().Order().ToArray();
        var ids = roots.Select((root, index) => (root, index)).ToDictionary(p => p.root, p => p.index);
        var adjacency = roots.Select(_ => new List<int>()).ToArray();
        var edges = ImmutableArray.CreateBuilder<GraphEdge>();
        foreach (var raw in rawEdges)
        {
            var a = ids[Root(raw.A)]; var b = ids[Root(raw.B)];
            if (raw.Length <= 1e-14) continue;
            var id = edges.Count; edges.Add(new(id, a, b, raw.Length, raw.Source.Entity, raw.Source.Occurrence));
            adjacency[a].Add(id); adjacency[b].Add(id);
        }
        var nodes = roots.Select((root, id) => new GraphNode(id, pointsList[root], adjacency[id].ToImmutableArray())).ToImmutableArray();
        return new(scene.DocumentId, scene.Revision, nodes, edges.ToImmutable(), diagnostics.ToImmutable());
    }

    private static double Cross(Vec3 a, Vec3 b) => a.X * b.Y - a.Y * b.X;
    private static void Intersections(Segment first, Segment second, List<double> firstCuts, List<double> secondCuts, TopologyOptions options)
    {
        var r = first.B - first.A; var s = second.B - second.A; var delta = second.A - first.A;
        var r2 = r.X * r.X + r.Y * r.Y; var s2 = s.X * s.X + s.Y * s.Y;
        if (r2 == 0 || s2 == 0) return;
        var denominator = Cross(r, s);
        if (Math.Abs(denominator) > 1e-12 * Math.Sqrt(r2 * s2))
        {
            var t = Cross(delta, s) / denominator; var u = Cross(delta, r) / denominator;
            var tEpsilon = options.EndpointTolerance / Math.Sqrt(r2); var uEpsilon = options.EndpointTolerance / Math.Sqrt(s2);
            if (t < -tEpsilon || t > 1 + tEpsilon || u < -uEpsilon || u > 1 + uEpsilon) return;
            t = Math.Clamp(t, 0, 1); u = Math.Clamp(u, 0, 1);
            if (options.RespectElevation && Math.Abs((first.A + r * t).Z - (second.A + s * u).Z) > options.EndpointTolerance) return;
            firstCuts.Add(t); secondCuts.Add(u); return;
        }
        // Collinear overlaps and endpoint-on-segment junctions are split at every participating endpoint.
        if (Math.Abs(Cross(delta, r)) > options.EndpointTolerance * Math.Sqrt(r2)) return;
        Project(second.A, first, firstCuts); Project(second.B, first, firstCuts);
        Project(first.A, second, secondCuts); Project(first.B, second, secondCuts);
        void Project(Vec3 point, Segment segment, List<double> cuts)
        {
            var d = segment.B - segment.A; var length2 = d.X * d.X + d.Y * d.Y;
            if (length2 == 0) return;
            var t = ((point.X - segment.A.X) * d.X + (point.Y - segment.A.Y) * d.Y) / length2;
            if (t < 0 || t > 1 || options.RespectElevation && Math.Abs((segment.A + d * t).Z - point.Z) > options.EndpointTolerance) return;
            cuts.Add(t);
        }
    }
}
