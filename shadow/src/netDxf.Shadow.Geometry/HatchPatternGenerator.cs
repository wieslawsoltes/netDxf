using System.Collections.Immutable;

namespace netDxf.Shadow.Geometry;

public sealed record CadPatternLine(Vec3 Origin, double Angle, Vec3 Delta, ImmutableArray<double> Dashes);
public sealed record HatchPatternOptions(double BoundaryTolerance = 0.01, int MaximumRows = 100_000,
    int MaximumSegments = 200_000, int MaximumBoundaryPoints = 1_000_000, long MaximumEdgeVisits = 10_000_000);
public readonly record struct DashInterval(double Start, double End)
{
    public bool IsDot => Start == End;
}

/// <summary>Signed DXF pattern lengths: positive strokes, negative gaps, and zero-length dots.
/// Phase is anchored to the line origin, not restarted at clipping boundaries.</summary>
public static class DashPattern
{
    public static ImmutableArray<DashInterval> Clip(double from, double to, ImmutableArray<double> pattern,
        double phase = 0, int maximumSegments = 200_000)
    {
        if (!double.IsFinite(from) || !double.IsFinite(to) || !double.IsFinite(phase) || to < from || maximumSegments < 1)
            throw new ArgumentOutOfRangeException(nameof(from));
        if (pattern.IsDefaultOrEmpty) return [new(from, to)];
        if (pattern.Any(length => !double.IsFinite(length))) throw new ArgumentException("Dash lengths must be finite.", nameof(pattern));
        var period = pattern.Sum(Math.Abs);
        if (!(period > 0) || !double.IsFinite(period)) throw new ArgumentException("A repeating pattern needs a finite nonzero period.", nameof(pattern));
        if (pattern.All(length => length > 0)) return [new(from, to)];
        var first = Math.Floor((from - phase) / period);
        var last = Math.Floor((to - phase) / period);
        if (!double.IsFinite(first) || !double.IsFinite(last) || Math.Abs(first) > 4_503_599_627_370_496 || Math.Abs(last) > 4_503_599_627_370_496)
            throw new ArithmeticException("Dash phase cannot be resolved at this coordinate magnitude.");
        var cycles = last - first + 1;
        if (cycles * pattern.Length > (long)maximumSegments * 4)
            throw new InvalidOperationException("Dash iteration budget exceeded.");
        var result = ImmutableArray.CreateBuilder<DashInterval>();
        for (long cycle = 0; cycle < (long)cycles; cycle++)
        {
            var at = phase + (first + cycle) * period;
            foreach (var length in pattern)
            {
                var end = at + Math.Abs(length);
                if (length != 0 && !(end > at)) throw new ArithmeticException("Dash length is below coordinate precision.");
                if (length > 0)
                {
                    var a = Math.Max(at, from); var b = Math.Min(end, to);
                    if (b > a) Add(new(a, b));
                }
                else if (length == 0 && at >= from && at <= to) Add(new(at, at));
                at = end;
            }
        }
        return result.ToImmutable();

        void Add(DashInterval interval)
        {
            if (result.Count > 0 && interval.IsDot && result[^1].IsDot && result[^1].Start == interval.Start) return;
            if (result.Count >= maximumSegments) throw new InvalidOperationException("Dash segment budget exceeded.");
            result.Add(interval);
        }
    }
}

/// <summary>Active-edge scan conversion of planar hatch regions. Boundary loops use even/odd
/// island semantics; only edges crossing the current pattern row are intersected.</summary>
public static class HatchPatternGenerator
{
    private readonly record struct Edge(double MinY, double MaxY, double X, double Slope);

    public static ImmutableArray<CadContour> Generate(ImmutableArray<CadContour> boundaries,
        ImmutableArray<CadPatternLine> definitions, HatchPatternOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new();
        if (!(options.BoundaryTolerance > 0) || !double.IsFinite(options.BoundaryTolerance)
            || options.MaximumRows < 1 || options.MaximumSegments < 1 || options.MaximumBoundaryPoints < 3 || options.MaximumEdgeVisits < 1)
            throw new ArgumentOutOfRangeException(nameof(options));
        var loops = new List<ImmutableArray<Vec3>>();
        var pointCount = 0;
        foreach (var boundary in boundaries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var points = ImmutableArray.CreateBuilder<Vec3>();
            foreach (var curve in boundary.Curves)
            {
                var sampled = CurveSampler.Sample(curve, new(options.BoundaryTolerance,
                    options.MaximumBoundaryPoints));
                foreach (var point in sampled)
                {
                    if (!point.IsFinite) throw new ArithmeticException("Nonfinite hatch boundary.");
                    if (points.Count > 0 && point == points[^1]) continue;
                    if (++pointCount > options.MaximumBoundaryPoints) throw new InvalidOperationException("Hatch boundary budget exceeded.");
                    points.Add(point);
                }
            }
            if (points.Count >= 3) loops.Add(points.ToImmutable());
        }
        var output = ImmutableArray.CreateBuilder<CadContour>();
        long visits = 0, rows = 0;
        foreach (var definition in definitions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!definition.Origin.IsFinite || !definition.Delta.IsFinite || !double.IsFinite(definition.Angle))
                throw new ArgumentException("Hatch line definitions must be finite.", nameof(definitions));
            var direction = new Vec3(Math.Cos(definition.Angle), Math.Sin(definition.Angle));
            var normal = new Vec3(-direction.Y, direction.X);
            var deltaX = definition.Delta.X; var spacing = definition.Delta.Y;
            if (spacing < 0) { spacing = -spacing; deltaX = -deltaX; }
            if (!(spacing > 0)) throw new ArgumentException("A hatch family needs nonzero perpendicular spacing.", nameof(definitions));
            var edges = new List<Edge>();
            foreach (var loop in loops)
            {
                for (var i = 0; i < loop.Length; i++)
                {
                    var a = loop[i] - definition.Origin; var b = loop[(i + 1) % loop.Length] - definition.Origin;
                    var ax = Vec3.Dot(a, direction); var ay = Vec3.Dot(a, normal);
                    var bx = Vec3.Dot(b, direction); var by = Vec3.Dot(b, normal);
                    if (ay == by) continue;
                    if (ay > by) { (ax, bx) = (bx, ax); (ay, by) = (by, ay); }
                    edges.Add(new(ay, by, ax, (bx - ax) / (by - ay)));
                }
            }
            if (edges.Count == 0) continue;
            edges.Sort(static (a, b) => a.MinY.CompareTo(b.MinY));
            var first = Math.Ceiling(edges[0].MinY / spacing);
            // Half-open edge intervals prevent double-counting vertices and horizontal tangencies.
            var last = Math.Ceiling(edges.Max(edge => edge.MaxY) / spacing) - 1;
            if (!double.IsFinite(first) || !double.IsFinite(last) || Math.Abs(first) > 4_503_599_627_370_496 || Math.Abs(last) > 4_503_599_627_370_496)
                throw new ArithmeticException("Hatch row spacing is below coordinate precision.");
            var count = Math.Max(0, last - first + 1);
            if (count > options.MaximumRows - rows) throw new InvalidOperationException("Hatch row budget exceeded.");
            rows += (long)count;
            var active = new List<Edge>(); var next = 0;
            var intersections = new List<double>();
            for (long row = 0; row < (long)count; row++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var number = first + row; var y = number * spacing;
                while (next < edges.Count && edges[next].MinY <= y) active.Add(edges[next++]);
                active.RemoveAll(edge => edge.MaxY <= y);
                visits += active.Count;
                if (visits > options.MaximumEdgeVisits) throw new InvalidOperationException("Hatch intersection budget exceeded.");
                intersections.Clear();
                foreach (var edge in active) intersections.Add(edge.X + (y - edge.MinY) * edge.Slope);
                intersections.Sort();
                if ((intersections.Count & 1) != 0) throw new ArgumentException("Hatch boundary does not define closed even/odd intervals.", nameof(boundaries));
                for (var i = 0; i + 1 < intersections.Count; i += 2)
                {
                    var from = intersections[i]; var to = intersections[i + 1];
                    if (!(to > from)) continue;
                    var remaining = options.MaximumSegments - output.Count;
                    if (remaining <= 0) throw new InvalidOperationException("Hatch segment budget exceeded.");
                    foreach (var dash in DashPattern.Clip(from, to, definition.Dashes, number * deltaX, remaining))
                    {
                        var a = definition.Origin + direction * dash.Start + normal * y;
                        var b = definition.Origin + direction * dash.End + normal * y;
                        output.Add(new([new LineCurve(a, b)]));
                    }
                }
            }
        }
        return output.ToImmutable();
    }
}
