// Copyright (c) netDxf contributors. Licensed under the MIT License.
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.Objects;
using netDxf.Tables;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private const int FillVariantCount = 12;
    private static AciColor? FillBase(int variant) => variant switch
    {
        0 or 5 or 9 => null, 6 => AciColor.ByBlock, 7 => AciColor.ByLayer,
        8 => new AciColor(18, 93, 172), _ => new AciColor(2)
    };
    private static bool FillOverridden(int variant) => variant is 3 or 4 or 5 or 10 or 11;
    private static AciColor? FillOverride(int variant) => variant switch
    {
        3 => new AciColor(4), 5 => new AciColor(3), 10 => AciColor.ByLayer,
        11 => AciColor.ByBlock, _ => null
    };
    private static short? FillExpected(int variant) => (FillOverridden(variant) ? FillOverride(variant) : FillBase(variant))?.Index;

    private static Dimension FillDimension(int kind, int variant)
    {
        var dim = TextBlockDimension(kind);
        dim.Style = (DimensionStyle)dim.Style.Clone("FILL_STYLE_" + variant.ToString("D2"));
        dim.Style.TextFillColor = FillBase(variant);
        dim.Layer = new Layer("FILL_" + variant.ToString("D2")); dim.UserText = "FILL";
        if (FillOverridden(variant)) dim.StyleOverrides.Add(DimensionStyleOverrideType.TextFillColor, FillOverride(variant));
        if (variant is 2 or 9) dim.StyleOverrides.Add(DimensionStyleOverrideType.TextHeight, 1.25);
        var keep = new XData(new ApplicationRegistry("FILL_KEEP"));
        keep.XDataRecord.Add(new XDataRecord(XDataCode.String, "unchanged")); dim.XData.Add(keep);
        return dim;
    }

    private static void CheckFillLabels(Block block, short? expected)
    {
        var labels = block.Entities.OfType<MText>().ToArray(); Check(labels.Length > 0, "No generated label");
        foreach (var label in labels)
        {
            var fill = label.BackgroundFill;
            Equal(expected.HasValue, fill != null, "Generated fill presence");
            if (fill == null) continue;
            Equal(MTextBackgroundFillFlags.UseColor, fill.Flags, "Explicit indexed fill flag");
            Equal(expected, fill.ColorIndex, "Generated fill index");
            Equal((double?)1.5, fill.ScaleFactor, "Existing MTEXT default mask margin");
            Check(fill.TrueColor == null && fill.ColorName == null && fill.Transparency == null, "Invented fill payload");
        }
        if (labels.Length > 1 && expected.HasValue)
            Check(!ReferenceEquals(labels[0].BackgroundFill, labels[1].BackgroundFill), "Split masks share mutable state");
    }

    private static void RegisterDimensionTextFillTests()
    {
        for (int kind = 0; kind < 8; kind++) for (int variant = 0; variant < FillVariantCount; variant++)
        foreach (bool manual in new[] { false, true })
        {
            int k = kind, v = variant;
            Run($"dimension-text-fill/api/{k}/{v}/{manual}", () =>
            {
                var dim = FillDimension(k, v); dim.UserText = "UPPER\\XLOWER";
                if (manual) dim.TextReferencePoint = new Vector2(17, 0);
                var style = dim.Style; var color = style.TextFillColor;
                var before = dim.StyleOverrides.ToArray(); double measure = dim.Measurement;
                var direct = TextBlockDirect(dim, "DIRECT"); CheckFillLabels(direct, FillExpected(v));
                var generic = DimensionBlock.Build(dim); CheckFillLabels(generic, FillExpected(v));
                Check(ReferenceEquals(style, dim.Style) && ReferenceEquals(color, style.TextFillColor), "Source style/color changed");
                Check(before.SequenceEqual(dim.StyleOverrides), "Sparse overrides changed");
                SameDoubleBits(measure, dim.Measurement, "Fill generation changed measurement");
                var clone = (Dimension)dim.Clone(); var copy = DimensionBlock.Build(clone); CheckFillLabels(copy, FillExpected(v));
                var label = copy.Entities.OfType<MText>().First();
                if (label.BackgroundFill != null) label.BackgroundFill.ColorIndex = 99;
                CheckFillLabels(direct, FillExpected(v)); CheckFillLabels(generic, FillExpected(v));
                dim.StyleOverrides.Remove(DimensionStyleOverrideType.TextFillColor);
                dim.StyleOverrides.Add(DimensionStyleOverrideType.TextFillColor, null);
                CheckFillLabels(DimensionBlock.Build(dim), null);
                CheckFillLabels(direct, FillExpected(v)); // Earlier generated block is an independent snapshot.
                dim.UserText = " "; Equal(0, DimensionBlock.Build(dim).Entities.OfType<MText>().Count(), "Suppression generated a mask");
            });
        }
        for (int kind = 0; kind < 8; kind++)
        {
            int k = kind;
            Run($"dimension-text-fill/arrow-isolation/{k}", () =>
            {
                var dim = FillDimension(k, 1);
                var text = new MText("ARROW", Vector2.Zero, 1, 0) { BackgroundFill = MTextBackgroundFill.FromColor(new AciColor(5)) };
                var arrow = new Block("FILL_ARROW", new EntityObject[] { text }); dim.Style.DimArrow1 = arrow; dim.Style.DimArrow2 = arrow;
                var block = DimensionBlock.Build(dim); CheckFillLabels(block, 2);
                Equal((short?)5, text.BackgroundFill.ColorIndex, "Nested arrow text changed");
                dim.Style.TextFillColor!.Index = 3;
                CheckFillLabels(block, 2); CheckFillLabels(DimensionBlock.Build(dim), 3);
            });
        }
        foreach (DxfVersion version in SupportedVersions) foreach (bool binary in new[] { false, true })
        for (int kind = 0; kind < 8; kind++) for (int placement = 0; placement < 3; placement++)
        {
            if (kind == 7 && version < DxfVersion.AutoCad2004) continue;
            int k = kind, p = placement;
            Run($"dimension-text-fill/wire/{version}/{binary}/{k}/{p}", () =>
            {
                var doc = new DxfDocument(version) { BuildDimensionBlocks = true }; doc.Comments.Clear();
                var dimensions = Enumerable.Range(0, FillVariantCount).Select(v => FillDimension(k, v)).ToArray();
                if (p == 0) foreach (var d in dimensions) doc.Entities.Add(d);
                else if (p == 1)
                {
                    doc.Layouts.Add(new Layout("FILL_PAPER"));
                    foreach (var d in dimensions) doc.Layouts["FILL_PAPER"].AssociatedBlock.Entities.Add(d);
                }
                else doc.Entities.Add(new Insert(new Block("FILL_HOLDER", dimensions)));
                string[] handles = dimensions.Select(d => d.Handle).ToArray();
                doc.Entities.Add(new Line(new Vector3(17.25, -4.5, 2), new Vector3(18.5, 9.25, -3)));
                CheckFillDocument(doc, handles);
                string stem = $"dimension-text-fill-{version}-{binary}-{k}-{p}";
                using var source = new MemoryStream(); Check(doc.Save(source, binary), "Fill source save");
                File.WriteAllBytes(Path.Combine(ArtifactDirectory, stem + "-source.dxf"), source.ToArray());
                source.Position = 0; var loaded = DxfDocument.Load(source) ?? throw new InvalidOperationException("Fill source load");
                CheckFillDocument(loaded, handles);
                foreach (bool output in new[] { false, true })
                {
                    using var stream = new MemoryStream(); Check(loaded.Save(stream, output), "Fill output save");
                    File.WriteAllBytes(Path.Combine(ArtifactDirectory, stem + $"-{output}.dxf"), stream.ToArray());
                    stream.Position = 0; var reloaded = DxfDocument.Load(stream) ?? throw new InvalidOperationException("Fill reload");
                    CheckFillDocument(reloaded, handles);
                }
                Check(source.CanRead, "Fill load closed caller stream");
            });
        }
        Run("dimension-text-fill/version-transition", () =>
        {
            var doc = new DxfDocument(DxfVersion.AutoCad2018) { BuildDimensionBlocks = true };
            var dim = FillDimension(1, 1); doc.Entities.Add(dim); CheckFillLabels(dim.Block, 2);
            doc.DrawingVariables.AcadVer = DxfVersion.AutoCad2000;
            using var rejected = new MemoryStream();
            bool refused;
            try { refused = !doc.Save(rejected); } catch (NotSupportedException) { refused = true; }
            Check(refused && rejected.Length == 0 && rejected.CanWrite, "Unsupported mask was silently down-saved");
            dim.Update(); CheckFillLabels(dim.Block, null); Equal((short)2, dim.Style.TextFillColor.Index, "Legacy generation cleared stored fill");
            using var legacy = new MemoryStream(); Check(doc.Save(legacy), "Explicit legacy regeneration save");
            doc.DrawingVariables.AcadVer = DxfVersion.AutoCad2018; dim.Update(); CheckFillLabels(dim.Block, 2);
        });
    }

    private static void CheckFillDocument(DxfDocument doc, string[] handles)
    {
        var dimensions = doc.Blocks.SelectMany(b => b.Entities).OfType<Dimension>().OrderBy(d => d.Layer.Name, StringComparer.Ordinal).ToArray();
        Equal(FillVariantCount, dimensions.Length, "Fill dimension count");
        for (int v = 0; v < dimensions.Length; v++)
        {
            var dim = dimensions[v]; Equal(handles[v], dim.Handle, "Fill handle changed");
            Equal(FillBase(v)?.Index, dim.Style.TextFillColor?.Index, "Stored base fill changed");
            Equal(FillOverridden(v), dim.StyleOverrides.ContainsType(DimensionStyleOverrideType.TextFillColor), "Sparse fill override presence");
            if (FillOverridden(v)) Equal(FillOverride(v)?.Index, ((AciColor?)dim.StyleOverrides[DimensionStyleOverrideType.TextFillColor].Value)?.Index, "Stored fill override changed");
            short? expected = doc.DrawingVariables.AcadVer >= DxfVersion.AutoCad2007 ? FillExpected(v) : null;
            CheckFillLabels(dim.Block, expected);
            for (int repeat = 0; repeat < 2; repeat++) { dim.Update(); CheckFillLabels(dim.Block, expected); }
            Equal("unchanged", (string)dim.XData["FILL_KEEP"].XDataRecord.Single().Value, "Other application changed");
        }
        Equal(0, doc.Objects.Validate().Count, "Fill graph validation");
        var line = doc.Entities.Lines.Single();
        RawLinePointBits(new Vector3(17.25, -4.5, 2), line.StartPoint); RawLinePointBits(new Vector3(18.5, 9.25, -3), line.EndPoint);
    }
}
