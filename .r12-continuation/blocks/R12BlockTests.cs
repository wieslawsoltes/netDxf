// Copyright (c) netDxf contributors. Licensed under the MIT License.
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.IO;
using netDxf.Tables;
using netDxf.Units;

namespace NetDxf.Conformance;
internal static partial class Program
{
    private static void RegisterR12BlockTests()
    {
        foreach (bool binary in new[] { false, true })
        {
            for (int normal = 0; normal < 4; normal++)
            for (int scale = 0; scale < 4; scale++)
            {
                int n = normal, s = scale;
                Run($"r12-blocks/graph/{binary}/{n}/{s}", () => RbGraph(binary, n, s));
            }
            for (int fault = 0; fault < 13; fault++)
            { int f = fault; Run($"r12-blocks/read-refusal/{binary}/{f}", () => RbReadRefusal(binary, f)); }
            Run("r12-blocks/deep/" + binary, () => RbDeep(binary));
            Run("r12-blocks/wire/" + binary, () => RbWire(binary));
            Run("r12-blocks/loaded-zero-scale/" + binary, () => RbZeroScale(binary));
            Run("r12-blocks/shared-resource-edit/" + binary, () => RbResourceEdit(binary));
        }
        for (int fault = 0; fault < 8; fault++)
        { int f = fault; Run("r12-blocks/write-refusal/" + f, () => RbWriteRefusal(f)); }
        Run("r12-blocks/budget-and-late-failure", RbBudget);
        Run("r12-blocks/registered-source-unchanged", RbRegistered);
    }

    private static Insert[] RbSeeds(int normal = 0, int scale = 0)
    {
        var blockLayer = new Layer("BLOCK_ONLY") { Color = new AciColor(3) };
        var leaf = new Block("LEAF") { Origin = new Vector3(2, 3, 4), Layer = blockLayer };
        leaf.Record.Units = DrawingUnits.Unitless;
        foreach (var entity in R12Seeds(0)) leaf.Entities.Add(entity);
        var outer = new Block("OUTER") { Origin = new Vector3(-2, 7, 1), Layer = blockLayer };
        outer.Record.Units = DrawingUnits.Unitless;
        outer.Entities.Add(new Insert(leaf, new Vector3(5, 6, 7)) { Rotation = 30, Scale = new Vector3(2, -3, .5) });
        outer.Entities.Add(new Insert(leaf, new Vector3(-5, 8, 2)) { Rotation = 170, ColumnCount = 3, RowCount = 2, ColumnSpacing = -7, RowSpacing = 9 });
        Vector3[] scales = { new(1, 1, 1), new(-1, 2, 3), new(.125, -.5, 4), new(-2, -3, -4) };
        return new[] {
            new Insert(outer, new Vector3(10, 20, 30)) { Normal = R12Normal(normal), Scale = scales[scale], Rotation = 75,
                ColumnCount = 4, RowCount = 3, ColumnSpacing = -12.5, RowSpacing = 8.25 },
            new Insert(outer, new Vector3(40, 50, 60)) { Color = new AciColor(6), Linetype = Linetype.ByBlock }
        };
    }

    private static void RbSameInsert(Insert expected, Insert actual)
    {
        Equal(expected.Block.Name, actual.Block.Name, "Block reference name");
        R12Vector(expected.Position, actual.Position); R12Vector(expected.Scale, actual.Scale); R12Vector(expected.Normal, actual.Normal);
        Near(expected.Rotation, actual.Rotation, "INSERT rotation");
        Equal(expected.ColumnCount, actual.ColumnCount, "INSERT columns"); Equal(expected.RowCount, actual.RowCount, "INSERT rows");
        Near(expected.ColumnSpacing, actual.ColumnSpacing, "INSERT column spacing"); Near(expected.RowSpacing, actual.RowSpacing, "INSERT row spacing");
        Equal(expected.Color.Index, actual.Color.Index, "INSERT color");
        Check(string.Equals(expected.Linetype.Name, actual.Linetype.Name, StringComparison.OrdinalIgnoreCase), "INSERT linetype");
        Equal(expected.Attributes.Count, actual.Attributes.Count, "INSERT manufactured attributes");
    }

    private static void RbGraph(bool binary, int normal, int scale)
    {
        var seeds = RbSeeds(normal, scale);
        Block sourceOuter = seeds[0].Block;
        var sourceChildren = sourceOuter.Entities.Cast<Insert>().ToArray();
        Block sourceLeaf = sourceChildren[0].Block;
        var raw = DxfR12Codec.Create(seeds, binary);
        Check(seeds.All(e => e.Handle == null && e.Owner == null), "Export adopted root INSERTs");
        Check(sourceOuter.Handle == null && sourceLeaf.Handle == null && sourceChildren.All(e => e.Handle == null), "Export assigned block graph identities");
        var snapshot = raw.Tags.ToArray();
        var roots = DxfR12Codec.ReadEntities(R12Reload(raw, binary)).Cast<Insert>().ToArray();
        Equal(2, roots.Length, "Selected root inventory");
        Check(ReferenceEquals(roots[0].Block, roots[1].Block), "Shared root block duplicated");
        for (int i = 0; i < 2; i++) RbSameInsert(seeds[i], roots[i]);
        Block outer = roots[0].Block;
        R12Vector(sourceOuter.Origin, outer.Origin);
        var children = outer.Entities.Cast<Insert>().ToArray();
        Equal(2, children.Length, "Nested INSERT inventory");
        Check(ReferenceEquals(children[0].Block, children[1].Block), "Shared leaf block duplicated");
        for (int i = 0; i < 2; i++)
        { RbSameInsert(sourceChildren[i], children[i]); Check(ReferenceEquals(children[i].Owner, outer), "Nested entity lost block ownership"); }
        Block leaf = children[0].Block;
        R12Vector(sourceLeaf.Origin, leaf.Origin);
        var expected = sourceLeaf.Entities.ToArray(); var actual = leaf.Entities.ToArray();
        Equal(expected.Length, actual.Length, "Leaf primitive inventory");
        for (int i = 0; i < expected.Length; i++) R12Compare(expected[i], actual[i]);
        Check(actual.All(e => ReferenceEquals(e.Layer, actual[0].Layer)), "Block-local shared layer duplicated");
        Check(ReferenceEquals(outer.Layer, leaf.Layer), "Block-only shared layer duplicated");
        Equal(DrawingUnits.Unitless, leaf.Record.Units, "R12 block units");
        Check(snapshot.SequenceEqual(raw.Tags), "Reading modified immutable source tags");
        Equal(2, raw.Sections.SelectMany(s => s.Records).Count(r => r.Name == "BLOCK"), "BLOCK inventory expanded or duplicated");
        Equal(4, raw.Sections.SelectMany(s => s.Records).Count(r => r.Name == "INSERT"), "INSERT arrays expanded unexpectedly");
    }

    private static void RbReadRefusal(bool binary, int fault)
    {
        var raw = DxfR12Codec.Create(RbSeeds(), binary);
        var rows = raw.Sections.SelectMany(s => s.Records).ToArray();
        var record = fault < 5 ? rows.First(r => r.Name == "INSERT") : fault == 11 ? rows.First(r => r.Name == "ENDBLK") : rows.First(r => r.Name == "BLOCK");
        var tags = record.Tags.ToList();
        void Set(short code, object value)
        { tags.RemoveAll(t => t.Code == code); tags.Add(new DxfTag(code, value)); }
        switch (fault)
        {
            case 0: Set(2, "MISSING"); break;
            case 1: Set(70, (short)0); break;
            case 2: Set(71, (short)-1); break;
            case 3: Set(66, (short)2); break;
            case 4: Set(39, 1.0); break;
            case 5: Set(70, (short)4); break;
            case 6: Set(3, "OTHER"); break;
            case 7: Set(1, "external.dwg"); break;
            case 8: Set(4, "modern-only description"); break;
            case 9: tags.Add(new DxfTag(2, "DUPLICATE")); break;
            case 10: Set(5, (string)rows.First(r => r.Name == "INSERT").Tags.Single(t => t.Code == 5).Value); break;
            case 11: Set(8, "OTHER_LAYER"); break;
            case 12: Set(70, (short)1); break;
        }
        var edited = raw.WithRecord(record, tags);
        edited = R12Reload(edited, binary);
        byte[] before = R12Bytes(edited, binary);
        R12Refuses(() => DxfR12Codec.ReadEntities(edited));
        Check(before.SequenceEqual(R12Bytes(edited, binary)), "Refused block graph changed source");
    }

    private static void RbWriteRefusal(int fault)
    {
        var roots = RbSeeds(); var outer = roots[0].Block;
        switch (fault)
        {
            case 0: outer.Description = "Must not silently drop"; break;
            case 1: outer.Record.Units = DrawingUnits.Millimeters; break;
            case 2: outer.Record.AllowExploding = false; break;
            case 3: outer.Record.ScaleUniformly = true; break;
            case 4: outer.Entities.Add(new Ellipse(Vector3.Zero, 4, 2)); break;
            case 5: outer.XData.Add(new XData(new ApplicationRegistry("APP"))); break;
            case 6: roots[1] = new Insert(new Block("outer"), Vector3.Zero); break;
            case 7: outer.Entities.Add(new Line(Vector3.Zero, Vector3.UnitX) { Layer = new Layer("BLOCK_ONLY") { Color = new AciColor(2) } }); break;
        }
        using var output = new MemoryStream(); output.WriteByte(91); output.Position = 0;
        R12Refuses(() => DxfR12Codec.Save(output, roots));
        Check(output.Length == 1 && output.Position == 0 && output.ToArray()[0] == 91, "Late block validation touched destination");
        Check(outer.Handle == null && roots.All(r => r.Handle == null), "Refused block graph assigned identities");
    }

    private static void RbDeep(bool binary)
    {
        var leaf = new Block("D0"); leaf.Entities.Add(new Line(Vector3.Zero, Vector3.UnitX));
        Block next = leaf;
        const int depth = 512;
        for (int i = 1; i < depth; i++)
        { var parent = new Block("D" + i); parent.Entities.Add(new Insert(next, Vector3.Zero)); next = parent; }
        var raw = DxfR12Codec.Create(new[] { new Insert(next, Vector3.Zero) }, binary);
        var root = (Insert)DxfR12Codec.ReadEntities(R12Reload(raw, binary)).Single();
        Block node = root.Block;
        for (int i = depth - 1; i > 0; i--)
        { Equal("D" + i, node.Name, "Deep graph node"); node = ((Insert)node.Entities.Single()).Block; }
        Equal("D0", node.Name, "Deep leaf");
        // Change the leaf primitive into a reference to the root without altering framing.
        var line = raw.Sections.SelectMany(s => s.Records).Single(r => r.Name == "LINE");
        var cycle = raw.WithRecord(line, new DxfTag[] { new(0, "INSERT"), new(2, "D511"), new(10, 0.0), new(20, 0.0), new(30, 0.0) });
        R12Refuses(() => DxfR12Codec.ReadEntities(cycle));
    }

    private static void RbZeroScale(bool binary)
    {
        var raw = DxfR12Codec.Create(RbSeeds(), binary);
        var record = raw.Sections.Single(s => s.Name == "ENTITIES").Records.First();
        raw = raw.WithRecord(record, record.Tags.Select(t => t.Code == 41 ? new DxfTag(41, 0.0) : t));
        var roots = DxfR12Codec.ReadEntities(R12Reload(raw, binary));
        Equal(0.0, ((Insert)roots[0]).Scale.X, "Loaded zero scale rejected or repaired");
        var output = DxfR12Codec.ReadEntities(DxfR12Codec.Create(roots, !binary));
        Equal(0.0, ((Insert)output[0]).Scale.X, "Loaded zero scale not preserved");
    }

    private static void RbResourceEdit(bool binary)
    {
        var roots = DxfR12Codec.ReadEntities(DxfR12Codec.Create(RbSeeds(), binary)).Cast<Insert>().ToArray();
        var children = roots[0].Block.Entities.Cast<Insert>().ToArray();
        var line = (Line)children[0].Block.Entities.First(); line.StartPoint = new Vector3(9, 8, 7);
        children[0].Block.Layer.IsFrozenInNewViewports = true;
        var result = DxfR12Codec.ReadEntities(R12Reload(DxfR12Codec.Create(roots, !binary), !binary)).Cast<Insert>().ToArray();
        var nested = result[1].Block.Entities.Cast<Insert>().ToArray();
        Check(ReferenceEquals(nested[0].Block, nested[1].Block), "Edit split shared block identity");
        R12Vector(new Vector3(9, 8, 7), ((Line)nested[1].Block.Entities.First()).StartPoint);
        Check(nested[0].Block.Layer.IsFrozenInNewViewports, "Block-only layer default edit lost");
    }

    private static void RbBudget()
    {
        using var output = new MemoryStream(); output.WriteByte(77); output.Position = 0;
        Throws<InvalidDataException>(() => DxfR12Codec.Save(output, RbSeeds(), false, new DxfRawOptions(1000000, 70, 1024)));
        Check(output.Position == 0 && output.Length == 1, "Nested block tag budget bypassed staging");
    }

    private static void RbRegistered()
    {
        var document = new DxfDocument(); var roots = RbSeeds(); foreach (var root in roots) document.Entities.Add(root);
        var before = new CompatibilityState(document); var handles = roots.Select(r => r.Handle).ToArray();
        DxfR12Codec.Create(roots); before.CheckUnchanged();
        for (int i = 0; i < roots.Length; i++) Check(ReferenceEquals(document.GetObjectByHandle(handles[i]), roots[i]), "Registered root identity changed");
    }

    private static void RbWire(bool binary)
    {
        IReadOnlyList<EntityObject> entities = RbSeeds();
        for (int stage = 0; stage < 3; stage++)
        {
            bool transport = stage == 1 ? !binary : binary;
            var raw = DxfR12Codec.Create(entities, transport);
            string suffix = new[] { "source", "output", "resave" }[stage];
            File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"r12-blocks-{(binary ? "binary" : "text")}-{suffix}.dxf"), R12Bytes(raw, transport));
            entities = DxfR12Codec.ReadEntities(R12Reload(raw, transport));
            if (stage == 0) ((Insert)entities[0]).Position = new Vector3(13, 24, 35);
        }
    }
}
