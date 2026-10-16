// Copyright (c) netDxf contributors. Licensed under the MIT License.
using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.Tables;
using netDxf.Units;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RunDimensionSmallRoundoffTests()
    {
        Run("small-roundoff/api-and-clone", SmallRoundoffApi);
        Run("small-roundoff/finite-quotient-overflow", SmallRoundoffQuotientOverflow);
        foreach (bool overrides in new[] { false, true })
        {
            Run($"small-roundoff/labels/{overrides}", () => SmallRoundoffLabels(overrides));
            Run($"small-roundoff/subnormal-labels/{overrides}", () => SmallRoundoffSubnormalLabels(overrides));
        }
        foreach (DxfVersion version in SupportedVersions)
            foreach (bool binary in new[] { false, true })
                Run($"small-roundoff/wire/{version}/{binary}", () => SmallRoundoffWire(version, binary));
    }

    private static void SmallRoundoffApi()
    {
        var style = new DimensionStyle("Small intervals");
        foreach (double value in new[] { 0.0, -0.0, double.Epsilon, 4e-15, 1.25e-7, Math.BitDecrement(1e-6), 1e-6 })
        {
            style.DimRoundoff = value;
            style.AlternateUnits.Roundoff = value;
            var primary = new DimensionStyleOverride(DimensionStyleOverrideType.DimRoundoff, value);
            var alternate = new DimensionStyleOverride(DimensionStyleOverrideType.AltUnitsRoundoff, value);
            SameDoubleBits(value, style.DimRoundoff, "Primary increment changed");
            SameDoubleBits(value, style.AlternateUnits.Roundoff, "Alternate increment changed");
            SameDoubleBits(value, (double)primary.Value, "Primary override increment changed");
            SameDoubleBits(value, (double)alternate.Value, "Alternate override increment changed");
            var copy = (DimensionStyle)style.Clone("Cloned intervals");
            SameDoubleBits(value, copy.DimRoundoff, "Primary clone increment changed");
            SameDoubleBits(value, copy.AlternateUnits.Roundoff, "Alternate clone increment changed");
        }
        foreach (double invalid in new[] { -double.Epsilon, -4e-15, -1.25e-7, -1.0, double.NegativeInfinity })
        {
            Throws<ArgumentOutOfRangeException>(() => style.DimRoundoff = invalid);
            Throws<ArgumentOutOfRangeException>(() => style.AlternateUnits.Roundoff = invalid);
            Throws<ArgumentOutOfRangeException>(() => new DimensionStyleOverride(DimensionStyleOverrideType.DimRoundoff, invalid));
            Throws<ArgumentOutOfRangeException>(() => new DimensionStyleOverride(DimensionStyleOverrideType.AltUnitsRoundoff, invalid));
        }
        Throws<ArgumentException>(() => new DimensionStyleOverride(DimensionStyleOverrideType.DimRoundoff, 1));
        Throws<ArgumentException>(() => new DimensionStyleOverride(DimensionStyleOverrideType.AltUnitsRoundoff, "1"));
    }

    private static void SmallRoundoffQuotientOverflow()
    {
        foreach (double value in new[] { 1.0, -1.0, 37.625, -37.625, double.MaxValue, -double.MaxValue })
            foreach (double interval in new[] { double.Epsilon, 1e-310 })
            {
                Check(double.IsInfinity(value / interval), "Synthetic quotient should overflow");
                SameDoubleBits(value, MathHelper.RoundToNearest(value, interval), "Tiny increment introduced overflow or changed a representable value");
            }
        SameDoubleBits(1.0, MathHelper.RoundToNearest(1.125, 0.25), "Exact midpoint must remain ties-to-even");
        SameDoubleBits(1.5, MathHelper.RoundToNearest(1.375, 0.25), "Odd midpoint must remain ties-to-even");
        Check(double.IsNaN(MathHelper.RoundToNearest(1, 0)), "Zero-increment behavior changed");
        Check(double.IsNaN(MathHelper.RoundToNearest(double.NaN, 1)), "NaN input behavior changed");
        Check(double.IsPositiveInfinity(MathHelper.RoundToNearest(double.PositiveInfinity, 1)), "Infinite input behavior changed");
    }

    private static AlignedDimension SmallRoundoffDimension(double measurement, bool overrides, double primary, double alternate)
    {
        var style = new DimensionStyle("Small roundoff presentation")
        {
            TextHeight = 0.1, ArrowSize = 0.05, DimScaleLinear = 1,
            DimLengthUnits = LinearUnitType.Decimal, LengthPrecision = 8, DecimalSeparator = ',',
            SuppressLinearLeadingZeros = false, SuppressLinearTrailingZeros = false,
            DimRoundoff = overrides ? 0 : primary
        };
        style.AlternateUnits.Enabled = true;
        style.AlternateUnits.LengthUnits = LinearUnitType.Decimal;
        style.AlternateUnits.LengthPrecision = 8;
        style.AlternateUnits.Multiplier = 2;
        style.AlternateUnits.SuppressLinearLeadingZeros = false;
        style.AlternateUnits.SuppressLinearTrailingZeros = false;
        style.AlternateUnits.Roundoff = overrides ? 0 : alternate;
        var dimension = new AlignedDimension(Vector2.Zero, new Vector2(measurement, 0), 0.5, style);
        if (overrides)
        {
            dimension.StyleOverrides.Add(DimensionStyleOverrideType.DimRoundoff, primary);
            dimension.StyleOverrides.Add(DimensionStyleOverrideType.AltUnitsRoundoff, alternate);
        }
        return dimension;
    }

    private static void SmallRoundoffLabels(bool overrides)
    {
        // Exact binary increments isolate the first rounding from display precision.
        double primary = 1.0 / (1 << 22), alternate = primary / 2;
        var dimension = SmallRoundoffDimension(1 + primary * 0.3, overrides, primary, alternate);
        double measurement = dimension.Measurement;
        Equal("1,00000000[2,00000012]", DimensionBlock.Build(dimension).Entities.OfType<MText>().Single().Value,
            "Primary and alternate labels must honor independent small increments");
        SameDoubleBits(measurement, dimension.Measurement, "Label generation mutated measurement");
        SameDoubleBits(overrides ? 0 : primary, dimension.Style.DimRoundoff, "Label generation mutated base primary rounding");
        SameDoubleBits(overrides ? 0 : alternate, dimension.Style.AlternateUnits.Roundoff, "Label generation mutated base alternate rounding");
        var copy = (Dimension)dimension.Clone();
        Equal("1,00000000[2,00000012]", DimensionBlock.Build(copy).Entities.OfType<MText>().Single().Value,
            "Cloned dimension lost small rounding increments");
        Equal(null, dimension.Block, "Read-only block generation attached its output");
    }

    private static void SmallRoundoffSubnormalLabels(bool overrides)
    {
        var dimension = SmallRoundoffDimension(37.625, overrides, double.Epsilon, double.Epsilon);
        Equal("37,62500000[75,25000000]", DimensionBlock.Build(dimension).Entities.OfType<MText>().Single().Value,
            "Subnormal increments must not reject finite primary or alternate measurements");
        SameDoubleBits(37.625, dimension.Measurement, "Subnormal rounding mutated measurement");
        Equal(null, dimension.Block, "Subnormal rounding attached output");
    }

    private static void SmallRoundoffWire(DxfVersion version, bool binary)
    {
        var document = new DxfDocument(version) { BuildDimensionBlocks = false };
        var values = new[] { 3.75e-7, 6.25e-8, 4e-15, double.Epsilon };
        for (int index = 0; index < values.Length; index++)
        {
            var style = new DimensionStyle("Interval " + index)
            {
                DimRoundoff = values[index], LengthPrecision = 8, DecimalSeparator = ','
            };
            style.AlternateUnits.Roundoff = values[(index + 1) % values.Length];
            var dimension = new AlignedDimension(Vector2.Zero, new Vector2(20 + index, 0), 4, style);
            dimension.StyleOverrides.Add(DimensionStyleOverrideType.DimRoundoff, values[(index + 2) % values.Length]);
            dimension.StyleOverrides.Add(DimensionStyleOverrideType.AltUnitsRoundoff, values[(index + 3) % values.Length]);
            document.Entities.Add(dimension);
        }
        for (int cycle = 0; cycle < 3; cycle++)
        {
            using var stream = new MemoryStream();
            Check(document.Save(stream, binary), "Small rounding document did not save");
            stream.Position = 0;
            document = DxfDocument.Load(stream) ?? throw new InvalidOperationException("Small rounding document did not load");
            for (int index = 0; index < values.Length; index++)
            {
                var dimension = document.Entities.Dimensions.Single(d => d.Style.Name == "Interval " + index);
                SameDoubleBits(values[index], dimension.Style.DimRoundoff, "Primary DIMSTYLE rounding was normalized");
                SameDoubleBits(values[(index + 1) % values.Length], dimension.Style.AlternateUnits.Roundoff, "Alternate DIMSTYLE rounding was normalized");
                SameDoubleBits(values[(index + 2) % values.Length], (double)dimension.StyleOverrides[DimensionStyleOverrideType.DimRoundoff].Value,
                    "Primary override rounding was normalized");
                SameDoubleBits(values[(index + 3) % values.Length], (double)dimension.StyleOverrides[DimensionStyleOverrideType.AltUnitsRoundoff].Value,
                    "Alternate override rounding was normalized");
                Equal(',', dimension.Style.DecimalSeparator, "Explicit decimal separator changed");
                var copy = (Dimension)dimension.Clone();
                Check(DimensionBlock.Build(copy).Entities.OfType<MText>().Any(), "Reloaded and cloned dimension did not regenerate");
            }
            document.BuildDimensionBlocks = false;
        }
    }
}
