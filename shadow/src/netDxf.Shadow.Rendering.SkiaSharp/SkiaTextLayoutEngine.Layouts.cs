using netDxf.Shadow.Geometry;

namespace netDxf.Shadow.Rendering.SkiaSharp;

public sealed partial class SkiaTextLayoutEngine
{
    private const int MaximumSharedLayouts = 512;
    private const int MaximumLayoutUnits = 262_144;

    // RichTextLayout works entirely in local text coordinates. Origin and axes are
    // applied later by the renderer and do not affect parsing, wrapping or alignment.
    // Retain double bit patterns rather than folding signed zero or NaN payloads.
    private readonly record struct LayoutKey(string Content, long Height, string Family,
        string Alignment, bool Multiline, long Width, bool HasSpacing, long Spacing, bool ExactSpacing)
    {
        internal LayoutKey(CadText text) : this(text.Content, BitConverter.DoubleToInt64Bits(text.Height),
            text.FontFamily, text.Alignment, text.Multiline, BitConverter.DoubleToInt64Bits(text.Width),
            text.LineSpacing is not null, BitConverter.DoubleToInt64Bits(text.LineSpacing?.Factor ?? 0),
            text.LineSpacing?.Exact ?? false) { }
    }
    private sealed record SharedLayout(LayoutKey Key, RichTextLayoutResult Layout, int Units);
    private readonly Dictionary<LayoutKey, LinkedListNode<SharedLayout>> sharedLayouts = new();
    private readonly LinkedList<SharedLayout> layoutOrder = new();
    private int layoutUnits, layoutDepth;
    private long layoutCompilations, sharedLayoutHits, layoutEpoch;

    // All callers hold gate; each CadText also retains its O(1) ephemeron lookup.
    private RichTextLayoutResult LayoutShared(CadText text)
    {
        var key = new LayoutKey(text);
        if (sharedLayouts.TryGetValue(key, out var cached))
        {
            layoutOrder.Remove(cached); layoutOrder.AddLast(cached); sharedLayoutHits++;
            return cached.Value.Layout;
        }
        var compilation = ++layoutCompilations;
        var fontRevision = revision;
        var epoch = layoutEpoch;
        // A host font resolver can synchronously re-enter Layout. Neither the inner
        // nor the interrupted outer computation may publish a shared entry then.
        if (layoutDepth++ > 0) ClearSharedLayouts();
        RichTextLayoutResult result;
        try { result = RichTextLayout.Layout(text, Measure); }
        finally { layoutDepth--; }
        if (disposed || revision != fontRevision || layoutEpoch != epoch || layoutCompilations != compilation || layoutDepth != 0) return result;

        var units = (long)text.Content.Length + (text.FontFamily?.Length ?? 0) + (text.Alignment?.Length ?? 0) +
            result.Runs.Length * 16L + result.Rules.Length * 8L + result.Lines.Length * 8L;
        foreach (var issue in result.Diagnostics) units += issue.Code.Length + (long)issue.Message.Length;
        // Large layouts are still returned in full and cached by CadText lifetime;
        // they must not displace the bounded pool of shared local layouts.
        if (units > MaximumLayoutUnits) return result;
        while (sharedLayouts.Count >= MaximumSharedLayouts || layoutUnits > MaximumLayoutUnits - units)
        {
            var oldest = layoutOrder.First!;
            layoutOrder.RemoveFirst(); sharedLayouts.Remove(oldest.Value.Key); layoutUnits -= oldest.Value.Units;
        }
        var entry = new SharedLayout(key, result, (int)units);
        sharedLayouts.Add(key, layoutOrder.AddLast(entry)); layoutUnits += entry.Units;
        return result;
    }

    private void ClearSharedLayouts()
    {
        sharedLayouts.Clear(); layoutOrder.Clear(); layoutUnits = 0; layoutEpoch++;
    }
}
