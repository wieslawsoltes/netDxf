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
    private static CultureInfo LabelCulture(int variant)
    {
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.NumberFormat.NumberDecimalDigits = variant == 0 ? 3 : 2;
        culture.NumberFormat.NumberDecimalSeparator = variant == 0 ? "." : ",";
        return culture;
    }

    private static void RegisterDimensionLabelScaleTests()
    {
        for (int context = 0; context < 5; context++) for (int variant = 0; variant < 8; variant++)
        for (int culture = 0; culture < 2; culture++) foreach (bool rounded in new[] { false, true })
        {
            int p = context, v = variant, c = culture;
            Run($"dimension-label-scale/api/{p}/{v}/{c}/{rounded}", () =>
            {
                var old = CultureInfo.CurrentCulture;
                try
                {
                    CultureInfo.CurrentCulture = LabelCulture(c);
                    var dim = LabelScaleDimension(v, rounded, false); var doc = new DxfDocument();
                    if (p != 0) AddLabelScaleDimensions(doc, new[] { dim }, p - 1);
                    double measure = dim.Measurement;
                    string expected = ExpectedScaleLabel(v, p == 2, rounded, c, false);
                    Equal(expected, TextBlockDirect(dim, "DIRECT").Entities.OfType<MText>().Single().Value, "Scaled label");
                    Equal(expected, DimensionBlock.Build(dim).Entities.OfType<MText>().Single().Value, "Generic scaled label");
                    SameDoubleBits(measure, dim.Measurement, "Formatting changed geometry");
                    CheckLabelScaleSettings(dim, v, rounded, false);
                    Equal(c == 0 ? 3 : 2, CultureInfo.CurrentCulture.NumberFormat.NumberDecimalDigits, "Formatting mutated culture precision");
                    Equal(c == 0 ? "." : ",", CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator, "Formatting mutated culture separator");
                    var clone = (Dimension)dim.Clone();
                    Equal(ExpectedScaleLabel(v, false, rounded, c, false), DimensionBlock.Build(clone).Entities.OfType<MText>().Single().Value,
                        "Detached clone must not infer paper context");
                    CheckLabelScaleSettings(dim, v, rounded, false);
                }
                finally { CultureInfo.CurrentCulture = old; }
            });
        }
        for (int kind = 0; kind < 8; kind++) foreach (bool useOverride in new[] { false, true })
        {
            int k = kind;
            Run($"dimension-label-scale/prefix/{k}/{useOverride}", () =>
            {
                var dim = TextBlockDimension(k); dim.UserText = "<>";
                string baseline = DimensionBlock.Build(dim).Entities.OfType<MText>().Single().Value;
                string number = k is 3 or 4 ? baseline.Substring(1) : baseline;
                if (useOverride)
                {
                    dim.StyleOverrides.Add(DimensionStyleOverrideType.DimPrefix, "P:");
                    dim.StyleOverrides.Add(DimensionStyleOverrideType.DimSuffix, ":S");
                }
                else { dim.Style.DimPrefix = "P:"; dim.Style.DimSuffix = ":S"; }
                dim.UserText = "A<>B<>C";
                Equal("AP:" + number + ":SBP:" + number + ":SC", DimensionBlock.Build(dim).Entities.OfType<MText>().Single().Value, "Prefix and repeated placeholder");
                dim.UserText = "FIXED";
                Equal("FIXED", DimensionBlock.Build(dim).Entities.OfType<MText>().Single().Value, "Literal bypasses affixes");
                dim.UserText = " "; Equal(0, DimensionBlock.Build(dim).Entities.OfType<MText>().Count(), "Suppression bypasses affixes");
            });
        }
        for (int kind = 0; kind < 8; kind++) foreach (bool direct in new[] { false, true })
        foreach (bool reentrant in new[] { false, true })
        {
            int k = kind;
            Run($"dimension-label-scale/adoption/{k}/{direct}/{reentrant}", () =>
            {
                var doc = new DxfDocument { BuildDimensionBlocks = true };
                var paper = new Layout("DESTINATION"); doc.Layouts.Add(paper);
                var dim = TextBlockDimension(k); dim.UserText = "<>"; dim.Style.DimScaleLinear = -3;
                string[] expected = { "25.9808", "30.0000", "90°", "Ø30.0000", "R15.0000", "90°", "6.0000", "23.5619" };
                int calls = 0;
                if (reentrant) dim.DimensionBlockChanged += (sender, e) =>
                {
                    calls++;
                    Check(sender.Owner == null, "Adoption callback ownership timing changed");
                    Equal(expected[k], e.NewValue.Entities.OfType<MText>().Single().Value, "Callback destination label");
                    var unrelated = LabelScaleDimension(2, false, false);
                    Equal("10.125", DimensionBlock.Build(unrelated).Entities.OfType<MText>().Single().Value,
                        "Destination context leaked into reentrant detached build");
                    Check(unrelated.Owner == null && unrelated.Block == null, "Nested build published ownership");
                };
                if (direct) paper.AssociatedBlock.Entities.Add(dim);
                else { doc.Entities.ActiveLayout = "DESTINATION"; doc.Entities.Add(dim); }
                Equal(reentrant ? 1 : 0, calls, "Adoption callback count");
                Check(ReferenceEquals(paper.AssociatedBlock, dim.Owner), "Destination owner not published");
                Equal(direct ? Layout.ModelSpaceName : "DESTINATION", doc.Entities.ActiveLayout, "Ambient layout changed");
                Equal(expected[k], dim.Block.Entities.OfType<MText>().Single().Value, "Paper adoption scale");
                Equal(expected[k], TextBlockDirect(dim, "REBUILD").Entities.OfType<MText>().Single().Value, "Direct owned scale");
                SameDoubleBits(-3, dim.Style.DimScaleLinear, "Adoption mutated scale");
                Equal(0, doc.Objects.Validate().Count, "Adoption graph validation");
            });
        }
        foreach (DxfVersion version in SupportedVersions) foreach (bool binary in new[] { false, true })
        for (int placement = 0; placement < 4; placement++) for (int culture = 0; culture < 2; culture++)
        {
            int p = placement, c = culture;
            Run($"dimension-label-scale/wire/{version}/{binary}/{p}/{c}", () =>
            {
                var old = CultureInfo.CurrentCulture;
                try
                {
                    CultureInfo.CurrentCulture = LabelCulture(c);
                    var doc = new DxfDocument(version) { BuildDimensionBlocks = true }; doc.Comments.Clear();
                    var dims = Enumerable.Range(0, 8).Select(v => LabelScaleDimension(v, true)).ToArray();
                    AddLabelScaleDimensions(doc, dims, p);
                    string[] handles = dims.Select(d => d.Handle).ToArray();
                    CheckLabelScaleDocument(doc, p, c, handles);
                    string stem = $"dimension-label-scale-{version}-{binary}-{p}-{c}";
                    using var source = new MemoryStream(); Check(doc.Save(source, binary), "Scale source save");
                    File.WriteAllBytes(Path.Combine(ArtifactDirectory, stem + "-source.dxf"), source.ToArray()); source.Position = 0;
                    doc = DxfDocument.Load(source) ?? throw new InvalidOperationException("Scale source load");
                    CheckLabelScaleDocument(doc, p, c, handles);
                    foreach (bool output in new[] { false, true })
                    {
                        using var stream = new MemoryStream(); Check(doc.Save(stream, output), "Scale resave");
                        File.WriteAllBytes(Path.Combine(ArtifactDirectory, stem + $"-{output}.dxf"), stream.ToArray()); stream.Position = 0;
                        var second = DxfDocument.Load(stream) ?? throw new InvalidOperationException("Scale reload");
                        CheckLabelScaleDocument(second, p, c, handles);
                    }
                    Check(source.CanRead, "Scale load closed caller stream");
                }
                finally { CultureInfo.CurrentCulture = old; }
            });
        }
    }

    private static AlignedDimension LabelScaleDimension(int variant, bool rounded, bool affixes = true)
    {
        bool desktop = (variant & 1) != 0, negative = (variant & 2) != 0, overrides = (variant & 4) != 0;
        double scale = negative ? -3.0 : 3.0;
        var style = new DimensionStyle("LABEL_SCALE_" + variant)
        {
            DimScaleLinear = overrides ? 9 : scale, DimLengthUnits = desktop ? LinearUnitType.WindowsDesktop : LinearUnitType.Decimal,
            LengthPrecision = 3, SuppressLinearTrailingZeros = false, SuppressLinearLeadingZeros = false,
            DecimalSeparator = '.', DimRoundoff = rounded ? 0.5 : 0, DimPrefix = affixes ? "S:" : "", DimSuffix = affixes ? ":END" : "",
            FitTextMove = DimensionStyleFitTextMove.OverDimLineWithoutLeader
        };
        var dim = new AlignedDimension(Vector2.Zero, new Vector2(10.125, 0), 3, style)
        { Layer = new Layer("LABEL_SCALE_" + variant), UserText = "<>" };
        if (overrides)
        {
            dim.StyleOverrides.Add(DimensionStyleOverrideType.DimScaleLinear, scale);
            // DIMPOST is one combined wire value: this fixture explicitly overrides both parts.
            if (affixes)
            {
                dim.StyleOverrides.Add(DimensionStyleOverrideType.DimPrefix, "O:");
                dim.StyleOverrides.Add(DimensionStyleOverrideType.DimSuffix, ":END");
            }
        }
        return dim;
    }

    private static string ExpectedScaleLabel(int variant, bool paper, bool rounded, int culture, bool affixes = true)
    {
        double value = (variant & 2) != 0 && !paper ? 10.125 : 30.375;
        if (rounded) value = value == 10.125 ? 10 : 30.5;
        bool desktop = (variant & 1) != 0;
        string number = value.ToString(desktop && culture == 1 ? "F2" : "F3", desktop ? LabelCulture(culture) : CultureInfo.InvariantCulture);
        return affixes ? ((variant & 4) != 0 ? "O:" : "S:") + number + ":END" : number;
    }

    private static void AddLabelScaleDimensions(DxfDocument doc, IEnumerable<Dimension> dimensions, int placement)
    {
        if (placement is 1 or 3)
        {
            doc.Layouts.Add(new Layout("SCALE_PAPER")); doc.Entities.ActiveLayout = "SCALE_PAPER";
        }
        if (placement < 2) foreach (var dim in dimensions) doc.Entities.Add(dim);
        else doc.Entities.Add(new Insert(new Block("SCALE_HOLDER", dimensions)));
    }

    private static void CheckLabelScaleSettings(Dimension dim, int variant, bool rounded, bool affixes = true)
    {
        SameDoubleBits((variant & 4) != 0 ? 9 : (variant & 2) != 0 ? -3 : 3, dim.Style.DimScaleLinear, "Stored scale changed");
        SameDoubleBits(rounded ? 0.5 : 0, dim.Style.DimRoundoff, "Stored rounding changed");
        Equal(affixes ? "S:" : "", dim.Style.DimPrefix, "Original style prefix changed");
        if ((variant & 4) != 0) SameDoubleBits((variant & 2) != 0 ? -3 : 3,
            (double)dim.StyleOverrides[DimensionStyleOverrideType.DimScaleLinear].Value, "Override scale changed");
    }

    private static void CheckLabelScaleDocument(DxfDocument doc, int placement, int culture, string[] handles)
    {
        var dims = doc.Blocks.SelectMany(b => b.Entities).OfType<AlignedDimension>().OrderBy(d => d.Layer.Name, StringComparer.Ordinal).ToArray();
        Equal(8, dims.Length, "Scale entity count");
        for (int v = 0; v < dims.Length; v++)
        {
            var dim = dims[v]; Equal(handles[v], dim.Handle, "Scale entity handle");
            string expected = ExpectedScaleLabel(v, placement == 1, true, culture);
            for (int generation = 0; generation < 3; generation++)
            {
                Equal(expected, dim.Block.Entities.OfType<MText>().Single().Value, "Stored/regenerated scale label");
                SameDoubleBits(10.125, dim.Measurement, "Label scale changed measurement");
                CheckLabelScaleSettings(dim, v, true); dim.Update();
            }
        }
        Equal(0, doc.Objects.Validate().Count, "Scale graph validation");
    }
}
