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
    private static readonly Vector2 ManualBlockTextPoint = new(17.25, -8.5);

    private static Dimension TextBlockDimension(int kind)
    {
        var style = new DimensionStyle("TEXT_BLOCK_STYLE")
        {
            TextHeight = 0.75, ArrowSize = 0.2, TextOffset = 0.25,
            FitTextMove = DimensionStyleFitTextMove.OverDimLineWithoutLeader
        };
        Dimension dim = kind switch
        {
            0 => new LinearDimension(Vector2.Zero, new Vector2(10, 0), 3, 30, style),
            1 => new AlignedDimension(Vector2.Zero, new Vector2(10, 0), 3, style),
            2 => new Angular2LineDimension(Vector2.Zero, Vector2.UnitX, Vector2.Zero, Vector2.UnitY, 3, style),
            3 => new DiametricDimension(Vector2.Zero, new Vector2(5, 0), style),
            4 => new RadialDimension(Vector2.Zero, new Vector2(5, 0), style),
            5 => new Angular3PointDimension(Vector2.Zero, Vector2.UnitX, Vector2.UnitY, 3, style),
            6 => new OrdinateDimension(Vector2.Zero, new Vector2(2, 3), new Vector2(7, 3), OrdinateDimensionAxis.X, style),
            7 => new ArcLengthDimension(Vector2.Zero, 5, 0, 90, 7, style),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        dim.UserText = "UPPER\\XLOWER";
        return dim;
    }

    private static Block TextBlockDirect(Dimension dim, string name) => dim switch
    {
        LinearDimension d => DimensionBlock.Build(d, name),
        AlignedDimension d => DimensionBlock.Build(d, name),
        Angular2LineDimension d => DimensionBlock.Build(d, name),
        DiametricDimension d => DimensionBlock.Build(d, name),
        RadialDimension d => DimensionBlock.Build(d, name),
        Angular3PointDimension d => DimensionBlock.Build(d, name),
        OrdinateDimension d => DimensionBlock.Build(d, name),
        ArcLengthDimension d => DimensionBlock.Build(d, name),
        _ => throw new ArgumentException("Unexpected dimension")
    };

    private static void SetManualBlockText(Dimension dim, int variant)
    {
        dim.TextReferencePoint = ManualBlockTextPoint;
        dim.TextRotation = 25;
        dim.AttachmentPoint = (MTextAttachmentPoint)(1 + variant % 9);
        dim.LineSpacingStyle = MTextLineSpacingStyle.Exact;
        dim.LineSpacingFactor = 1.5;
    }

    private static void CheckManualBlockText(Dimension dim, Block block, int variant)
    {
        Check(dim.TextPositionManuallySet, "Block generation erased manual text flag");
        SameDoubleBits(ManualBlockTextPoint.X, dim.TextReferencePoint.X, "Manual text X changed");
        SameDoubleBits(ManualBlockTextPoint.Y, dim.TextReferencePoint.Y, "Manual text Y changed");
        var text = block.Entities.OfType<MText>().Single();
        SameDoubleBits(ManualBlockTextPoint.X, text.Position.X, "Rendered manual text X");
        SameDoubleBits(ManualBlockTextPoint.Y, text.Position.Y, "Rendered manual text Y");
        SameDoubleBits(0, text.Position.Z, "Dimension block text is local planar geometry");
        Equal((MTextAttachmentPoint)(1 + variant % 9), text.AttachmentPoint, "Rendered attachment");
        Equal(MTextLineSpacingStyle.Exact, text.LineSpacingStyle, "Rendered spacing style");
        SameDoubleBits(1.5, text.LineSpacingFactor, "Rendered spacing factor");
        Equal("UPPER\\PLOWER", text.Value, "Manual split text content");
    }

    private static void RegisterDimensionTextBlockTests()
    {
        for (int kind = 0; kind < 8; kind++)
        {
            int k = kind;
            for (int variant = 0; variant < 9; variant++) foreach (bool direct in new[] { false, true })
            {
                int v = variant;
                Run($"dimension-text-block/manual/{k}/{v}/{direct}", () =>
                {
                    var dim = TextBlockDimension(k);
                    var automatic = TextBlockDirect(dim, "AUTOMATIC");
                    var before = automatic.Entities.OfType<MText>().ToArray();
                    double defaultRotation = before[0].Rotation;
                    double measure = dim.Measurement;
                    SetManualBlockText(dim, v);
                    var block = direct ? TextBlockDirect(dim, "MANUAL") : DimensionBlock.Build(dim);
                    CheckManualBlockText(dim, block, v);
                    Near((defaultRotation + 25) % 360, block.Entities.OfType<MText>().Single().Rotation, "Rendered rotation offset");
                    SameDoubleBits(measure, dim.Measurement, "Direct block build changed measured geometry");
                    Check(dim.Owner == null && dim.Block == null, "Direct build adopted entity or assigned drawing block");
                    var repeated = DimensionBlock.Build(dim, "REPEATED");
                    CheckManualBlockText(dim, repeated, v);
                    Check(!ReferenceEquals(block.Entities.OfType<MText>().Single(), repeated.Entities.OfType<MText>().Single()), "Generated labels shared");
                    var clone = (Dimension)dim.Clone();
                    CheckManualBlockText(clone, TextBlockDirect(clone, "CLONE"), v);
                    clone.TextReferencePoint = new Vector2(-4, 8);
                    SameDoubleBits(ManualBlockTextPoint.X, dim.TextReferencePoint.X, "Clone changed source text");
                    dim.TextPositionManuallySet = false;
                    dim.TextRotation = 0; dim.LineSpacingStyle = MTextLineSpacingStyle.AtLeast; dim.LineSpacingFactor = 1;
                    // Radial/diametric builders deliberately derive layout from text distance.
                    dim.Update();
                    var reset = TextBlockDirect(dim, "RESET");
                    Check(!dim.TextPositionManuallySet, "Reset failed to restore automatic placement");
                    Check(reset.Entities.OfType<MText>().All(t => t.LineSpacingFactor == 1), "Automatic spacing reset");
                });
            }
            Run($"dimension-text-block/automatic-settings/{k}", () =>
            {
                var dim = TextBlockDimension(k);
                var before = TextBlockDirect(dim, "BEFORE").Entities.OfType<MText>().ToArray();
                dim.TextRotation = 25; dim.LineSpacingStyle = MTextLineSpacingStyle.Exact; dim.LineSpacingFactor = 1.5;
                var after = TextBlockDirect(dim, "AFTER").Entities.OfType<MText>().ToArray();
                Equal(before.Length, after.Length, "Automatic label count changed");
                Check(!dim.TextPositionManuallySet, "Automatic settings set manual mode");
                for (int i = 0; i < before.Length; i++)
                {
                    RawLinePointBits(before[i].Position, after[i].Position);
                    Equal(before[i].Value, after[i].Value, "Automatic content changed");
                    Equal(before[i].AttachmentPoint, after[i].AttachmentPoint, "Automatic attachment changed");
                    Near((before[i].Rotation + 25) % 360, after[i].Rotation, "Automatic orientation offset");
                    Equal(MTextLineSpacingStyle.Exact, after[i].LineSpacingStyle, "Automatic spacing style");
                    SameDoubleBits(1.5, after[i].LineSpacingFactor, "Automatic spacing factor");
                }
            });
            Run($"dimension-text-block/arrow-text-isolation/{k}", () =>
            {
                var dim = TextBlockDimension(k); SetManualBlockText(dim, k);
                var arrowText = new MText("ARROW", new Vector3(3, 4, 0), 0.2) { Rotation = 15 };
                var arrow = new Block("TEXT_ARROW", new EntityObject[] { arrowText });
                dim.Style.DimArrow1 = arrow; dim.Style.DimArrow2 = arrow;
                var block = TextBlockDirect(dim, "MANUAL_ARROW"); CheckManualBlockText(dim, block, k);
                foreach (var insert in block.Entities.OfType<Insert>())
                    Check(ReferenceEquals(arrow, insert.Block), "Arrow definition replaced");
                Equal("ARROW", arrowText.Value, "Arrow text was edited");
                RawLinePointBits(new Vector3(3, 4, 0), arrowText.Position);
                SameDoubleBits(15, arrowText.Rotation, "Arrow rotation changed");
                SameDoubleBits(1, arrowText.LineSpacingFactor, "Arrow spacing changed");
                Check(ReferenceEquals(arrow, arrowText.Owner), "Arrow text ownership changed");
            });
            for (int signs = 0; signs < 4; signs++)
            {
                int bits = signs;
                Run($"dimension-text-block/signed-zero/{k}/{bits}", () =>
                {
                    var dim = TextBlockDimension(k); dim.UserText = "ZERO";
                    var point = new Vector2(BitConverter.Int64BitsToDouble((bits & 1) == 0 ? 0 : long.MinValue),
                        BitConverter.Int64BitsToDouble((bits & 2) == 0 ? 0 : long.MinValue));
                    dim.TextReferencePoint = point;
                    var text = TextBlockDirect(dim, "SIGNED_ZERO").Entities.OfType<MText>().Single();
                    Check(dim.TextPositionManuallySet, "Zero anchor lost manual mode");
                    SameDoubleBits(point.X, dim.TextReferencePoint.X, "Stored zero X bits");
                    SameDoubleBits(point.Y, dim.TextReferencePoint.Y, "Stored zero Y bits");
                    SameDoubleBits(point.X, text.Position.X, "Label zero X bits");
                    SameDoubleBits(point.Y, text.Position.Y, "Label zero Y bits");
                });
            }
            foreach (bool manual in new[] { false, true })
                Run($"dimension-text-block/name-refusal/{k}/{manual}", () =>
                {
                    var dim = TextBlockDimension(k);
                    if (manual) SetManualBlockText(dim, k);
                    Vector2 point = dim.TextReferencePoint;
                    Throws<ArgumentNullException>(() => TextBlockDirect(dim, null!));
                    Equal(manual, dim.TextPositionManuallySet, "Failed Build changed text mode");
                    SameDoubleBits(point.X, dim.TextReferencePoint.X, "Failed Build changed X");
                    SameDoubleBits(point.Y, dim.TextReferencePoint.Y, "Failed Build changed Y");
                });
            Run($"dimension-text-block/suppressed/{k}", () =>
            {
                var dim = TextBlockDimension(k); SetManualBlockText(dim, k); dim.UserText = " ";
                var block = TextBlockDirect(dim, "SUPPRESSED");
                Equal(0, block.Entities.OfType<MText>().Count(), "Suppressed label generated");
                Check(dim.TextPositionManuallySet, "Suppression erased manual flag");
                SameDoubleBits(ManualBlockTextPoint.X, dim.TextReferencePoint.X, "Suppression changed text X");
            });
            foreach (int invalid in Enumerable.Range(0, 8))
                Run($"dimension-text-block/reject/{k}/{invalid}", () =>
                {
                    var dim = TextBlockDimension(k); SetManualBlockText(dim, k);
                    switch (invalid)
                    {
                        case 0: dim.TextReferencePoint = new Vector2(double.NaN, 3); break;
                        case 1: dim.TextReferencePoint = new Vector2(3, double.PositiveInfinity); break;
                        case 2: dim.TextRotation = double.NaN; break;
                        case 3: dim.LineSpacingFactor = double.NaN; break;
                        case 4: dim.LineSpacingStyle = (MTextLineSpacingStyle)99; break;
                        case 5: dim.AttachmentPoint = (MTextAttachmentPoint)0; break;
                        case 6: dim.AttachmentPoint = (MTextAttachmentPoint)10; break;
                        case 7: dim.LineSpacingStyle = MTextLineSpacingStyle.Multiple; break;
                    }
                    Vector2 point = dim.TextReferencePoint;
                    Throws<ArgumentException>(() => TextBlockDirect(dim, "INVALID"));
                    Check(dim.TextPositionManuallySet && dim.Block == null, "Failed Build published state");
                    SameDoubleBits(point.X, dim.TextReferencePoint.X, "Failed Build mutated X");
                    SameDoubleBits(point.Y, dim.TextReferencePoint.Y, "Failed Build mutated Y");
                });
        }

        foreach (DxfVersion version in SupportedVersions) foreach (bool binary in new[] { false, true })
        for (int placement = 0; placement < 3; placement++) for (int kind = 0; kind < 8; kind++)
        {
            if (kind == 7 && version < DxfVersion.AutoCad2004) continue;
            int p = placement, k = kind;
            Run($"dimension-text-block/wire/{version}/{binary}/{p}/{k}", () =>
            {
                var dim = TextBlockDimension(k); SetManualBlockText(dim, k);
                dim.Normal = p == 0 ? Vector3.UnitZ : new Vector3(0, 1, 0);
                dim.Elevation = 2.5;
                var doc = new DxfDocument(version) { BuildDimensionBlocks = true }; doc.Comments.Clear();
                if (p == 0) doc.Entities.Add(dim);
                else if (p == 1)
                {
                    doc.Layouts.Add(new Layout("TEXT_PAPER")); doc.Entities.ActiveLayout = "TEXT_PAPER"; doc.Entities.Add(dim);
                }
                else doc.Entities.Add(new Insert(new Block("TEXT_HOLDER", new EntityObject[] { dim })));
                CheckManualBlockText(dim, dim.Block, k); // The original loss happened during adoption.
                string handle = dim.Handle;
                for (int generation = 0; generation < 3; generation++)
                {
                    dim.Update();
                    CheckManualBlockText(dim, dim.Block, k);
                    Equal(handle, dim.Handle, "Update changed dimension identity");
                    Equal(0, doc.Objects.Validate().Count, "Update damaged document graph");
                }
                string stem = $"dimension-text-block-{version}-{binary}-{p}-{k}";
                using var source = new MemoryStream(); Check(doc.Save(source, binary), "Regenerated dimension save");
                File.WriteAllBytes(Path.Combine(ArtifactDirectory, stem + "-source.dxf"), source.ToArray());
                source.Position = 0;
                doc = DxfDocument.Load(source) ?? throw new InvalidOperationException("Regenerated dimension load");
                dim = doc.Blocks.SelectMany(b => b.Entities).OfType<Dimension>().Single();
                CheckManualBlockText(dim, dim.Block, k); Equal(handle, dim.Handle, "Reload changed dimension handle");
                dim.Update(); CheckManualBlockText(dim, dim.Block, k);
                foreach (bool output in new[] { false, true })
                {
                    using var stream = new MemoryStream(); Check(doc.Save(stream, output), "Regenerated dimension resave");
                    File.WriteAllBytes(Path.Combine(ArtifactDirectory, stem + $"-{output}.dxf"), stream.ToArray());
                    stream.Position = 0;
                    var second = DxfDocument.Load(stream) ?? throw new InvalidOperationException("Regenerated dimension reload");
                    var loaded = second.Blocks.SelectMany(b => b.Entities).OfType<Dimension>().Single();
                    CheckManualBlockText(loaded, loaded.Block, k);
                    SameDoubleBits(2.5, loaded.Elevation, "Dimension elevation changed");
                    Equal(0, second.Objects.Validate().Count, "Round-trip graph invalid");
                }
            });
        }
    }
}
