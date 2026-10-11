using System.Collections.Immutable;

namespace netDxf.Shadow.Geometry;

public enum CadFillRule { EvenOdd, NonZero }

/// <summary>A painted region is independent of the semantic curves used for graph analysis.</summary>
public sealed record CadRegion(ImmutableArray<CadContour> Contours, CadFillRule FillRule = CadFillRule.EvenOdd,
    CadGradient? Gradient = null)
{
    public CadRegion Transform(Affine3 transform) => this with
    {
        Contours = CadGeometry.TransformContours(Contours, transform),
        Gradient = Gradient is { } gradient ? gradient with { Frame = transform * gradient.Frame } : null
    };
}

/// <summary>Gradient coordinates are normalized to the unit square in Frame.
/// The two authored RGB stops remain authoritative, including single-color DXF gradients.</summary>
public sealed record CadGradient(string Kind, uint Color1, uint Color2, Affine3 Frame, double Shift = 0);

/// <summary>An exact offset of a line/circular arc with a linearly varying width.
/// Affine transforms are applied after the offset, preserving nonuniform inserts.</summary>
public sealed record WidthBoundaryCurve(Curve3 Centerline, double StartWidth, double EndWidth,
    double Side, Affine3 Frame) : Curve3
{
    public override Vec3 At(double parameter)
    {
        if (!double.IsFinite(parameter) || parameter < 0 || parameter > 1)
            throw new ArgumentOutOfRangeException(nameof(parameter));
        var tangent = Centerline switch
        {
            LineCurve line => line.B - line.A,
            EllipseCurve arc => (arc.U * -Math.Sin(arc.StartAngle + arc.Sweep * parameter)
                + arc.V * Math.Cos(arc.StartAngle + arc.Sweep * parameter)) * arc.Sweep,
            _ => throw new NotSupportedException("Width boundaries require a line or bulge arc.")
        };
        var length = Math.Sqrt(tangent.X * tangent.X + tangent.Y * tangent.Y);
        if (!(length > 0)) return Frame.Point(Centerline.At(parameter));
        var normal = new Vec3(-tangent.Y / length, tangent.X / length);
        var halfWidth = (StartWidth * (1 - parameter) + EndWidth * parameter) * 0.5;
        return Frame.Point(Centerline.At(parameter) + normal * (Side * halfWidth));
    }
    public override Curve3 Transform(Affine3 transform) => this with { Frame = transform * Frame };
}

public sealed record ReversedCurve(Curve3 Curve) : Curve3
{
    public override Vec3 At(double parameter) => Curve.At(1 - parameter);
    public override Curve3 Transform(Affine3 transform) => new ReversedCurve(Curve.Transform(transform));
    public override bool Closed => Curve.Closed;
}
