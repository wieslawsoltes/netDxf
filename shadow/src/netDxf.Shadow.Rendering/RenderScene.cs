using System.Collections.Immutable;
using netDxf.Shadow.Geometry;

namespace netDxf.Shadow.Rendering;

public sealed record RenderContour(ImmutableArray<Vec3> Points, bool Closed)
{
    /// <summary>Strictly increasing interior point indices where the periodic pattern restarts.
    /// Default/empty continues across the entire contour. Each boundary belongs to both adjacent paths.</summary>
    public ImmutableArray<int> PatternBreaks { get; init; }
}
public sealed record RenderRegion(ImmutableArray<RenderContour> Contours, CadFillRule FillRule, CadGradient? Gradient)
{
    public uint? Argb { get; init; }
}
public sealed record RenderStrokeGroup(ImmutableArray<RenderContour> Contours, uint? Argb = null);
public sealed record RenderItem(string Key, ObjectId Entity, ImmutableArray<ObjectId> InsertPath, CadStyle Style,
    ImmutableArray<RenderContour> Contours, bool Filled, CadText? Text, CadImage? Image, Bounds2 Bounds)
{
    public ImmutableArray<RenderRegion> Regions { get; init; } = [];
    public ImmutableArray<RenderStrokeGroup> Strokes { get; init; } = [];
    public RichTextLayoutResult? TextLayout { get; init; }
    public ImmutableArray<RenderClip> Clips { get; init; } = [];
    public RenderClip? TextClip { get; init; }
    public RenderClip? ImageClip { get; init; }
    public PaperViewportProjection? Projection { get; init; }
    public Affine3 SourceToWorld { get; init; } = Affine3.Identity;
    /// <summary>Metric frame for signed pattern distances. Identity retains world-space periods.</summary>
    public Affine3 PatternToWorld { get; init; } = Affine3.Identity;
    /// <summary>Pickable presentation with source identity, excluded from ordinary CAD snapping.</summary>
    public bool IsPresentationOnly { get; init; }
}
public sealed record RenderScene(Guid DocumentId, long Revision, string Layout, ImmutableArray<RenderItem> Items,
    Bounds2 Bounds, SpatialIndex Index, ImmutableArray<ShadowDiagnostic> Diagnostics);
public sealed record RenderCompileOptions(string Layout = "Model", double Tolerance = 0.05)
{
    /// <summary>Definition-space rendering, isolated from model annotation and paper projection.</summary>
    public ObjectId? RootBlock { get; init; }
    public bool IncludePaperViewports { get; init; } = true;
    /// <summary>Consumer-only multiplier for authored linetype scales. Paper composition compensates
    /// this scale per viewport before compiling both dashes and decorations.</summary>
    public double LinetypeScale { get; init; } = 1;
    /// <summary>Opt-in endpoint fitting. Periodic retains the original rendering contract.</summary>
    public CadLinetypeAlignment LinetypeAlignment { get; init; }
    /// <summary>Model-tab-only annotation context. Paper model views explicitly disable it.</summary>
    public ModelAnnotationContext? ModelAnnotation { get; init; } = ModelAnnotationContext.Default;
    /// <summary>Document-bound viewport layer appearance, applied before model style resolution.
    /// Null retains drawing properties. Model-tab compilation ignores this paper-only context.</summary>
    public ViewportOverrideCatalog? ViewportOverrides { get; init; }
    public PaperSpaceOptions PaperSpace { get; init; } = new();
}
