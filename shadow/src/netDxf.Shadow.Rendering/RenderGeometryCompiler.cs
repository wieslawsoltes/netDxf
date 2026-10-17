using System.Collections.Immutable;
using netDxf.Shadow.Geometry;

namespace netDxf.Shadow.Rendering;

internal sealed record RenderGeometryData(ImmutableArray<RenderContour> Contours,
    ImmutableArray<RenderRegion> Regions, ImmutableArray<RenderStrokeGroup> Strokes, Bounds2 Bounds, RichTextLayoutResult? TextLayout);

internal static class RenderGeometryCompiler
{
    internal static RenderGeometryData Compile(CadGeometry geometry, SamplingOptions sampling, ITextLayoutEngine textEngine)
    {
        var bounds = Bounds2.Empty;
        var points = 0L;
        var lineSamplingValidated = false;
        var contours = CompileContours(geometry.DisplayContours.IsDefault ? geometry.Contours : geometry.DisplayContours);
        var regions = ImmutableArray.CreateBuilder<RenderRegion>(geometry.Regions.Length);
        foreach (var region in geometry.Regions)
            regions.Add(new(CompileContours(region.Contours), region.FillRule, region.Gradient) { Argb = region.Argb });
        var strokes = geometry.Strokes.Select(stroke => new RenderStrokeGroup(CompileContours(stroke.Contours), stroke.Argb)).ToImmutableArray();
        RichTextLayoutResult? textLayout = null;
        if (geometry.Text is { Height: > 0 } text)
        {
            textLayout = textEngine.Layout(text);
            var rectangle = textLayout.Bounds;
            if (!rectangle.IsEmpty)
                foreach (var x in new[] { rectangle.MinX, rectangle.MaxX })
                    foreach (var y in new[] { rectangle.MinY, rectangle.MaxY })
                        bounds = bounds.Include(text.Origin + text.XAxis * x - text.YAxis * y);
        }
        if (geometry.Image is { Visible: true } image) foreach (var point in image.Corners) bounds = bounds.Include(point);
        return new(contours, regions.MoveToImmutable(), strokes, bounds, textLayout);

        ImmutableArray<RenderContour> CompileContours(ImmutableArray<CadContour> source)
        {
            var result = ImmutableArray.CreateBuilder<RenderContour>(source.Length);
            foreach (var contour in source)
            {
                var vertices = ImmutableArray.CreateBuilder<Vec3>(Math.Min(contour.Curves.Length, 1023) + 1);
                var breaks = contour.RestartLinetypeAtCurves && contour.Curves.Length > 1
                    ? ImmutableArray.CreateBuilder<int>() : null;
                foreach (var curve in contour.Curves)
                {
                    if (curve is LineCurve line)
                    {
                        // Lines already are exact samples. Retain the sampler's validation
                        // contract while avoiding a temporary immutable array per segment.
                        if (!lineSamplingValidated)
                        {
                            if (!double.IsFinite(sampling.Tolerance) || sampling.Tolerance <= 0 || sampling.MaximumPoints < 2 || sampling.MaximumDepth is < 1 or > 30)
                                throw new ArgumentOutOfRangeException("options");
                            lineSamplingValidated = true;
                        }
                        if (!BeginCurve(line.A)) AddPoint(line.A);
                        AddPoint(line.B);
                    }
                    else
                    {
                        var sampled = CurveSampler.Sample(curve, sampling);
                        var first = !sampled.IsEmpty && BeginCurve(sampled[0]) ? 1 : 0;
                        for (var i = first; i < sampled.Length; i++) AddPoint(sampled[i]);
                    }
                }
                if (breaks is { Count: > 0 } && breaks[^1] >= vertices.Count - 1) breaks.RemoveAt(breaks.Count - 1);
                result.Add(new(vertices.Count == vertices.Capacity ? vertices.MoveToImmutable() : vertices.ToImmutable(), contour.Closed)
                    { PatternBreaks = breaks?.ToImmutable() ?? default });

                bool BeginCurve(Vec3 first)
                {
                    var distance = vertices.Count > 0 ? Vec3.Distance(vertices[^1], first) : double.PositiveInfinity;
                    if (breaks is not null && vertices.Count > 0)
                    {
                        if (distance >= 1e-12)
                            throw new ArgumentException("Restarted linetype curves must form a connected chain.");
                        var boundary = vertices.Count - 1;
                        if (boundary > 0 && (breaks.Count == 0 || breaks[^1] != boundary)) breaks.Add(boundary);
                    }
                    return distance < 1e-12;
                }
                void AddPoint(Vec3 point)
                {
                    if (++points > 4_000_000) throw new InvalidOperationException("Per-entity render geometry budget exceeded.");
                    vertices.Add(point); bounds = bounds.Include(point);
                }
            }
            return result.MoveToImmutable();
        }
    }
}
