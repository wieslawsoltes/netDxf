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
        return new(contours, regions.ToImmutable(), strokes, bounds, textLayout);

        ImmutableArray<RenderContour> CompileContours(ImmutableArray<CadContour> source)
        {
            var result = ImmutableArray.CreateBuilder<RenderContour>(source.Length);
            foreach (var contour in source)
            {
                var vertices = ImmutableArray.CreateBuilder<Vec3>();
                foreach (var curve in contour.Curves)
                {
                    var sampled = CurveSampler.Sample(curve, sampling);
                    for (var i = 0; i < sampled.Length; i++)
                    {
                        if (i == 0 && vertices.Count > 0 && Vec3.Distance(vertices[^1], sampled[i]) < 1e-12) continue;
                        if (++points > 4_000_000) throw new InvalidOperationException("Per-entity render geometry budget exceeded.");
                        vertices.Add(sampled[i]); bounds = bounds.Include(sampled[i]);
                    }
                }
                result.Add(new(vertices.ToImmutable(), contour.Closed));
            }
            return result.ToImmutable();
        }
    }
}
