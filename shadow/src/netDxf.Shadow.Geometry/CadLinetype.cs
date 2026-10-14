using System.Collections.Immutable;

namespace netDxf.Shadow.Geometry;

public enum CadLinetypeDecorationKind { Text, Shape }
public enum CadLinetypeRotation { Relative, Absolute, Upright }

/// <summary>A detached complex element. Length belongs to the preceding dash/gap;
/// the decoration is anchored at its end. Offsets are in linetype units, not glyph-scale units.</summary>
public sealed record CadLinetypeElement(double Length, CadLinetypeDecoration? Decoration = null);

/// <summary>Immutable text/shape metadata; resource names never grant filesystem access.</summary>
public sealed record CadLinetypeDecoration(CadLinetypeDecorationKind Kind, string Content, string Resource,
    int? ShapeNumber = null, double Scale = 1, double Rotation = 0,
    CadLinetypeRotation RotationType = CadLinetypeRotation.Relative, Vec3 Offset = default,
    double StyleHeight = 0, double WidthFactor = 1, double ObliqueAngle = 0,
    string FontFamily = "sans-serif", bool IsBackward = false, bool IsUpsideDown = false,
    bool IsVertical = false, string BigFont = "")
{
    /// <summary>Unit-height local text. Placement supplies the effective height and orientation.</summary>
    public CadText CreateText() => new(Content, Vec3.Zero,
        Vec3.UnitX * (WidthFactor * (IsBackward ? -1 : 1)),
        new Vec3(Math.Tan(ObliqueAngle * Math.PI / 180), 1) * (IsUpsideDown ? -1 : 1),
        1, FontFamily, "BaselineLeft", false, 0);
}

/// <summary>A reusable pattern, with effective entity/global scale and entity-local plane normal.
/// All values are detached from netDxf; consumers validate their own resource and work budgets.</summary>
public sealed record CadLinetype(ImmutableArray<CadLinetypeElement> Elements, double Scale = 1, Vec3 Normal = default)
{
    public CadLinetype MultiplyScale(double factor) => this with { Scale = Scale * factor };

    public static bool Equivalent(CadLinetype? left, CadLinetype? right)
        => ReferenceEquals(left, right) || left is not null && right is not null &&
            left.Scale == right.Scale && left.Normal == right.Normal && left.Elements.AsSpan().SequenceEqual(right.Elements.AsSpan());

    /// <summary>Reads through the dependency observer, including referenced STYLE records.
    /// Returns null for simple patterns to keep the existing simple-dash path unchanged.</summary>
    public static CadLinetype? Read(ShadowReader linetype, double scale, Vec3 normal)
    {
        // Do not allocate per-segment objects for the overwhelmingly common simple/continuous case.
        if (!linetype["Segments"].Items.Any(segment => segment["Type"].Text is "Text" or "Shape")) return null;
        var values = ImmutableArray.CreateBuilder<CadLinetypeElement>();
        foreach (var segment in linetype["Segments"].Items)
        {
            CadLinetypeDecoration? decoration = null;
            var kind = segment["Type"].Text;
            if (kind is "Text" or "Shape")
            {
                var style = segment["Style"];
                var numeric = segment["ShapeNumber"].Number(double.NaN);
                var family = style["FontFamilyName"].Text;
                decoration = new(kind == "Text" ? CadLinetypeDecorationKind.Text : CadLinetypeDecorationKind.Shape,
                    segment[kind == "Text" ? "Text" : "Name"].Text,
                    style[kind == "Text" ? "FontFile" : "File"].Text,
                    double.IsNaN(numeric) ? null : checked((int)numeric),
                    segment["Scale"].Number(1), segment["Rotation"].Number(),
                    segment["RotationType"].Text switch
                    {
                        "Absolute" => CadLinetypeRotation.Absolute,
                        "Upright" => CadLinetypeRotation.Upright,
                        _ => CadLinetypeRotation.Relative
                    }, segment["Offset"].Vector(), style[kind == "Text" ? "Height" : "Size"].Number(),
                    style["WidthFactor"].Number(1), style["ObliqueAngle"].Number(),
                    family.Length == 0 ? "sans-serif" : family,
                    style["IsBackward"].Boolean(), style["IsUpsideDown"].Boolean(),
                    style["IsVertical"].Boolean(), style["BigFont"].Text);
            }
            values.Add(new(segment["Length"].Number(), decoration));
        }
        return new(values.ToImmutable(), scale, normal);
    }
}
