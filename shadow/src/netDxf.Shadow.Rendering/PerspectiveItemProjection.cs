using System.Collections.Immutable;
using netDxf.Shadow.Geometry;

namespace netDxf.Shadow.Rendering;

/// <summary>Optional text capability. Produces managed glyph outlines in text-local, Y-down
/// layout coordinates. No native font handle may escape. Implementations must honor the curve quota.</summary>
public interface ITextOutlineProvider
{
    CadGeometry Outline(CadText text, RichTextLayoutResult layout, int maximumCurves,
        CancellationToken cancellationToken = default);
}

internal sealed class PerspectiveWorkBudget(PerspectiveOptions options)
{
    private long points = options.MaximumPoints, work = options.MaximumWork;
    internal int RemainingCurves => (int)Math.Min(points, int.MaxValue);
    internal void Input(long count)
    {
        if (count < 0 || count > work) throw new InvalidOperationException("Perspective work budget exceeded.");
        work -= count;
    }
    internal void Charge(long count)
    {
        Input(count);
        if (count < 0 || count > points) throw new InvalidOperationException("Perspective output point budget exceeded.");
        points -= count;
    }
}

/// <summary>Clip in homogeneous coordinates BEFORE dividing by W. This pipeline intentionally
/// keeps painter order and does not claim hidden-surface removal or arbitrary projective texture sampling.</summary>
internal static class PerspectiveItemProjection
{
    internal static RenderItem? Project(RenderItem source, PaperViewport view, PerspectiveWorkBudget budget,
        ITextOutlineProvider? outlines, double tolerance, CancellationToken token)
    {
        var camera = view.Projection.Perspective!;
        var matrix = camera.ModelToTarget;
        var bounds = Bounds2.Empty;
        var back = view.Back; var front = Math.Min(view.Front, camera.MaximumDepth);
        if (back > front) return null;
        var clip = view.Clip.Bounds;
        var center = camera.Center;
        // Inside means distance >= 0. Homogeneous coordinates are normalized by eye distance.
        double Plane(Vec3 q, int plane)
        {
            var distance = plane switch
            {
            0 => q.Z - back, 1 => front - q.Z,
            2 => q.X - center.X - (clip.MinX - center.X) * (1 - q.Z / camera.EyeDistance),
            3 => (clip.MaxX - center.X) * (1 - q.Z / camera.EyeDistance) - (q.X - center.X),
            4 => q.Y - center.Y - (clip.MinY - center.Y) * (1 - q.Z / camera.EyeDistance),
                _ => (clip.MaxY - center.Y) * (1 - q.Z / camera.EyeDistance) - (q.Y - center.Y)
            };
            if (!double.IsFinite(distance)) throw new ArithmeticException("Frustum predicate exceeds numeric range.");
            return distance;
        }
        Vec3 At(Vec3 q)
        {
            if (!camera.TryProjectTarget(q, out var paper)) throw new ArithmeticException("Clipped perspective vertex cannot be divided safely.");
            return paper;
        }
        Vec3 Target(Vec3 p)
        {
            var q = matrix.Point(p);
            if (!q.IsFinite) throw new ArithmeticException("Perspective camera coordinate overflow.");
            return q;
        }
        bool Segment(ref Vec3 a, ref Vec3 b)
        {
            for (var plane = double.IsNegativeInfinity(back) ? 1 : 0; plane < 6; plane++)
            {
                budget.Input(1);
                var da = Plane(a, plane); var db = Plane(b, plane);
                if (double.IsNaN(da) || double.IsNaN(db)) throw new ArithmeticException("Nonfinite frustum predicate.");
                if (da < 0 && db < 0) return false;
                if (da >= 0 && db >= 0) continue;
                var p = Intersect(a, b, da, db);
                if (da < 0) a = p; else b = p;
            }
            return true;
        }
        ImmutableArray<Vec3> Polygon(ImmutableArray<Vec3> input)
        {
            token.ThrowIfCancellationRequested(); budget.Input(input.Length);
            var vertices = input.Select(Target).ToList();
            if (vertices.Count > 1 && vertices[0] == vertices[^1]) vertices.RemoveAt(vertices.Count - 1);
            for (var plane = double.IsNegativeInfinity(back) ? 1 : 0; plane < 6 && vertices.Count > 0; plane++)
            {
                var next = new List<Vec3>();
                var a = vertices[^1]; var da = Plane(a, plane);
                foreach (var b in vertices)
                {
                    token.ThrowIfCancellationRequested(); budget.Input(1);
                    var db = Plane(b, plane);
                    if ((da < 0) != (db < 0)) next.Add(Intersect(a, b, da, db));
                    if (db >= 0) next.Add(b);
                    a = b; da = db;
                }
                vertices = next;
            }
            if (vertices.Count < 3) return [];
            budget.Charge(vertices.Count);
            return vertices.Select(At).ToImmutableArray();
        }
        ImmutableArray<RenderContour> Polygons(ImmutableArray<RenderContour> input)
        {
            var result = ImmutableArray.CreateBuilder<RenderContour>();
            foreach (var c in input)
            {
                var p = Polygon(c.Points);
                if (!p.IsEmpty) result.Add(new(p, true));
            }
            return result.ToImmutable();
        }
        ImmutableArray<RenderContour> Lines(ImmutableArray<RenderContour> input)
        {
            var result = ImmutableArray.CreateBuilder<RenderContour>();
            foreach (var c in input)
            {
                token.ThrowIfCancellationRequested(); budget.Input(c.Points.Length);
                var n = c.Points.Length;
                if (n == 0) continue;
                var count = n == 1 ? 1 : n - 1 + (c.Closed && c.Points[0] != c.Points[^1] ? 1 : 0);
                for (var i = 0; i < count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    var a = Target(c.Points[i]); var b = Target(c.Points[(i + 1) % n]);
                    if (!Segment(ref a, ref b)) continue;
                    budget.Charge(2); result.Add(new([At(a), At(b)], false));
                }
            }
            return result.ToImmutable();
        }
        Affine3 ParallelFrame(Affine3 frame)
        {
            var x = matrix.Direction(frame.X); var y = matrix.Direction(frame.Y); var origin = Target(frame.Translation);
            if (Math.Abs(x.Z) > 1e-12 * x.Length || Math.Abs(y.Z) > 1e-12 * y.Length)
                throw new NotSupportedException("Tilted perspective image/gradient paint needs a projective texture provider; no affine substitution is made.");
            var w = 1 - origin.Z / camera.EyeDistance;
            if (!(w > 0)) throw new NotSupportedException("Paint origin is behind the perspective eye.");
            return new(x / w, y / w, Vec3.UnitZ, At(origin));
        }

        if (source.Text is { } original && source.TextLayout is { } originalLayout &&
            (Math.Abs(matrix.Direction(original.XAxis).Z) > 1e-12 * original.XAxis.Length ||
             Math.Abs(matrix.Direction(original.YAxis).Z) > 1e-12 * original.YAxis.Length))
        {
            if (outlines is null) throw new NotSupportedException("Tilted perspective text requires ITextOutlineProvider; no affine substitution is made.");
            var geometry = outlines.Outline(original, originalLayout, budget.RemainingCurves, token);
            if (!geometry.Components.IsDefaultOrEmpty || geometry.Text is not null || geometry.Image is not null ||
                !geometry.Contours.IsDefaultOrEmpty || !geometry.DisplayContours.IsDefaultOrEmpty)
                throw new NotSupportedException("Text outline providers must return primitive local regions and stroke groups.");
            var frame = new Affine3(original.XAxis, -original.YAxis, Vec3.UnitZ, original.Origin);
            var compiled = RenderGeometryCompiler.Compile(geometry.Transform(frame), new(tolerance), ApproximateTextLayoutEngine.Instance);
            source = source with { Text = null, TextLayout = null, Regions = source.Regions.AddRange(compiled.Regions),
                Strokes = source.Strokes.AddRange(compiled.Strokes) };
        }
        // Dash resolution must happen in model/definition metric space, never on divided/clipped coordinates.
        var contours = source.Filled ? ImmutableArray<RenderContour>.Empty : Lines(StrokePattern.GetContours(source));
        var regions = ImmutableArray.CreateBuilder<RenderRegion>();
        foreach (var region in source.Regions)
        {
            var loops = Polygons(region.Contours);
            if (loops.IsEmpty) continue;
            var gradient = region.Gradient is { } g ? g with { Frame = ParallelFrame(g.Frame) } : null;
            regions.Add(region with { Contours = loops, Gradient = gradient });
        }
        if (source.Filled)
        {
            var loops = Polygons(source.Contours);
            if (!loops.IsEmpty) regions.Add(new(loops, CadFillRule.EvenOdd, null));
        }
        var strokes = source.Strokes.Select(s => s with { Contours = Lines(s.Contours) }).ToImmutableArray();
        CadText? text = null; RenderClip? textClip = null;
        if (source.Text is { } t && source.TextLayout is { Bounds.IsEmpty: false } layout)
        {
            var box = layout.Bounds;
            Vec3 P(double x, double y) => t.Origin + t.XAxis * x - t.YAxis * y;
            var footprint = Polygon([P(box.MinX, box.MinY), P(box.MaxX, box.MinY), P(box.MaxX, box.MaxY), P(box.MinX, box.MaxY)]);
            if (!footprint.IsEmpty)
            {
                var frame = ParallelFrame(new(t.XAxis, t.YAxis, Vec3.UnitZ, t.Origin));
                text = t with { Origin = frame.Translation, XAxis = frame.X, YAxis = frame.Y };
                textClip = Mask(footprint); bounds = bounds.Union(textClip.Bounds);
            }
        }
        CadImage? image = null; RenderClip? imageClip = null;
        if (source.Image is { Visible: true } img && img.Corners.Length >= 3)
        {
            var depth = Target(img.Corners[0]).Z;
            if (img.Corners.Any(p => Math.Abs(Target(p).Z - depth) > 1e-10 * Math.Max(1, camera.EyeDistance)))
                throw new NotSupportedException("Tilted perspective raster images require projective texture sampling; no affine substitution is made.");
            var footprint = Polygon(img.Corners);
            if (!footprint.IsEmpty)
            {
                // The backend retains the complete image/clip payload as well as the frustum mask.
                budget.Charge((long)img.Corners.Length + img.ClipPolygon.Length);
                image = img with { Corners = img.Corners.Select(p => At(Target(p))).ToImmutableArray(),
                    ClipPolygon = img.ClipPolygon.Select(p => At(Target(p))).ToImmutableArray() };
                imageClip = Mask(footprint); bounds = bounds.Union(imageClip.Bounds);
            }
        }
        Include(contours); foreach (var region in regions) Include(region.Contours); foreach (var s in strokes) Include(s.Contours);
        if (bounds.IsEmpty) return null;
        return source with
        {
            Key = view.Projection.ViewportEntity.Value + "/viewport/" + source.Key,
            Projection = view.Projection, // SourceToWorld deliberately remains the actual source-to-MODEL affine transform.
            PatternToWorld = Affine3.Identity, Style = source.Style with { Dashes = [], Linetype = null },
            Contours = contours, Filled = false, Regions = regions.ToImmutable(), Strokes = strokes,
            Text = text, Image = image, TextClip = textClip, ImageClip = imageClip, Clips = [view.Clip], Bounds = bounds
        };
        void Include(ImmutableArray<RenderContour> values)
        { foreach (var c in values) foreach (var p in c.Points) bounds = bounds.Include(p); }
    }

    private static Vec3 Intersect(Vec3 a, Vec3 b, double da, double db)
    {
        // Normalize before subtracting to avoid overflow for opposite, very large signed distances.
        var scale = Math.Max(Math.Abs(da), Math.Abs(db));
        if (!(scale > 0) || !double.IsFinite(scale)) throw new ArithmeticException("Frustum intersection exceeds numeric range.");
        var u = da / scale; var v = db / scale; var t = Math.Clamp(u / (u - v), 0, 1);
        var result = a * (1 - t) + b * t;
        if (!result.IsFinite) throw new ArithmeticException("Frustum intersection overflow.");
        return result;
    }
    private static RenderClip Mask(ImmutableArray<Vec3> points)
    {
        var bounds = Bounds2.Empty; foreach (var p in points) bounds = bounds.Include(p);
        return new([new(points, true)], bounds);
    }
    internal static long PointCount(RenderItem? item) => item is null ? 0 :
        Count(item.Contours) + item.Regions.Sum(r => Count(r.Contours)) + item.Strokes.Sum(s => Count(s.Contours)) +
        (item.TextClip is { } t ? Count(t.Contours) : 0) + (item.ImageClip is { } image ? Count(image.Contours) : 0) +
        (item.Image is { } raster ? (long)raster.Corners.Length + raster.ClipPolygon.Length : 0);
    private static long Count(ImmutableArray<RenderContour> contours) => contours.Sum(c => (long)c.Points.Length);
}
