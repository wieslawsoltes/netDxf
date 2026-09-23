// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System.Reflection;
using System.Text.Json;
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
    private static readonly (string raw, string encoded)[] StackLexemes = {
        ("", ""), ("1/2", @"1\/2"), ("0#5", @"0\#5"), ("0;5", @"0\;5"),
        ("0^5", "0\\^ 5"), (@"0\5", @"0\\5"), ("0{5", @"0\{5"), ("0}5", @"0\}5"),
        (@"1/2;3\4^5", "1\\/2\\;3\\\\4\\^ 5"), ("Ł±", "Ł±"),
        (@"#/{};\^", "\\#\\/\\{\\}\\;\\\\\\^ ")
    };
    private static readonly char[] StackSeparators = { '#', '/', '^', ';', '\\', '{', '}' };
    private static readonly string[][] StackExpectedRows = {
        new[] { "+0 1/2", "-0 1/4" }, new[] { "10 1/2", "9 3/4" },
        new[] { "+0'-0 1/2\"", "-0'-0 1/4\"" }, new[] { "0'-10 1/2\"", "0'-9 3/4\"" },
        new[] { "+0.500", "-0.250", "+1", "-0 1/2" },
        new[] { "+0.500", "-0.250", "+0'-1\"", "-0'-0 1/2\"" },
        new[] { "10.500", "9.750", "21", "19 1/2" },
        new[] { "10.500", "9.750", "1'-9\"", "1'-7 1/2\"" }
    };

    // This independent golden encoding intentionally does not call production StackLiteral.
    private static string StackGolden(string row)
    {
        var s = new System.Text.StringBuilder();
        foreach (char c in row)
        {
            if ("\\;/#{}^".Contains(c)) s.Append('\\');
            s.Append(c); if (c == '^') s.Append(' ');
        }
        return s.ToString();
    }
    private static string[] StackRows(int variant) => variant < 8 ? StackExpectedRows[variant] :
        new[] { "10" + StackSeparators[variant - 8] + "500", "9" + StackSeparators[variant - 8] + "750" };

    private static Dimension StackDimension(int kind, int variant, bool useOverride)
    {
        var dim = TextBlockDimension(kind);
        dim.Style = new DimensionStyle($"STACK_STYLE_{kind}_{variant}_{useOverride}");
        dim.Layer = new Layer("STACK_ROW_" + variant.ToString("D2"));
        dim.UserText = "BEGIN<>END";
        var style = dim.Style; style.TextHeight = .75; style.TextFractionHeightScale = .5;
        style.LengthPrecision = 3; style.SuppressLinearLeadingZeros = false; style.SuppressLinearTrailingZeros = false;
        style.DimLengthUnits = variant < 2 ? LinearUnitType.Fractional : variant < 4 ? LinearUnitType.Architectural : LinearUnitType.Decimal;
        style.DecimalSeparator = variant >= 8 ? StackSeparators[variant - 8] : '.';
        style.SuppressZeroFeet = false; style.SuppressZeroInches = false;
        style.AlternateUnits.Enabled = variant >= 4 && variant < 8;
        style.AlternateUnits.Multiplier = 2; style.AlternateUnits.LengthPrecision = 3;
        style.AlternateUnits.LengthUnits = variant is 5 or 7 ? LinearUnitType.Architectural : LinearUnitType.Fractional;
        style.AlternateUnits.StackUnits = false;
        style.AlternateUnits.SuppressZeroFeet = false; style.AlternateUnits.SuppressZeroInches = false;
        var method = variant is 1 or 3 or 6 or 7 || variant >= 8
            ? DimensionStyleTolerancesDisplayMethod.Limits : DimensionStyleTolerancesDisplayMethod.Deviation;
        var t = style.Tolerances;
        t.DisplayMethod = useOverride ? DimensionStyleTolerancesDisplayMethod.None : method;
        t.UpperLimit = useOverride ? 9 : .5; t.LowerLimit = useOverride ? 8 : .25;
        t.Precision = 3; t.AlternatePrecision = 3;
        t.SuppressLinearLeadingZeros = false; t.SuppressLinearTrailingZeros = false;
        t.AlternateSuppressLinearLeadingZeros = false; t.AlternateSuppressLinearTrailingZeros = false;
        t.SuppressZeroFeet = false; t.SuppressZeroInches = false;
        t.AlternateSuppressZeroFeet = false; t.AlternateSuppressZeroInches = false;
        if (useOverride)
        {
            dim.StyleOverrides.Add(DimensionStyleOverrideType.TolerancesDisplayMethod, method);
            dim.StyleOverrides.Add(DimensionStyleOverrideType.TolerancesUpperLimit, .5);
            dim.StyleOverrides.Add(DimensionStyleOverrideType.TolerancesLowerLimit, .25);
        }
        dim.StyleOverrides.Add(DimensionStyleOverrideType.TextColor, new AciColor(4));
        var data = new XData(new ApplicationRegistry("STACK_KEEP"));
        data.XDataRecord.Add(new XDataRecord(XDataCode.String, "untouched")); dim.XData.Add(data);
        return dim;
    }

    private static void CheckStackLabel(Dimension dim, int variant)
    {
        string text = TolText(dim); var rows = StackRows(variant);
        for (int i = 0; i < rows.Length; i += 2)
            Check(text.Contains("\\S" + StackGolden(rows[i]) + "^ " + StackGolden(rows[i + 1]) + ";", StringComparison.Ordinal),
                "Literal stack rows: " + text);
        Check(!text.Contains("U+00", StringComparison.Ordinal), "Outer Unicode encoding leaked into stack grammar");
        Equal("BEGIN<>END", dim.UserText, "Literal template changed");
        Equal("untouched", (string)dim.XData["STACK_KEEP"].XDataRecord.Single().Value, "Other application changed");
    }

    private static void RegisterToleranceStackEscapeTests()
    {
        var encoded = new List<object>();
        for (int i = 0; i < StackLexemes.Length; i++)
        {
            int at = i;
            Run("tolerance-stack-escape/lexeme/" + at, () =>
            {
                var method = typeof(DimensionBlock).GetMethod("StackLiteral", BindingFlags.NonPublic | BindingFlags.Static)!;
                string actual = (string)method.Invoke(null, new object[] { StackLexemes[at].raw })!;
                Equal(StackLexemes[at].encoded, actual, "Stack character escape");
                encoded.Add(new { index = at, raw = StackLexemes[at].raw, encoded = actual });
            });
        }
        if (encoded.Count == StackLexemes.Length)
            File.WriteAllText(Path.Combine(ArtifactDirectory, "tolerance-stack-lexemes.json"), JsonSerializer.Serialize(encoded));
        foreach (int kind in new[] { 0, 1, 3, 4, 6, 7 }) foreach (int variant in new[] { 0, 2 })
        foreach (bool useOverride in new[] { false, true })
            Run($"tolerance-stack-escape/family/{kind}/{variant}/{useOverride}", () =>
            {
                var dim = StackDimension(kind, variant, useOverride);
                var t = dim.Style.Tolerances; var before = dim.Measurement; int count = dim.StyleOverrides.Count;
                CheckStackLabel(dim, variant);
                CheckStackLabel((Dimension)dim.Clone(), variant);
                Equal(count, dim.StyleOverrides.Count, "Build changed override count");
                Check(ReferenceEquals(t, dim.Style.Tolerances), "Build replaced source settings");
                SameDoubleBits(before, dim.Measurement, "Formatting changed geometry");
                dim.UserText = "FIXED"; Equal("FIXED", TolText(dim), "Literal bypass");
                dim.UserText = " "; Equal(0, DimensionBlock.Build(dim).Entities.OfType<MText>().Count(), "Suppression bypass");
            });
        foreach (DxfVersion version in SupportedVersions) foreach (bool binary in new[] { false, true })
        for (int placement = 0; placement < 3; placement++) foreach (bool useOverride in new[] { false, true })
        {
            int p = placement;
            Run($"tolerance-stack-escape/wire/{version}/{binary}/{p}/{useOverride}", () =>
            {
                var doc = new DxfDocument(version) { BuildDimensionBlocks = true }; doc.Comments.Clear();
                var dims = Enumerable.Range(0, 15).Select(v => StackDimension(1, v, useOverride)).ToArray();
                if (p == 0) foreach (var dim in dims) doc.Entities.Add(dim);
                else if (p == 1)
                {
                    doc.Layouts.Add(new Layout("STACK_PAPER"));
                    foreach (var dim in dims) doc.Layouts["STACK_PAPER"].AssociatedBlock.Entities.Add(dim);
                }
                else doc.Entities.Add(new Insert(new Block("STACK_HOLDER", dims)));
                doc.Entities.Add(new Line(new Vector3(17.25, -4.5, 2), new Vector3(18.5, 9.25, -3)));
                var handles = dims.Select(d => d.Handle).ToArray();
                var labels = dims.Select(TolText).ToArray();
                CheckStackDocument(doc, handles, labels, useOverride);
                string stem = $"tolerance-stack-escape-{version}-{binary}-{p}-{useOverride}";
                using var input = new MemoryStream(); Check(doc.Save(input, binary), "Stack source save");
                File.WriteAllBytes(Path.Combine(ArtifactDirectory, stem + "-source.dxf"), input.ToArray());
                input.Position = 0; doc = DxfDocument.Load(input) ?? throw new InvalidOperationException("Stack source load");
                CheckStackDocument(doc, handles, labels, useOverride);
                foreach (bool output in new[] { false, true })
                {
                    using var stream = new MemoryStream(); Check(doc.Save(stream, output), "Stack output save");
                    File.WriteAllBytes(Path.Combine(ArtifactDirectory, stem + $"-{output}.dxf"), stream.ToArray());
                    stream.Position = 0; var second = DxfDocument.Load(stream) ?? throw new InvalidOperationException("Stack output load");
                    CheckStackDocument(second, handles, labels, useOverride);
                    Check(stream.CanRead, "Reload closed caller output stream");
                }
                Check(input.CanRead, "Load closed caller input stream");
            });
        }
    }

    private static void CheckStackDocument(DxfDocument doc, string[] handles, string[] labels, bool useOverride)
    {
        var dims = doc.Blocks.SelectMany(b => b.Entities).OfType<Dimension>().OrderBy(d => d.Layer.Name, StringComparer.Ordinal).ToArray();
        Equal(15, dims.Length, "Stack host count");
        for (int v = 0; v < dims.Length; v++)
        {
            var d = dims[v]; Equal(handles[v], d.Handle, "Stack host identity"); CheckStackLabel(d, v);
            Equal(labels[v], d.Block.Entities.OfType<MText>().Single().Value, "Cached label changed on IO");
            for (int repeat = 0; repeat < 2; repeat++)
            {
                d.Update(); CheckStackLabel(d, v);
                Equal(labels[v], d.Block.Entities.OfType<MText>().Single().Value, "Regenerated label mismatch");
                SameDoubleBits(10, d.Measurement, "Regeneration changed geometry");
            }
            SameDoubleBits(useOverride ? 9 : .5, d.Style.Tolerances.UpperLimit, "Source upper changed");
            SameDoubleBits(useOverride ? 8 : .25, d.Style.Tolerances.LowerLimit, "Source lower changed");
        }
        Equal(0, doc.Objects.Validate().Count, "Stack object graph");
        var line = doc.Entities.Lines.Single();
        RawLinePointBits(new Vector3(17.25, -4.5, 2), line.StartPoint);
        RawLinePointBits(new Vector3(18.5, 9.25, -3), line.EndPoint);
    }
}
