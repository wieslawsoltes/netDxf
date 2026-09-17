using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.Tables;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static readonly string[] CloneReviewKinds = { "solid", "trace", "shape", "quadratic", "cubic", "leader", "annotated-leader" };

    private static void RegisterEntityCloneReviewTests()
    {
        foreach (string kind in CloneReviewKinds)
        {
            foreach (int variant in new[] { 0, 1 })
            {
                foreach (int operation in new[] { 0, 1, 2 })
                    Run($"clone-review/state/{kind}/{variant}/{operation}", () => CloneReviewState(kind, variant, operation));
                Run($"clone-review/isolation/{kind}/{variant}", () => CloneReviewIsolation(kind, variant));
            }
            foreach (DxfVersion version in SupportedVersions)
                foreach (bool binary in new[] { false, true })
                    Run($"clone-review/wire/{kind}/{version}/{binary}", () => CloneReviewWire(kind, version, binary));
        }
        Run("clone-review/leader-color-value", () =>
        {
            var source = (Leader)CloneReviewEntity("leader", 0);
            var clone = (Leader)source.Clone();
            Equal(source.LineColor.Index, clone.LineColor.Index, "Leader line ACI");
            Equal(AciColor.ToTrueColor(source.LineColor), AciColor.ToTrueColor(clone.LineColor), "Leader line RGB");
            Check(!ReferenceEquals(source.LineColor, clone.LineColor), "Leader line color is shared");
        });
    }

    private static EntityObject CloneReviewEntity(string kind, int variant)
    {
        double sign = variant == 0 ? -1 : 1;
        var points = new[] { new Vector3(1, 2, 3), new Vector3(4, 6, -2), new Vector3(8, 5, 4), new Vector3(11, -3, 7) };
        EntityObject entity;
        switch (kind)
        {
            case "solid": entity = new Solid(new Vector2(1, 2), new Vector2(4, 3), new Vector2(2, 7), new Vector2(6, 8))
                { Elevation = sign * 7.25, Thickness = sign * 2.5 }; break;
            case "trace": entity = new Trace(new Vector2(1, 2), new Vector2(4, 3), new Vector2(2, 7), new Vector2(6, 8))
                { Elevation = sign * 7.25, Thickness = sign * 2.5 }; break;
            case "shape": entity = new Shape("TRACK1", new ShapeStyle("CloneShape", "ltypeshp.shx"))
                { Position = new Vector3(1, 2, 7.25), Size = 3.5, Rotation = 27, WidthFactor = sign * 2.75, ObliqueAngle = 15, Thickness = sign * 2.5 }; break;
            case "quadratic":
            case "cubic": entity = new Polyline3D(points, variant == 1)
                { SmoothType = kind == "quadratic" ? PolylineSmoothType.Quadratic : PolylineSmoothType.Cubic }; break;
            default:
                var leader = new Leader(new[] { new Vector2(1, 2), new Vector2(4, 3), new Vector2(6, 8) })
                {
                    Elevation = sign * 7.25, Offset = new Vector2(0.5, -0.75), Direction = new Vector2(-3, 4),
                    LineColor = new AciColor(12, 34, 56), ShowArrowhead = false, PathType = LeaderPathType.Spline
                };
                if (kind == "annotated-leader")
                {
                    leader.Annotation = new MText("clone annotation", new Vector3(20, 30, 0), 2.0, 12.0);
                    leader.HasHookline = true;
                }
                entity = leader; break;
        }
        entity.Normal = variant == 0 ? Vector3.UnitZ : new Vector3(0, 0.6, 0.8);
        entity.IsVisible = false;
        entity.Layer = new Layer("CloneReviewLayer") { Color = new AciColor(4) };
        entity.Color = new AciColor(5);
        entity.Transparency = new Transparency(25);
        entity.Lineweight = Lineweight.W25;
        entity.LinetypeScale = 2.25;
        entity.ColorName = "Clone$Blue";
        entity.ShadowMode = EntityShadowMode.CastAndReceive;
        entity.ProxyGraphics = new byte[] { 13, 37, 0, 255 };
        var data = new XData(new ApplicationRegistry("CLONE_REVIEW"));
        data.XDataRecord.Add(new XDataRecord(XDataCode.String, "independent clone"));
        entity.XData.Add(data);
        return entity;
    }

    private static void AssertCloneReviewGeometry(EntityObject expected, EntityObject actual, bool wire = false)
    {
        Equal(expected.GetType(), actual.GetType(), "Entity kind");
        if (!wire) Equal(expected.Normal, actual.Normal, "Normal");
        switch (expected)
        {
            case Solid a when actual is Solid b:
                Equal(a.FirstVertex, b.FirstVertex, "Solid first"); Equal(a.SecondVertex, b.SecondVertex, "Solid second");
                Equal(a.ThirdVertex, b.ThirdVertex, "Solid third"); Equal(a.FourthVertex, b.FourthVertex, "Solid fourth");
                SameDoubleBits(a.Elevation, b.Elevation, "Solid elevation"); SameDoubleBits(a.Thickness, b.Thickness, "Solid thickness"); break;
            case Trace a when actual is Trace b:
                Equal(a.FirstVertex, b.FirstVertex, "Trace first"); Equal(a.SecondVertex, b.SecondVertex, "Trace second");
                Equal(a.ThirdVertex, b.ThirdVertex, "Trace third"); Equal(a.FourthVertex, b.FourthVertex, "Trace fourth");
                SameDoubleBits(a.Elevation, b.Elevation, "Trace elevation"); SameDoubleBits(a.Thickness, b.Thickness, "Trace thickness"); break;
            case Shape a when actual is Shape b:
                Equal(a.Position, b.Position, "Shape position"); Equal(a.Name, b.Name, "Shape name");
                SameDoubleBits(a.WidthFactor, b.WidthFactor, "Shape width factor"); SameDoubleBits(a.Size, b.Size, "Shape size");
                SameDoubleBits(a.Rotation, b.Rotation, "Shape rotation"); SameDoubleBits(a.ObliqueAngle, b.ObliqueAngle, "Shape oblique");
                SameDoubleBits(a.Thickness, b.Thickness, "Shape thickness"); break;
            case Polyline3D a when actual is Polyline3D b:
                Equal(a.SmoothType, b.SmoothType, "Polyline smooth type"); Equal(a.IsClosed, b.IsClosed, "Polyline closed");
                Check(a.Vertexes.SequenceEqual(b.Vertexes), "Polyline control vertices changed"); break;
            case Leader a when actual is Leader b:
                Equal(a.Direction, b.Direction, "Leader direction"); Equal(a.Offset, b.Offset, "Leader offset");
                SameDoubleBits(a.Elevation, b.Elevation, "Leader elevation");
                Equal(a.HasHookline, b.HasHookline, "Leader hookline"); Equal(a.PathType, b.PathType, "Leader path");
                Equal(a.ShowArrowhead, b.ShowArrowhead, "Leader arrowhead"); Equal(a.LineColor.Index, b.LineColor.Index, "Leader color");
                Check(a.Vertexes.SequenceEqual(b.Vertexes), "Leader vertices changed");
                Equal(a.Annotation?.GetType(), b.Annotation?.GetType(), "Annotation kind");
                if (a.Annotation is MText text && b.Annotation is MText copy)
                { Equal(text.Value, copy.Value, "Annotation text"); Equal(text.Position, copy.Position, "Annotation position"); }
                break;
            default: throw new InvalidOperationException("Unexpected clone test entity");
        }
    }

    private static void CloneReviewState(string kind, int variant, int operation)
    {
        EntityObject source = CloneReviewEntity(kind, variant), copy;
        if (operation == 0) copy = (EntityObject)source.Clone();
        else
        {
            var block = new Block("CloneReviewInner"); block.Entities.Add(source);
            if (operation == 1) copy = ((Block)block.Clone()).Entities.Single(e => e.Type == source.Type);
            else
            {
                var outer = new Block("CloneReviewOuter"); outer.Entities.Add(new Insert(block));
                var nested = (Insert)new Insert(outer).Clone();
                copy = nested.Block.Entities.OfType<Insert>().Single().Block.Entities.Single(e => e.Type == source.Type);
            }
        }
        AssertCloneReviewGeometry(source, copy);
        Check(!ReferenceEquals(source, copy), "Clone returned source");
        Check(copy.Handle == null, "Clone retained handle");
        Equal(source.IsVisible, copy.IsVisible, "Visibility"); Equal(source.LinetypeScale, copy.LinetypeScale, "Linetype scale");
        Equal(source.Lineweight, copy.Lineweight, "Lineweight"); Equal(source.Transparency.Value, copy.Transparency.Value, "Transparency");
        Equal(source.ColorName, copy.ColorName, "Color name"); Equal(source.ShadowMode, copy.ShadowMode, "Shadow mode");
        Check(source.ProxyGraphics!.SequenceEqual(copy.ProxyGraphics!), "Proxy packet changed during clone");
        Equal("independent clone", copy.XData["CLONE_REVIEW"].XDataRecord.Single().Value, "XData");
    }

    private static void CloneReviewIsolation(string kind, int variant)
    {
        EntityObject source = CloneReviewEntity(kind, variant), copy = (EntityObject)source.Clone();
        copy.Layer.Color.Index = 2; copy.Color.Index = 1; copy.Transparency.Value = 50;
        copy.ProxyGraphics = new byte[] { 1 }; copy.XData["CLONE_REVIEW"].XDataRecord.Clear();
        Equal((short)4, source.Layer.Color.Index, "Shared layer"); Equal((short)5, source.Color.Index, "Shared color");
        Equal((short)25, source.Transparency.Value, "Shared transparency"); Equal(4, source.ProxyGraphics!.Length, "Shared proxy");
        Equal(1, source.XData["CLONE_REVIEW"].XDataRecord.Count, "Shared XData");
        if (source is Leader a && copy is Leader b)
        {
            int color = AciColor.ToTrueColor(a.LineColor); b.LineColor.Index = 2;
            Equal(color, AciColor.ToTrueColor(a.LineColor), "Shared leader line color");
            b.Vertexes[0] = Vector2.Zero; Equal(new Vector2(1, 2), a.Vertexes[0], "Shared leader vertexes");
            if (a.Annotation is MText text && b.Annotation is MText changed)
            { changed.Value = "edited clone"; Equal("clone annotation", text.Value, "Shared annotation"); }
        }
        if (source is Shape shape && copy is Shape changedShape)
        { changedShape.Style.File = "other.shx"; Equal("ltypeshp.shx", shape.Style.File, "Shared shape style"); }
        if (source is Polyline3D poly && copy is Polyline3D changedPoly)
        { changedPoly.Vertexes[0] = Vector3.Zero; Equal(new Vector3(1, 2, 3), poly.Vertexes[0], "Shared polyline vertices"); }
    }

    private static void CloneReviewWire(string kind, DxfVersion version, bool binary)
    {
        EntityObject source = CloneReviewEntity(kind, 0);
        // Profile-ineligible common fields are not part of this geometry/clone review.
        source.ColorName = null; source.ShadowMode = null; source.ProxyGraphics = null;
        var support = new[] { Path.GetFullPath("TestDxfDocument/Support") };
        var document = new DxfDocument(version, support);
        EntityObject copy = (EntityObject)source.Clone();
        AssertCloneReviewGeometry(source, copy);
        document.Entities.Add(source); document.Entities.Add(copy);
        for (int cycle = 0; cycle < 2; cycle++)
        {
            using var stream = new MemoryStream(); Check(document.Save(stream, cycle == 0 ? binary : !binary), "Clone save failed");
            File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"clone-review-{kind}-{version}-{binary}-{cycle}.dxf"), stream.ToArray());
            stream.Position = 0;
            document = DxfDocument.Load(stream, support) ?? throw new InvalidOperationException("Clone reload failed");
            EntityObject[] entities = document.Entities.All.Where(e => e.Type == source.Type).ToArray();
            Equal(2, entities.Length, "Clone wire entity count");
            foreach (EntityObject entity in entities) AssertCloneReviewGeometry(source, entity, true);
        }
    }
}
