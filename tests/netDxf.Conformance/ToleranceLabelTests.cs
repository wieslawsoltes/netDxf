// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System.Globalization;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;
using netDxf.Tables;
using netDxf.Units;

namespace NetDxf.Conformance;
internal static partial class Program
{
    private static readonly string[] TolNominal = { "8.66", "10.00", "90.00°", "Ø10.00", "R5.00", "90.00°", "2.00", "7.85" };
    private static readonly string[] TolHigh = { "8.910", "10.250", "90.250°", "10.250", "5.250", "90.250°", "2.250", "8.104" };
    private static readonly string[] TolLow = { "8.535", "9.875", "89.875°", "9.875", "4.875", "89.875°", "1.875", "7.729" };
    private static readonly string[] TolAlternate = { "17.32", "20.00", "", "20.00", "10.00", "", "4.00", "15.71" };
    private static readonly string[] TolAlternateHigh = { "17.82", "20.50", "", "20.50", "10.50", "", "4.50", "16.21" };
    private static readonly string[] TolAlternateLow = { "17.07", "19.75", "", "19.75", "9.75", "", "3.75", "15.46" };
    private static string TolStack(string high, string low) => "{\\H0.5x;\\S" + high + "^ " + low + ";}";
    private static string TolExpected(int kind, int mode, bool alternate)
    {
        string angle = kind is 2 or 5 ? "°" : "";
        string primary = mode switch
        {
            0 => TolNominal[kind],
            1 => "{\\A1;" + TolNominal[kind] + "{\\H0.5x;±0.250" + angle + "}}",
            2 => "{\\A1;" + TolNominal[kind] + "{\\H0.5x;\\S+0.250" + angle + "^ -0.125" + angle + ";}}",
            _ => (kind == 3 ? "Ø" : kind == 4 ? "R" : "") + TolStack(TolHigh[kind], TolLow[kind])
        };
        if (!alternate || kind is 2 or 5) return primary;
        string secondary = mode switch
        {
            0 => TolAlternate[kind] + "mm",
            1 => "{\\A1;" + TolAlternate[kind] + "mm{\\H0.5x;±0.50}}",
            2 => "{\\A1;" + TolAlternate[kind] + "mm{\\H0.5x;\\S+0.50^ -0.25;}}",
            _ => TolStack(TolAlternateHigh[kind], TolAlternateLow[kind]) + "mm"
        };
        return primary + "[" + secondary + "]";
    }

    private static Dimension TolDimension(int kind, int mode, int variant, bool alternate)
    {
        var dim = TextBlockDimension(kind);
        dim.Style = (DimensionStyle)dim.Style.Clone($"TOL_STYLE_{mode}_{variant}");
        dim.Layer = new Layer($"TOL_{mode}_{variant}"); dim.UserText = "<>";
        var style = dim.Style;
        style.LengthPrecision = 2; style.AngularPrecision = 2; style.TextFractionHeightScale = 0.5;
        style.AlternateUnits.Enabled = alternate; style.AlternateUnits.Multiplier = 2;
        style.AlternateUnits.LengthPrecision = 2; style.AlternateUnits.Suffix = "mm";
        var tolerance = style.Tolerances;
        tolerance.DisplayMethod = (DimensionStyleTolerancesDisplayMethod)mode;
        tolerance.UpperLimit = 0.25; tolerance.LowerLimit = mode == 1 ? 999 : 0.125;
        tolerance.Precision = 3; tolerance.AlternatePrecision = 2;
        if (variant == 1) dim.StyleOverrides.Add(DimensionStyleOverrideType.TextColor, new AciColor(4));
        if (variant == 2)
        {
            // Exercise every tolerance override without mutating the base value object.
            dim.StyleOverrides.Add(DimensionStyleOverrideType.TolerancesDisplayMethod, tolerance.DisplayMethod);
            dim.StyleOverrides.Add(DimensionStyleOverrideType.TolerancesUpperLimit, tolerance.UpperLimit);
            dim.StyleOverrides.Add(DimensionStyleOverrideType.TolerancesLowerLimit, tolerance.LowerLimit);
            dim.StyleOverrides.Add(DimensionStyleOverrideType.TolerancesPrecision, tolerance.Precision);
            dim.StyleOverrides.Add(DimensionStyleOverrideType.TolerancesAlternatePrecision, tolerance.AlternatePrecision);
            dim.StyleOverrides.Add(DimensionStyleOverrideType.TolerancesVerticalPlacement, tolerance.VerticalPlacement);
            foreach (var item in new[] {
                DimensionStyleOverrideType.TolerancesSuppressLinearLeadingZeros, DimensionStyleOverrideType.TolerancesSuppressLinearTrailingZeros,
                DimensionStyleOverrideType.TolerancesAltSuppressLinearLeadingZeros, DimensionStyleOverrideType.TolerancesAltSuppressLinearTrailingZeros })
                dim.StyleOverrides.Add(item, false);
            foreach (var item in new[] { DimensionStyleOverrideType.TolerancesSuppressZeroFeet, DimensionStyleOverrideType.TolerancesSuppressZeroInches,
                DimensionStyleOverrideType.TolerancesAltSuppressZeroFeet, DimensionStyleOverrideType.TolerancesAltSuppressZeroInches }) dim.StyleOverrides.Add(item, true);
            tolerance.DisplayMethod = DimensionStyleTolerancesDisplayMethod.None;
            tolerance.UpperLimit = 9; tolerance.LowerLimit = 8; tolerance.Precision = 1; tolerance.AlternatePrecision = 1;
        }
        var data = new XData(new ApplicationRegistry("TOL_KEEP"));
        data.XDataRecord.Add(new XDataRecord(XDataCode.String, "untouched")); dim.XData.Add(data);
        return dim;
    }

    private static string TolText(Dimension dim) => DimensionBlock.Build(dim).Entities.OfType<MText>().Single().Value;

    private static void RegisterToleranceLabelTests()
    {
        for (int kind = 0; kind < 8; kind++) for (int mode = 0; mode < 4; mode++)
        for (int variant = 0; variant < 3; variant++) foreach (bool alternate in new[] { false, true })
        {
            int k = kind, m = mode, v = variant;
            Run($"tolerance-label/api/{k}/{m}/{v}/{alternate}", () =>
            {
                var dim = TolDimension(k, m, v, alternate);
                double lower = dim.Style.Tolerances.LowerLimit; int count = dim.StyleOverrides.Count;
                string expected = TolExpected(k, m, alternate);
                Equal(expected, TolText(dim), "Tolerance label");
                Equal(expected, TextBlockDirect(dim, "DIRECT").Entities.OfType<MText>().Single().Value, "Typed builder");
                var clone = (Dimension)dim.Clone(); Equal(expected, TolText(clone), "Cloned tolerance label");
                clone.Style.Tolerances.UpperLimit = 17;
                SameDoubleBits(lower, dim.Style.Tolerances.LowerLimit, "Source tolerance changed");
                Equal(count, dim.StyleOverrides.Count, "Formatting changed dictionary");
                dim.UserText = "A<>B<>C"; Equal("A" + expected + "B" + expected + "C", TolText(dim), "Repeated scoped placeholders");
                dim.UserText = "FIXED"; Equal("FIXED", TolText(dim), "Literal text");
                dim.UserText = " "; Equal(0, DimensionBlock.Build(dim).Entities.OfType<MText>().Count(), "Suppression");
            });
        }
        foreach (DxfVersion version in SupportedVersions) foreach (bool binary in new[] { false, true })
        for (int placement = 0; placement < 3; placement++) for (int kind = 0; kind < 8; kind++)
        {
            if (kind == 7 && version < DxfVersion.AutoCad2004) continue;
            int p = placement, k = kind;
            Run($"tolerance-label/wire/{version}/{binary}/{p}/{k}", () =>
            {
                var doc = new DxfDocument(version) { BuildDimensionBlocks = true }; doc.Comments.Clear();
                var dims = Enumerable.Range(0, 4).SelectMany(m => Enumerable.Range(0, 3).Select(v => TolDimension(k, m, v, true))).ToArray();
                if (p == 0) foreach (var dim in dims) doc.Entities.Add(dim);
                else if (p == 1)
                {
                    doc.Layouts.Add(new Layout("TOL_PAPER"));
                    foreach (var dim in dims) doc.Layouts["TOL_PAPER"].AssociatedBlock.Entities.Add(dim);
                }
                else doc.Entities.Add(new Insert(new Block("TOL_HOLDER", dims)));
                doc.Entities.Add(new Line(new Vector3(17.25, -4.5, 2), new Vector3(18.5, 9.25, -3)));
                string[] handles = dims.Select(d => d.Handle).ToArray();
                CheckTolDocument(doc, k, handles);
                string stem = $"tolerance-label-{version}-{binary}-{p}-{k}";
                using var source = new MemoryStream(); Check(doc.Save(source, binary), "Tolerance source save");
                File.WriteAllBytes(Path.Combine(ArtifactDirectory, stem + "-source.dxf"), source.ToArray());
                source.Position = 0; var loaded = DxfDocument.Load(source) ?? throw new InvalidOperationException("Tolerance source load");
                CheckTolDocument(loaded, k, handles);
                foreach (bool output in new[] { false, true })
                {
                    using var stream = new MemoryStream(); Check(loaded.Save(stream, output), "Tolerance output save");
                    File.WriteAllBytes(Path.Combine(ArtifactDirectory, stem + $"-{output}.dxf"), stream.ToArray());
                    stream.Position = 0; CheckTolDocument(DxfDocument.Load(stream) ?? throw new InvalidOperationException("Tolerance reload"), k, handles);
                }
                Check(source.CanRead, "Caller stream closed");
            });
        }
        RunToleranceScalarTests();
        RunToleranceRawInputTests();
    }

    private static void CheckTolDocument(DxfDocument doc, int kind, string[] handles)
    {
        var dims = doc.Blocks.SelectMany(b => b.Entities).OfType<Dimension>().OrderBy(d => d.Layer.Name, StringComparer.Ordinal).ToArray();
        Equal(12, dims.Length, "Tolerance host count");
        for (int i = 0; i < dims.Length; i++)
        {
            var dim = dims[i]; Equal(handles[i], dim.Handle, "Tolerance identity");
            string expected = TolExpected(kind, i / 3, true);
            Equal(expected, dim.Block.Entities.OfType<MText>().Single().Value, "Stored cached label");
            Equal(expected, TolText(dim), "Rebuilt label");
            dim.Update(); Equal(expected, dim.Block.Entities.OfType<MText>().Single().Value, "Updated label");
            Equal("untouched", (string)dim.XData["TOL_KEEP"].XDataRecord.Single().Value, "Other application");
        }
        Equal(0, doc.Objects.Validate().Count, "Tolerance graph");
        var line = doc.Entities.Lines.Single();
        RawLinePointBits(new Vector3(17.25, -4.5, 2), line.StartPoint); RawLinePointBits(new Vector3(18.5, 9.25, -3), line.EndPoint);
    }

    private static void RunToleranceScalarTests()
    {
        foreach (double epsilon in new[] { 1e-12, 0.01, 100.0 })
        foreach (double lower in new[] { 0.0, -0.0, double.Epsilon, 0.25, 0.25000000000000006 })
            Run($"tolerance-label/exact-mode/{epsilon:R}/{BitConverter.DoubleToInt64Bits(lower)}", () =>
            {
                var doc = new DxfDocument(); var style = new DimensionStyle("TOL_EXACT");
                style.Tolerances.DisplayMethod = DimensionStyleTolerancesDisplayMethod.Deviation;
                style.Tolerances.UpperLimit = 0.25; style.Tolerances.LowerLimit = lower;
                doc.DimensionStyles.Add(style); doc.DrawingVariables.DimStyle = style.Name;
                double old = MathHelper.Epsilon;
                try
                {
                    MathHelper.Epsilon = epsilon;
                    using var stream = new MemoryStream(); Check(doc.Save(stream), "Exact tolerance save");
                    var tags = LoadRaw(stream.ToArray()).Tags;
                    int at = Enumerable.Range(0, tags.Count - 1).Single(i => tags[i].Code == 9 && Equals(tags[i].Value, "$DIMTM"));
                    SameDoubleBits(lower, (double)tags[at + 1].Value, "Header must not synthesize epsilon");
                    stream.Position = 0; var loaded = DxfDocument.Load(stream) ?? throw new InvalidOperationException("Exact tolerance load");
                    var tol = loaded.DimensionStyles[style.Name].Tolerances;
                    SameDoubleBits(lower, tol.LowerLimit, "Lower scalar");
                    Equal(lower == 0.25 ? DimensionStyleTolerancesDisplayMethod.Symmetrical : DimensionStyleTolerancesDisplayMethod.Deviation, tol.DisplayMethod, "Exact mode inference");
                }
                finally { MathHelper.Epsilon = old; }
            });
        for (int placement = 0; placement < 3; placement++)
        {
            int p = placement;
            Run($"tolerance-label/alignment/{p}", () =>
            {
                var dim = TolDimension(1, 2, 0, false); dim.Style.Tolerances.VerticalPlacement = (DimensionStyleTolerancesVerticalPlacement)p;
                Equal(TolExpected(1, 2, false).Replace("\\A1;", "\\A" + p + ";"), TolText(dim), "Alignment code");
                dim.Style.Tolerances.UpperLimit = -0.25; dim.Style.Tolerances.LowerLimit = -0.125;
                Check(TolText(dim).Contains("\\S-0.250^ +0.125;"), "Signed deviations");
            });
        }
        Run("tolerance-label/scale-rounding-and-overrides", () =>
        {
            var dim = TolDimension(1, 2, 0, true); dim.Style.DimScaleLinear = 3; dim.Style.DimRoundoff = 7;
            Check(TolText(dim).Contains("28.00{\\H0.5x;\\S+0.250^ -0.125;"), "Tolerance not multiplied by DIMLFAC or DIMRND");
            dim.Style.Tolerances.DisplayMethod = DimensionStyleTolerancesDisplayMethod.Limits;
            Equal(TolStack("30.250", "29.875") + "[" + TolStack("60.50", "59.75") + "mm]", TolText(dim), "Unrounded limit endpoints");
            dim.StyleOverrides.Add(DimensionStyleOverrideType.TolerancesDisplayMethod, DimensionStyleTolerancesDisplayMethod.None);
            Equal("28.00[60.00mm]", TolText(dim), "Explicit disable");
        });
        Run("tolerance-label/zeros-and-culture", () =>
        {
            var dim = TolDimension(1, 2, 0, true);
            dim.StyleOverrides.Add(DimensionStyleOverrideType.TolerancesSuppressLinearLeadingZeros, true);
            dim.StyleOverrides.Add(DimensionStyleOverrideType.TolerancesSuppressLinearTrailingZeros, true);
            dim.StyleOverrides.Add(DimensionStyleOverrideType.TolerancesAltSuppressLinearLeadingZeros, true);
            dim.StyleOverrides.Add(DimensionStyleOverrideType.TolerancesAltSuppressLinearTrailingZeros, true);
            Check(TolText(dim).Contains("\\S+.25^ -.125;"), "Primary tolerance zero suppression");
            Check(TolText(dim).Contains("\\S+.5^ -.25;"), "Alternate tolerance zero suppression");
            var old = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE"); dim.Style.DimLengthUnits = LinearUnitType.WindowsDesktop;
                Check(TolText(dim).Contains("\\S+,25^ -,125;"), "Culture separator and explicit tolerance precision");
            }
            finally { CultureInfo.CurrentCulture = old; }
        });
        foreach (double bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            Run("tolerance-label/nonfinite/" + BitConverter.DoubleToInt64Bits(bad), () =>
            {
                var dim = TolDimension(1, 2, 0, false); dim.Style.Tolerances.UpperLimit = bad;
                Throws<ArgumentOutOfRangeException>(() => TolText(dim));
                dim.UserText = "FIXED"; Equal("FIXED", TolText(dim), "Inactive numeric formatting");
            });
    }
    private static void RunToleranceRawInputTests()
    {
        foreach (DxfVersion version in SupportedVersions) foreach (bool binary in new[] { false, true })
        foreach (bool leader in new[] { false, true }) for (int mode = 0; mode < 4; mode++) for (int mutation = 0; mutation < 6; mutation++)
        {
            int m = mode, change = mutation;
            Run($"tolerance-label/foreign-partial/{version}/{binary}/{leader}/{m}/{change}", () =>
            {
                var doc = new DxfDocument(version) { BuildDimensionBlocks = true };
                var style = new DimensionStyle("FOREIGN_TOLERANCE");
                style.Tolerances.DisplayMethod = (DimensionStyleTolerancesDisplayMethod)m;
                style.Tolerances.UpperLimit = .25; style.Tolerances.LowerLimit = m == 1 ? 999 : .125;
                EntityObject host = leader ? new Leader(new[] { Vector2.Zero, new Vector2(5, 3) }, style)
                    : new AlignedDimension(Vector2.Zero, new Vector2(10, 0), 3, style) { UserText = "FIXED" };
                ContainerOverrides(host).Add(DimensionStyleOverrideType.TextHeight, .75);
                doc.Entities.Add(host);
                using var stream = new MemoryStream(); Check(doc.Save(stream, binary), "Foreign seed save");
                var raw = LoadRaw(stream.ToArray()); var record = raw.Sections.SelectMany(s => s.Records).Single(r => r.Name == (leader ? "LEADER" : "DIMENSION"));
                var tags = record.Tags.ToList(); int start = tags.FindIndex(t => t.Code == 1001 && Equals(t.Value, "ACAD"));
                Check(start >= 0, "Seed ACAD packet");
                // Independently replace the seed packet with one native setting (or two flags).
                var packet = new List<DxfTag> { new(1001, "ACAD"), new(1000, "DSTYLE"), new(1002, "{"), new(1070, (short)140), new(1040, .75) };
                int tol = m is 1 or 2 ? 1 : 0, lim = m == 3 ? 1 : 0;
                double upper = .25, lower = m == 1 ? .25 : .125;
                switch (change)
                {
                    case 0: upper = .375; packet.Add(new DxfTag(1070, (short)47)); packet.Add(new DxfTag(1040, upper)); break;
                    case 1: upper = BitConverter.Int64BitsToDouble(long.MinValue); packet.Add(new DxfTag(1070, (short)47)); packet.Add(new DxfTag(1040, upper)); break;
                    case 2: lower = .5; packet.Add(new DxfTag(1070, (short)48)); packet.Add(new DxfTag(1040, lower)); break;
                    case 3: tol = 0; packet.Add(new DxfTag(1070, (short)71)); packet.Add(new DxfTag(1070, (short)0)); break;
                    case 4: lim = 1; packet.Add(new DxfTag(1070, (short)72)); packet.Add(new DxfTag(1070, (short)1)); break;
                    case 5: tol = lim = 1; packet.Add(new DxfTag(1070, (short)71)); packet.Add(new DxfTag(1070, (short)1)); packet.Add(new DxfTag(1070, (short)72)); packet.Add(new DxfTag(1070, (short)1)); break;
                }
                packet.Add(new DxfTag(1002, "}"));
                raw = raw.WithRecord(record, tags.Take(start).Concat(packet));
                using var input = new MemoryStream(SaveRaw(raw, binary));
                var loaded = DxfDocument.Load(input) ?? throw new InvalidOperationException("Foreign tolerance load");
                for (int pass = 0; pass < 2; pass++)
                {
                    var target = loaded.Blocks.SelectMany(b => b.Entities).Single(e => e is Dimension || e is Leader);
                    var overrides = ContainerOverrides(target);
                    bool hasUpper = change is 0 or 1, hasLower = change == 2;
                    Equal(hasUpper, overrides.ContainsType(DimensionStyleOverrideType.TolerancesUpperLimit), "Sparse foreign upper presence");
                    Equal(hasLower, overrides.ContainsType(DimensionStyleOverrideType.TolerancesLowerLimit), "Sparse foreign lower presence");
                    SameDoubleBits(upper, hasUpper ? (double)overrides[DimensionStyleOverrideType.TolerancesUpperLimit].Value
                        : CompositeStyle(target).Tolerances.UpperLimit, "Foreign effective upper");
                    SameDoubleBits(lower, hasLower ? (double)overrides[DimensionStyleOverrideType.TolerancesLowerLimit].Value
                        : CompositeStyle(target).Tolerances.LowerLimit, "Foreign effective lower");
                    var expected = tol != 0 ? (upper == lower ? DimensionStyleTolerancesDisplayMethod.Symmetrical : DimensionStyleTolerancesDisplayMethod.Deviation)
                        : lim != 0 ? DimensionStyleTolerancesDisplayMethod.Limits : DimensionStyleTolerancesDisplayMethod.None;
                    bool hasMethod = change >= 3 || expected != (DimensionStyleTolerancesDisplayMethod)m;
                    Equal(hasMethod, overrides.ContainsType(DimensionStyleOverrideType.TolerancesDisplayMethod), "Sparse foreign method presence");
                    Equal(expected, hasMethod ? (DimensionStyleTolerancesDisplayMethod)overrides[DimensionStyleOverrideType.TolerancesDisplayMethod].Value
                        : CompositeStyle(target).Tolerances.DisplayMethod, "Foreign effective mode");
                    Equal(1 + (hasUpper ? 1 : 0) + (hasLower ? 1 : 0) + (hasMethod ? 1 : 0), overrides.Count, "Sparse values plus unrelated scalar");
                    using var output = new MemoryStream(); Check(loaded.Save(output, !binary), "Foreign materialized save"); output.Position = 0;
                    loaded = DxfDocument.Load(output) ?? throw new InvalidOperationException("Foreign materialized reload");
                }
                Check(input.CanRead, "Foreign input ownership");
            });
        }
        foreach (LinearUnitType units in Enum.GetValues<LinearUnitType>())
            Run($"tolerance-label/unit-format/{units}", () =>
            {
                var dim = TolDimension(1, 2, 0, false); dim.Style.DimLengthUnits = units;
                string first = TolText(dim);
                Check(first.Contains("\\S"), "Deviation stack missing");
                Check(!first.Contains("NaN") && !first.Contains("Infinity"), "Nonfinite tolerance string");
                Equal(first, TolText((Dimension)dim.Clone()), "Numeric-format clone");
            });
        foreach (AngleUnitType units in Enum.GetValues<AngleUnitType>())
            Run($"tolerance-label/angular-format/{units}", () =>
            {
                var dim = TolDimension(2, 2, 0, false);
                if (units == AngleUnitType.SurveyorUnits)
                {
                    Throws<ArgumentException>(() => dim.Style.DimAngularUnits = units);
                    return; // The existing angular API intentionally excludes bearing notation.
                }
                dim.Style.DimAngularUnits = units;
                dim.Style.DimScaleLinear = 17; dim.Style.DimRoundoff = 9;
                string first = TolText(dim); dim.Style.DimScaleLinear = 1; dim.Style.DimRoundoff = 0;
                Equal(first, TolText(dim), "Angular tolerance incorrectly used linear scale/rounding");
                Check(first.Contains("\\S"), "Angular tolerance missing");
            });
    }

}
