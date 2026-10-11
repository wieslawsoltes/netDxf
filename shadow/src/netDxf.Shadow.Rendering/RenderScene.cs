using System.Collections.Immutable;
using netDxf.Shadow.Geometry;

namespace netDxf.Shadow.Rendering;

public sealed record RenderContour(ImmutableArray<Vec3> Points, bool Closed);
public sealed record RenderRegion(ImmutableArray<RenderContour> Contours, CadFillRule FillRule, CadGradient? Gradient);
public sealed record RenderItem(string Key, ObjectId Entity, ImmutableArray<ObjectId> InsertPath, CadStyle Style,
    ImmutableArray<RenderContour> Contours, bool Filled, CadText? Text, CadImage? Image, Bounds2 Bounds)
{
    public ImmutableArray<RenderRegion> Regions { get; init; } = [];
    public RichTextLayoutResult? TextLayout { get; init; }
    public ImmutableArray<RenderClip> Clips { get; init; } = [];
    public RenderClip? TextClip { get; init; }
    public RenderClip? ImageClip { get; init; }
    public PaperViewportProjection? Projection { get; init; }
    public Affine3 SourceToWorld { get; init; } = Affine3.Identity;
}
public sealed record RenderScene(Guid DocumentId, long Revision, string Layout, ImmutableArray<RenderItem> Items,
    Bounds2 Bounds, SpatialIndex Index, ImmutableArray<ShadowDiagnostic> Diagnostics);
public sealed record RenderCompileOptions(string Layout = "Model", double Tolerance = 0.05)
{
    public bool IncludePaperViewports { get; init; } = true;
    public PaperSpaceOptions PaperSpace { get; init; } = new();
}
