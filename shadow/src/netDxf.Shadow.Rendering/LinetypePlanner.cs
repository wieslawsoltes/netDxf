using System.Collections.Immutable;
using netDxf.Shadow.Geometry;

namespace netDxf.Shadow.Rendering;

/// <summary>Planner and expansion quotas. Expansion work counts attempted item, contour, curve and UTF-16
/// character copies, including failed groups; it is not a byte limit or a wall-clock time guarantee.</summary>
public sealed record LinetypePlanOptions(int MaximumElements = 4096, int MaximumPlacements = 20_000,
    int MaximumPoints = 2_000_000, int MaximumTextLength = 100_000, int MaximumScenePlacements = 200_000,
    int MaximumExpansionWork = 500_000, int MaximumSceneExpansionWork = 2_000_000)
{
    internal void Validate()
    {
        if (MaximumElements < 1 || MaximumPlacements < 1 || MaximumPoints < 2 || MaximumTextLength < 1 ||
            MaximumScenePlacements < 1 || MaximumExpansionWork < 1 || MaximumSceneExpansionWork < 1)
            throw new ArgumentOutOfRangeException(nameof(LinetypePlanOptions));
    }
}

public sealed record LinetypePlacement(int ContourIndex, int ElementIndex, int Ordinal, double Distance,
    CadLinetypeDecoration Decoration, Affine3 Frame);

/// <summary>Bounded, renderer-independent complex-linetype placement on sampled world-space paths.
/// The distance metric matches StrokePattern. Symbols remain whole at path ends; closed seams emit once.
/// This is deterministic periodic placement, not AutoCAD's proprietary A-alignment end fitting.</summary>
public static class LinetypePlanner
{
    private readonly record struct Event(int Element, double Offset, CadLinetypeDecoration Decoration);

    public static ImmutableArray<LinetypePlacement> Plan(ImmutableArray<RenderContour> contours, CadLinetype pattern,
        Affine3? sourceToWorld = null, LinetypePlanOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        cancellationToken.ThrowIfCancellationRequested();
        options ??= new(); options.Validate();
        if (!(pattern.Scale > 0) || !double.IsFinite(pattern.Scale) || !pattern.Normal.IsFinite)
            throw new ArgumentException("Linetype scale and plane must be finite, with positive scale.", nameof(pattern));
        if (pattern.Elements.IsDefaultOrEmpty || pattern.Elements.Length > options.MaximumElements)
            throw new ArgumentException("Linetype element budget exceeded or the pattern is empty.", nameof(pattern));
        var events = new List<Event>();
        var period = 0.0;
        for (var i = 0; i < pattern.Elements.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var element = pattern.Elements[i] ?? throw new ArgumentException("Null linetype element.", nameof(pattern));
            var length = Math.Abs(element.Length) * pattern.Scale;
            if (!double.IsFinite(length)) throw new ArgumentException("Linetype lengths must be finite.", nameof(pattern));
            var next = period + length;
            if (!double.IsFinite(next) || length > 0 && next <= period)
                throw new ArgumentException("Linetype period exceeds numeric resolution.", nameof(pattern));
            period = next;
            if (element.Decoration is not { } decoration) continue;
            Validate(decoration, pattern.Scale, options);
            events.Add(new(i, period, decoration));
        }
        if (!(period > 0)) throw new ArgumentException("A linetype needs a positive period; zero-length elements alone cannot repeat.", nameof(pattern));
        if (events.Count == 0 || contours.IsDefaultOrEmpty) return [];
        contours = LinetypePaths.Split(contours, options.MaximumPoints, cancellationToken);
        var source = sourceToWorld ?? Affine3.Identity;
        var localPlane = Affine3.ObjectCoordinates(pattern.Normal == Vec3.Zero ? Vec3.UnitZ : pattern.Normal);
        var normal = Unit(Cross(source.Direction(localPlane.X), source.Direction(localPlane.Y)));
        var absolute = Affine3.ObjectCoordinates(normal);
        var result = ImmutableArray.CreateBuilder<LinetypePlacement>();
        long pointsVisited = 0;
        for (var contourIndex = 0; contourIndex < contours.Length; contourIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var contour = contours[contourIndex];
            var points = contour.Points;
            if (points.IsDefaultOrEmpty) continue;
            var close = contour.Closed && points.Length > 1 && points[0] != points[^1];
            var count = checked(points.Length + (close ? 1 : 0));
            pointsVisited += count;
            if (pointsVisited > options.MaximumPoints) throw new InvalidOperationException("Linetype point budget exceeded.");
            if (count < 2) continue;
            Vec3 Point(int i) => i == points.Length ? points[0] : points[i];
            var distances = new double[count];
            if (!Point(0).IsFinite) throw new ArgumentException("Non-finite linetype path.", nameof(contours));
            for (var i = 1; i < count; i++)
            {
                if ((i & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                if (!Point(i).IsFinite) throw new ArgumentException("Non-finite linetype path.", nameof(contours));
                var length = Vec3.Distance(Point(i - 1), Point(i));
                distances[i] = distances[i - 1] + length;
                if (!double.IsFinite(distances[i]) || length > 0 && distances[i] <= distances[i - 1])
                    throw new ArgumentException("Linetype path exceeds numeric resolution.", nameof(contours));
            }
            var total = distances[^1];
            if (!(total > 0)) continue;
            var cycleCount = Math.Floor(total / period);
            // Each complete period emits every event; reject before walking a microscopic pattern billions of times.
            if (!double.IsFinite(cycleCount) || cycleCount * events.Count > options.MaximumPlacements - result.Count)
                throw new InvalidOperationException("Linetype placement budget exceeded.");
            var schedule = contour.Closed
                ? events.Select(e => e.Offset == period ? e with { Offset = 0 } : e).OrderBy(e => e.Offset).ThenBy(e => e.Element).ToArray()
                : events.ToArray();
            var edge = 0;
            for (long cycle = 0; cycle <= (long)cycleCount; cycle++)
            {
                var cycleStart = cycle * period;
                foreach (var entry in schedule)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var at = cycleStart + entry.Offset;
                    if (at > total || contour.Closed && at >= total) continue;
                    if (result.Count >= options.MaximumPlacements) throw new InvalidOperationException("Linetype placement budget exceeded.");
                    while (edge < count - 2 && (distances[edge + 1] < at || distances[edge + 1] <= distances[edge])) edge++;
                    var length = distances[edge + 1] - distances[edge];
                    if (!(length > 0)) continue;
                    var t = Math.Clamp((at - distances[edge]) / length, 0, 1);
                    var origin = Point(edge) * (1 - t) + Point(edge + 1) * t;
                    var tangent = Unit(Point(edge + 1) - Point(edge));
                    if (Math.Abs(Dot(tangent, normal)) > 1e-8)
                        throw new NotSupportedException("Complex linetypes currently require paths in the authored entity plane.");
                    var side = Unit(Cross(normal, tangent));
                    var decoration = entry.Decoration;
                    var angle = (decoration.Rotation % 360) * Math.PI / 180;
                    var c = Math.Cos(angle); var s = Math.Sin(angle);
                    var basisX = decoration.RotationType == CadLinetypeRotation.Absolute ? absolute.X : tangent;
                    var basisY = decoration.RotationType == CadLinetypeRotation.Absolute ? absolute.Y : side;
                    var x = basisX * c + basisY * s;
                    var y = basisY * c - basisX * s;
                    if (decoration.RotationType == CadLinetypeRotation.Upright &&
                        (Dot(x, absolute.X) < -1e-12 || Math.Abs(Dot(x, absolute.X)) <= 1e-12 && Dot(x, absolute.Y) < 0))
                    { x = x * -1; y = y * -1; }
                    origin += tangent * (decoration.Offset.X * pattern.Scale) + side * (decoration.Offset.Y * pattern.Scale);
                    var size = (decoration.StyleHeight > 0 ? decoration.StyleHeight : 1) * decoration.Scale * pattern.Scale;
                    var frame = new Affine3(x * size, y * size, normal * size, origin);
                    if (!origin.IsFinite || !frame.X.IsFinite || !frame.Y.IsFinite)
                        throw new ArgumentException("Linetype placement overflowed.", nameof(pattern));
                    result.Add(new(contourIndex, entry.Element, result.Count, at, decoration, frame));
                }
            }
        }
        return result.ToImmutable();
    }

    private static void Validate(CadLinetypeDecoration value, double scale, LinetypePlanOptions options)
    {
        if (!Enum.IsDefined(value.Kind) || !Enum.IsDefined(value.RotationType) || value.Content is null || value.Resource is null || value.FontFamily is null || value.BigFont is null)
            throw new ArgumentException("Invalid complex linetype metadata.");
        if (value.Content.Length > options.MaximumTextLength || value.Resource.Length > 4096 || value.FontFamily.Length > 4096 || value.BigFont.Length > 4096)
            throw new InvalidOperationException("Linetype text/resource budget exceeded.");
        if (!(value.Scale > 0) || !(value.WidthFactor > 0) || !(value.StyleHeight >= 0) || !value.Offset.IsFinite || value.Offset.Z != 0 ||
            !double.IsFinite(value.Scale) || !double.IsFinite(value.WidthFactor) || !double.IsFinite(value.StyleHeight) ||
            !double.IsFinite(value.Rotation) || !double.IsFinite(value.ObliqueAngle) || Math.Abs(value.ObliqueAngle) >= 90)
            throw new ArgumentException("Invalid complex linetype dimensions or rotation.");
        var size = (value.StyleHeight > 0 ? value.StyleHeight : 1) * value.Scale * scale;
        if (!(size > 0) || !double.IsFinite(size) || !double.IsFinite(size * value.WidthFactor))
            throw new ArgumentException("Complex linetype dimensions exceed numeric resolution.");
        if (value.ShapeNumber is < short.MinValue or > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(value.ShapeNumber));
    }
    private static Vec3 Cross(Vec3 a, Vec3 b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
    private static double Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    private static Vec3 Unit(Vec3 value)
    {
        var length = value.Length;
        if (!(length > 0) || !double.IsFinite(length)) throw new ArgumentException("A linetype frame needs a finite, nonsingular plane.");
        return value / length;
    }
}
