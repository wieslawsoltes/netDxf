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
    private static void RegisterBlockExchangeTests()
    {
        foreach (string active in new[] { "Model", "Sheet" })
            Run("block-exchange/create-keeps-source-associations/" + active, () => BlockCreateKeepsSource(active));
        foreach (DxfVersion version in SupportedVersions)
        foreach (bool binary in new[] { false, true })
            Run($"block-exchange/stream/{version}/{binary}", () => BlockStreamRoundTrip(version, binary));
        Run("block-exchange/detached-export", BlockExportDetached);
        foreach (bool binary in new[] { false, true })
        {
            Run("block-exchange/linked-columns/" + binary, () =>
            {
                var text = new MText("abcdef") { Columns = ColumnModel(0, MTextColumnStorage.Embedded) };
                var linked = text.ConvertToLinkedColumns(new[] { "ab", "cd", "ef" });
                var block = new Block("COLUMNS"); block.Entities.AddRange(linked);
                using var stream = new MemoryStream(); Check(block.Save(stream, DxfVersion.AutoCad2013, binary), "Column save failed");
                stream.Position = 0; var loaded = Block.Load(stream, "IMPORTED_COLUMNS");
                var root = loaded.Entities.OfType<MText>().Single(t => t.Columns != null);
                Check(root.Columns!.LinkedColumns.All(c => c.Owner == loaded && loaded.Entities.Contains(c)), "Imported columns refer outside the block");
                Equal("abcdef", root.ConvertToEmbeddedColumns().Value, "Column text changed");
                Check(linked.All(c => c.Owner == block), "Source column ownership changed");
            });
            Run("block-exchange/multiline-attributes/" + binary, () =>
            {
                var (_, source) = AmSeed(); var parent = new Block("LIBRARY");
                parent.Entities.Add((Insert)source.Clone());
                parent.AttributeDefinitions.Add((AttributeDefinition)source.Block.AttributeDefinitions["VALUE"].Clone());
                using var stream = new MemoryStream(); Check(parent.Save(stream, DxfVersion.AutoCad2018, binary), "Multiline save failed");
                stream.Position = 0; var imported = Block.Load(stream, "IMPORTED");
                var actual = imported.Entities.OfType<Insert>().Single().Attributes.Single();
                var expected = source.Attributes.Single().GetTextData(); expected.Version ??= 0;
                Equal(AtStamp(expected), AtStamp(actual.GetTextData()), "Embedded instance packet");
                Equal(source.Attributes.Single().Value, actual.Value, "Independent instance fallback");
                var definition = source.Block.AttributeDefinitions["VALUE"].GetTextData(); definition.Version ??= 0;
                Equal(AtStamp(definition), AtStamp(imported.AttributeDefinitions["VALUE"].GetTextData()), "Embedded root definition packet");
                Check(actual.Definition == imported.Entities.OfType<Insert>().Single().Block.AttributeDefinitions["VALUE"], "Multiline definition binding changed");
            });
        }
        Run("block-exchange/input-validation-before-reading", BlockExchangeInputValidation);
        Run("block-exchange/default-text-stream-and-support-folders", () =>
        {
            using var bytes = new MemoryStream();
            Check(BlockExchangeSeed().Save(bytes, DxfVersion.AutoCad2018), "Text save failed");
            bytes.Position = 0;
            var block = Block.Load(bytes, "  IMPORTED  ", new[] { ArtifactDirectory });
            Equal("IMPORTED", block.Name, "Trimmed stream name");
            Check(bytes.CanRead && bytes.CanWrite, "Stream was closed");
        });
        foreach (bool binary in new[] { false, true }) Run("block-exchange/file-api/" + binary, () =>
        {
            string path = Path.Combine(ArtifactDirectory, "block-exchange-file-" + binary + ".dxf");
            var source = BlockExchangeSeed(); Check(source.Save(path, DxfVersion.AutoCad2018, binary), "File save failed");
            var loaded = Block.Load(path);
            Equal(Path.GetFileNameWithoutExtension(path), loaded.Name, "Inferred file name");
            BlockExchangeVerify(loaded, source);
        });
        Run("block-exchange/malformed-load-behavior", () =>
        {
            byte[] malformed = System.Text.Encoding.ASCII.GetBytes("0\nSECTION\n2\nHEADER\n9\n$ACADVER\n1\nAC1032\n0\nENDSEC\n0\nSECTION\n2\nENTITIES\n0\nLINE\n10\nnot-a-number\n0\nENDSEC\n0\nEOF\n");
            using var stream = new MemoryStream(malformed);
            string path = Path.Combine(ArtifactDirectory, "block-exchange-malformed.dxf"); File.WriteAllBytes(path, malformed);
#if DEBUG
            Throws<Exception>(() => Block.Load(stream, "IMPORT"));
            Throws<Exception>(() => Block.Load(path, "IMPORT"));
#else
            Check(Block.Load(stream, "IMPORT") == null, "Malformed stream was accepted");
            Check(Block.Load(path, "IMPORT") == null, "Malformed file did not return null");
#endif
            Check(stream.CanRead, "Failed read closed caller stream");
        });
        Run("block-exchange/unsupported-version", () =>
        {
            using var stream = new MemoryStream(System.Text.Encoding.ASCII.GetBytes("not a DXF file\n"));
            Throws<netDxf.IO.DxfVersionNotSupportedException>(() => Block.Load(stream, "IMPORT"));
            Check(stream.CanRead, "Unsupported version closed caller stream");
        });
    }

    private static void BlockCreateKeepsSource(string active)
    {
        var doc = new DxfDocument();
        doc.DrawingVariables.InsBase = new(7, 8, 9); doc.DrawingVariables.InsUnits = DrawingUnits.Inches;
        var boundary = new Circle(new Vector3(2, 3, 0), 5);
        var first = new Hatch(HatchPattern.Solid, new[] { new HatchBoundaryPath(new[] { boundary }) }, true);
        var second = new Hatch(HatchPattern.Line, new[] { new HatchBoundaryPath(new[] { boundary }) }, true);
        doc.Entities.Add(first); doc.Entities.Add(second);
        doc.Layouts[Layout.ModelSpaceName].AssociatedBlock.AttributeDefinitions.Add(new AttributeDefinition("ROOT") { Value = "default" });
        doc.Layouts.Add(new Layout("Sheet")); doc.Entities.ActiveLayout = "Sheet";
        var paperBoundary = new Circle(Vector3.Zero, 2);
        var paperHatch = new Hatch(HatchPattern.Solid, new[] { new HatchBoundaryPath(new[] { paperBoundary }) }, true);
        doc.Entities.Add(paperHatch); doc.Entities.ActiveLayout = active;
        var reactors = boundary.Reactors.ToArray(); var paperReactors = paperBoundary.Reactors.ToArray();
        string handle = boundary.Handle; var owner = boundary.Owner;
        var block = Block.Create(doc, "IMPORTED");
        Check(first.Associative && second.Associative && paperHatch.Associative, "Import mutated source hatch associations");
        Check(first.BoundaryPaths.Single().Entities.Single() == boundary && second.BoundaryPaths.Single().Entities.Single() == boundary,
            "Import unlinked a shared source contour");
        Check(boundary.Reactors.SequenceEqual(reactors) && paperBoundary.Reactors.SequenceEqual(paperReactors), "Source reactors changed");
        Check(boundary.Handle == handle && boundary.Owner == owner, "Source identity changed");
        Equal(active, doc.Entities.ActiveLayout, "Active layout changed");
        Equal(3, block.Entities.Count, "Only model-space entities should be copied");
        Check(block.Origin == doc.DrawingVariables.InsBase && block.Record.Units == DrawingUnits.Inches, "Insertion header lost");
        Check(block.Entities.OfType<Hatch>().All(h => !h.Associative && h.BoundaryPaths.Single().Entities.Count == 0), "Copied hatch policy changed");
        Check(block.AttributeDefinitions["ROOT"] != doc.Layouts[Layout.ModelSpaceName].AssociatedBlock.AttributeDefinitions["ROOT"], "Definition was shared");
        block.Entities.OfType<Circle>().Single().Radius = 12;
        Near(5, boundary.Radius, "Imported geometry is not independent");
    }

    private static Block BlockExchangeSeed()
    {
        var layer = new Layer("EQUIPMENT") { Color = AciColor.Blue, Lineweight = Lineweight.W30 };
        var style = new TextStyle("LIBRARY_TEXT", "romans.shx") { WidthFactor = .8 };
        var child = new Block("MOTOR") { Origin = new(1, 2, 0), Description = "Nested motor" };
        child.Record.Units = DrawingUnits.Millimeters; child.Record.AllowExploding = false; child.Record.ScaleUniformly = true;
        child.Entities.Add(new Circle(new Vector3(3, 4, 0), 2) { Layer = layer });
        child.AttributeDefinitions.Add(new AttributeDefinition("SERIAL") { Value = "default", Style = style, Position = new(2, 3, 0) });
        var block = new Block("ASSEMBLY") { Origin = new(10, 20, 3), Description = "Root metadata is not part of WBLOCK contents" };
        block.Record.Units = DrawingUnits.Inches;
        block.AttributeDefinitions.Add(new AttributeDefinition("TAG") { Value = "P-101", Prompt = "Equipment tag", Style = style, Flags = AttributeFlags.Verify });
        foreach (string serial in new[] { "A-1", "B-2" })
        {
            var insert = new Insert(child, new Vector3(serial == "A-1" ? 20 : 40, 30, 0)) { Scale = new(2, 2, 2), Rotation = 30 };
            insert.Attributes.Single().Value = serial; block.Entities.Add(insert);
        }
        return block;
    }

    private static void BlockStreamRoundTrip(DxfVersion version, bool binary)
    {
        var source = BlockExchangeSeed();
        var sourceChild = source.Entities.OfType<Insert>().First().Block;
        using var output = new MemoryStream(); Check(source.Save(output, version, binary), "Block stream save failed");
        Check(output.CanWrite, "Save closed caller stream");
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"block-exchange-{version}-{binary}.dxf"), output.ToArray());
        output.Position = 0;
        var doc = DxfDocument.Load(output)!;
        Check(doc.DrawingVariables.InsBase == source.Origin && doc.DrawingVariables.InsUnits == source.Record.Units, "WBLOCK insertion header changed");
        Check(ReferenceEquals(doc.Entities.Inserts.First().Block, doc.Entities.Inserts.Last().Block), "Repeated nested block was duplicated");
        Check(ReferenceEquals(doc.Entities.Inserts.First().Block.Entities.Single().Layer, doc.Layers["EQUIPMENT"]), "Imported layer is not canonical");
        output.Position = 0; var loaded = Block.Load(output, "IMPORTED");
        Check(loaded != null && output.CanRead, "Block load failed or closed caller stream");
        BlockExchangeVerify(loaded!, source);
        loaded!.Entities.OfType<Insert>().First().Block.Entities.OfType<Circle>().Single().Radius = 10;
        Near(2, sourceChild.Entities.OfType<Circle>().Single().Radius, "Stream import shares source geometry");
        Check(sourceChild.Record.Owner == null && sourceChild.Handle == null, "Save registered the source block");
    }

    private static void BlockExchangeVerify(Block actual, Block source)
    {
        Check(actual.Origin == source.Origin && actual.Record.Units == source.Record.Units, "Block origin or units changed");
        var definition = actual.AttributeDefinitions["TAG"];
        Equal("P-101", definition.Value, "Root attribute default"); Equal("Equipment tag", definition.Prompt, "Root prompt");
        Check(definition.Flags == AttributeFlags.Verify && definition.Style.Name == "LIBRARY_TEXT", "Definition flags or style lost");
        var instances = actual.Entities.OfType<Insert>().ToArray(); Equal(2, instances.Length, "Nested insert count");
        Check(instances.Select(i => i.Attributes.Single().Value).SequenceEqual(new[] { "A-1", "B-2" }), "Instance values were replaced by defaults");
        foreach (var insert in instances)
        {
            Check(insert.Block.Name == "MOTOR" && insert.Block.Origin == new Vector3(1, 2, 0), "Nested definition changed");
            Check(insert.Block.Record.Units == DrawingUnits.Millimeters && !insert.Block.Record.AllowExploding && insert.Block.Record.ScaleUniformly, "Nested record settings changed");
            Near(30, insert.Rotation, "Nested rotation"); Check(insert.Scale == new Vector3(2, 2, 2), "Nested scale");
            Check(insert.Attributes.Single().Definition == insert.Block.AttributeDefinitions["SERIAL"], "Attribute definition identity lost");
            var circle = insert.Block.Entities.OfType<Circle>().Single();
            Near(2, circle.Radius, "Nested circle"); Check(circle.Layer.Name == "EQUIPMENT" && circle.Layer.Color.Index == AciColor.Blue.Index, "Layer resource lost");
        }
    }

    private static void BlockExportDetached()
    {
        var block = BlockExchangeSeed(); var source = new DxfDocument(); source.Entities.Add(new Insert(block));
        var handle = block.Handle; var owner = block.Record.Owner;
        var exported = block.ToDocument(DxfVersion.AutoCad2018);
        exported.Entities.Inserts.First().Block.Entities.OfType<Circle>().Single().Radius = 40;
        exported.Layers["EQUIPMENT"].Color = AciColor.Red;
        exported.Layouts[Layout.ModelSpaceName].AssociatedBlock.AttributeDefinitions["TAG"].Value = "changed";
        Near(2, block.Entities.OfType<Insert>().First().Block.Entities.OfType<Circle>().Single().Radius, "Export changed source geometry");
        Equal("P-101", block.AttributeDefinitions["TAG"].Value, "Export changed source default");
        Check(source.Layers["EQUIPMENT"].Color.Index == AciColor.Blue.Index && block.Handle == handle && block.Record.Owner == owner, "Export changed source resources or registration");
    }

    private static void BlockExchangeInputValidation()
    {
        using var bytes = new MemoryStream(new byte[] { 1, 2, 3 });
        foreach (string name in new[] { "", "   ", "BAD/NAME", "*Model_Space" })
        {
            Throws<ArgumentException>(() => Block.Load(bytes, name));
            Equal(0L, bytes.Position, "Invalid name consumed input");
        }
        Throws<ArgumentException>(() => Block.Load(bytes, null!));
        Throws<ArgumentNullException>(() => Block.Load((Stream)null!, "NAME"));
        Throws<ArgumentNullException>(() => Block.Load(bytes, "NAME", null!));
        Throws<ArgumentNullException>(() => new Block("B").Save((Stream)null!, DxfVersion.AutoCad2018));
        using var readOnly = new MemoryStream(new byte[8], false);
        Throws<ArgumentException>(() => new Block("B").Save(readOnly, DxfVersion.AutoCad2018));
        using var closed = new MemoryStream(); closed.Dispose();
        Throws<ArgumentException>(() => Block.Load(closed, "NAME"));
        Throws<ArgumentException>(() => new Block("B").Save(closed, DxfVersion.AutoCad2018));
    }
}
