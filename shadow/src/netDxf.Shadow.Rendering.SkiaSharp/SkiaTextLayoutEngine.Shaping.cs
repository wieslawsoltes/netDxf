using global::SkiaSharp;

namespace netDxf.Shadow.Rendering.SkiaSharp;

public sealed partial class SkiaTextLayoutEngine
{
    private const int MaximumShapedRuns = 4096;
    private const int MaximumShapedUnits = 262_144;
    private readonly record struct ShapeKey(FontEntry Font, float Height, string Content);
    private sealed record ShapedRun(ShapeKey Key, TextRunMetrics Metrics, SKTextBlob? Blob, int Units) : IDisposable
    {
        public void Dispose() => Blob?.Dispose();
    }
    private readonly struct ShapedRunLease(ShapedRun value, bool owned) : IDisposable
    {
        internal ShapedRun Value => value;
        public void Dispose() { if (owned) value.Dispose(); }
    }
    private readonly Dictionary<ShapeKey, LinkedListNode<ShapedRun>> shapedRuns = new();
    private readonly LinkedList<ShapedRun> shapeOrder = new();
    private int shapedUnits;
    private long shapeCompilations, shapeHits;

    // All callers hold gate. Color, layout position, width, shear and camera transforms
    // remain draw-time state; the native font and exact string determine glyph shaping.
    private ShapedRunLease GetShapedRun(RichTextStyle style, string content)
    {
        var key = new ShapeKey(GetFont(style), F(style.Height), content);
        if (shapedRuns.TryGetValue(key, out var node))
        {
            shapeOrder.Remove(node); shapeOrder.AddLast(node); shapeHits++;
            return new(node.Value, false);
        }
        using var font = new SKFont(key.Font.Typeface, key.Height)
        { LinearMetrics = true, Subpixel = true, Hinting = SKFontHinting.None, Edging = SKFontEdging.Antialias };
        var shape = key.Font.Shaper.Shape(content, font);
        var glyphs = new ushort[shape.Codepoints.Length];
        for (var i = 0; i < glyphs.Length; i++) glyphs[i] = checked((ushort)shape.Codepoints[i]);
        var rectangles = new SKRect[glyphs.Length];
        font.GetGlyphWidths(glyphs, Span<float>.Empty, rectangles);
        var bounds = Bounds2.Empty;
        for (var i = 0; i < rectangles.Length; i++)
        {
            var r = rectangles[i]; var p = shape.Points[i];
            if (!r.IsEmpty) bounds = bounds.Include(new(r.Left + p.X, r.Top + p.Y)).Include(new(r.Right + p.X, r.Bottom + p.Y));
        }
        var metrics = font.Metrics;
        SKTextBlob? blob = null;
        if (glyphs.Length > 0)
        {
            using var builder = new SKTextBlobBuilder();
            var run = builder.AllocateRawPositionedRun(font, glyphs.Length, null);
            glyphs.AsSpan().CopyTo(run.Glyphs);
            shape.Points.AsSpan().CopyTo(run.Positions);
            blob = builder.Build();
        }
        var result = new ShapedRun(key,
            new(Math.Max(0, shape.Width), Math.Max(0, -metrics.Ascent), Math.Max(0, metrics.Descent), bounds),
            blob, Math.Max(content.Length, glyphs.Length));
        shapeCompilations++;
        // A single unusually long run is still rendered in full, with temporary ownership.
        if (result.Units > MaximumShapedUnits) return new(result, true);
        while (shapedRuns.Count >= MaximumShapedRuns || shapedUnits > MaximumShapedUnits - result.Units)
        {
            var oldest = shapeOrder.First!;
            shapeOrder.RemoveFirst(); shapedRuns.Remove(oldest.Value.Key);
            shapedUnits -= oldest.Value.Units; oldest.Value.Dispose();
        }
        shapedRuns.Add(key, shapeOrder.AddLast(result)); shapedUnits += result.Units;
        return new(result, false);
    }

    private void ClearShapedRuns()
    {
        foreach (var run in shapeOrder) run.Dispose();
        shapedRuns.Clear(); shapeOrder.Clear(); shapedUnits = 0;
    }
}
