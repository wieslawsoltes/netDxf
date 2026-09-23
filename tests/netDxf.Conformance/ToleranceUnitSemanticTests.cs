// Copyright (c) netDxf contributors. Licensed under the MIT License.
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
    private static readonly char[] SemanticSeparators = { '/', '#', ';', '^', '{', '}', '\\' };
    private static readonly string[] SemanticNominal = { "90.00°", "90°0'", "100.00g", "1.57r" };
    private static readonly string[] SemanticUpper = { "0.250°", "0°15'0\"", "0.250g", "0.250r" };
    private static readonly string[] SemanticLower = { "0.125°", "0°7'30\"", "0.125g", "0.125r" };
    private static readonly string[] SemanticHigh = { "90.250°", "90°15'0\"", "100.250g", "1.821r" };
    private static readonly string[] SemanticLow = { "89.875°", "89°52'30\"", "99.875g", "1.446r" };
    private static string SemanticStack(string upper, string lower) => "{\\H0.5x;\\S" + upper + "^ " + lower + ";}";
    private static string SemanticAngularExpected(int units, int mode) => (mode switch {
        1 => "{\\A1;" + SemanticNominal[units] + "{\\H0.5x;±" + SemanticUpper[units] + "}}",
        2 => "{\\A1;" + SemanticNominal[units] + "{\\H0.5x;\\S+" + SemanticUpper[units] + "^ -" + SemanticLower[units] + ";}}",
        _ => SemanticStack(SemanticHigh[units], SemanticLow[units])
    }) + "TAIL";

    private static DimensionStyle SemanticStyle(string name) => new(name) {
        TextHeight = .75, TextFractionHeightScale = .5, LengthPrecision = 2, AngularPrecision = 2,
        FractionType = FractionFormatType.NotStacked,
        FitTextMove = DimensionStyleFitTextMove.OverDimLineWithoutLeader,
        Tolerances = new DimensionStyleTolerances { UpperLimit = .5, LowerLimit = .25, Precision = 2, AlternatePrecision = 2 }
    };

    private static Dimension SemanticAngular(int kind, int units, int mode, int variant)
    {
        var dim = TextBlockDimension(kind);
        dim.Style = SemanticStyle($"TSEM_A{kind}_{units}_{mode}_{variant}");
        dim.Layer = new Layer($"TSEM_A_{units}_{mode}_{variant}"); dim.UserText = "<>TAIL";
        dim.Style.DimAngularUnits = (AngleUnitType)units;
        dim.Style.Tolerances.DisplayMethod = (DimensionStyleTolerancesDisplayMethod)mode;
        dim.Style.Tolerances.UpperLimit = .25; dim.Style.Tolerances.LowerLimit = mode == 1 ? .25 : .125;
        dim.Style.Tolerances.Precision = 3;
        dim.Style.DimScaleLinear = 17; dim.Style.DimRoundoff = 9; // Angular quantities must ignore these.
        if (variant == 1) dim.StyleOverrides.Add(DimensionStyleOverrideType.TextColor, new AciColor(4));
        if (variant == 2)
        {
            dim.StyleOverrides.Add(DimensionStyleOverrideType.DimAngularUnits, (AngleUnitType)units);
            dim.StyleOverrides.Add(DimensionStyleOverrideType.TolerancesDisplayMethod, (DimensionStyleTolerancesDisplayMethod)mode);
            dim.StyleOverrides.Add(DimensionStyleOverrideType.TolerancesUpperLimit, .25);
            dim.StyleOverrides.Add(DimensionStyleOverrideType.TolerancesLowerLimit, mode == 1 ? .25 : .125);
            dim.StyleOverrides.Add(DimensionStyleOverrideType.TolerancesPrecision, (short)3);
            dim.Style.DimAngularUnits = AngleUnitType.DecimalDegrees;
            dim.Style.Tolerances.DisplayMethod = DimensionStyleTolerancesDisplayMethod.None;
            dim.Style.Tolerances.UpperLimit = 9; dim.Style.Tolerances.LowerLimit = 8; dim.Style.Tolerances.Precision = 1;
        }
        return SemanticMetadata(dim);
    }

    private static Dimension SemanticMetadata(Dimension dim)
    {
        var data = new XData(new ApplicationRegistry("TSEM_KEEP"));
        data.XDataRecord.Add(new XDataRecord(XDataCode.String, "unchanged")); dim.XData.Add(data);
        dim.TextReferencePoint = new Vector2(20, 40);
        return dim;
    }

    private static Dimension SemanticFraction(int kind, int units, int mode, bool alternate)
    {
        var dim = kind == 1 ? new AlignedDimension(Vector2.Zero, new Vector2(10.5, 0), 3) : TextBlockDimension(kind);
        dim.Style = SemanticStyle($"TSEM_F{kind}_{units}_{mode}_{alternate}");
        dim.Layer = new Layer($"TSEM_F_{units}_{mode}_{(alternate ? 1 : 0)}"); dim.UserText = "<>TAIL";
        dim.Style.DimLengthUnits = units == 0 ? LinearUnitType.Fractional : LinearUnitType.Architectural;
        dim.Style.Tolerances.DisplayMethod = (DimensionStyleTolerancesDisplayMethod)mode;
        dim.Style.AlternateUnits.Enabled = alternate; dim.Style.AlternateUnits.LengthUnits = dim.Style.DimLengthUnits;
        dim.Style.AlternateUnits.LengthPrecision = 2; dim.Style.AlternateUnits.Multiplier = 2; dim.Style.AlternateUnits.StackUnits = false;
        return SemanticMetadata(dim);
    }

    private static Dimension SemanticCustom(int separator)
    {
        var dim = new AlignedDimension(Vector2.Zero, new Vector2(10.5, 0), 3, SemanticStyle("TSEM_CUSTOM_" + separator));
        dim.Layer = new Layer("TSEM_C_" + separator); dim.UserText = "<>TAIL";
        dim.Style.Tolerances.DisplayMethod = DimensionStyleTolerancesDisplayMethod.Limits;
        dim.Style.DecimalSeparator = SemanticSeparators[separator];
        return SemanticMetadata(dim);
    }

    private static void RegisterToleranceUnitSemanticTests()
    {
        foreach (int kind in new[] { 2, 5 }) for (int units = 0; units < 4; units++)
        for (int mode = 1; mode <= 3; mode++) for (int variant = 0; variant < 3; variant++)
        {
            int k = kind, u = units, m = mode, v = variant;
            Run($"tolerance-unit-semantic/angular/{k}/{u}/{m}/{v}", () =>
            {
                var dim = SemanticAngular(k, u, m, v); string expected = SemanticAngularExpected(u, m);
                var style = dim.Style; var data = dim.XData["TSEM_KEEP"]; int count = dim.StyleOverrides.Count;
                double upper = style.Tolerances.UpperLimit, lower = style.Tolerances.LowerLimit;
                Equal(expected, TolText(dim), "Angular allowances use selected units");
                Equal(expected, TextBlockDirect(dim, "DIRECT").Entities.OfType<MText>().Single().Value, "Typed builder");
                var clone = (Dimension)dim.Clone(); Equal(expected, TolText(clone), "Clone angular units");
                clone.Style.Tolerances.UpperLimit = 7;
                SameDoubleBits(upper, style.Tolerances.UpperLimit, "Source upper mutated");
                SameDoubleBits(lower, style.Tolerances.LowerLimit, "Source lower mutated");
                Equal(count, dim.StyleOverrides.Count, "Sparse override count changed");
                Check(ReferenceEquals(style, dim.Style) && ReferenceEquals(data, dim.XData["TSEM_KEEP"]), "Source identities changed");
                Check(dim.Owner == null && dim.Block == null, "Direct formatting published an owner or block");
                dim.UserText = "A<>B<>C";
                Equal("A" + expected[..^4] + "B" + expected[..^4] + "C", TolText(dim), "Repeated angular placeholders");
                dim.UserText = "FIXED"; Equal("FIXED", TolText(dim), "Literal text must bypass allowances");
                dim.UserText = " "; Equal(0, DimensionBlock.Build(dim).Entities.OfType<MText>().Count(), "Suppressed label");
            });
        }
        foreach (int kind in new[] { 0, 1, 3, 4, 6, 7 }) for (int units = 0; units < 2; units++)
        foreach (bool alternate in new[] { false, true })
        {
            int k = kind, u = units;
            Run($"tolerance-unit-semantic/fraction/{k}/{u}/{alternate}", () =>
            {
                var dim = SemanticFraction(k, u, 2, alternate); string quote = u == 1 ? "\"" : "";
                string expected = "\\S+0 1\\/2" + quote + "^ -0 1\\/4" + quote + ";";
                string text = TolText(dim); Check(text.Contains(expected, StringComparison.Ordinal), "Fraction rows must escape, not embed Unicode commands");
                Check(!text.Contains("\\U+002F", StringComparison.Ordinal), "Unicode command inside a stack");
                Equal(text, TolText((Dimension)dim.Clone()), "Fraction clone");
            });
        }
        for (int separator = 0; separator < SemanticSeparators.Length; separator++)
        {
            int index = separator;
            Run("tolerance-unit-semantic/separator/" + index, () =>
            {
                var dim = SemanticCustom(index); string escape = "\\" + SemanticSeparators[index] + (SemanticSeparators[index] == '^' ? " " : "");
                Equal(SemanticStack("11" + escape + "00", "10" + escape + "25") + "TAIL", TolText(dim), "Literal stack delimiter");
                Equal(SemanticSeparators[index], dim.Style.DecimalSeparator, "Formatting changed separator");
            });
        }
        foreach (DxfVersion version in SupportedVersions) foreach (bool binary in new[] { false, true })
        for (int placement = 0; placement < 3; placement++) for (int group = 0; group < 3; group++)
        {
            int p = placement, g = group;
            Run($"tolerance-unit-semantic/wire/{version}/{binary}/{p}/{g}", () =>
            {
                var dims = new List<Dimension>();
                if (g < 2) for (int u = 0; u < 4; u++) for (int m = 1; m <= 3; m++) for (int v = 0; v < 3; v++)
                    dims.Add(SemanticAngular(g == 0 ? 2 : 5, u, m, v));
                else
                {
                    for (int u = 0; u < 2; u++) for (int m = 2; m <= 3; m++) foreach (bool a in new[] { false, true })
                        dims.Add(SemanticFraction(1, u, m, a));
                    for (int i = 0; i < SemanticSeparators.Length; i++) dims.Add(SemanticCustom(i));
                }
                var doc = new DxfDocument(version) { BuildDimensionBlocks = true }; doc.Comments.Clear();
                if (p == 0) foreach (var dim in dims) doc.Entities.Add(dim);
                else if (p == 1)
                {
                    doc.Layouts.Add(new Layout("TSEM_PAPER"));
                    foreach (var dim in dims) doc.Layouts["TSEM_PAPER"].AssociatedBlock.Entities.Add(dim);
                }
                else doc.Entities.Add(new Insert(new Block("TSEM_BLOCK", dims)));
                doc.Entities.Add(new Line(new Vector3(1.25, -2.5, 3.75), new Vector3(-4.5, 5.25, 6.75)));
                var expected = dims.ToDictionary(d => d.Layer.Name, d => (d.Handle, Text: TolText(d)), StringComparer.Ordinal);
                string stem = $"tolerance-unit-semantic-{version}-{binary}-{p}-{g}";
                using var source = new MemoryStream(); Check(doc.Save(source, binary), "Semantic source save");
                File.WriteAllBytes(Path.Combine(ArtifactDirectory, stem + "-source.dxf"), source.ToArray());
                source.Position = 0; var loaded = DxfDocument.Load(source) ?? throw new InvalidOperationException("Semantic source load");
                CheckSemanticDocument(loaded, expected, g);
                foreach (bool output in new[] { false, true })
                {
                    using var stream = new MemoryStream(); Check(loaded.Save(stream, output), "Semantic resave");
                    File.WriteAllBytes(Path.Combine(ArtifactDirectory, stem + $"-{output}.dxf"), stream.ToArray());
                    stream.Position = 0;
                    CheckSemanticDocument(DxfDocument.Load(stream) ?? throw new InvalidOperationException("Semantic reload"), expected, g);
                }
                Check(source.CanRead, "Caller stream closed");
            });
        }
    }

    private static void CheckSemanticDocument(DxfDocument doc, Dictionary<string, (string Handle, string Text)> expected, int group)
    {
        var dims = doc.Blocks.SelectMany(b => b.Entities).OfType<Dimension>().ToArray();
        Equal(group < 2 ? 36 : 15, dims.Length, "Semantic dimension count");
        foreach (var dim in dims)
        {
            var item = expected[dim.Layer.Name]; Equal(item.Handle, dim.Handle, "Semantic handle changed");
            Equal(item.Text, dim.Block.Entities.OfType<MText>().Single().Value, "Stored semantic text");
            for (int i = 0; i < 2; i++) { dim.Update(); Equal(item.Text, dim.Block.Entities.OfType<MText>().Single().Value, "Regenerated semantic text"); }
            Equal(item.Text, TolText((Dimension)dim.Clone()), "Detached semantic clone");
            if (group < 2)
            {
                string[] parts = dim.Layer.Name.Split('_');
                Equal(SemanticAngularExpected(int.Parse(parts[2]), int.Parse(parts[3])), item.Text, "Independent angular golden");
                SameDoubleBits(90, dim.Measurement, "Angular geometry altered by allowances");
            }
            Equal("unchanged", (string)dim.XData["TSEM_KEEP"].XDataRecord.Single().Value, "Other-application data");
        }
        Equal(0, doc.Objects.Validate().Count, "Semantic graph validation");
        var line = doc.Entities.Lines.Single();
        RawLinePointBits(new Vector3(1.25, -2.5, 3.75), line.StartPoint);
        RawLinePointBits(new Vector3(-4.5, 5.25, 6.75), line.EndPoint);
    }
}
