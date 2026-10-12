using System.Collections.Immutable;

namespace netDxf.Shadow.Geometry;

/// <summary>A viewport frame is presentation geometry, not a model-space connectivity edge.</summary>
internal sealed class ViewportEntityProjector : IEntityProjector
{
    public bool TryProject(ShadowReader entity, out CadGeometry geometry, out string? diagnostic)
    {
        geometry = CadGeometry.Empty; diagnostic = null;
        if (entity.Type != "Viewport") return false;
        // netDxf independently owns and enumerates a custom viewport clipping entity in the paper block.
        if (entity["ClippingBoundary"].Exists) return true;
        var center = entity["Center"].Vector(); var width = entity["Width"].Number() / 2; var height = entity["Height"].Number() / 2;
        if (!(width > 0 && height > 0) || !double.IsFinite(width + height) || !center.IsFinite)
            throw new ArgumentException("Invalid viewport frame.");
        Vec3[] points = [new(center.X - width, center.Y - height), new(center.X + width, center.Y - height),
            new(center.X + width, center.Y + height), new(center.X - width, center.Y + height)];
        var curves = ImmutableArray.CreateBuilder<Curve3>(4);
        for (var i = 0; i < 4; i++) curves.Add(new LineCurve(points[i], points[(i + 1) % 4]));
        geometry = new([]) { DisplayContours = [new(curves.MoveToImmutable(), true)] };
        return true;
    }
}
