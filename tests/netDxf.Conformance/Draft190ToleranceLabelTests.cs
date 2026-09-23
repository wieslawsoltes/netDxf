// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System.Globalization;
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
    private static readonly string[] Draft190_TolNominal = { "8.6603", "10.0000", "90°", "Ø10.0000", "R5.0000", "90°", "2.0000", "7.8540" };
    private static readonly string[] Draft190_TolMaximum = { "8.910", "10.250", "90.250°", "10.250", "5.250", "90.250°", "2.250", "8.104" };
    private static readonly string[] Draft190_TolMinimum = { "8.535", "9.875", "89.875°", "9.875", "4.875", "89.875°", "1.875", "7.729" };
    private static readonly string[] Draft190_TolAltNominal = { "17.32", "20.00", "", "20.00", "10.00", "", "4.00", "15.71" };
    private static readonly string[] Draft190_TolAltMaximum = { "17.82", "20.50", "", "20.50", "10.50", "", "4.50", "16.21" };
    private static readonly string[] Draft190_TolAltMinimum = { "17.07", "19.75", "", "19.75", "9.75", "", "3.75", "15.46" };

    private static string Draft190_TolExpected(int kind, int variant)
    {
        int mode = variant % 4; bool angular = kind is 2 or 5;
        string angle = angular ? "°" : "", prefix = kind == 3 ? "Ø" : kind == 4 ? "R" : "";
        string result = mode switch
        {
            0 => Draft190_TolNominal[kind],
            1 => "{\\A1;" + Draft190_TolNominal[kind] + "{\\H0.5x;±0.125" + angle + "}}",
            2 => "{\\A1;" + Draft190_TolNominal[kind] + "{\\H0.5x;\\S+0.250" + angle + "^ -0.125" + angle + ";}}",
            _ => prefix + "{\\H0.5x;\\S" + Draft190_TolMaximum[kind] + "^ " + Draft190_TolMinimum[kind] + ";}"
        };
        if (variant < 4 || angular) return result;
        string nominal = "A:" + Draft190_TolAltNominal[kind] + "u";
        return result + "[" + (mode switch
        {
            0 => nominal,
            1 => "{\\A1;" + nominal + "{\\H0.5x;±0.25}}",
            2 => "{\\A1;" + nominal + "{\\H0.5x;\\S+0.50^ -0.25;}}",
            _ => "A:{\\H0.5x;\\S" + Draft190_TolAltMaximum[kind] + "^ " + Draft190_TolAltMinimum[kind] + ";}u"
        }) + "]";
    }

    private static Dimension Draft190_TolLabelDimension(int kind, int variant)
    {
        var dim = TextBlockDimension(kind);
        dim.Style = (DimensionStyle)dim.Style.Clone("TL_STYLE_" + variant.ToString("D2"));
        dim.UserText = "<>"; dim.Layer = new Layer("TL_" + variant.ToString("D2"));
        dim.TextReferencePoint = new Vector2(17.25 + variant, 0); dim.Elevation = 2.5;
        dim.AttachmentPoint = MTextAttachmentPoint.MiddleCenter;
        var style = dim.Style; style.TextFractionHeightScale = 0.5; style.DimScaleOverall = 2;
        var t = style.Tolerances;
        t.DisplayMethod = (DimensionStyleTolerancesDisplayMethod)(variant % 4);
        t.UpperLimit = variant % 4 == 1 ? 0.125 : 0.25; t.LowerLimit = 0.125;
        t.Precision = 3; t.AlternatePrecision = 2; t.VerticalPlacement = DimensionStyleTolerancesVerticalPlacement.Middle;
        style.AlternateUnits.Enabled = variant >= 4; style.AlternateUnits.Multiplier = 2;
        style.AlternateUnits.LengthPrecision = 2; style.AlternateUnits.Prefix = "A:"; style.AlternateUnits.Suffix = "u";
        dim.StyleOverrides.Add(DimensionStyleOverrideType.TextColor, new AciColor(3));
        if (variant >= 4)
        {
            dim.StyleOverrides.Add(DimensionStyleOverrideType.TolerancesDisplayMethod, t.DisplayMethod);
            dim.StyleOverrides.Add(DimensionStyleOverrideType.TolerancesUpperLimit, t.UpperLimit);
            dim.StyleOverrides.Add(DimensionStyleOverrideType.TolerancesLowerLimit, t.LowerLimit);
            dim.StyleOverrides.Add(DimensionStyleOverrideType.TolerancesPrecision, t.Precision);
            dim.StyleOverrides.Add(DimensionStyleOverrideType.TolerancesAlternatePrecision, t.AlternatePrecision);
            dim.StyleOverrides.Add(DimensionStyleOverrideType.TolerancesVerticalPlacement, t.VerticalPlacement);
            t.DisplayMethod = DimensionStyleTolerancesDisplayMethod.None;
            t.UpperLimit = 0.75; t.LowerLimit = 0.5; t.Precision = 0; t.AlternatePrecision = 1;
            t.VerticalPlacement = DimensionStyleTolerancesVerticalPlacement.Bottom;
        }
        var data = new XData(new ApplicationRegistry("TL_KEEP"));
        data.XDataRecord.Add(new XDataRecord(XDataCode.String, "untouched")); dim.XData.Add(data);
        return dim;
    }

    private static string Draft190_TolLabel(Dimension dim) => DimensionBlock.Build(dim).Entities.OfType<MText>().Single().Value;

    private static void Draft190_RegisterToleranceLabelTests()
    {
        for (int kind = 0; kind < 8; kind++) for (int variant = 0; variant < 8; variant++)
        {
            int k = kind, v = variant;
            Run($"draft190-tolerance-label/api/{k}/{v}", () =>
            {
                var dim = Draft190_TolLabelDimension(k, v); var sourceTolerance = dim.Style.Tolerances;
                var unchanged = DxSnapshot(dim); double measured = dim.Measurement;
                Equal(Draft190_TolExpected(k, v), Draft190_TolLabel(dim), "Tolerance generated text");
                Equal(Draft190_TolExpected(k, v), TextBlockDirect(dim, "DIRECT").Entities.OfType<MText>().Single().Value, "Typed builder");
                unchanged(); Check(ReferenceEquals(sourceTolerance, dim.Style.Tolerances), "Tolerance source object replaced");
                SameDoubleBits(measured, dim.Measurement, "Tolerance changed geometry");
                var clone = (Dimension)dim.Clone(); Equal(Draft190_TolExpected(k, v), Draft190_TolLabel(clone), "Detached clone label");
                clone.Style.Tolerances.UpperLimit = 9; clone.StyleOverrides.Clear(); unchanged();
                Equal(Draft190_TolExpected(k, v), Draft190_TolLabel(dim), "Clone changed source labels");
                dim.UserText = "FIRST<>SECOND<>";
                Equal("FIRST" + Draft190_TolExpected(k, v) + "SECOND" + Draft190_TolExpected(k, v), Draft190_TolLabel(dim), "Repeated placeholders");
                dim.UserText = "FIXED"; Equal("FIXED", Draft190_TolLabel(dim), "Literal tolerance bypass");
                dim.UserText = " "; Equal(0, DimensionBlock.Build(dim).Entities.OfType<MText>().Count(), "Suppressed tolerance bypass");
                Check(dim.Owner == null && dim.Block == null, "Direct builder published ownership");
            });
        }
        for (int alignment = 0; alignment < 3; alignment++) foreach (double scale in new[] { 0.5, 0.75, 1.0 })
        {
            int a = alignment;
            Run($"draft190-tolerance-label/alignment/{a}/{scale}", () =>
            {
                var dim = Draft190_TolLabelDimension(1, 2); dim.Style.Tolerances.VerticalPlacement = (DimensionStyleTolerancesVerticalPlacement)a;
                dim.Style.TextFractionHeightScale = scale;
                Equal("{\\A" + a + ";10.0000{\\H" + scale.ToString("G17", CultureInfo.InvariantCulture) + "x;\\S+0.250^ -0.125;}}", Draft190_TolLabel(dim), "Scoped tolerance alignment/height");
            });
        }
        foreach (double epsilon in new[] { 1e-12, 0.001 })
            Run("draft190-tolerance-label/exact-mode/" + epsilon, () =>
            {
                double old = MathHelper.Epsilon;
                try
                {
                    MathHelper.Epsilon = epsilon;
                    var dim = Draft190_TolLabelDimension(1, 2); dim.Style.Tolerances.UpperLimit = 1; dim.Style.Tolerances.LowerLimit = 1 + 1e-13;
                    Check(Draft190_TolLabel(dim).Contains("\\S+1.000^ -1.000;"), "Epsilon collapsed unequal bounds");
                    dim.Style.Tolerances.LowerLimit = 1;
                    Check(Draft190_TolLabel(dim).Contains("±1.000"), "Equal native tolerance bounds not symmetrical");
                }
                finally { MathHelper.Epsilon = old; }
            });
        foreach (bool limits in new[] { false, true }) foreach (bool negative in new[] { false, true })
        {
            Run($"draft190-tolerance-label/scale/{limits}/{negative}", () =>
            {
                var dim = new AlignedDimension(Vector2.Zero, new Vector2(10.125, 0), 3, new DimensionStyle("TOL_SCALE"));
                dim.Style.DimScaleLinear = negative ? -3 : 3; dim.Style.DimRoundoff = 1;
                dim.Style.Tolerances.DisplayMethod = limits ? DimensionStyleTolerancesDisplayMethod.Limits : DimensionStyleTolerancesDisplayMethod.Deviation;
                dim.Style.Tolerances.UpperLimit = 0.25; dim.Style.Tolerances.LowerLimit = 0.125; dim.Style.Tolerances.Precision = 3;
                dim.Style.TextFractionHeightScale = 0.5;
                string detached = limits ? (negative ? "{\\H0.5x;\\S10.375^ 10.000;}" : "{\\H0.5x;\\S30.625^ 30.250;}")
                    : "{\\A1;" + (negative ? "10.0000" : "30.0000") + "{\\H0.5x;\\S+0.250^ -0.125;}}";
                Equal(detached, Draft190_TolLabel(dim), "Measurement scaling must not scale tolerance or pre-round limits");
                var doc = new DxfDocument { BuildDimensionBlocks = true }; doc.Layouts.Add(new Layout("PAPER_TOL"));
                doc.Layouts["PAPER_TOL"].AssociatedBlock.Entities.Add(dim);
                Equal(limits ? "{\\H0.5x;\\S30.625^ 30.250;}" : "{\\A1;30.0000{\\H0.5x;\\S+0.250^ -0.125;}}", dim.Block.Entities.OfType<MText>().Single().Value, "Paper destination scale");
                SameDoubleBits(10.125, dim.Measurement, "Scaled geometry");
            });
        }
        Run("draft190-tolerance-label/signed-zero-and-negative", () =>
        {
            var dim = Draft190_TolLabelDimension(1, 2); var t = dim.Style.Tolerances;
            t.UpperLimit = -0.25; t.LowerLimit = -0.125;
            Equal("{\\A1;10.0000{\\H0.5x;\\S-0.250^ +0.125;}}", Draft190_TolLabel(dim), "Signed tolerance bounds");
            t.UpperLimit = -0.0; t.LowerLimit = 0.125;
            Check(Draft190_TolLabel(dim).Contains("\\S 0.000^ -0.125;"), "Negative zero tolerance sign");
            t.LowerLimit = 0.0; Check(Draft190_TolLabel(dim).Contains("±0.000"), "Zero symmetrical tolerance");
            SameDoubleBits(-0.0, t.UpperLimit, "Rendering changed stored negative zero");
        });
        foreach (bool alternate in new[] { false, true })
            Run("draft190-tolerance-label/independent-precision-zero/" + alternate, () =>
            {
                var dim = Draft190_TolLabelDimension(1, alternate ? 6 : 2);
                dim.StyleOverrides.Add(alternate ? DimensionStyleOverrideType.TolerancesAltSuppressLinearLeadingZeros : DimensionStyleOverrideType.TolerancesSuppressLinearLeadingZeros, true);
                dim.StyleOverrides.Add(alternate ? DimensionStyleOverrideType.TolerancesAltSuppressLinearTrailingZeros : DimensionStyleOverrideType.TolerancesSuppressLinearTrailingZeros, true);
                Check(Draft190_TolLabel(dim).Contains(alternate ? "\\S+.5^ -.25;" : "\\S+.25^ -.125;"), "Tolerance zero suppression override");
                Check(!dim.Style.Tolerances.SuppressLinearLeadingZeros && !dim.Style.Tolerances.AlternateSuppressLinearLeadingZeros, "Suppression changed base tolerance");
            });
        var formats = new[] { LinearUnitType.Scientific, LinearUnitType.Decimal, LinearUnitType.Engineering, LinearUnitType.Architectural, LinearUnitType.Fractional, LinearUnitType.WindowsDesktop };
        string[] units = { "1.25E+01", "12.50", "1'-0.50\"", "1'-0 1/2\"", "12 1/2", "12,50" };
        for (int mode = 0; mode < formats.Length; mode++)
        {
            int m = mode;
            Run("draft190-tolerance-label/numeric-format/" + m, () =>
            {
                var old = CultureInfo.CurrentCulture;
                try
                {
                    var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone(); culture.NumberFormat.NumberDecimalSeparator = ","; culture.NumberFormat.NumberDecimalDigits = 1; CultureInfo.CurrentCulture = culture;
                    var dim = Draft190_TolLabelDimension(1, 1); dim.Style.DimLengthUnits = formats[m]; dim.Style.Tolerances.Precision = 2;
                    dim.Style.Tolerances.UpperLimit = dim.Style.Tolerances.LowerLimit = 12.5;
                    Check(Draft190_TolLabel(dim).Contains("±" + units[m] + "}"), "Tolerance unit format and explicit precision");
                    dim.Style.Tolerances.LowerLimit = 1; dim.Style.Tolerances.DisplayMethod = DimensionStyleTolerancesDisplayMethod.Deviation;
                    string numerator = units[m].Replace("/", "\\/");
                    Check(Draft190_TolLabel(dim).Contains("\\S+" + numerator + "^ "), "Stack must escape fractional slash");
                    Equal(1, culture.NumberFormat.NumberDecimalDigits, "Tolerance mutated current culture");
                }
                finally { CultureInfo.CurrentCulture = old; }
            });
        }
        var angles = new[] { AngleUnitType.DecimalDegrees, AngleUnitType.DegreesMinutesSeconds, AngleUnitType.Gradians, AngleUnitType.Radians };
        string[] angleUpper = { "90.250°", "90°15'0\"", "100.250g", "1.821r", "90.250°" };
        string[] angleLower = { "89.875°", "89°52'30\"", "99.875g", "1.446r", "89.875°" };
        for (int mode = 0; mode < angles.Length; mode++)
        {
            int m = mode;
            Run("draft190-tolerance-label/angular-format/" + m, () =>
            {
                var dim = Draft190_TolLabelDimension(2, 3); dim.Style.DimAngularUnits = angles[m]; dim.Style.AlternateUnits.Enabled = true;
                Equal("{\\H0.5x;\\S" + angleUpper[m] + "^ " + angleLower[m] + ";}", Draft190_TolLabel(dim), "Angular limits use selected angular units, not DIMLFAC or alternate units");
            });
        }
        Run("draft190-tolerance-label/surveyor-policy", () =>
        {
            var dim = Draft190_TolLabelDimension(2, 2);
            Throws<ArgumentException>(() => dim.Style.DimAngularUnits = AngleUnitType.SurveyorUnits);
            Equal(AngleUnitType.DecimalDegrees, dim.Style.DimAngularUnits, "Existing angular admission changed");
        });
        foreach (double value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            Run("draft190-tolerance-label/nonfinite/" + ParameterBits(value), () =>
            {
                var dim = Draft190_TolLabelDimension(1, 2); dim.Style.Tolerances.UpperLimit = value;
                Throws<ArgumentOutOfRangeException>(() => Draft190_TolLabel(dim)); Check(dim.Block == null, "Failed generation attached a block");
                dim.UserText = "FIXED"; Equal("FIXED", Draft190_TolLabel(dim), "Unused nonfinite tolerance affects literal label");
            });
        Run("draft190-tolerance-label/limit-overflow", () =>
        {
            var dim = Draft190_TolLabelDimension(1, 3); dim.Style.DimScaleLinear = 1e307; dim.Style.Tolerances.UpperLimit = double.MaxValue;
            Throws<ArgumentOutOfRangeException>(() => Draft190_TolLabel(dim));
        });
        Run("draft190-tolerance-label/alternate-overflow", () =>
        {
            var dim = Draft190_TolLabelDimension(1, 6); dim.StyleOverrides[DimensionStyleOverrideType.TolerancesUpperLimit] = new DimensionStyleOverride(DimensionStyleOverrideType.TolerancesUpperLimit, double.MaxValue);
            Throws<ArgumentOutOfRangeException>(() => Draft190_TolLabel(dim));
        });
        foreach (DxfVersion version in SupportedVersions) foreach (bool binary in new[] { false, true })
        for (int kind = 0; kind < 8; kind++) for (int placement = 0; placement < 3; placement++)
        {
            if (kind == 7 && version < DxfVersion.AutoCad2004) continue;
            int k = kind, p = placement;
            Run($"draft190-tolerance-label/wire/{version}/{binary}/{k}/{p}", () =>
            {
                var doc = new DxfDocument(version) { BuildDimensionBlocks = true }; doc.Comments.Clear();
                var dimensions = Enumerable.Range(0, 8).Select(v => Draft190_TolLabelDimension(k, v)).ToArray();
                if (p == 0) foreach (var dim in dimensions) doc.Entities.Add(dim);
                else if (p == 1) { doc.Layouts.Add(new Layout("TL_PAPER")); foreach (var dim in dimensions) doc.Layouts["TL_PAPER"].AssociatedBlock.Entities.Add(dim); }
                else doc.Entities.Add(new Insert(new Block("TL_HOLDER", dimensions)));
                doc.Entities.Add(new Line(new Vector3(17.25, -4.5, 2), new Vector3(18.5, 9.25, -3)));
                string[] handles = dimensions.Select(d => d.Handle).ToArray();
                string stem = $"draft190-tolerance-label-{version}-{binary}-{k}-{p}";
                Draft190_CheckToleranceLabels(doc, k, handles);
                var unchanged = dimensions.Select(DxSnapshot).ToArray();
                using var source = new MemoryStream(); Check(doc.Save(source, binary), "Tolerance source save"); foreach (var check in unchanged) check();
                File.WriteAllBytes(Path.Combine(ArtifactDirectory, stem + "-source.dxf"), source.ToArray()); source.Position = 0;
                var loaded = DxfDocument.Load(source) ?? throw new InvalidOperationException("Tolerance source load"); Draft190_CheckToleranceLabels(loaded, k, handles);
                foreach (bool output in new[] { false, true })
                {
                    using var stream = new MemoryStream(); Check(loaded.Save(stream, output), "Tolerance output save");
                    File.WriteAllBytes(Path.Combine(ArtifactDirectory, stem + $"-{output}.dxf"), stream.ToArray()); stream.Position = 0;
                    Draft190_CheckToleranceLabels(DxfDocument.Load(stream) ?? throw new InvalidOperationException("Tolerance reload"), k, handles);
                }
                Check(source.CanRead, "Tolerance caller stream closed");
            });
        }
    }

    private static void Draft190_CheckToleranceLabels(DxfDocument doc, int kind, string[] handles)
    {
        var dims = doc.Blocks.SelectMany(b => b.Entities).OfType<Dimension>().OrderBy(d => d.Layer.Name, StringComparer.Ordinal).ToArray();
        Equal(8, dims.Length, "Tolerance dimension inventory");
        for (int v = 0; v < dims.Length; v++)
        {
            var dim = dims[v]; Equal(handles[v], dim.Handle, "Tolerance identity"); Equal(TextBlockDimension(kind).GetType(), dim.GetType(), "Tolerance dimension family");
            double measure = dim.Measurement;
            for (int repeat = 0; repeat < 2; repeat++)
            {
                var text = dim.Block.Entities.OfType<MText>().Single(); Equal(Draft190_TolExpected(kind, v), text.Value, "Stored/regenerated tolerance text");
                SameDoubleBits(1.5, text.Height, "Inherited overall text scale"); SameDoubleBits(17.25 + v, text.Position.X, "Manual tolerance X"); SameDoubleBits(0, text.Position.Y, "Manual tolerance Y");
                SameDoubleBits(2.5, dim.Elevation, "Tolerance elevation"); Check(dim.TextPositionManuallySet, "Tolerance erased manual flag");
                Equal("untouched", (string)dim.XData["TL_KEEP"].XDataRecord.Single().Value, "Unrelated application data");
                dim.Update(); SameDoubleBits(measure, dim.Measurement, "Update changed measured geometry");
            }
            var clone = (Dimension)dim.Clone(); Equal(Draft190_TolExpected(kind, v), Draft190_TolLabel(clone), "Loaded detached clone");
            Equal(v < 4 ? (DimensionStyleTolerancesDisplayMethod)(v % 4) : DimensionStyleTolerancesDisplayMethod.None, dim.Style.Tolerances.DisplayMethod, "Base tolerance method changed");
            SameDoubleBits(v >= 4 ? 0.75 : v % 4 == 1 ? 0.125 : 0.25, dim.Style.Tolerances.UpperLimit, "Base tolerance bound changed");
        }
        Equal(0, doc.Objects.Validate().Count, "Tolerance graph integrity");
        var line = doc.Entities.Lines.Single(); RawLinePointBits(new Vector3(17.25, -4.5, 2), line.StartPoint); RawLinePointBits(new Vector3(18.5, 9.25, -3), line.EndPoint);
    }
}
