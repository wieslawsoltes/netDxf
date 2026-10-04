// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System.Reflection;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.IO;
using netDxf.Tables;
using Attribute = netDxf.Entities.Attribute;

namespace NetDxf.Conformance;
internal static partial class Program
{
    private static void RegisterR12AttributeTests()
    {
        foreach (bool binary in new[] { false, true })
        {
            foreach (TextAlignment alignment in Enum.GetValues<TextAlignment>())
                Run($"r12-attributes/alignment/{binary}/{alignment}", () => RaRoundTrip(binary, alignment, 0));
            for (int flags = 0; flags < 16; flags++)
            { int f = flags; Run($"r12-attributes/flags/{binary}/{f}", () => RaRoundTrip(binary, TextAlignment.BaselineLeft, f)); }
            for (int fault = 0; fault < 12; fault++)
            { int f = fault; Run($"r12-attributes/read-refusal/{binary}/{f}", () => RaReadRefusal(binary, f)); }
            Run("r12-attributes/orphan/" + binary, () => RaOrphan(binary));
            Run("r12-attributes/empty-sequence/" + binary, () => RaEmpty(binary));
            Run("r12-attributes/no-manufactured-constant/" + binary, () => RaConstant(binary));
            Run("r12-attributes/wire/" + binary, () => RaWire(binary));
        }
        for (int fault = 0; fault < 9; fault++)
        { int f = fault; Run("r12-attributes/write-refusal/" + f, () => RaWriteRefusal(f)); }
        Run("r12-attributes/source-sequence-not-materialized", RaNonMutating);
        Run("r12-attributes/registered-graph-unchanged", RaRegistered);
    }

    private static Insert RaSeed(TextAlignment alignment = TextAlignment.BaselineLeft, int flags = 0)
    {
        var style = new TextStyle("ATTR_STYLE", "txt.shx") { WidthFactor = .8, ObliqueAngle = 5 };
        var layer = new Layer("ATTR_ONLY") { Color = new AciColor(4), IsFrozenInNewViewports = true };
        var block = new Block("ATTR_BLOCK") { Origin = new Vector3(3, 4, 5) };
        var definition = new AttributeDefinition("IDENTIFIER", 2.5, style)
        {
            Prompt = "Café ^ prompt\tentry", Value = "Default ^ text", Flags = (AttributeFlags)flags,
            Position = new Vector3(8, 9, 10), Normal = R12Normal(flags % 4),
            Alignment = alignment, Rotation = 32, Width = alignment == TextAlignment.Aligned || alignment == TextAlignment.Fit ? 7.5 : 1,
            WidthFactor = .9, ObliqueAngle = -13, IsBackward = (flags & 1) != 0, IsUpsideDown = (flags & 2) != 0,
            Layer = layer, Linetype = Linetype.ByBlock, Color = new AciColor(5)
        };
        block.AttributeDefinitions.Add(definition);
        block.Entities.Add(new Line(Vector3.Zero, Vector3.UnitX));
        var root = new Insert(block, new Vector3(20, 30, 40))
            { Rotation = 71, Scale = new Vector3(-2, 3, .5), ColumnCount = 3, RowCount = 2, ColumnSpacing = -7, RowSpacing = 9 };
        var value = root.Attributes.Single();
        value.Position = new Vector3(-4, 8, 12); value.Normal = R12Normal((flags + 1) % 4);
        value.Rotation = 133; value.Alignment = alignment; value.Height = 3.5;
        value.Width = alignment == TextAlignment.Aligned || alignment == TextAlignment.Fit ? 11.25 : 1;
        value.WidthFactor = 1.2; value.ObliqueAngle = 17; value.Value = "Actual café\t^\nvalue";
        value.IsBackward = true; value.IsUpsideDown = false;
        return root;
    }

    private static void RaSame(AttributeDefinition expected, AttributeDefinition actual)
    {
        Equal(expected.Tag, actual.Tag, "ATTDEF tag"); Equal(expected.Prompt, actual.Prompt, "ATTDEF prompt");
        Equal(expected.Value, actual.Value, "ATTDEF default"); Equal(expected.Flags, actual.Flags, "ATTDEF flags");
        R12Vector(expected.Position, actual.Position); R12Vector(expected.Normal, actual.Normal);
        Near(expected.Height, actual.Height, "ATTDEF height"); Near(expected.Rotation, actual.Rotation, "ATTDEF rotation");
        Near(expected.WidthFactor, actual.WidthFactor, "ATTDEF width factor"); Near(expected.ObliqueAngle, actual.ObliqueAngle, "ATTDEF oblique");
        if (expected.Alignment == TextAlignment.Aligned || expected.Alignment == TextAlignment.Fit) Near(expected.Width, actual.Width, "ATTDEF two-point width");
        Equal(expected.Alignment, actual.Alignment, "ATTDEF alignment"); Equal(expected.IsBackward, actual.IsBackward, "ATTDEF backward");
        Equal(expected.IsUpsideDown, actual.IsUpsideDown, "ATTDEF upside down"); Equal(expected.Layer.Name, actual.Layer.Name, "ATTDEF layer");
        Equal(expected.Color.Index, actual.Color.Index, "ATTDEF color"); Equal(expected.Style.Name, actual.Style.Name, "ATTDEF style");
    }

    private static void RaSame(Attribute expected, Attribute actual)
    {
        Equal(expected.Tag, actual.Tag, "ATTRIB tag"); Equal(expected.Value, actual.Value, "ATTRIB value"); Equal(expected.Flags, actual.Flags, "ATTRIB flags");
        R12Vector(expected.Position, actual.Position); R12Vector(expected.Normal, actual.Normal);
        Near(expected.Height, actual.Height, "ATTRIB height"); Near(expected.Rotation, actual.Rotation, "ATTRIB rotation");
        Near(expected.WidthFactor, actual.WidthFactor, "ATTRIB width factor"); Near(expected.ObliqueAngle, actual.ObliqueAngle, "ATTRIB oblique");
        if (expected.Alignment == TextAlignment.Aligned || expected.Alignment == TextAlignment.Fit) Near(expected.Width, actual.Width, "ATTRIB two-point width");
        Equal(expected.Alignment, actual.Alignment, "ATTRIB alignment"); Equal(expected.IsBackward, actual.IsBackward, "ATTRIB backward");
        Equal(expected.IsUpsideDown, actual.IsUpsideDown, "ATTRIB upside down"); Equal(expected.Layer.Name, actual.Layer.Name, "ATTRIB layer");
        Equal(expected.Color.Index, actual.Color.Index, "ATTRIB color"); Equal(expected.Style.Name, actual.Style.Name, "ATTRIB style");
    }

    private static void RaRoundTrip(bool binary, TextAlignment alignment, int flags)
    {
        var root = RaSeed(alignment, flags);
        var outer = new Block("ATTR_OUTER"); outer.Entities.Add(root);
        var seed = new Insert(outer, new Vector3(100, 200, 300));
        var raw = DxfR12Codec.Create(new[] { seed }, binary); var before = raw.Tags.ToArray();
        var decoded = (Insert)DxfR12Codec.ReadEntities(R12Reload(raw, binary)).Single();
        var child = (Insert)decoded.Block.Entities.Single();
        var definition = child.Block.AttributeDefinitions.Values.Single(); var attribute = child.Attributes.Single();
        RaSame(root.Block.AttributeDefinitions.Values.Single(), definition); RaSame(root.Attributes.Single(), attribute);
        Check(ReferenceEquals(attribute.Definition, definition), "ATTRIB lost canonical definition identity");
        Check(ReferenceEquals(attribute.Owner, child) && ReferenceEquals(definition.Owner, child.Block), "Attribute graph owner differs");
        Check(ReferenceEquals(attribute.Layer, definition.Layer) && ReferenceEquals(attribute.Style, definition.Style), "Attribute resource identity split");
        Check(attribute.Layer.IsFrozenInNewViewports, "Attribute-only layer default lost");
        Check(attribute.Handle != null && definition.Handle != null && child.EndSequenceRecord?.Handle != null, "Attribute sequence identities lost");
        Check(before.SequenceEqual(raw.Tags), "Attribute read changed raw source");
        var again = (Insert)DxfR12Codec.ReadEntities(R12Reload(DxfR12Codec.Create(new[] { decoded }, !binary), !binary)).Single();
        RaSame(attribute, ((Insert)again.Block.Entities.Single()).Attributes.Single());
        Check(root.Handle == null && root.Attributes.Single().Handle == null && root.Block.Handle == null, "Attribute export adopted source graph");
    }

    private static void RaReadRefusal(bool binary, int fault)
    {
        var raw = DxfR12Codec.Create(new[] { RaSeed() }, binary);
        string type = fault < 4 ? "ATTDEF" : fault < 10 ? "ATTRIB" : "SEQEND";
        var record = raw.Sections.SelectMany(s => s.Records).Single(r => r.Name == type);
        var tags = record.Tags.ToList();
        void Set(short code, object value) { tags.RemoveAll(t => t.Code == code); tags.Add(new DxfTag(code, value)); }
        switch (fault)
        {
            case 0: Set(2, "BAD TAG"); break;
            case 1: Set(70, (short)16); break;
            case 2: Set(73, (short)4); break;
            case 3: Set(74, (short)8); break;
            case 4: Set(2, "BAD!TAG"); break;
            case 5: Set(70, (short)-1); break;
            case 6: Set(40, 0.0); break;
            case 7: Set(3, "unexpected prompt"); break;
            case 8: tags.Add(new DxfTag(2, "SECOND")); break;
            case 9:
                Set(5, (string)raw.Sections.SelectMany(s => s.Records).Single(r => r.Name == "ATTDEF").Tags.Single(t => t.Code == 5).Value); break;
            case 10: Set(67, (short)1); break;
            case 11: tags[0] = new DxfTag(0, "POINT"); Set(10, 0.0); Set(20, 0.0); break;
        }
        var edited = R12Reload(raw.WithRecord(record, tags), binary); byte[] snapshot = R12Bytes(edited, binary);
        R12Refuses(() => DxfR12Codec.ReadEntities(edited));
        Check(snapshot.SequenceEqual(R12Bytes(edited, binary)), "Rejected attribute packet changed input");
    }

    private static void RaWriteRefusal(int fault)
    {
        var seed = RaSeed(); var definition = seed.Block.AttributeDefinitions.Values.Single(); var attribute = seed.Attributes.Single();
        switch (fault)
        {
            case 0: attribute.Flags = (AttributeFlags)16; break;
            case 1: definition.Flags = (AttributeFlags)32; break;
            case 2: attribute.ColorName = "Explicit modern name"; break;
            case 3: definition.ColorName = ""; break;
            case 4: attribute.Lineweight = Lineweight.W25; break;
            case 5: attribute.XData.Add(new XData(new ApplicationRegistry("ATTR_APP"))); break;
            case 6: definition.XData.Add(new XData(new ApplicationRegistry("DEF_APP"))); break;
            case 7: seed.EndSequenceRecord!.XData.Add(new XData(new ApplicationRegistry("END_APP"))); break;
            case 8: definition.Prompt = "Unsupported \u2603"; break;
        }
        using var output = new MemoryStream(); output.WriteByte(91); output.Position = 0;
        R12Refuses(() => DxfR12Codec.Save(output, new[] { seed }));
        Check(output.Position == 0 && output.Length == 1 && output.ToArray()[0] == 91, "Late attribute failure touched destination");
        Check(seed.Handle == null && attribute.Handle == null && definition.Handle == null, "Rejected attribute export assigned identities");
    }

    private static void RaOrphan(bool binary)
    {
        var raw = DxfR12Codec.Create(new[] { RaSeed() }, binary);
        var record = raw.Sections.SelectMany(s => s.Records).Single(r => r.Name == "ATTRIB");
        raw = raw.WithRecord(record, record.Tags.Select(t => t.Code == 2 ? new DxfTag(2, "ORPHAN") : t));
        var root = (Insert)DxfR12Codec.ReadEntities(R12Reload(raw, binary)).Single();
        Check(root.Attributes.Single().Definition == null, "Orphan attribute acquired a fabricated definition");
        var roundTrip = (Insert)DxfR12Codec.ReadEntities(DxfR12Codec.Create(new[] { root }, !binary)).Single();
        Check(roundTrip.Attributes.Single().Definition == null && roundTrip.Attributes.Single().Tag == "ORPHAN", "Orphan attribute was discarded");
    }

    private static void RaEmpty(bool binary)
    {
        var root = new Insert(new Block("EMPTY_ATTRIBUTES"));
        typeof(Insert).GetMethod("EnsureSequenceEnd", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(root, null);
        var result = (Insert)DxfR12Codec.ReadEntities(R12Reload(DxfR12Codec.Create(new[] { root }, binary), binary)).Single();
        Equal(0, result.Attributes.Count, "Empty sequence acquired an attribute");
        Check(result.EndSequenceRecord != null, "Explicit empty SEQEND lost");
        var again = (Insert)DxfR12Codec.ReadEntities(DxfR12Codec.Create(new[] { result }, !binary)).Single();
        Check(again.EndSequenceRecord != null && again.Attributes.Count == 0, "Empty sequence resave lost framing");
    }

    private static void RaConstant(bool binary)
    {
        var block = new Block("CONSTANT_ONLY"); var root = new Insert(block);
        block.AttributeDefinitions.Add(new AttributeDefinition("CONST") { Flags = AttributeFlags.Constant, Value = "constant" });
        var result = (Insert)DxfR12Codec.ReadEntities(R12Reload(DxfR12Codec.Create(new[] { root }, binary), binary)).Single();
        Equal(1, result.Block.AttributeDefinitions.Count, "Constant definition lost");
        Equal(0, result.Attributes.Count, "Reader manufactured an ATTRIB from a constant definition");
        Check(result.EndSequenceRecord == null, "Reader manufactured an attribute sequence");
    }

    private static void RaNonMutating()
    {
        var root = RaSeed();
        FieldInfo field = typeof(Insert).GetField("sequenceEnd", BindingFlags.Instance | BindingFlags.NonPublic)!;
        field.SetValue(root, null);
        var values = root.Attributes.Select(a => (a.Position, a.Normal, a.Value, a.Handle)).ToArray();
        DxfR12Codec.Create(new[] { root });
        Check(field.GetValue(root) == null, "Export materialized the source's lazy SEQEND");
        Check(values.SequenceEqual(root.Attributes.Select(a => (a.Position, a.Normal, a.Value, a.Handle))), "Export transformed or changed source attributes");
    }

    private static void RaRegistered()
    {
        var document = new DxfDocument(); var root = RaSeed(); document.Entities.Add(root);
        var before = new CompatibilityState(document); string? end = root.EndSequenceRecord!.Handle;
        DxfR12Codec.Create(new[] { root }); before.CheckUnchanged();
        Check(root.EndSequenceRecord!.Handle == end, "Registered attribute SEQEND identity changed");
    }

    private static void RaWire(bool binary)
    {
        Insert root = RaSeed(TextAlignment.MiddleCenter, 5);
        for (int stage = 0; stage < 3; stage++)
        {
            bool transport = stage == 1 ? !binary : binary;
            var raw = DxfR12Codec.Create(new[] { root }, transport);
            string suffix = new[] { "source", "output", "resave" }[stage];
            File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"r12-attributes-{(binary ? "binary" : "text")}-{suffix}.dxf"), R12Bytes(raw, transport));
            root = (Insert)DxfR12Codec.ReadEntities(R12Reload(raw, transport)).Single();
            if (stage == 0) { root.Attributes.Single().Value = "Edited café"; root.Attributes.Single().Position = new Vector3(6, 7, 8); }
        }
    }
}
