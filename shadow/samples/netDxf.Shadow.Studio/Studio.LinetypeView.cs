using netDxf.Shadow.Geometry;
using netDxf.Shadow.Rendering;

namespace netDxf.Shadow.Studio;

public sealed record LinetypeViewRequest(long Revision, string Alignment);

public sealed partial class Studio
{
    private CadLinetypeAlignment livePatternAlignment;
    private CadLinetypeAlignment PatternAlignment
    {
        get => activeBranch?.LinetypeAlignment ?? livePatternAlignment;
        set { if (activeBranch is { } branch) branch.LinetypeAlignment = value; else livePatternAlignment = value; }
    }
    private RenderCompileOptions ViewOptions() => new(layout) { LinetypeAlignment = PatternAlignment, ModelAnnotation = Annotation };

    /// <summary>Changes workspace presentation only. Stale requests are guarded by the existing
    /// document/workspace epoch; no source edit or history entry is generated.</summary>
    public object SetLinetypeView(LinetypeViewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (gate)
        {
            Check(request.Revision);
            if (request.Alignment is not ("Periodic" or "A")) throw new ArgumentException("Choose Periodic or A alignment.", nameof(request));
            var alignment = request.Alignment == "A" ? CadLinetypeAlignment.A : CadLinetypeAlignment.Periodic;
            if (alignment == PatternAlignment) return State();
            var nextVersion = checked(viewVersion + 1);
            PatternAlignment = alignment; viewVersion = nextVersion;
            editor.Select([]); // Presentation ordinals can change when fitting removes short motifs.
            return UpdatedState();
        }
    }
}
