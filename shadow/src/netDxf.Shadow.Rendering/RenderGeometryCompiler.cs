using System.Collections.Immutable;
using netDxf.Shadow.Geometry;

namespace netDxf.Shadow.Rendering;

internal sealed record RenderGeometryData(ImmutableArray<RenderContour> Contours,
    ImmutableArray<RenderRegion> Regions, Bounds2 Bounds);

internal static class RenderGeometryCompiler
{
    internal static RenderGeometryData Compile(CadGeometry geometry, SamplingOptions sampling)
    {
        var bounds = Bounds2.Empty;
        var points = 0L;
        var contours = CompileContours(geometry.DisplayContours.IsDefault ? geometry.Contours : geometry.DisplayContours);
        var regions = ImmutableArray.CreateBuilder<RenderRegion>(geometry.Regions.Length);
        foreach (var region in geometry.Regions)
            regions.Add(new(CompileContours(region.Contours), region.FillRule, region.Gradient));
        if (geometry.Text is { } text)
        {
            var paragraphs = TextLayout.PlainText(text.Content, text.Multiline).Split('\n');
            var width = Math.Max(text.Width, paragraphs.Select(p => p.Length).DefaultIfEmpty(1).Max() * text.Height * 1.5);
            var height = text.Height * (paragraphs.Length + 1) * 1.5;
            foreach (var dx in new[] { -width, width }) foreach (var dy in new[] { -height, height })
                bounds = bounds.Include(text.Origin + text.XAxis * dx + text.YAxis * dy);
        }
        if (geometry.Image is { Visible: true } image) foreach (var point in image.Corners) bounds = bounds.Include(point);
        return new(contours, regions.ToImmutable(), bounds);

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
