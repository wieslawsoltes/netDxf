using System.Collections.Immutable;
using global::SkiaSharp;
using netDxf.Shadow.Geometry;

namespace netDxf.Shadow.Rendering.SkiaSharp;

public sealed partial class SkiaTextLayoutEngine
{
    /// <summary>Shapes the same runs as native drawing and detaches TrueType/CFF paths into
    /// line/rational-Bezier contours. Caller owns no native resources. Rules become closed strips.</summary>
    public CadGeometry Outline(CadText text, RichTextLayoutResult layout, int maximumCurves,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text); ArgumentNullException.ThrowIfNull(layout);
        if (maximumCurves < 1) throw new ArgumentOutOfRangeException(nameof(maximumCurves));
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            var regions = ImmutableArray.CreateBuilder<CadRegion>();
            var work = 0;
            foreach (var run in layout.Runs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entry = GetFont(run.Style);
                using var font = new SKFont(entry.Typeface, F(run.Style.Height))
                    { LinearMetrics = true, Subpixel = true, Hinting = SKFontHinting.None };
                var shaped = entry.Shaper.Shape(run.Text, font);
                var contours = ImmutableArray.CreateBuilder<CadContour>();
                for (var g = 0; g < shaped.Codepoints.Length; g++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (++work > maximumCurves) throw new InvalidOperationException("Text outline work budget exceeded.");
                    using var path = font.GetGlyphPath(checked((ushort)shaped.Codepoints[g]));
                    if (path is null)
                    {
                        // Spaces may be blank; bitmap/color-only visible glyphs must not disappear silently.
                        font.GetGlyphWidths(new ushort[] { checked((ushort)shaped.Codepoints[g]) }, out SKRect[] ink);
                        if (ink.Any(bounds => !bounds.IsEmpty))
                            throw new NotSupportedException("A visible glyph has no scalable outline; projective bitmap glyph rendering is not supported.");
                        continue;
                    }
                    using var iterator = path.CreateRawIterator();
                    var points = new SKPoint[4]; var curves = ImmutableArray.CreateBuilder<Curve3>();
                    Vec3 start = default, end = default;
                    Vec3 Point(int index)
                    {
                        var p = points[index]; var offset = shaped.Points[g];
                        var x = p.X + offset.X; var y = p.Y + offset.Y;
                        return new(run.X + x * run.Style.WidthFactor - y * Math.Tan(run.Style.ObliqueDegrees * Math.PI / 180), run.Y + y);
                    }
                    void Flush()
                    {
                        if (curves.Count == 0) return;
                        if (end != start) Add(new LineCurve(end, start));
                        contours.Add(new(curves.ToImmutable(), true)); curves.Clear();
                    }
                    void Add(Curve3 curve)
                    {
                        if (++work > maximumCurves) throw new InvalidOperationException("Text outline curve budget exceeded.");
                        curves.Add(curve); end = curve.End;
                    }
                    for (var verb = iterator.Next(points); verb != SKPathVerb.Done; verb = iterator.Next(points))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        switch (verb)
                        {
                            case SKPathVerb.Move: Flush(); start = end = Point(0); break;
                            case SKPathVerb.Line: Add(new LineCurve(Point(0), Point(1))); break;
                            case SKPathVerb.Quad: Add(new NurbsCurve([Point(0), Point(1), Point(2)], [], [0, 0, 0, 1, 1, 1], 2)); break;
                            case SKPathVerb.Conic: Add(new NurbsCurve([Point(0), Point(1), Point(2)], [1, iterator.ConicWeight(), 1], [0, 0, 0, 1, 1, 1], 2)); break;
                            case SKPathVerb.Cubic: Add(new NurbsCurve([Point(0), Point(1), Point(2), Point(3)], [], [0, 0, 0, 0, 1, 1, 1, 1], 3)); break;
                            case SKPathVerb.Close: Flush(); break;
                            default: throw new NotSupportedException("Unsupported native glyph path verb.");
                        }
                    }
                    Flush();
                }
                if (contours.Count > 0) regions.Add(new(contours.ToImmutable(), CadFillRule.NonZero) { Argb = run.Style.Color });
            }
            foreach (var rule in layout.Rules)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (work > maximumCurves - 4) throw new InvalidOperationException("Text rule outline budget exceeded.");
                work += 4;
                var a = new Vec3(rule.X1, rule.Y1); var b = new Vec3(rule.X2, rule.Y2); var d = b - a;
                if (!(d.Length > 0) || !(rule.Thickness > 0)) continue;
                var n = new Vec3(-d.Y, d.X) * (rule.Thickness / (2 * d.Length));
                Vec3[] p = [a + n, b + n, b - n, a - n];
                regions.Add(new([new(p.Select((v, i) => (Curve3)new LineCurve(v, p[(i + 1) % 4])).ToImmutableArray(), true)],
                    CadFillRule.NonZero) { Argb = rule.Color });
            }
            return new([]) { Regions = regions.ToImmutable() };
        }
    }
}
