using System.Runtime.CompilerServices;
using netDxf.Shadow.Geometry;

namespace netDxf.Shadow.Rendering;

/// <summary>Host-supplied text shaping/metrics boundary. Results contain immutable managed data
/// only. Advance Revision whenever font resolution or layout configuration changes.</summary>
public interface ITextLayoutEngine
{
    long Revision { get; }
    RichTextLayoutResult Layout(CadText text);
}

/// <summary>Portable fallback for hosts without font services. Explicitly approximate metrics,
/// not glyph-accurate text bounds. Supply a shaping engine for production typography.</summary>
public sealed class ApproximateTextLayoutEngine : ITextLayoutEngine
{
    public static ApproximateTextLayoutEngine Instance { get; } = new();
    private readonly ConditionalWeakTable<CadText, RichTextLayoutResult> cache = new();
    public long Revision => 0;
    public RichTextLayoutResult Layout(CadText text) => cache.GetValue(text, static value =>
    {
        var layout = RichTextLayout.Layout(value, RichTextLayout.ApproximateMetrics);
        return layout with { Diagnostics = layout.Diagnostics.Add(new(0, "text.metrics", "Portable approximate font metrics; configure ITextLayoutEngine for shaped glyph bounds.")) };
    });
}
