using System.Collections.Immutable;
using netDxf.Shadow.Geometry;

namespace netDxf.Shadow.Rendering;

/// <summary>A shared immutable paper-space clip. No backend path or mutable source object is retained.</summary>
public sealed record RenderClip(ImmutableArray<RenderContour> Contours, Bounds2 Bounds);

/// <summary>Paper-view provenance. Affine mapping is invertible only for orthographic views;
/// a non-null Perspective supplies projective mapping and picking rays.</summary>
public sealed record PaperViewportProjection(ObjectId ViewportEntity, Affine3 ModelToPaper,
    Affine3 PaperToModel, double Scale)
{
    /// <summary>Non-null for a projective view. The affine fields then describe only its target plane.</summary>
    public PerspectiveCamera? Perspective { get; init; }
}

/// <summary>Bounds the amount of work performed while composing multiple model views.</summary>
public sealed record PaperSpaceOptions(int MaximumViewports = 256, int MaximumItems = 1_000_000,
    int MaximumCandidates = 4_000_000, int MaximumClipPoints = 262_144)
{
    /// <summary>Maximum distinct model linetype scales compiled for one paper scene.</summary>
    public int MaximumPatternScales { get; init; } = 32;
    public PerspectiveOptions? Perspective { get; init; }
}

internal sealed record PaperViewport(PaperViewportProjection Projection, RenderClip Clip,
    ImmutableHashSet<string> FrozenLayers, double Back, double Front, int Stacking)
{
    internal static bool HasFlag(ShadowReader value, string name, int bit)
    {
        var text = value.Text;
        return long.TryParse(text, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out var numeric)
            ? (numeric & bit) != 0 : text.Split(',').Any(part => part.Trim() == name);
    }

    internal static PaperViewport Read(ShadowReader entity, double tolerance, int maximumClipPoints, PerspectiveOptions? perspective = null)
    {
        if (entity.Id is not { } id) throw new ArgumentException("A viewport needs a source identity.");
        var width = entity["Width"].Number(); var height = entity["Height"].Number();
        var viewHeight = entity["ViewHeight"].Number(); var center = entity["Center"].Vector();
        var target = entity["ViewTarget"].Vector(); var direction = entity["ViewDirection"].Vector(Vec3.UnitZ);
        var viewCenter = entity["ViewCenter"].Vector(); var twist = entity["TwistAngle"].Number();
        if (!(width > 0 && height > 0 && viewHeight > 0) || !double.IsFinite(width + height + viewHeight) ||
            !center.IsFinite || !target.IsFinite || !viewCenter.IsFinite || !direction.IsFinite ||
            !double.IsFinite(direction.Length) || direction.Length == 0 || !double.IsFinite(twist))
            throw new ArgumentException("Viewport dimensions and camera values must be finite and nondegenerate.");
        var isPerspective = HasFlag(entity["Status"], "PerspectiveMode", 1);
        if (isPerspective && perspective is null)
            throw new NotSupportedException("Enable an explicit PerspectiveOptions policy to display perspective viewports.");
        perspective?.Validate();
        if (isPerspective && perspective!.FieldOfView == PerspectiveFieldOfView.LensLength)
        {
            var lens = entity["LensLength"].Number();
            if (!(lens > 0) || !double.IsFinite(lens)) throw new ArgumentException("Perspective lens length must be positive and finite.");
            viewHeight = direction.Length * (perspective.FilmHeightMillimeters / lens);
            if (!(viewHeight > 0) || !double.IsFinite(viewHeight)) throw new ArgumentException("Lens field of view exceeds numeric range.");
        }
        var basis = Affine3.ObjectCoordinates(direction);
        var worldToDcs = new Affine3(new(basis.X.X, basis.Y.X, basis.Z.X),
            new(basis.X.Y, basis.Y.Y, basis.Z.Y), new(basis.X.Z, basis.Y.Z, basis.Z.Z), Vec3.Zero);
        var scale = height / viewHeight;
        var transform = Affine3.Move(new(center.X - viewCenter.X * scale, center.Y - viewCenter.Y * scale)) *
            Affine3.Scale(new(scale, scale, 1)) * Affine3.RotateZ(-twist * Math.PI / 180) * worldToDcs * Affine3.Move(-target);
        if (!transform.TryInverse(out var inverse)) throw new ArgumentException("Viewport camera is singular or outside the coordinate range.");
        var corners = ImmutableArray.Create(new Vec3(center.X - width / 2, center.Y - height / 2),
            new Vec3(center.X + width / 2, center.Y - height / 2), new Vec3(center.X + width / 2, center.Y + height / 2),
            new Vec3(center.X - width / 2, center.Y + height / 2));
        var contours = ImmutableArray.Create(new RenderContour(corners, true));
        if (HasFlag(entity["Status"], "NonRectangularClipping", 65536))
        {
            var boundary = entity["ClippingBoundary"];
            if (!boundary.Exists) throw new ArgumentException("A nonrectangular viewport has no clipping boundary.");
            if (!new StandardEntityProjector().TryProject(boundary, out var geometry, out _) || geometry.Contours.IsEmpty)
                throw new NotSupportedException($"Unsupported viewport clipping boundary {boundary.Type}.");
            var result = ImmutableArray.CreateBuilder<RenderContour>(); var total = 0;
            foreach (var contour in geometry.Contours)
            {
                var points = ImmutableArray.CreateBuilder<Vec3>();
                foreach (var curve in contour.Curves)
                    foreach (var point in CurveSampler.Sample(curve, new(tolerance, maximumClipPoints)))
                    {
                        if (++total > maximumClipPoints) throw new InvalidOperationException("Viewport clipping point budget exceeded.");
                        var flat = point with { Z = 0 };
                        if (points.Count == 0 || points[^1] != flat) points.Add(flat);
                    }
                if (points.Count < 3) throw new ArgumentException("Viewport clipping boundary has fewer than three points.");
                result.Add(new(points.ToImmutable(), true));
            }
            contours = result.ToImmutable();
        }
        var bounds = Bounds2.Empty;
        foreach (var contour in contours) foreach (var point in contour.Points) bounds = bounds.Include(point);
        if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0) throw new ArgumentException("Viewport clip has no area.");
        var backOn = HasFlag(entity["Status"], "BackClipping", 4);
        var frontOn = HasFlag(entity["Status"], "FrontClipping", 2);
        var back = backOn ? entity["BackClipPlane"].Number() : double.NegativeInfinity;
        // VIEWMODE bit 16 selects authored FRONTZ; otherwise the front plane passes through the eye.
        var front = !frontOn ? double.PositiveInfinity : HasFlag(entity["Status"], "FrontClipNotAtEye", 16)
            ? entity["FrontClipPlane"].Number() : direction.Length;
        if ((backOn && !double.IsFinite(back)) || (frontOn && !double.IsFinite(front)) || back > front)
            throw new ArgumentException("Invalid viewport depth clipping interval.");
        var projection = new PaperViewportProjection(id, transform, inverse, scale);
        if (isPerspective) projection = projection with { Perspective = new(transform,
            new(center.X - viewCenter.X * scale, center.Y - viewCenter.Y * scale), direction.Length, perspective!.NearDistanceFraction) };
        return new(projection, new(contours, bounds),
            entity["FrozenLayers"].Items.Select(layer => layer["Name"].Text).ToImmutableHashSet(StringComparer.OrdinalIgnoreCase),
            back, front, (int)entity["Stacking"].Number(2));
    }
}
