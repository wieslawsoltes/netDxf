using System.Collections.Immutable;

namespace netDxf.Shadow.Geometry;

public sealed record CadStyle(uint Argb, double Lineweight, ImmutableArray<double> Dashes, bool Visible, string Layer)
{
    public static CadStyle Default { get; } = new(0xffe4e8ef, 0.25, [], true, "0");
    /// <summary>Optional detached complex metadata. Dashes retains the signed effective stroke lengths.</summary>
    public CadLinetype? Linetype { get; init; }
}
public sealed record CadContour(ImmutableArray<Curve3> Curves, bool Closed = false)
{
    /// <summary>Restart the periodic linetype at each authored analytic curve, not each sampled edge.
    /// The curve chain must be connected. Semantic contours remain unchanged.</summary>
    public bool RestartLinetypeAtCurves { get; init; }
}
/// <summary>Entity-level baseline spacing. Factor multiplies a single line (5/3 of the entity height).</summary>
public sealed record CadLineSpacing(double Factor = 1, bool Exact = false);
public sealed record CadText(string Content, Vec3 Origin, Vec3 XAxis, Vec3 YAxis, double Height,
    string FontFamily, string Alignment, bool Multiline, double Width)
{
    /// <summary>Null preserves portable legacy spacing; projected MTEXT carries its authored settings.</summary>
    public CadLineSpacing? LineSpacing { get; init; }
}
public sealed record CadImage(string FileName, ImmutableArray<Vec3> Corners)
{
    /// <summary>World-space clipping polygon; empty means the complete image parallelogram.</summary>
    public ImmutableArray<Vec3> ClipPolygon { get; init; } = [];
    public bool ClipOutside { get; init; }
    public double Brightness { get; init; } = 0.5;
    public double Contrast { get; init; } = 0.5;
    public double Fade { get; init; }
    public bool Visible { get; init; } = true;
}
public sealed record CadGeometry(ImmutableArray<CadContour> Contours, bool Filled = false,
    CadText? Text = null, CadImage? Image = null)
{
    public static CadGeometry Empty { get; } = new([]);
    /// <summary>Immutable semantic components, flattened by the scene builder with the parent entity identity.</summary>
    public ImmutableArray<CadComponent> Components { get; init; } = [];
    /// <summary>Optional presentation strokes. Default uses Contours; empty suppresses them.
    /// Contours always retains the semantic curves consumed by topology and analysis.</summary>
    public ImmutableArray<CadContour> DisplayContours { get; init; }
    public ImmutableArray<CadRegion> Regions { get; init; } = [];
    public ImmutableArray<CadStrokeGroup> Strokes { get; init; } = [];
    public CadGeometry Transform(Affine3 matrix) => this with
    {
        Components = Components.Select(part => part with { Geometry = part.Geometry.Transform(matrix) }).ToImmutableArray(),
        Contours = TransformContours(Contours, matrix),
        DisplayContours = DisplayContours.IsDefault ? default : TransformContours(DisplayContours, matrix),
        Regions = Regions.Select(region => region.Transform(matrix)).ToImmutableArray(),
        Strokes = Strokes.Select(stroke => stroke.Transform(matrix)).ToImmutableArray(),
        Text = Text is { } text ? text with { Origin = matrix.Point(text.Origin), XAxis = matrix.Direction(text.XAxis), YAxis = matrix.Direction(text.YAxis) } : null,
        Image = Image is { } image ? image with
        {
            Corners = image.Corners.Select(matrix.Point).ToImmutableArray(),
            ClipPolygon = image.ClipPolygon.Select(matrix.Point).ToImmutableArray()
        } : null
    };
    internal static ImmutableArray<CadContour> TransformContours(ImmutableArray<CadContour> contours, Affine3 matrix)
        => contours.Select(c => c with { Curves = c.Curves.Select(p => p.Transform(matrix)).ToImmutableArray() }).ToImmutableArray();
}
public sealed record CadOccurrence(string Key, ObjectId Entity, ImmutableArray<ObjectId> InsertPath,
    CadStyle Style, CadGeometry Geometry)
{
    public Affine3 SourceToWorld { get; init; } = Affine3.Identity;
    /// <summary>Marks consumer-generated presentation, such as a linetype symbol. The stock semantic builder does not emit these.</summary>
    public bool IsPresentationOnly { get; init; }
}
public sealed record CadScene(Guid DocumentId, long Revision, string Layout, ImmutableArray<CadOccurrence> Occurrences,
    ImmutableArray<ShadowDiagnostic> Diagnostics);
public sealed record SceneOptions(string Layout = "Model", int MaximumDepth = 64, int MaximumOccurrences = 1_000_000, bool IncludeInvisible = false)
{
    /// <summary>Consumer scale multiplied with authored LTSCALE and entity scale; never writes source state.</summary>
    public double LinetypeScale { get; init; } = 1;
}

/// <summary>Custom entity projection has access only to detached shadow values.</summary>
public interface IEntityProjector
{
    bool TryProject(ShadowReader entity, out CadGeometry geometry, out string? diagnostic);
}
