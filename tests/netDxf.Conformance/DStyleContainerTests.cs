// Copyright (c) netDxf contributors. Licensed under the MIT License.
using netDxf;
using netDxf.Blocks;
using netDxf.Collections;
using netDxf.Entities;
using netDxf.Header;
using netDxf.Objects;
using netDxf.Tables;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static DimensionStyleOverrideDictionary ContainerOverrides(EntityObject entity) => entity is Dimension d
        ? d.StyleOverrides : ((Leader)entity).StyleOverrides;

    private static void RegisterDStyleContainerTests()
    {
        foreach (DxfVersion version in SupportedVersions) foreach (bool binary in new[] { false, true })
        {
            for (int placement = 0; placement < 6; placement++)
            {
                int p = placement;
                Run($"dstyle-containers/wire/{version}/{binary}/{p}", () =>
                {
                    var doc = DStyleContainerSource(version, p);
                    CheckDStyleContainer(doc, p, null);
                    string[] handles = DStyleContainerEntities(doc, p).Select(e => e.Handle).ToArray();
                    using var source = new MemoryStream(); Check(doc.Save(source, binary), "Container source save");
                    string stem = $"dstyle-containers-{version}-{binary}-{p}";
                    File.WriteAllBytes(Path.Combine(ArtifactDirectory, stem + "-source.dxf"), source.ToArray());
                    source.Position = 0;
                    doc = DxfDocument.Load(source) ?? throw new InvalidOperationException("Container source load");
                    CheckDStyleContainer(doc, p, handles);
                    foreach (bool output in new[] { false, true })
                    {
                        using var stream = new MemoryStream(); Check(doc.Save(stream, output), "Container save");
                        File.WriteAllBytes(Path.Combine(ArtifactDirectory, stem + $"-{output}.dxf"), stream.ToArray());
                        stream.Position = 0;
                        var second = DxfDocument.Load(stream) ?? throw new InvalidOperationException("Container reload");
                        CheckDStyleContainer(second, p, handles);
                        Equal(Layout.ModelSpaceName, second.Entities.ActiveLayout, "Load changed active entity layout");
                    }
                    Check(source.CanRead, "Container source ownership changed");
                });
            }
            Run($"dstyle-containers/zero-block-refusal/{version}/{binary}", () =>
            {
                using var source = new MemoryStream(DimLfacSource(version, binary, true, 1, 0));
                bool rejected;
                try { rejected = DxfDocument.Load(source) == null; }
                catch (ArgumentOutOfRangeException) { rejected = true; }
                Check(rejected && source.CanRead, "Invalid block override skipped or stream closed");
            });
        }
    }

    private static DxfDocument DStyleContainerSource(DxfVersion version, int placement)
    {
        var doc = new DxfDocument(version) { BuildDimensionBlocks = true }; doc.Comments.Clear();
        var style = new DimensionStyle("DSTYLE_BASE");
        var arrow = new Block("DSTYLE_ARROW", new EntityObject[] { new Line(Vector3.Zero, Vector3.UnitX) });
        var text = new TextStyle("DSTYLE_TEXT", "txt.shx");
        var line = new Linetype("DSTYLE_LINES");
        EntityObject[] entities = {
            new LinearDimension(Vector2.Zero, new Vector2(10, 0), 3, (placement % 3) * 45.0, style),
            new AlignedDimension(Vector2.Zero, new Vector2(10, 0), 3, style),
            new Angular2LineDimension(Vector2.Zero, Vector2.UnitX, Vector2.Zero, Vector2.UnitY, 3, style),
            new Angular3PointDimension(Vector2.Zero, Vector2.UnitX, Vector2.UnitY, 3, style),
            new RadialDimension(Vector2.Zero, new Vector2(5, 0), style),
            new DiametricDimension(Vector2.Zero, new Vector2(5, 0), style),
            new OrdinateDimension(Vector2.Zero, new Vector2(2, 3), new Vector2(7, 3), OrdinateDimensionAxis.X, style),
            new Leader(new[] { Vector2.Zero, new Vector2(5, 3), new Vector2(9, 3) }, style)
        };
        for (int i = 0; i < entities.Length; i++)
        {
            var entity = entities[i]; entity.Layer = new Layer("DSTYLE_ENTITY_" + i);
            if (entity is Dimension dimension) dimension.UserText = "FIXED";
            var data = new XData(new ApplicationRegistry("DSTYLE_KEEP"));
            data.XDataRecord.Add(new XDataRecord(XDataCode.String, "preserved")); entity.XData.Add(data);
            var overrides = ContainerOverrides(entity);
            overrides.Add(DimensionStyleOverrideType.DimScaleLinear, -0.75);
            overrides.Add(DimensionStyleOverrideType.TextHeight, 0.75);
            overrides.Add(DimensionStyleOverrideType.TextVerticalPosition, -0.25);
            overrides.Add(DimensionStyleOverrideType.UserPositionedText, true);
            overrides.Add(DimensionStyleOverrideType.TextColor, new AciColor(4));
            overrides.Add(DimensionStyleOverrideType.TextStyle, text);
            overrides.Add(DimensionStyleOverrideType.DimLineLinetype, line);
            overrides.Add(DimensionStyleOverrideType.DimArrow1, arrow);
            overrides.Add(DimensionStyleOverrideType.LeaderArrow, arrow);
        }
        if (placement < 3)
        {
            if (placement > 0) doc.Layouts.Add(new Layout("DSTYLE_PAPER_A"));
            if (placement == 2) doc.Layouts.Add(new Layout("DSTYLE_PAPER_B"));
            var target = placement == 0 ? doc.Layouts[Layout.ModelSpaceName].AssociatedBlock :
                doc.Layouts[placement == 1 ? "DSTYLE_PAPER_A" : "DSTYLE_PAPER_B"].AssociatedBlock;
            foreach (var entity in entities) target.Entities.Add(entity);
            if (placement == 2) doc.Entities.ActiveLayout = "DSTYLE_PAPER_A";
        }
        else
        {
            var block = new Block("DSTYLE_CONTAINER", entities);
            if (placement == 3) doc.Entities.Add(new Insert(block));
            else if (placement == 4)
            {
                var outer = new Block("DSTYLE_OUTER", new EntityObject[] { new Insert(block), new Insert(block, new Vector3(20, 0, 0)) });
                doc.Entities.Add(new Insert(outer)); doc.Entities.Add(new Insert(outer, new Vector3(40, 0, 0)));
            }
            else doc.Blocks.Add(block);
        }
        doc.Layouts[Layout.ModelSpaceName].AssociatedBlock.Entities.Add(new Line(new Vector3(17.25, -4.5, 2), new Vector3(18.5, 9.25, -3)));
        // Adoption builds the initial display block and resets automatic text placement.
        // Assign the stored manual position afterwards; this fixture does not qualify block regeneration.
        if (placement >= 3) ((LinearDimension)entities[0]).TextReferencePoint = new Vector2(7, 5);
        return doc;
    }

    private static EntityObject[] DStyleContainerEntities(DxfDocument doc, int placement)
    {
        Block owner = placement == 0 ? doc.Layouts[Layout.ModelSpaceName].AssociatedBlock : placement < 3
            ? doc.Layouts[placement == 1 ? "DSTYLE_PAPER_A" : "DSTYLE_PAPER_B"].AssociatedBlock : doc.Blocks["DSTYLE_CONTAINER"];
        return owner.Entities.Where(e => e is Dimension || e is Leader).OrderBy(e => e.Layer.Name, StringComparer.Ordinal).ToArray();
    }

    private static void CheckDStyleContainer(DxfDocument doc, int placement, string[]? handles)
    {
        var entities = DStyleContainerEntities(doc, placement); Equal(8, entities.Length, "All dimension families and leader");
        Equal(7, entities.OfType<Dimension>().Select(d => d.GetType()).Distinct().Count(), "Distinct dimension families");
        var linear = entities.OfType<LinearDimension>().Single();
        SameDoubleBits((placement % 3) * 45.0, linear.Rotation, "Rotated dimension angle");
        Equal(placement >= 3, linear.TextPositionManuallySet, "Rotated dimension text-position flag");
        if (placement >= 3)
        {
            SameDoubleBits(7, linear.TextReferencePoint.X, "Manual dimension text X");
            SameDoubleBits(5, linear.TextReferencePoint.Y, "Manual dimension text Y");
        }
        SameDoubleBits(0, linear.FirstReferencePoint.X, "Linear first reference X");
        SameDoubleBits(0, linear.FirstReferencePoint.Y, "Linear first reference Y");
        SameDoubleBits(10, linear.SecondReferencePoint.X, "Linear second reference X");
        SameDoubleBits(0, linear.SecondReferencePoint.Y, "Linear second reference Y");
        for (int i = 0; i < entities.Length; i++)
        {
            var entity = entities[i]; var overrides = ContainerOverrides(entity);
            Equal(9, overrides.Count, "Override count, including reference-valued entries");
            if (handles != null) Equal(handles[i], entity.Handle, "Container entity identity");
            SameDoubleBits(-0.75, (double)overrides[DimensionStyleOverrideType.DimScaleLinear].Value, "Container scale");
            SameDoubleBits(0.75, (double)overrides[DimensionStyleOverrideType.TextHeight].Value, "Container text height");
            SameDoubleBits(-0.25, (double)overrides[DimensionStyleOverrideType.TextVerticalPosition].Value, "Container text placement");
            Check((bool)overrides[DimensionStyleOverrideType.UserPositionedText].Value, "Container user-positioned text");
            Equal((short)4, ((AciColor)overrides[DimensionStyleOverrideType.TextColor].Value).Index, "Container color");
            Check(ReferenceEquals(doc.TextStyles["DSTYLE_TEXT"], overrides[DimensionStyleOverrideType.TextStyle].Value), "Canonical text style");
            Check(ReferenceEquals(doc.Linetypes["DSTYLE_LINES"], overrides[DimensionStyleOverrideType.DimLineLinetype].Value), "Canonical linetype");
            Check(ReferenceEquals(doc.Blocks["DSTYLE_ARROW"], overrides[DimensionStyleOverrideType.DimArrow1].Value) &&
                  ReferenceEquals(doc.Blocks["DSTYLE_ARROW"], overrides[DimensionStyleOverrideType.LeaderArrow].Value), "Canonical arrow blocks");
            Check(doc.TextStyles.GetReferences("DSTYLE_TEXT").Any(r => ReferenceEquals(r.Reference, entity)), "Text-style reference ledger");
            Check(doc.Linetypes.GetReferences("DSTYLE_LINES").Any(r => ReferenceEquals(r.Reference, entity)), "Linetype reference ledger");
            Check(doc.Blocks.GetReferences("DSTYLE_ARROW").Any(r => ReferenceEquals(r.Reference, entity) && r.Uses == 2), "Arrow reference multiplicity");
            Equal("preserved", (string)entity.XData["DSTYLE_KEEP"].XDataRecord.Single().Value, "Unrelated XData");
            var clone = (EntityObject)entity.Clone(); var copied = ContainerOverrides(clone);
            Equal(entity.GetType(), clone.GetType(), "Clone dimension family");
            Equal(9, copied.Count, "Detached clone overrides"); SameDoubleBits(-0.75, (double)copied[DimensionStyleOverrideType.DimScaleLinear].Value, "Clone scale");
            foreach (var kind in new[] { DimensionStyleOverrideType.TextStyle, DimensionStyleOverrideType.DimLineLinetype, DimensionStyleOverrideType.DimArrow1, DimensionStyleOverrideType.LeaderArrow })
                Check(!ReferenceEquals(overrides[kind].Value, copied[kind].Value), "Detached referenced-object clone " + kind);
            copied[DimensionStyleOverrideType.TextHeight] = new DimensionStyleOverride(DimensionStyleOverrideType.TextHeight, 2.0);
            SameDoubleBits(0.75, (double)overrides[DimensionStyleOverrideType.TextHeight].Value, "Clone mutation changed source");
        }
        Equal(0, doc.Objects.Validate().Count, "Container graph validation");
        var line = doc.Layouts[Layout.ModelSpaceName].AssociatedBlock.Entities.OfType<Line>().Single();
        RawLinePointBits(new Vector3(17.25, -4.5, 2), line.StartPoint); RawLinePointBits(new Vector3(18.5, 9.25, -3), line.EndPoint);
    }
}
