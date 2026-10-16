using netDxf;
using netDxf.Entities;
using netDxf.Shadow;
using netDxf.Shadow.Dimensions;
using netDxf.Shadow.Editor;
using netDxf.Shadow.Geometry;
using netDxf.Shadow.NetDxf;
using netDxf.Tables;
using netDxf.Units;

internal static class DimensionRoundoffVerification
{
    internal static void Register(List<(string Name, Action Run)> tests)
    {
        foreach (bool overrides in new[] { false, true })
            tests.Add(($"dimension/small rounding retains primary, alternate and separator/{overrides}", () => SmallIntervals(overrides)));
        tests.Add(("dimension/subnormal rounding remains finite across source edits and history", SubnormalHistory));
    }

    private static AlignedDimension Dimension(double measurement, double primary, double alternate, bool overrides)
    {
        var style = new DimensionStyle("Small interval annotation")
        {
            TextHeight = 0.1, ArrowSize = 0.05, DimLengthUnits = LinearUnitType.Decimal,
            LengthPrecision = 8, DecimalSeparator = ',', SuppressLinearLeadingZeros = false,
            SuppressLinearTrailingZeros = false, DimRoundoff = overrides ? 0 : primary
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

    private static void SmallIntervals(bool overrides)
    {
        double primary = 1.0 / (1 << 22), alternate = primary / 2;
        var dimension = Dimension(1 + primary * 0.3, primary, alternate, overrides);
        var document = new DxfDocument { BuildDimensionBlocks = false }; document.Entities.Add(dimension);
        var source = new ShadowSession(document); var snapshot = source.Current;
        Label(snapshot, "1,00000000[2,00000012]");
        Require(dimension.Style.DimRoundoff == (overrides ? 0 : primary)
            && dimension.Style.AlternateUnits.Roundoff == (overrides ? 0 : alternate),
            "Projection changed the base rounding intervals.");
        Require(ReferenceEquals(source.Refresh(), snapshot) && dimension.Block is null,
            "Dimension regeneration changed the source graph.");
    }

    private static void SubnormalHistory()
    {
        var dimension = Dimension(37.625, double.Epsilon, double.Epsilon, false);
        var document = new DxfDocument { BuildDimensionBlocks = false }; document.Entities.Add(dimension);
        var source = new ShadowSession(document); var editor = new EditorSession(source);
        var style = source.Current.DrawingEntities().Single(d => d.Type == "AlignedDimension")["Style"];
        Label(source.Current, "37,62500000[75,25000000]");
        editor.SetProperty(style.Id!.Value, "DimRoundoff", ShadowValue.From(0.25));
        for (int cycle = 0; cycle < 3; cycle++)
        {
            Label(source.Current, "37,50000000[75,25000000]");
            editor.Undo();
            Label(source.Current, "37,62500000[75,25000000]");
            Require(dimension.Style.DimRoundoff == double.Epsilon, "Undo lost the subnormal increment.");
            editor.Redo();
        }
        Require(dimension.Style.AlternateUnits.Roundoff == double.Epsilon && dimension.Measurement == 37.625,
            "Rounding history changed the alternate interval or measurement.");
        Require(dimension.Block is null && ReferenceEquals(source.Current, source.Refresh()),
            "Projection after history changed source presentation state.");
    }

    private static void Label(ShadowSnapshot snapshot, string expected)
    {
        var scene = new CadSceneBuilder([new DimensionEntityProjector()]).Build(snapshot);
        Require(!scene.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error),
            "Small rounding intervals prevented dimension projection.");
        var text = scene.Occurrences.Where(o => o.Geometry.Text is not null).Select(o => o.Geometry.Text!.Content).ToArray();
        Require(text.SequenceEqual([expected]), "The detached dimension label changed its rounding or decimal separator.");
        Require(scene.Occurrences.Count(o => o.Geometry.Filled) >= 2, "The rounded dimension lost its arrowheads.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
