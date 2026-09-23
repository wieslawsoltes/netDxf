// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System.Globalization;
using System.Reflection;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.Objects;
using netDxf.Tables;
using netDxf.Units;

namespace NetDxf.Conformance;
internal static partial class Program
{
    private static readonly string[] ToleranceMaxLabels = { "8.79", "10.12", "90.12°", "10.12", "5.12", "90.12°", "2.12", "7.98" };
    private static readonly string[] ToleranceMinLabels = { "8.41", "9.75", "89.75°", "9.75", "4.75", "89.75°", "1.75", "7.60" };
    private static string ToleranceLabelExpected(int kind, int row)
    {
        string prefix = kind == 3 ? "Ø" : kind == 4 ? "R" : "";
        string nominal = prefix.Length == 0 ? AltPrimary[kind] : AltPrimary[kind].Substring(1);
        string symbol = kind is 2 or 5 ? "°" : "";
        if (row is 0 or 5) return prefix + nominal;
        if (row == 3) return prefix + "{\\H0.5x;\\S" + ToleranceMaxLabels[kind] + "^ " + ToleranceMinLabels[kind] + ";}";
        string content = row == 1 ? "±0.12" + symbol : "\\S+0.12" + symbol + "^ -0.25" + symbol + ";";
        return prefix + "{\\A1;" + nominal + "{\\H0.5x;" + content + "}}";
    }
    private static Dimension ToleranceLabelDimension(int kind, int row)
    {
        var dim = TextBlockDimension(kind); dim.Style = (DimensionStyle)dim.Style.Clone("TOL_LABEL_STYLE_" + row);
        dim.UserText = "<>"; dim.Layer = new Layer("TOL_LABEL_" + row); dim.Style.TextFractionHeightScale = .5;
        var t = dim.Style.Tolerances; t.Precision = 2; t.UpperLimit = .125; t.LowerLimit = row == 1 ? .125 : .25;
        t.DisplayMethod = row == 1 ? DimensionStyleTolerancesDisplayMethod.Symmetrical : row == 3 ? DimensionStyleTolerancesDisplayMethod.Limits
            : row is 2 or 5 ? DimensionStyleTolerancesDisplayMethod.Deviation : DimensionStyleTolerancesDisplayMethod.None;
        if (row == 4) dim.StyleOverrides.Add(DimensionStyleOverrideType.TolerancesDisplayMethod, DimensionStyleTolerancesDisplayMethod.Deviation);
        if (row == 5) dim.StyleOverrides.Add(DimensionStyleOverrideType.TolerancesDisplayMethod, DimensionStyleTolerancesDisplayMethod.None);
        // An unrelated override must not reset the inherited tolerance value object.
        if (row >= 2) dim.StyleOverrides.Add(DimensionStyleOverrideType.TextColor, new AciColor(3));
        return dim;
    }
    private static Dimension[] ToleranceLabelHosts(DxfDocument doc) => doc.Blocks.SelectMany(b => b.Entities).OfType<Dimension>()
        .Where(d => d.Layer.Name.StartsWith("TOL_LABEL_", StringComparison.Ordinal)).OrderBy(d => d.Layer.Name, StringComparer.Ordinal).ToArray();
    private static void RegisterToleranceLabelTests()
    {
        for (int kind = 0; kind < 8; kind++) for (int row = 0; row < 6; row++) {
            int k = kind, r = row;
            Run($"tolerance-label/api/{k}/{r}", () => {
                var dim = ToleranceLabelDimension(k, r); var tolerance = dim.Style.Tolerances; var clone = (DimensionStyleTolerances)tolerance.Clone();
                string expected = ToleranceLabelExpected(k, r); double measurement = dim.Measurement;
                Equal(expected, AltLabel(dim), "Shared tolerance label");
                Equal(expected, TextBlockDirect(dim, "TOL_DIRECT").Entities.OfType<MText>().Single().Value, "Direct typed builder");
                dim.UserText = "A<>B<>C"; Equal("A" + expected + "B" + expected + "C", AltLabel(dim), "Repeated complete placeholder");
                dim.UserText = "FIXED"; Equal("FIXED", AltLabel(dim), "Literal label");
                dim.UserText = " "; Equal(0, DimensionBlock.Build(dim).Entities.OfType<MText>().Count(), "Suppressed label");
                Check(ReferenceEquals(tolerance, dim.Style.Tolerances), "Base tolerance identity");
                foreach (var property in typeof(DimensionStyleTolerances).GetProperties()) Equal(property.GetValue(clone), property.GetValue(tolerance), "Base tolerance value unchanged");
                SameDoubleBits(measurement, dim.Measurement, "Measurement unchanged");
            });
        }
        string[] properties = { "DisplayMethod", "UpperLimit", "LowerLimit", "VerticalPlacement", "Precision", "SuppressLinearLeadingZeros", "SuppressLinearTrailingZeros",
            "SuppressZeroFeet", "SuppressZeroInches", "AlternatePrecision", "AlternateSuppressLinearLeadingZeros", "AlternateSuppressLinearTrailingZeros", "AlternateSuppressZeroFeet", "AlternateSuppressZeroInches" };
        object[] values = { DimensionStyleTolerancesDisplayMethod.Limits, -.5, .75, DimensionStyleTolerancesVerticalPlacement.Top, (short)3, true, true, false, false, (short)4, true, true, false, false };
        for (int index = 0; index < properties.Length; index++) {
            int i = index;
            Run($"tolerance-label/effective/{i}", () => {
                var dim = ToleranceLabelDimension(1, 2); var prior = (DimensionStyleTolerances)dim.Style.Tolerances.Clone();
                string name = properties[i].StartsWith("Alternate", StringComparison.Ordinal) && i != 9 ? "TolerancesAlt" + properties[i].Substring(9) : "Tolerances" + properties[i];
                dim.StyleOverrides.Add(Enum.Parse<DimensionStyleOverrideType>(name), values[i]);
                var resolved = (DimensionStyle)typeof(DimensionBlock).GetMethod("BuildDimensionStyleOverride", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { dim })!;
                Check(!ReferenceEquals(resolved.Tolerances, dim.Style.Tolerances), "Effective tolerance must be an independent value object");
                foreach (var property in typeof(DimensionStyleTolerances).GetProperties()) {
                    Equal(property.GetValue(prior), property.GetValue(dim.Style.Tolerances), "Base setting mutated");
                    Equal(property.Name == properties[i] ? values[i] : property.GetValue(prior), property.GetValue(resolved.Tolerances), "Effective override or inherited setting lost");
                }
            });
        }
        foreach (int alignment in new[] { 0, 1, 2 }) foreach (bool negative in new[] { false, true })
            Run($"tolerance-label/sign-alignment/{alignment}/{negative}", () => {
                var dim = ToleranceLabelDimension(1, 2); dim.Style.Tolerances.VerticalPlacement = (DimensionStyleTolerancesVerticalPlacement)alignment;
                dim.Style.Tolerances.UpperLimit = negative ? -.25 : .25; dim.Style.Tolerances.LowerLimit = negative ? -.5 : 0;
                Equal("{\\A" + alignment + ";10.0000{\\H0.5x;\\S" + (negative ? "-0.25^ +0.50" : "+0.25^ 0.00") + ";}}", AltLabel(dim), "Signed bounds and alignment");
            });
        Run("tolerance-label/alternate-and-scaling", () => {
            var dim = ToleranceLabelDimension(1, 2); dim.Style.DimScaleLinear = 2;
            dim.Style.AlternateUnits.Enabled = true; dim.Style.AlternateUnits.Multiplier = 2;
            dim.Style.AlternateUnits.LengthPrecision = 3; dim.Style.Tolerances.AlternatePrecision = 3;
            dim.Style.AlternateUnits.Prefix = "ALT:"; dim.Style.AlternateUnits.Suffix = "u";
            Equal("{\\A1;20.0000{\\H0.5x;\\S+0.12^ -0.25;}}[ALT:{\\A1;40.000{\\H0.5x;\\S+0.250^ -0.500;}}u]", AltLabel(dim), "Bounds must not use DIMLFAC; alternate uses its own multiplier and precision");
            File.WriteAllText(Path.Combine(ArtifactDirectory, "tolerance-label-alternate.txt"), AltLabel(dim));
            dim.Style.Tolerances.AlternateSuppressLinearLeadingZeros = true; dim.Style.Tolerances.AlternateSuppressLinearTrailingZeros = true;
            Check(AltLabel(dim).Contains("\\S+.25^ -.5;"), "Alternate zero suppression");
            dim.Style.Tolerances.SuppressLinearLeadingZeros = true; dim.Style.Tolerances.SuppressLinearTrailingZeros = true;
            Check(AltLabel(dim).StartsWith("{\\A1;20.0000{\\H0.5x;\\S+.12^ -.25;}}", StringComparison.Ordinal), "Primary tolerance zero suppression");
        });
        Run("tolerance-label/limits-independent-rounding", () => {
            var dim = new AlignedDimension(Vector2.Zero, new Vector2(12.25, 0), 2) { Style = (DimensionStyle)DimensionStyle.Default.Clone("LIMIT_TEST") };
            dim.Style.TextFractionHeightScale = .5; dim.Style.DimRoundoff = 5;
            dim.Style.Tolerances.DisplayMethod = DimensionStyleTolerancesDisplayMethod.Limits; dim.Style.Tolerances.Precision = 2;
            dim.Style.Tolerances.UpperLimit = .125; dim.Style.Tolerances.LowerLimit = .25;
            Equal("{\\H0.5x;\\S12.38^ 12.00;}", AltLabel(dim), "Limits must not be derived from rounded nominal text");
        });
        var modes = new[] { LinearUnitType.Scientific, LinearUnitType.Decimal, LinearUnitType.Engineering, LinearUnitType.Architectural, LinearUnitType.Fractional, LinearUnitType.WindowsDesktop };
        string[] upperText = { "1.25E+00", "1.25", "1.25\"", "1 1\\/4\"", "1 1\\/4", "1,25" };
        string[] lowerText = { "5.00E-01", "0.50", "0.50\"", "0 1\\/2\"", "0 1\\/2", "0,50" };
        for (int index = 0; index < modes.Length; index++) {
            int i = index;
            Run($"tolerance-label/linear-format/{i}", () => {
                var prior = CultureInfo.CurrentCulture;
                try {
                    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
                    var dim = ToleranceLabelDimension(1, 2); dim.Style.DimLengthUnits = modes[i]; dim.Style.Tolerances.Precision = 2;
                    dim.Style.Tolerances.UpperLimit = 1.25; dim.Style.Tolerances.LowerLimit = .5;
                    Check(AltLabel(dim).EndsWith("{\\H0.5x;\\S+" + upperText[i] + "^ -" + lowerText[i] + ";}}", StringComparison.Ordinal), "Tolerance number format or stack escaping");
                    File.WriteAllText(Path.Combine(ArtifactDirectory, $"tolerance-label-linear-{i}.txt"), AltLabel(dim));
                } finally { CultureInfo.CurrentCulture = prior; }
            });
        }
        var angleModes = new[] { AngleUnitType.DecimalDegrees, AngleUnitType.DegreesMinutesSeconds, AngleUnitType.Gradians, AngleUnitType.Radians, AngleUnitType.SurveyorUnits };
        string[] angularUpper = { "+0.125°", "+0°7'30\"", "+0.125g", "+0.125r", "+0.125°" };
        string[] angularLower = { "-0.250°", "-0°15'0\"", "-0.250g", "-0.250r", "-0.250°" };
        for (int index = 0; index < angleModes.Length; index++) {
            int i = index;
            Run($"tolerance-label/angular-format/{i}", () => {
                var dim = ToleranceLabelDimension(2, 2); dim.Style.DimAngularUnits = angleModes[i]; dim.Style.Tolerances.Precision = 3;
                Check(AltLabel(dim).EndsWith("{\\H0.5x;\\S" + angularUpper[i] + "^ " + angularLower[i] + ";}}", StringComparison.Ordinal), "Angular tolerance units");
                File.WriteAllText(Path.Combine(ArtifactDirectory, $"tolerance-label-angular-{i}.txt"), AltLabel(dim));
                if (i is 2 or 3) {
                    dim.Style.Tolerances.DisplayMethod = DimensionStyleTolerancesDisplayMethod.Limits; dim.Style.Tolerances.Precision = 2;
                    Equal(i == 2 ? "{\\H0.5x;\\S100.12g^ 99.75g;}" : "{\\H0.5x;\\S1.70r^ 1.32r;}", AltLabel(dim), "Selected-unit limits");
                }
            });
        }
        foreach (string field in new[] { "UpperLimit", "LowerLimit" }) foreach (double bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            Run($"tolerance-label/reject/{field}/{ParameterBits(bad)}", () => {
                var dim = ToleranceLabelDimension(1, 2); typeof(DimensionStyleTolerances).GetProperty(field)!.SetValue(dim.Style.Tolerances, bad);
                var before = DxSnapshot(dim); Throws<ArgumentException>(() => AltLabel(dim)); before();
                dim.UserText = "FIXED"; Equal("FIXED", AltLabel(dim), "Literal does not evaluate unused tolerances");
            });
        Run("tolerance-label/reject-precision", () => {
            var dim = ToleranceLabelDimension(1, 2); dim.Style.Tolerances.Precision = 9; Throws<ArgumentOutOfRangeException>(() => AltLabel(dim));
        });
        foreach (DxfVersion version in SupportedVersions) foreach (bool binary in new[] { false, true })
        for (int kind = 0; kind < 8; kind++) for (int placement = 0; placement < 4; placement++) {
            if (kind == 7 && version < DxfVersion.AutoCad2004) continue;
            int k = kind, p = placement;
            Run($"tolerance-label/wire/{version}/{binary}/{k}/{p}", () => {
                var doc = new DxfDocument(version) { BuildDimensionBlocks = true }; doc.Comments.Clear();
                var hosts = Enumerable.Range(0, 6).Select(row => ToleranceLabelDimension(k, row)).ToArray();
                if (p == 0) foreach (var host in hosts) doc.Entities.Add(host);
                else if (p == 1) { doc.Layouts.Add(new Layout("TOL_LABEL_PAPER")); foreach (var host in hosts) doc.Layouts["TOL_LABEL_PAPER"].AssociatedBlock.Entities.Add(host); }
                else { var block = new Block("TOL_LABEL_HOLDER", hosts); if (p == 2) doc.Entities.Add(new Insert(block)); else doc.Blocks.Add(block); }
                using var source = new MemoryStream(); Check(doc.Save(source, binary), "Tolerance-label source save");
                string stem = $"tolerance-label-{version}-{binary}-{k}-{p}";
                File.WriteAllBytes(Path.Combine(ArtifactDirectory, stem + "-source.dxf"), source.ToArray());
                source.Position = 0; var loaded = DxfDocument.Load(source) ?? throw new InvalidOperationException("Tolerance-label load");
                foreach (bool output in new[] { false, true }) {
                    hosts = ToleranceLabelHosts(loaded); Equal(6, hosts.Length, "Label host inventory");
                    for (int row = 0; row < 6; row++) { Equal(ToleranceLabelExpected(k, row), hosts[row].Block.Entities.OfType<MText>().Single().Value, "Stored label"); hosts[row].Update(); Equal(ToleranceLabelExpected(k, row), AltLabel(hosts[row]), "Regenerated label"); }
                    using var stream = new MemoryStream(); Check(loaded.Save(stream, output), "Tolerance-label resave");
                    File.WriteAllBytes(Path.Combine(ArtifactDirectory, stem + $"-{output}.dxf"), stream.ToArray());
                    stream.Position = 0; var again = DxfDocument.Load(stream) ?? throw new InvalidOperationException("Tolerance-label reload");
                    hosts = ToleranceLabelHosts(again); for (int row = 0; row < 6; row++) Equal(ToleranceLabelExpected(k, row), hosts[row].Block.Entities.OfType<MText>().Single().Value, "Reloaded label");
                    Equal(0, again.Objects.Validate().Count, "Tolerance-label graph");
                }
            });
        }
    }
}
