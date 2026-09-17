using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Tables;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static readonly string[] CloneReviewKinds = { "solid", "trace", "shape", "polyline", "leader" };

    private static void RegisterEntityCloneReviewTests()
    {
        foreach (string kind in CloneReviewKinds)
        foreach (int variant in Enumerable.Range(0, 3))
        foreach (int depth in Enumerable.Range(0, 3))
        {
            string k = kind; int v = variant, d = depth;
            foreach (bool registered in new[] { false, true })
            {
                bool r = registered;
                Run($"clone-review/model/{k}/{v}/{d}/{r}", () => CloneReviewModel(k, v, d, r));
            }
            foreach (DxfVersion version in SupportedVersions)
            foreach (bool binary in new[] { false, true })
            {
                DxfVersion profile = version; bool transport = binary;
                Run($"clone-review/wire/{k}/{v}/{d}/{profile}/{transport}",
                    () => CloneReviewWire(k, v, d, profile, transport));
            }
        }
        foreach (int variant in Enumerable.Range(0, 3))
        {
            int v = variant;
            Run($"clone-review/leader-annotation/{v}", () => CloneReviewAnnotation(v));
        }
        Run("clone-review/leader-exact-directions", CloneReviewDirections);
        foreach (DxfVersion version in SupportedVersions)
        foreach (bool binary in new[] { false, true })
        {
            DxfVersion v = version; bool b = binary;
            Run($"clone-review/retained-guards/{v}/{b}", () => CloneReviewRetained(v, b));
        }
    }

    private static EntityObject CloneReviewSubject(string kind, int variant)
    {
        double elevation = new[] { 13.75, -7.25, 128.5 }[variant];
        EntityObject result = kind switch
        {
            "solid" => new Solid(new Vector2(1, 2), new Vector2(5, 3), new Vector2(2, 8), new Vector2(7, 9))
                { Elevation = elevation, Thickness = -2.5 },
            "trace" => new Trace(new Vector2(1, 2), new Vector2(5, 3), new Vector2(2, 8), new Vector2(7, 9))
                { Elevation = elevation, Thickness = -2.5 },
            "shape" => new Shape("CLONE_AUDIT_SHAPE", new ShapeStyle("CLONE_AUDIT_STYLE", "clone-audit-not-installed.shx"))
                { Position = new Vector3(3, -4, elevation), Size = 2.25, Rotation = 37, ObliqueAngle = 15,
                    WidthFactor = new[] { .5, -2.25, 1.375 }[variant], Thickness = -2.5 },
            "polyline" => new Polyline3D(new[] { new Vector3(1, 2, 3), new Vector3(5, 3, 7),
                    new Vector3(2, 8, -2), new Vector3(7, 9, 11) }, variant == 1)
                { SmoothType = new[] { PolylineSmoothType.NoSmooth, PolylineSmoothType.Quadratic, PolylineSmoothType.Cubic }[variant],
                    LinetypeGeneration = true },
            "leader" => new Leader(new[] { new Vector2(1, 2), new Vector2(5, 3), new Vector2(7, 9) })
                { Elevation = elevation, Direction = new[] { new Vector2(3, 4), Vector2.UnitY, new Vector2(-5, -12) }[variant],
                    Offset = new Vector2(2.25, -1.5), LineColor = new AciColor((short)(120 + variant)),
                    ShowArrowhead = false, PathType = LeaderPathType.StraightLineSegments },
            _ => throw new ArgumentException(kind)
        };
        result.Normal = new[] { Vector3.UnitZ, -Vector3.UnitZ, Vector3.UnitX }[variant];
        result.Layer = new Layer("CLONE_AUDIT_LAYER"); result.Color = new AciColor(3);
        result.LinetypeScale = 1.75; result.IsVisible = false;
        result.ProxyGraphics = new byte[] { 19, 83, (byte)variant };
        var data = new XData(new ApplicationRegistry("CLONE_AUDIT"));
        data.XDataRecord.Add(new XDataRecord(XDataCode.String, "clone-state-" + variant));
        result.XData.Add(data);
        return result;
    }

    private static EntityObject CloneReviewWrap(EntityObject leaf, int depth)
    {
        EntityObject root = leaf;
        for (int i = 0; i < depth; i++)
        {
            var block = new Block("CLONE_AUDIT_BLOCK_" + i);
            block.Entities.Add(root);
            root = new Insert(block);
        }
        return root;
    }

    private static EntityObject CloneReviewLeaf(EntityObject root)
    {
        while (root is Insert insert) root = insert.Block.Entities.Single();
        return root;
    }

    private static void CloneReviewEqual(EntityObject original, EntityObject copy, bool serialized = false)
    {
        Equal(original.GetType(), copy.GetType(), "clone runtime kind");
        Equal(original.Layer.Name, copy.Layer.Name, "clone layer");
        Equal(original.Color.Index, copy.Color.Index, "clone color");
        Near(original.LinetypeScale, copy.LinetypeScale, "clone linetype scale");
        Equal(original.Normal, copy.Normal, "clone normal");
        Equal(original.IsVisible, copy.IsVisible, "clone visibility");
        Check(original.ProxyGraphics!.SequenceEqual(copy.ProxyGraphics!), "clone proxy packet");
        Equal((string)original.XData["CLONE_AUDIT"].XDataRecord.Single().Value,
            (string)copy.XData["CLONE_AUDIT"].XDataRecord.Single().Value, "clone XData");
        switch (original)
        {
            case Solid a:
                var b = (Solid)copy;
                Equal(a.Elevation, b.Elevation, "SOLID clone elevation");
                Equal(a.Thickness, b.Thickness, "SOLID clone thickness");
                Equal(a.FirstVertex, b.FirstVertex, "SOLID first corner"); Equal(a.SecondVertex, b.SecondVertex, "SOLID second corner");
                Equal(a.ThirdVertex, b.ThirdVertex, "SOLID third corner"); Equal(a.FourthVertex, b.FourthVertex, "SOLID fourth corner");
                break;
            case Trace a:
                var c = (Trace)copy;
                Equal(a.Elevation, c.Elevation, "TRACE clone elevation");
                Equal(a.Thickness, c.Thickness, "TRACE clone thickness");
                Equal(a.FirstVertex, c.FirstVertex, "TRACE first corner"); Equal(a.SecondVertex, c.SecondVertex, "TRACE second corner");
                Equal(a.ThirdVertex, c.ThirdVertex, "TRACE third corner"); Equal(a.FourthVertex, c.FourthVertex, "TRACE fourth corner");
                break;
            case Shape a:
                var s = (Shape)copy;
                Equal(a.WidthFactor, s.WidthFactor, "SHAPE clone width factor");
                Equal(a.Position, s.Position, "SHAPE position"); Equal(a.Size, s.Size, "SHAPE size");
                Equal(a.Rotation, s.Rotation, "SHAPE rotation"); Equal(a.ObliqueAngle, s.ObliqueAngle, "SHAPE oblique angle");
                Equal(a.Thickness, s.Thickness, "SHAPE thickness"); Equal(a.Name, s.Name, "SHAPE name");
                Equal(a.Style.File, s.Style.File, "SHAPE style file");
                break;
            case Polyline3D a:
                var p = (Polyline3D)copy;
                Equal(a.SmoothType, p.SmoothType, "POLYLINE clone smoothing");
                Equal(a.IsClosed, p.IsClosed, "POLYLINE closed flag");
                Equal(a.LinetypeGeneration, p.LinetypeGeneration, "POLYLINE linetype flag");
                Check(a.Vertexes.SequenceEqual(p.Vertexes), "POLYLINE control vertices");
                break;
            case Leader a:
                var l = (Leader)copy;
                Near(a.Direction.X, l.Direction.X, "LEADER direction X"); Near(a.Direction.Y, l.Direction.Y, "LEADER direction Y");
                if (!serialized)
                {
                    Equal(BitConverter.DoubleToInt64Bits(a.Direction.X), BitConverter.DoubleToInt64Bits(l.Direction.X), "LEADER exact direction X");
                    Equal(BitConverter.DoubleToInt64Bits(a.Direction.Y), BitConverter.DoubleToInt64Bits(l.Direction.Y), "LEADER exact direction Y");
                }
                Equal(a.LineColor.Index, l.LineColor.Index, "LEADER line color");
                Equal(a.Elevation, l.Elevation, "LEADER elevation"); Equal(a.Offset, l.Offset, "LEADER offset");
                Equal(a.ShowArrowhead, l.ShowArrowhead, "LEADER arrow flag"); Equal(a.PathType, l.PathType, "LEADER path type");
                Equal(a.HasHookline, l.HasHookline, "LEADER hookline"); Check(a.Vertexes.SequenceEqual(l.Vertexes), "LEADER vertices");
                break;
        }
    }

    private static void CloneReviewModel(string kind, int variant, int depth, bool registered)
    {
        EntityObject original = CloneReviewSubject(kind, variant);
        EntityObject root = CloneReviewWrap(original, depth);
        var source = new DxfDocument(); if (registered) source.Entities.Add(root);
        string? handle = original.Handle; object? owner = original.Owner;
        int objects = source.Objects.Items.Count();
        var copyRoot = (EntityObject)root.Clone(); EntityObject copy = CloneReviewLeaf(copyRoot);
        CloneReviewEqual(original, copy);
        Check(copy.Handle == null && copyRoot.Handle == null && copyRoot.Owner == null, "clone retained source root identity");
        Check(!ReferenceEquals(original, copy) && !ReferenceEquals(original.Layer, copy.Layer), "clone shares mutable object or layer");
        Check(!ReferenceEquals(original.Color, copy.Color) && !ReferenceEquals(original.XData["CLONE_AUDIT"], copy.XData["CLONE_AUDIT"]), "clone shares mutable common data");
        if (original is Leader leader)
        {
            var cloned = (Leader)copy;
            Check(!ReferenceEquals(leader.LineColor, cloned.LineColor), "LEADER clone shares mutable line color");
            cloned.LineColor.Index = 2; Equal((short)(120 + variant), leader.LineColor.Index, "clone color edit changed source");
        }
        if (original is Polyline3D polyline)
        {
            var cloned = (Polyline3D)copy;
            cloned.Vertexes[0] = Vector3.Zero; Equal(new Vector3(1, 2, 3), polyline.Vertexes[0], "clone vertex edit changed source");
            cloned.SmoothType = PolylineSmoothType.NoSmooth;
            Equal(new[] { PolylineSmoothType.NoSmooth, PolylineSmoothType.Quadratic, PolylineSmoothType.Cubic }[variant], polyline.SmoothType, "clone smoothing edit changed source");
        }
        if (original is Shape shape)
        {
            var cloned = (Shape)copy; Check(!ReferenceEquals(shape.Style, cloned.Style), "SHAPE style shared");
            cloned.WidthFactor = 4; Equal(new[] { .5, -2.25, 1.375 }[variant], shape.WidthFactor, "clone width edit changed source");
        }
        copy.ClearProxyGraphics(); Check(original.ProxyGraphics != null, "clone proxy clear changed source");
        copy.XData["CLONE_AUDIT"].XDataRecord.Clear(); Equal(1, original.XData["CLONE_AUDIT"].XDataRecord.Count, "clone XData edit changed source");
        Equal(handle, original.Handle, "clone changed source handle"); Check(ReferenceEquals(owner, original.Owner), "clone changed source owner");
        Equal(objects, source.Objects.Items.Count(), "clone changed source database membership");
        Equal(0, source.Objects.Validate().Count, "source graph after clone");
    }

    private static byte[] CloneReviewSave(EntityObject root, DxfVersion version, bool binary, string file)
    {
        var document = new DxfDocument(version); document.Entities.Add(root);
        using var output = new MemoryStream(); Check(document.Save(output, binary), "clone review save");
        Equal(0, document.Objects.Validate().Count, "clone review graph");
        byte[] bytes = output.ToArray(); File.WriteAllBytes(Path.Combine(ArtifactDirectory, file), bytes); return bytes;
    }

    private static void CloneReviewWire(string kind, int variant, int depth, DxfVersion version, bool binary)
    {
        EntityObject original = CloneReviewSubject(kind, variant), root = CloneReviewWrap(original, depth);
        var copy = (EntityObject)root.Clone(); CloneReviewEqual(original, CloneReviewLeaf(copy));
        string stem = $"clone-review-{kind}-{variant}-{depth}-{version}-{binary}";
        byte[] before = CloneReviewSave(root, version, binary, stem + "-source.dxf");
        byte[] after = CloneReviewSave(copy, version, binary, stem + "-clone.dxf");
        using var rawBefore = new MemoryStream(before); using var rawAfter = new MemoryStream(after);
        string[] Records(Stream input) => DxfRawDocument.Load(input).Sections.Where(s => s.Name is "ENTITIES" or "BLOCKS")
            .SelectMany(s => s.Records).SelectMany(r => r.Tags).Where(t => t.Code is not (5 or 330))
            .Select(PolylineRecordTagKey).ToArray();
        Check(Records(rawBefore).SequenceEqual(Records(rawAfter)), "clone changed generated ordered entity/block packets");
        // SHAPE typed loading needs the referenced installed SHX definition. Wire
        // preservation is independently checked without fabricating a native font.
        if (kind != "shape")
        {
            using var input = new MemoryStream(after);
            var loaded = DxfDocument.Load(input) ?? throw new InvalidOperationException("clone review reload");
            CloneReviewEqual(original, CloneReviewLeaf(loaded.Entities.All.Single()), true);
            Equal(0, loaded.Objects.Validate().Count, "reloaded clone graph");
        }
    }

    private static void CloneReviewAnnotation(int variant)
    {
        var original = (Leader)CloneReviewSubject("leader", variant);
        original.Annotation = new MText("independent annotation", new Vector3(3, 5, 7), 2, 15);
        original.HasHookline = true; original.Direction = new Vector2(-2, 3);
        original.StyleOverrides.Add(new DimensionStyleOverride(DimensionStyleOverrideType.DimLineColor, new AciColor(5)));
        var copy = (Leader)original.Clone(); CloneReviewEqual(original, copy);
        Check(!ReferenceEquals(original.Annotation, copy.Annotation), "leader annotation shared");
        Check(copy.Annotation.Reactors.Any(x => ReferenceEquals(x, copy)) && !copy.Annotation.Reactors.Any(x => ReferenceEquals(x, original)), "cloned annotation reactor not remapped");
        Check(original.Annotation.Reactors.Any(x => ReferenceEquals(x, original)) && !original.Annotation.Reactors.Any(x => ReferenceEquals(x, copy)), "source annotation reactors changed");
        var color = (AciColor)copy.StyleOverrides[DimensionStyleOverrideType.DimLineColor].Value; color.Index = 1;
        Equal((short)5, ((AciColor)original.StyleOverrides[DimensionStyleOverrideType.DimLineColor].Value).Index, "override color shared");
    }

    private static void CloneReviewDirections()
    {
        var random = new Random(171069);
        for (int i = 0; i < 512; i++)
        {
            var source = new Leader(new[] { Vector2.Zero, Vector2.UnitX })
                { Direction = new Vector2(random.NextDouble() - .5, random.NextDouble() - .5), LineColor = new AciColor(17, 83, 149) };
            var copy = (Leader)source.Clone();
            Equal(BitConverter.DoubleToInt64Bits(source.Direction.X), BitConverter.DoubleToInt64Bits(copy.Direction.X), "normalized direction X renormalized on clone");
            Equal(BitConverter.DoubleToInt64Bits(source.Direction.Y), BitConverter.DoubleToInt64Bits(copy.Direction.Y), "normalized direction Y renormalized on clone");
            Equal(AciColor.ToTrueColor(source.LineColor), AciColor.ToTrueColor(copy.LineColor), "true-color clone");
            Check(!ReferenceEquals(source.LineColor, copy.LineColor), "true-color clone alias");
        }
    }

    private static void CloneReviewRetained(DxfVersion version, bool binary)
    {
        // Existing producer fixture carries external DIMASSOC/reactor dependencies.
        // The clone correction must not relax complete-graph admission.
        var document = StoredDimAssocLoad(StoredDimAssocInput(version, binary));
        var polyline = (Polyline3D)document.GetObjectByHandle(StoredDimAssocHandle(version, "41A"));
        int count = document.Objects.Items.Count(); string handle = polyline.Handle;
        Throws<NotSupportedException>(() => polyline.Clone());
        Throws<NotSupportedException>(() => ((Block)polyline.Owner).Clone("CLONE_REVIEW_REJECT"));
        Equal(count, document.Objects.Items.Count(), "rejected clone changed object count");
        Check(ReferenceEquals(polyline, document.GetObjectByHandle(handle)), "rejected clone changed source identity");
        Equal(0, document.Objects.Validate().Count, "rejected clone changed graph validity");
    }
}
