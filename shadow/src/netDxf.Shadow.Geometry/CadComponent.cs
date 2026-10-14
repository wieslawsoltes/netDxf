using System.Collections.Immutable;

namespace netDxf.Shadow.Geometry;

public enum CadStyleSource { Explicit, Parent, Layer }

/// <summary>A detached component style keeps inherited color and resource semantics symbolic until instancing.</summary>
public sealed record CadComponentStyle(CadStyle Value, CadStyleSource Color = CadStyleSource.Explicit,
    CadStyleSource Alpha = CadStyleSource.Explicit, CadStyleSource Lineweight = CadStyleSource.Explicit,
    CadStyleSource Dashes = CadStyleSource.Explicit, bool InheritLayer = false, double DashScale = 1)
{
    public CadStyle Resolve(CadStyle parent, CadStyle layer)
    {
        if (!(DashScale > 0) || !double.IsFinite(DashScale)) throw new ArgumentOutOfRangeException(nameof(DashScale));
        CadStyle Pick(CadStyleSource source) => source switch { CadStyleSource.Parent => parent, CadStyleSource.Layer => layer, _ => Value };
        return new((Pick(Color).Argb & 0xffffff) | (Pick(Alpha).Argb & 0xff000000), Pick(Lineweight).Lineweight,
            Pick(Dashes).Dashes.Select(d => d * DashScale).ToImmutableArray(), parent.Visible && Value.Visible,
            InheritLayer ? parent.Layer : Value.Layer) { Linetype = Pick(Dashes).Linetype?.MultiplyScale(DashScale) };
    }
}

public sealed record CadComponent(string Key, CadGeometry Geometry, CadComponentStyle? Style = null);
