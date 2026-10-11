using System.Collections.Immutable;
using netDxf.Shadow.Geometry;

namespace netDxf.Shadow.Rendering;

/// <summary>Shared, deterministic display mapping for native and SVG gradient paints.
/// This is not a claim of equivalence to AutoCAD's proprietary gradient transfer functions.</summary>
public sealed record GradientPlan(bool Radial, Vec3 Start, Vec3 End, double Radius,
    ImmutableArray<uint> Colors, ImmutableArray<double> Positions)
{
    public static GradientPlan Create(CadGradient gradient, byte alpha = 255)
    {
        if (!double.IsFinite(gradient.Shift) || gradient.Shift < 0 || gradient.Shift > 1)
            throw new ArgumentOutOfRangeException(nameof(gradient));
        static uint Opacity(uint color, byte alpha) => (color & 0xffffffu) | ((color >> 24) * alpha / 255) << 24;
        var a = Opacity(gradient.Color1, alpha); var b = Opacity(gradient.Color2, alpha);
        var kind = gradient.Kind;
        if (kind.StartsWith("Inv", StringComparison.Ordinal)) { (a, b) = (b, a); kind = kind[3..]; }
        var shift = gradient.Shift;
        return kind switch
        {
            "Linear" => new(false, new(-shift * 0.5, 0), new(1 - shift * 0.5, 0), 0, [a, b], [0, 1]),
            "Cylinder" => new(false, new(-shift * 0.5, 0), new(1 - shift * 0.5, 0), 0, [a, b, a], [0, 0.5, 1]),
            "Spherical" => new(true, new(0.5 * (1 - shift), 0.5), default, 0.5, [b, a], [0, 1]),
            "Hemispherical" => new(true, new(-shift * 0.5, 0.5), default, 1, [b, a], [0, 1]),
            "Curved" => new(true, new(-shift * 0.5, 0), default, Math.Sqrt(2), [b, a], [0, 1]),
            _ => throw new NotSupportedException($"Unknown gradient display type '{gradient.Kind}'.")
        };
    }
}
