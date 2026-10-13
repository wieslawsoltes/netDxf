using System.Collections.Immutable;

namespace netDxf.Shadow.Geometry;

/// <summary>DXF HATCH group 75: alternating levels, outermost level, or no islands.</summary>
public enum CadHatchIslandStyle { Normal, Outer, Ignore }

/// <summary>
/// Budgets for planar loop classification. Curves are sampled in local XY coordinates;
/// ContactTolerance is independent of the sampling tolerance and never repairs crossings.
/// </summary>
public sealed record HatchIslandOptions(double BoundaryTolerance = 0.01, double ContactTolerance = 1e-9,
    int MaximumContours = 100_000, int MaximumBoundaryPoints = 1_000_000,
    long MaximumSegmentComparisons = 10_000_000, long MaximumContainmentEdgeVisits = 10_000_000);

public readonly record struct HatchBoundaryInfo(int SourceIndex, int ParentIndex, int Depth, Bounds2 Bounds);

/// <summary>Detached containment forest. Selection shares the original analytic contours.</summary>
public sealed class HatchBoundaryTopology
{
    private readonly ImmutableArray<CadContour> boundaries;
    public ImmutableArray<HatchBoundaryInfo> Loops { get; }
    public int SampledPointCount { get; }
    public long SegmentComparisons { get; }
    public long ContainmentEdgeVisits { get; }

    internal HatchBoundaryTopology(ImmutableArray<CadContour> boundaries, ImmutableArray<HatchBoundaryInfo> loops,
        int points, long comparisons, long visits)
    {
        this.boundaries = boundaries; Loops = loops; SampledPointCount = points;
        SegmentComparisons = comparisons; ContainmentEdgeVisits = visits;
    }

    public ImmutableArray<CadContour> Select(CadHatchIslandStyle style)
    {
        if (!Enum.IsDefined(style)) throw new ArgumentOutOfRangeException(nameof(style));
        if (style == CadHatchIslandStyle.Normal) return boundaries;
        var output = ImmutableArray.CreateBuilder<CadContour>();
        var maximumDepth = style == CadHatchIslandStyle.Outer ? 1 : 0;
        foreach (var loop in Loops)
            if (loop.Depth <= maximumDepth) output.Add(boundaries[loop.SourceIndex]);
        return output.ToImmutable();
    }
}

/// <summary>
/// Bounded, winding-independent containment of simple disjoint or strictly nested loops.
/// Segment and loop BVHs avoid all-pairs work on spatially separate boundaries. Crossing,
/// touching, coincident, disconnected and degenerate sampled loops are rejected instead
/// of assigning an arbitrary parent. This is not an exact-predicate CAD boolean kernel.
/// </summary>
public static class HatchIslandResolver
{
    private sealed record Loop(ImmutableArray<Vec3> Points, Bounds2 Bounds, double Area);
    private readonly record struct Segment(int Loop, int Index, Vec3 A, Vec3 B, Bounds2 Bounds);
    private const double MachineEpsilon = 2.2204460492503131e-16;

    /// <summary>
    /// Normal retains existing even/odd behavior without requiring simple-loop topology.
    /// Outer/Ignore validate a containment forest and select original, unsampled curves.
    /// </summary>
    public static ImmutableArray<CadContour> Select(ImmutableArray<CadContour> boundaries,
        CadHatchIslandStyle style, HatchIslandOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(style)) throw new ArgumentOutOfRangeException(nameof(style));
        Validate(boundaries, options ?? new());
        cancellationToken.ThrowIfCancellationRequested();
        return style == CadHatchIslandStyle.Normal ? boundaries : Analyze(boundaries, options, cancellationToken).Select(style);
    }

    public static HatchBoundaryTopology Analyze(ImmutableArray<CadContour> boundaries,
        HatchIslandOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new(); Validate(boundaries, options);
        cancellationToken.ThrowIfCancellationRequested();
        var loops = new Loop[boundaries.Length];
        var segments = new List<Segment>();
        var sampleVisits = 0; var pointCount = 0;
        for (var index = 0; index < boundaries.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var contour = boundaries[index];
            if (contour is null || !contour.Closed || contour.Curves.IsDefaultOrEmpty)
                throw new ArgumentException($"Hatch contour {index} must be nonempty and closed.", nameof(boundaries));
            var points = ImmutableArray.CreateBuilder<Vec3>();
            foreach (var curve in contour.Curves)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (curve is null) throw new ArgumentException("Hatch curves cannot be null.", nameof(boundaries));
                var samples = CurveSampler.Sample(curve, new(options.BoundaryTolerance, options.MaximumBoundaryPoints));
                if (!samples.IsEmpty && points.Count > 0 && !Near(points[^1], samples[0], options.ContactTolerance))
                    throw new ArgumentException($"Hatch contour {index} has disconnected edges.", nameof(boundaries));
                for (var sampleIndex = 0; sampleIndex < samples.Length; sampleIndex++)
                {
                    var sample = samples[sampleIndex];
                    if (++sampleVisits > options.MaximumBoundaryPoints)
                        throw new InvalidOperationException("Hatch island boundary-point budget exceeded.");
                    if ((sampleVisits & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                    if (!sample.IsFinite) throw new ArithmeticException("Nonfinite hatch island boundary.");
                    // Weld only neighbouring curve endpoints within the explicit contact
                    // tolerance. Never change the analytic contours returned to consumers.
                    if (sampleIndex == 0 && points.Count > 0) continue;
                    var point = sample with { Z = 0 };
                    if (points.Count == 0 || point != points[^1]) points.Add(point);
                }
            }
            if (points.Count > 1 && Near(points[0], points[^1], options.ContactTolerance)) points.RemoveAt(points.Count - 1);
            else throw new ArgumentException($"Hatch contour {index} has an open boundary.", nameof(boundaries));
            if (points.Count < 3) throw new ArgumentException($"Hatch contour {index} is degenerate.", nameof(boundaries));
            var values = points.ToImmutable(); var bounds = Bounds2.Empty;
            foreach (var point in values) bounds = bounds.Include(point);
            var area = Area(values);
            if (!(area > 0)) throw new ArgumentException($"Hatch contour {index} has zero sampled area.", nameof(boundaries));
            loops[index] = new(values, bounds, area); pointCount += values.Length;
            for (var i = 0; i < values.Length; i++)
            {
                var a = values[i]; var b = values[(i + 1) % values.Length];
                if (Near(a, b, options.ContactTolerance))
                    throw new ArgumentException($"Hatch contour {index} has an unresolved short edge.", nameof(boundaries));
                var box = Bounds2.Empty.Include(a).Include(b).Inflate(options.ContactTolerance);
                if (!Finite(box)) throw new ArithmeticException("Hatch island bounds exceed coordinate precision.");
                segments.Add(new(index, i, a, b, box));
            }
        }

        var segmentIndex = new SpatialIndex(segments.Select(s => s.Bounds));
        long comparisons = 0;
        for (var i = 0; i < segments.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var a = segments[i];
            foreach (var j in segmentIndex.Query(a.Bounds))
            {
                if (j <= i) continue;
                if (++comparisons > options.MaximumSegmentComparisons)
                    throw new InvalidOperationException("Hatch island segment-comparison budget exceeded.");
                if ((comparisons & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                var b = segments[j];
                var count = loops[a.Loop].Points.Length;
                var adjacent = a.Loop == b.Loop && ((a.Index + 1) % count == b.Index || (b.Index + 1) % count == a.Index);
                if (adjacent)
                {
                    // A shared vertex is legal; a collinear reversal overlaps its neighbour.
                    var first = (a.Index + 1) % count == b.Index ? a : b;
                    var second = (a.Index + 1) % count == b.Index ? b : a;
                    if (OnSegment(first.A, second, options.ContactTolerance) || OnSegment(second.B, first, options.ContactTolerance))
                        throw new ArgumentException($"Hatch contour {a.Loop} contains overlapping adjacent edges.", nameof(boundaries));
                }
                else if (Intersects(a, b, options.ContactTolerance))
                    throw new ArgumentException($"Hatch contours {a.Loop} and {b.Loop} cross, touch or coincide; island nesting is ambiguous.", nameof(boundaries));
            }
        }

        var loopIndex = new SpatialIndex(loops.Select(loop => loop.Bounds));
        var parents = Enumerable.Repeat(-1, loops.Length).ToArray();
        long visits = 0;
        for (var i = 0; i < loops.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var child = loops[i]; var bestArea = double.PositiveInfinity;
            // A boundary vertex, unlike a centroid, is reliable for concave loops once
            // all inter-loop crossings/contacts have been excluded.
            var point = child.Points[0];
            foreach (var candidate in loopIndex.Query(new(point.X, point.Y, point.X, point.Y)))
            {
                if (candidate == i) continue;
                var outer = loops[candidate];
                if (!Contains(outer.Bounds, child.Bounds) || outer.Area >= bestArea) continue;
                var inside = false;
                for (var edge = 0; edge < outer.Points.Length; edge++)
                {
                    if (++visits > options.MaximumContainmentEdgeVisits)
                        throw new InvalidOperationException("Hatch island containment budget exceeded.");
                    if ((visits & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                    var a = outer.Points[edge]; var b = outer.Points[(edge + 1) % outer.Points.Length];
                    if ((a.Y > point.Y) == (b.Y > point.Y)) continue;
                    var side = Side(a, b, point, 0);
                    if (side == 0) throw new ArithmeticException("Hatch containment is unresolved at this coordinate precision.");
                    if ((side > 0) == (b.Y > a.Y)) inside = !inside;
                }
                if (!inside) continue;
                if (!(outer.Area > child.Area)) throw new ArithmeticException("Nested hatch areas cannot be distinguished at this coordinate precision.");
                parents[i] = candidate; bestArea = outer.Area;
            }
        }
        var depths = new int[loops.Length];
        foreach (var i in Enumerable.Range(0, loops.Length).OrderByDescending(i => loops[i].Area))
            depths[i] = parents[i] < 0 ? 0 : checked(depths[parents[i]] + 1);
        var metadata = Enumerable.Range(0, loops.Length)
            .Select(i => new HatchBoundaryInfo(i, parents[i], depths[i], loops[i].Bounds)).ToImmutableArray();
        return new(boundaries, metadata, pointCount, comparisons, visits);
    }

    private static void Validate(ImmutableArray<CadContour> boundaries, HatchIslandOptions options)
    {
        if (boundaries.IsDefault) throw new ArgumentException("Hatch boundary array must be initialized.", nameof(boundaries));
        if (!(options.BoundaryTolerance > 0) || !double.IsFinite(options.BoundaryTolerance)
            || !(options.ContactTolerance > 0) || !double.IsFinite(options.ContactTolerance)
            || options.MaximumContours < 1 || options.MaximumBoundaryPoints < 3
            || options.MaximumSegmentComparisons < 1 || options.MaximumContainmentEdgeVisits < 1)
            throw new ArgumentOutOfRangeException(nameof(options));
        if (boundaries.Length > options.MaximumContours) throw new InvalidOperationException("Hatch island contour budget exceeded.");
    }

    private static bool Finite(Bounds2 b) => double.IsFinite(b.MinX) && double.IsFinite(b.MinY) && double.IsFinite(b.MaxX) && double.IsFinite(b.MaxY);
    private static bool Contains(Bounds2 a, Bounds2 b) => a.MinX <= b.MinX && a.MinY <= b.MinY && a.MaxX >= b.MaxX && a.MaxY >= b.MaxY;
    private static bool Near(Vec3 a, Vec3 b, double tolerance) => Math.Abs(a.X - b.X) <= tolerance && Math.Abs(a.Y - b.Y) <= tolerance;

    private static double Area(ImmutableArray<Vec3> points)
    {
        // Translation-relative, compensated shoelace avoids subtracting huge world-space products.
        var origin = points[0]; var sum = 0.0; var correction = 0.0;
        for (var i = 1; i + 1 < points.Length; i++)
        {
            var a = points[i] - origin; var b = points[i + 1] - origin;
            var term = a.X * b.Y - a.Y * b.X;
            var corrected = term - correction; var next = sum + corrected;
            correction = (next - sum) - corrected; sum = next;
        }
        if (!double.IsFinite(sum)) throw new ArithmeticException("Hatch island area exceeds coordinate precision.");
        return Math.Abs(sum) * 0.5;
    }

    private static int Side(Vec3 a, Vec3 b, Vec3 point, double tolerance)
    {
        var dx = b.X - a.X; var dy = b.Y - a.Y;
        var scale = Math.Max(Math.Abs(dx), Math.Abs(dy));
        if (!(scale > 0) || !double.IsFinite(scale)) throw new ArithmeticException("Unresolved hatch edge direction.");
        dx /= scale; dy /= scale;
        var length = Math.Sqrt(dx * dx + dy * dy); dx /= length; dy /= length;
        var first = dx * (point.Y - a.Y); var second = dy * (point.X - a.X);
        var distance = first - second;
        if (!double.IsFinite(distance)) throw new ArithmeticException("Hatch intersection exceeds coordinate precision.");
        var error = tolerance + 16 * MachineEpsilon * (Math.Abs(first) + Math.Abs(second));
        return distance > error ? 1 : distance < -error ? -1 : 0;
    }

    private static bool OnSegment(Vec3 p, Segment edge, double tolerance)
        => edge.Bounds.Contains(p) && Side(edge.A, edge.B, p, tolerance) == 0;

    private static bool Intersects(Segment a, Segment b, double tolerance)
    {
        var a0 = Side(a.A, a.B, b.A, tolerance); var a1 = Side(a.A, a.B, b.B, tolerance);
        var b0 = Side(b.A, b.B, a.A, tolerance); var b1 = Side(b.A, b.B, a.B, tolerance);
        return a0 * a1 < 0 && b0 * b1 < 0
            || a0 == 0 && a.Bounds.Contains(b.A) || a1 == 0 && a.Bounds.Contains(b.B)
            || b0 == 0 && b.Bounds.Contains(a.A) || b1 == 0 && b.Bounds.Contains(a.B);
    }
}
