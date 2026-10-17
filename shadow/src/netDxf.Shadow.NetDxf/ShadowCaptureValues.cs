using System.Collections.Immutable;

namespace netDxf.Shadow.NetDxf;

/// <summary>Exact, bounded sharing for the common immutable values in captured geometry.</summary>
internal static class ShadowCaptureValues
{
    private static readonly ShadowValue True = ShadowValue.From(true);
    private static readonly ShadowValue False = ShadowValue.From(false);
    private static readonly ShadowValue Zero = ShadowValue.From(0.0);
    private static readonly ShadowValue NegativeZero = ShadowValue.From(-0.0);
    private static readonly ShadowValue One = ShadowValue.From(1.0);
    private static readonly ShadowValue NegativeOne = ShadowValue.From(-1.0);
    private static readonly ImmutableDictionary<string, ShadowValue> Empty =
        ImmutableDictionary.Create<string, ShadowValue>(StringComparer.Ordinal);
    private static readonly ShadowValue[] CommonVector2 = CreateCommonVectors(false);
    private static readonly ShadowValue[] CommonVector3 = CreateCommonVectors(true);

    internal static ShadowValue Boolean(bool value) => value ? True : False;

    internal static ShadowValue Double(double value) => BitConverter.DoubleToInt64Bits(value) switch
    {
        0 => Zero,
        long.MinValue => NegativeZero,
        0x3ff0000000000000 => One,
        unchecked((long)0xbff0000000000000UL) => NegativeOne,
        _ => ShadowValue.From(value)
    };

    internal static ShadowValue Vector(Vector2 value)
    {
        var index = CommonIndex(value.X, value.Y, 0);
        return index >= 0 ? CommonVector2[index * 2 + (value.IsNormalized ? 1 : 0)]
            : CreateVector(value.X, value.Y, null, value.IsNormalized);
    }

    internal static ShadowValue Vector(Vector3 value)
    {
        var index = CommonIndex(value.X, value.Y, value.Z);
        return index >= 0 ? CommonVector3[index * 2 + (value.IsNormalized ? 1 : 0)]
            : CreateVector(value.X, value.Y, value.Z, value.IsNormalized);
    }

    private static int CommonIndex(double x, double y, double z)
    {
        // The mutable vector normalization flag and the sign of zero are part of
        // public state. Arithmetic vector equality would lose both distinctions.
        var xBits = BitConverter.DoubleToInt64Bits(x);
        var yBits = BitConverter.DoubleToInt64Bits(y);
        var zBits = BitConverter.DoubleToInt64Bits(z);
        if (zBits == 0 && yBits == 0)
        {
            if (xBits == 0) return 0;
            if (xBits == 0x3ff0000000000000) return 1;
        }
        if (xBits == 0 && zBits == 0 && yBits == 0x3ff0000000000000) return 2;
        if (xBits == 0 && yBits == 0 && zBits == 0x3ff0000000000000) return 3;
        return -1;
    }

    private static ShadowValue[] CreateCommonVectors(bool threeDimensional)
    {
        var result = new ShadowValue[threeDimensional ? 8 : 6];
        for (var index = 0; index < result.Length / 2; index++)
            for (var normalized = 0; normalized < 2; normalized++)
                result[index * 2 + normalized] = CreateVector(index == 1 ? 1 : 0, index == 2 ? 1 : 0,
                    threeDimensional ? index == 3 ? 1.0 : 0.0 : null, normalized != 0);
        return result;
    }

    private static ShadowValue CreateVector(double x, double y, double? z, bool normalized)
    {
        var fields = Empty.ToBuilder();
        fields.Add("IsNormalized", Boolean(normalized));
        fields.Add("X", Double(x));
        fields.Add("Y", Double(y));
        if (z is { } coordinate) fields.Add("Z", Double(coordinate));
        return new ShadowValue.Structure(z.HasValue ? "netDxf.Vector3" : "netDxf.Vector2", fields.ToImmutable());
    }
}
