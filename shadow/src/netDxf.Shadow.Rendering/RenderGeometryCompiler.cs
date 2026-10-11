using System.Collections.Immutable;
using netDxf.Shadow.Geometry;

namespace netDxf.Shadow.Rendering;

internal sealed record RenderGeometryData(ImmutableArray<RenderContour> Contours, Bounds2 Bounds);

internal static class RenderGeometryCompiler
{
    internal static RenderGeometryData Compile(CadGeometry geometry, SamplingOptions sampling)
    {
        var contours = ImmutableArray.CreateBuilder<RenderContour>();
        var bounds = Bounds2.Empty;
        foreach (var contour in geometry.Contours)
        {
            var points = ImmutableArray.CreateBuilder<Vec3>();
            foreach (var curve in contour.Curves)
            {
                var sampled = CurveSampler.Sample(curve, sampling);
                for (var i = 0; i < sampled.Length; i++)
                {
                    if (i == 0 && points.Count > 0 && Vec3.Distance(points[^1], sampled[i]) < 1e-12) continue;
                    points.Add(sampled[i]); bounds = bounds.Include(sampled[i]);
                }
            }
            contours.Add(new(points.ToImmutable(), contour.Closed));
        }
        if (geometry.Text is { } text)
        {
            var paragraphs = TextLayout.PlainText(text.Content, text.Multiline).Split('\n');
            var width = Math.Max(text.Width, paragraphs.Select(p => p.Length).DefaultIfEmpty(1).Max() * text.Height * 1.5);
            var height = text.Height * (paragraphs.Length + 1) * 1.5;
            foreach (var dx in new[] { -width, width }) foreach (var dy in new[] { -height, height })
                bounds = bounds.Include(text.Origin + text.XAxis * dx + text.YAxis * dy);
        }
        if (geometry.Image is { } image) foreach (var point in image.Corners) bounds = bounds.Include(point);
        return new(contours.ToImmutable(), bounds);
    }
}
