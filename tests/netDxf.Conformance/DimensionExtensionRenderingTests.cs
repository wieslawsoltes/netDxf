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
    private static void RegisterDimensionExtensionRenderingTests()
    {
        for (int kind = 0; kind < 5; kind++) for (int row = 0; row < 12; row++) for (int pose = 0; pose < 4; pose++)
        {
            int k = kind, r = row, p = pose;
            Run($"dimension-extension-render/api/{k}/{r}/{p}", () =>
            {
                var dim = ExtensionFixture(k, r, p);
                CheckExtensionBlock(dim, DimensionBlock.Build(dim), k, r, p);
                CheckExtensionBlock(dim, TextBlockDirect(dim, "DIRECT"), k, r, p);
                var clone = (Dimension)dim.Clone();
                CheckExtensionBlock(clone, DimensionBlock.Build(clone), k, r, p);
                clone.Style.ExtLineFixedLength = 123;
                Equal(ExtensionBaseLength(r), dim.Style.ExtLineFixedLength, "Clone changed source length");
                Equal(r != 0 && r != 3, dim.Style.ExtLineFixed, "Builder changed base enable flag");
                CheckExtensionBlock(dim, DimensionBlock.Build(dim), k, r, p);
            });
        }
        foreach (DxfVersion version in SupportedVersions) foreach (bool binary in new[] { false, true })
        for (int kind = 0; kind < 5; kind++) for (int placement = 0; placement < 4; placement++)
        {
            if (kind == 4 && version < DxfVersion.AutoCad2004) continue;
            int k = kind, place = placement;
            Run($"dimension-extension-render/wire/{version}/{binary}/{k}/{place}", () =>
            {
                var doc = new DxfDocument(version) { BuildDimensionBlocks = true }; doc.Comments.Clear();
                var dims = Enumerable.Range(0, 12).Select(row => ExtensionFixture(k, row, place)).ToArray();
                if (place == 0) foreach (var dim in dims) doc.Entities.Add(dim);
                else if (place == 1)
                {
                    doc.Layouts.Add(new Layout("EXT_PAPER"));
                    foreach (var dim in dims) doc.Layouts["EXT_PAPER"].AssociatedBlock.Entities.Add(dim);
                }
                else
                {
                    var holder = new Block("EXT_HOLDER", dims);
                    if (place == 2) doc.Entities.Add(new Insert(holder)); else doc.Blocks.Add(holder);
                }
                doc.Entities.Add(new Line(new Vector3(17.25, -4.5, 2), new Vector3(18.5, 9.25, -3)));
                var handles = dims.Select(d => d.Handle).ToArray();
                CheckExtensionDocument(doc, k, handles, place);
                using var source = new MemoryStream(); Check(doc.Save(source, binary), "Extension source save");
                string stem = $"dimension-extension-render-{version}-{binary}-{k}-{place}";
                File.WriteAllBytes(Path.Combine(ArtifactDirectory, stem + "-source.dxf"), source.ToArray());
                source.Position = 0; var loaded = DxfDocument.Load(source) ?? throw new InvalidOperationException("Extension source load");
                CheckExtensionDocument(loaded, k, handles, place);
                foreach (bool output in new[] { false, true })
                {
                    using var stream = new MemoryStream(); Check(loaded.Save(stream, output), "Extension output save");
                    File.WriteAllBytes(Path.Combine(ArtifactDirectory, stem + $"-{output}.dxf"), stream.ToArray());
                    stream.Position = 0; var second = DxfDocument.Load(stream) ?? throw new InvalidOperationException("Extension reload");
                    CheckExtensionDocument(second, k, handles, place);
                }
                Check(source.CanRead, "Extension load closed caller stream");
            });
        }
        for (int kind = 0; kind < 5; kind++) for (int field = 0; field < 3; field++)
        foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.MaxValue })
        {
            int k = kind, f = field;
            Run($"dimension-extension-render/invalid/{k}/{f}/{ParameterBits(invalid)}", () =>
            {
                var dim = ExtensionFixture(k, 1, 0); dim.Style.DimScaleOverall = 2;
                if (f == 0) dim.Style.ExtLineFixedLength = invalid;
                else if (f == 1) dim.Style.ExtLineOffset = invalid;
                else dim.Style.ExtLineExtend = invalid;
                var text = dim.TextReferencePoint; bool manual = dim.TextPositionManuallySet;
                Throws<ArgumentOutOfRangeException>(() => DimensionBlock.Build(dim));
                Equal(text, dim.TextReferencePoint, "Rejected fixed geometry changed text anchor");
                Equal(manual, dim.TextPositionManuallySet, "Rejected fixed geometry changed manual flag");
                Check(dim.Block == null && dim.Owner == null, "Rejected build published ownership");
            });
        }
    }

    private static double ExtensionBaseLength(int row) => row == 5 ? 100 : row == 6 ? 0 : 2;
    private static double ExtensionScale(int row) => row is 5 or 7 ? 2 : 1;
    private static Dimension ExtensionFixture(int kind, int row, int pose)
    {
        var style = new DimensionStyle("EXT_STYLE_" + row.ToString("D2"))
        {
            ExtLineFixed = row != 0 && row != 3, ExtLineFixedLength = ExtensionBaseLength(row),
            ExtLineOffset = row == 11 ? 20 : .5, ExtLineExtend = row == 11 ? 1 : .25,
            DimScaleOverall = ExtensionScale(row), ExtLine1Off = row is 8 or 10, ExtLine2Off = row is 9 or 10,
            ExtLineColor = new AciColor(3), ExtLineLineweight = Lineweight.W35,
            ExtLine1Linetype = new Linetype("EXT_FIRST"), ExtLine2Linetype = new Linetype("EXT_SECOND")
        };
        double angle = pose == 2 ? 37 : 0, radians = angle * MathHelper.DegToRad;
        Vector2 Turn(double x, double y) => Vector2.Rotate(new Vector2(x, y), radians);
        double distance = pose == 1 ? 1 : pose == 3 && kind != 2 ? -8 : 8;
        Dimension dim = kind switch
        {
            0 => new AlignedDimension(Turn(0, 0), Turn(10, 0), distance, style),
            1 => new LinearDimension(Turn(0, 0), Turn(10, 0), distance, angle, style),
            2 => new Angular2LineDimension(Turn(pose == 1 ? 3 : 0, 0), Turn(pose == 1 ? 5 : 2, 0),
                Turn(0, pose == 1 ? 3 : 0), Turn(0, pose == 1 ? 5 : 2), distance, style),
            3 => new Angular3PointDimension(Turn(0, 0), Turn(2, 0), Turn(0, pose == 2 ? 12 : 3), distance, style),
            _ => new ArcLengthDimension(Turn(0, 0), 2, angle, angle + 90, distance, style)
        };
        dim.Layer = new Layer("EXT_ROW_" + row.ToString("D2")); dim.UserText = "FIXED";
        if (row == 2) dim.StyleOverrides.Add(DimensionStyleOverrideType.TextHeight, .9);
        if (row == 3)
        {
            dim.StyleOverrides.Add(DimensionStyleOverrideType.ExtLineFixed, true);
            dim.StyleOverrides.Add(DimensionStyleOverrideType.ExtLineFixedLength, 3.0);
            dim.StyleOverrides.Add(DimensionStyleOverrideType.ExtLine2Linetype, new Linetype("EXT_OVERRIDE"));
        }
        if (row == 4) dim.StyleOverrides.Add(DimensionStyleOverrideType.ExtLineFixed, false);
        var keep = new XData(new ApplicationRegistry("EXT_KEEP")); keep.XDataRecord.Add(new XDataRecord(XDataCode.String, "unchanged"));
        dim.XData.Add(keep); return dim;
    }

    private static (Vector2[] Origins, Vector2[] Anchors) ExtensionExpectedGeometry(int kind, int pose)
    {
        double angle = pose == 2 ? 37 * MathHelper.DegToRad : 0;
        Vector2 Turn(double x, double y) => Vector2.Rotate(new Vector2(x, y), angle);
        double depth = pose == 1 ? 1 : 8;
        if (kind < 2) return (new[] { Turn(0, 0), Turn(10, 0) }, new[] { Turn(0, pose == 3 ? -8 : depth), Turn(10, pose == 3 ? -8 : depth) });
        Vector2[] origins = kind == 2 ? new[] { Turn(pose == 1 ? 3 : 2, 0), Turn(0, pose == 1 ? 3 : 2) }
            : new[] { Turn(2, 0), Turn(0, kind == 3 ? pose == 2 ? 12 : 3 : 2) };
        Vector2[] anchors = new[] { Turn(depth, 0), Turn(0, depth) };
        if (pose == 3 && kind != 2) { Array.Reverse(origins); Array.Reverse(anchors); }
        return (origins, anchors);
    }

    private static void CheckExtensionBlock(Dimension dim, Block block, int kind, int row, int pose)
    {
        var (origins, anchors) = ExtensionExpectedGeometry(kind, pose);
        bool fixedLength = row != 0 && row != 4;
        double below = (row == 3 ? 3 : ExtensionBaseLength(row)) * ExtensionScale(row);
        double gap = (row == 11 ? 20 : .5) * ExtensionScale(row), above = (row == 11 ? 1 : .25) * ExtensionScale(row);
        var lines = block.Entities.OfType<Line>().Where(l => l.Linetype.Name.StartsWith("EXT_", StringComparison.Ordinal)).ToArray();
        int count = 0;
        for (int side = 0; side < 2; side++)
        {
            if (row == 10 || side == 0 && row == 8 || side == 1 && row == 9) continue;
            var delta = anchors[side] - origins[side]; double distance = delta.Modulus(); var direction = delta / distance;
            var start = origins[side] + gap * direction;
            var end = anchors[side] + above * direction;
            if (fixedLength)
            {
                double startAt = Math.Max(gap, distance - below);
                if (startAt >= distance + above) continue;
                start = origins[side] + startAt * direction;
            }
            string name = side == 0 ? "EXT_FIRST" : row == 3 ? "EXT_OVERRIDE" : "EXT_SECOND";
            var line = lines.Single(l => l.Linetype.Name == name); count++;
            Check((new Vector2(line.StartPoint.X, line.StartPoint.Y) - start).Modulus() <= 1e-9, "Extension start mismatch");
            Check((new Vector2(line.EndPoint.X, line.EndPoint.Y) - end).Modulus() <= 1e-9, "Extension end mismatch");
            Equal((short)3, line.Color.Index, "Extension color"); Equal(Lineweight.W35, line.Lineweight, "Extension lineweight");
            SameDoubleBits(0, line.StartPoint.Z, "Extension start OCS plane"); SameDoubleBits(0, line.EndPoint.Z, "Extension end OCS plane");
        }
        Equal(count, lines.Length, "Missing or extra extension lines");
        Equal("FIXED", block.Entities.OfType<MText>().Single().Value, "Unrelated label");
        Equal("unchanged", (string)dim.XData["EXT_KEEP"].XDataRecord.Single().Value, "Unrelated XData");
    }

    private static void CheckExtensionDocument(DxfDocument doc, int kind, string[] handles, int pose)
    {
        var dims = doc.Blocks.SelectMany(b => b.Entities).OfType<Dimension>().OrderBy(d => d.Layer.Name, StringComparer.Ordinal).ToArray();
        Equal(12, dims.Length, "Extension dimension count");
        for (int row = 0; row < dims.Length; row++)
        {
            var dim = dims[row]; Equal(handles[row], dim.Handle, "Extension dimension identity");
            for (int repeat = 0; repeat < 3; repeat++)
            {
                CheckExtensionBlock(dim, dim.Block, kind, row, pose); dim.Update();
            }
            Check(ReferenceEquals(doc.Linetypes["EXT_FIRST"], dim.Style.ExtLine1Linetype), "Canonical first linetype");
        }
        Equal(0, doc.Objects.Validate().Count, "Extension document graph");
        var line = doc.Entities.Lines.Single(); RawLinePointBits(new Vector3(17.25, -4.5, 2), line.StartPoint);
        RawLinePointBits(new Vector3(18.5, 9.25, -3), line.EndPoint);
    }
}
