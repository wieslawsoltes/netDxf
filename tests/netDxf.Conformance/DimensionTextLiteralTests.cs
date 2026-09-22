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
    private static readonly string[] DimensionLiterals = { " ", "  ", "\t", " \t ", "", "<>", "  FIXED  ", "\u00a0" };

    private static void RegisterDimensionTextLiteralTests()
    {
        for (int kind = 0; kind < 8; kind++) for (int variant = 0; variant < DimensionLiterals.Length; variant++)
        {
            int k = kind, v = variant;
            Run($"dimension-text-literal/api/{k}/{v}", () =>
            {
                var dim = LiteralDimension(k, v);
                var first = TextBlockDirect(dim, "FIRST");
                CheckLiteralDimension(dim, v, first, null);
                var clone = (Dimension)dim.Clone();
                Equal(dim.GetType(), clone.GetType(), "Literal clone dimension family");
                CheckLiteralDimension(clone, v, TextBlockDirect(clone, "CLONE"), LiteralLabels(first));
                clone.UserText = "CHANGED";
                Equal(DimensionLiterals[v], dim.UserText, "Clone edited original literal");
                CheckLiteralDimension(dim, v, TextBlockDirect(dim, "REPEATED"), LiteralLabels(first));
            });
        }

        foreach (DxfVersion version in SupportedVersions) foreach (bool binary in new[] { false, true })
        for (int placement = 0; placement < 3; placement++) for (int kind = 0; kind < 8; kind++)
        {
            if (kind == 7 && version < DxfVersion.AutoCad2004) continue;
            int p = placement, k = kind;
            Run($"dimension-text-literal/wire/{version}/{binary}/{p}/{k}", () =>
            {
                var doc = new DxfDocument(version) { BuildDimensionBlocks = true }; doc.Comments.Clear();
                var dims = Enumerable.Range(0, DimensionLiterals.Length).Select(v => LiteralDimension(k, v)).ToArray();
                if (p == 0) foreach (var dim in dims) doc.Entities.Add(dim);
                else if (p == 1)
                {
                    doc.Layouts.Add(new Layout("LITERAL_PAPER")); doc.Entities.ActiveLayout = "LITERAL_PAPER";
                    foreach (var dim in dims) doc.Entities.Add(dim);
                }
                else doc.Entities.Add(new Insert(new Block("LITERAL_HOLDER", dims)));
                doc.Layouts[Layout.ModelSpaceName].AssociatedBlock.Entities.Add(
                    new Line(new Vector3(17.25, -4.5, 2), new Vector3(18.5, 9.25, -3)));
                string[][] labels = dims.Select(d => LiteralLabels(d.Block)).ToArray();
                string[] handles = dims.Select(d => d.Handle).ToArray();
                CheckLiteralDocument(doc, labels, handles, k, false);
                string stem = $"dimension-text-literal-{version}-{binary}-{p}-{k}";
                using var source = new MemoryStream(); Check(doc.Save(source, binary), "Literal source save");
                File.WriteAllBytes(Path.Combine(ArtifactDirectory, stem + "-source.dxf"), source.ToArray());
                source.Position = 0;
                doc = DxfDocument.Load(source) ?? throw new InvalidOperationException("Literal source load");
                CheckLiteralDocument(doc, labels, handles, k, true);
                foreach (bool output in new[] { false, true })
                {
                    using var stream = new MemoryStream(); Check(doc.Save(stream, output), "Literal resave");
                    File.WriteAllBytes(Path.Combine(ArtifactDirectory, stem + $"-{output}.dxf"), stream.ToArray());
                    stream.Position = 0;
                    var second = DxfDocument.Load(stream) ?? throw new InvalidOperationException("Literal reload");
                    CheckLiteralDocument(second, labels, handles, k, true);
                }
                Check(source.CanRead, "Literal load closed caller's stream");
            });
        }
    }

    private static Dimension LiteralDimension(int kind, int variant)
    {
        var dim = TextBlockDimension(kind);
        dim.UserText = DimensionLiterals[variant]; dim.Layer = new Layer("LITERAL_" + variant);
        // A cardinal manual anchor isolates text IO from radial re-projection rounding.
        dim.TextReferencePoint = new Vector2(17.25 + variant, 0); dim.Elevation = 2.5;
        dim.AttachmentPoint = MTextAttachmentPoint.MiddleCenter;
        return dim;
    }

    private static string[] LiteralLabels(Block block) => block.Entities.OfType<MText>().Select(t => t.Value).ToArray();

    private static void CheckLiteralDimension(Dimension dim, int variant, Block block, string[]? expected)
    {
        Equal(DimensionLiterals[variant], dim.UserText, "Stored literal changed");
        Check(dim.TextPositionManuallySet, "Literal lost manual text flag");
        SameDoubleBits(17.25 + variant, dim.TextReferencePoint.X, "Literal anchor X");
        SameDoubleBits(0, dim.TextReferencePoint.Y, "Literal anchor Y");
        SameDoubleBits(2.5, dim.Elevation, "Literal elevation");
        var labels = LiteralLabels(block);
        Equal(variant == 0 ? 0 : 1, labels.Length, "Exactly one blank suppresses labels");
        if (variant != 0 && variant != 4 && variant != 5) Equal(DimensionLiterals[variant], labels.Single(), "Literal display content");
        if (expected != null) Check(expected.SequenceEqual(labels), "Label changed after regeneration");
    }

    private static void CheckLiteralDocument(DxfDocument doc, string[][] labels, string[] handles, int kind, bool regenerate)
    {
        var dims = doc.Blocks.SelectMany(b => b.Entities).OfType<Dimension>()
            .OrderBy(d => d.Layer.Name, StringComparer.Ordinal).ToArray();
        Equal(DimensionLiterals.Length, dims.Length, "Literal entity count");
        for (int v = 0; v < dims.Length; v++)
        {
            var dim = dims[v]; Equal(TextBlockDimension(kind).GetType(), dim.GetType(), "Literal dimension family");
            Equal(handles[v], dim.Handle, "Literal identity changed");
            CheckLiteralDimension(dim, v, dim.Block, labels[v]);
            double measure = dim.Measurement;
            if (regenerate)
            {
                for (int generation = 0; generation < 2; generation++)
                {
                    dim.Update(); CheckLiteralDimension(dim, v, dim.Block, labels[v]);
                    SameDoubleBits(measure, dim.Measurement, "Regeneration changed measured geometry");
                }
            }
            var clone = (Dimension)dim.Clone();
            CheckLiteralDimension(clone, v, DimensionBlock.Build(clone), labels[v]);
        }
        Equal(0, doc.Objects.Validate().Count, "Literal graph validation");
        var line = doc.Layouts[Layout.ModelSpaceName].AssociatedBlock.Entities.OfType<Line>().Single();
        RawLinePointBits(new Vector3(17.25, -4.5, 2), line.StartPoint);
        RawLinePointBits(new Vector3(18.5, 9.25, -3), line.EndPoint);
    }
}
