using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Shadow;
using netDxf.Shadow.Drafting;
using netDxf.Shadow.Editor;
using netDxf.Shadow.NetDxf;
using netDxf.Tables;
using Att = netDxf.Entities.Attribute;

internal static class CapturedInsertConstructionVerification
{
    internal static void Register(List<(string Name, Action Run)> tests)
    {
        foreach (var mode in new[] { "attributes", "duplicate-tags", "empty-sequence", "no-sequence" })
        foreach (bool binary in new[] { false, true })
            tests.Add(($"captured-insert/copy-preview-history-transport/{mode}/{binary}", () => Copy(mode, binary)));
        foreach (bool arrows in new[] { false, true })
            tests.Add(("captured-insert/nested-definition-adoption-and-removal-retain-history/" + arrows, () => NestedImport(arrows)));
    }

    private static void Copy(string mode, bool binary)
    {
        var doc = new DxfDocument(); var block = new Block("PART");
        var definition = new AttributeDefinition("TAG") { Value = "default", Position = new(1, 2, 0) };
        block.AttributeDefinitions.Add(definition); block.Entities.Add(new Circle(Vector3.Zero, 2));
        var root = Insert.CreateWithAttributes(block, []);
        if (mode is "attributes" or "duplicate-tags")
            root.ReplaceAttributes(mode == "attributes" ? [new Att(definition) { Value = "A", Position = new(3, 4, 0) }]
                : [new Att(definition) { Value = "A", Position = new(3, 4, 0) }, new Att(definition) { Value = "B", Position = new(5, 6, 0) }]);
        if (mode != "no-sequence")
        {
            var end = root.EnsureAttributeSequenceEnd(); end.XData.Add(new XData(new ApplicationRegistry("SEQUENCE_METADATA")));
            end.XData["SEQUENCE_METADATA"].XDataRecord.Add(new XDataRecord(XDataCode.String, "retained"));
        }
        doc.Entities.Add(root); var session = new ShadowSession(doc); var editor = new EditorSession(session); var before = session.Current;
        var sourceAttributes = root.Attributes.ToArray(); var sourceEnd = root.EndSequenceRecord;
        var plan = new DraftingPlanner().Plan(before, new("COPY", [Vec3.Zero, new(10, 20)], [Id(root)]));
        var preview = plan.Patch.Apply(before); var copyId = plan.Created.Single();
        Check(preview.Read(copyId)["Attributes"].Items.Select(a => a["Value"].Text).SequenceEqual(sourceAttributes.Select(a => a.Value)), "Preview lost assigned values");
        Check(ReferenceEquals(before, session.Current) && doc.Entities.Inserts.Count() == 1, "Preview mutated source");
        plan.Execute(editor); var copy = doc.Entities.Inserts.Single(i => i != root); var copiedAttributes = copy.Attributes.ToArray(); var copiedEnd = copy.EndSequenceRecord;
        Check(copy.Block == block && copy.Attributes.Count == root.Attributes.Count, "Copy changed shared definition or membership");
        Check(copy.Attributes.All(a => a.Definition == definition && a.Owner == copy) && copy.Attributes.All(a => !sourceAttributes.Contains(a)), "Copy lost definition binding or attribute independence");
        Check(mode == "no-sequence" ? copiedEnd == null : copiedEnd != null && copiedEnd != sourceEnd, "Sequence identity was lost or shared");
        if (copiedEnd != null) Check((string)copiedEnd.XData["SEQUENCE_METADATA"].XDataRecord.Single().Value == "retained", "Sequence metadata lost");
        for (int i = 0; i < copiedAttributes.Length; i++) Check(copiedAttributes[i].Position == sourceAttributes[i].Position + new Vector3(10, 20, 0), "Copy did not translate attribute geometry");
        for (int n = 0; n < 3; n++)
        {
            editor.Undo(); Check(doc.Entities.Inserts.Single() == root && root.EndSequenceRecord == sourceEnd, "Undo changed the source insertion");
            editor.Redo(); Check(doc.Entities.Inserts.Last() == copy && copy.Attributes.SequenceEqual(copiedAttributes) && copy.EndSequenceRecord == copiedEnd, "Redo replaced captured identities");
        }
        using var stream = new MemoryStream(); Check(doc.Save(stream, binary), "Copy save failed"); stream.Position = 0;
        var restored = DxfDocument.Load(stream)!.Entities.Inserts.Last();
        Check(restored.Attributes.Select(a => a.Value).SequenceEqual(sourceAttributes.Select(a => a.Value)), "DXF lost copied values");
        Check((restored.EndSequenceRecord != null) == (mode != "no-sequence"), "DXF changed sequence presence");
        Check(root.Attributes.SequenceEqual(sourceAttributes) && root.EndSequenceRecord == sourceEnd, "Source attributes changed");
        Check(definition.Position == new Vector3(1, 2, 0) && definition.Value == "default", "Copy changed the shared definition");
    }

    private static void NestedImport(bool arrows)
    {
        var input = new DxfDocument(); var child = new Block("CHILD");
        child.Entities.Add(new Circle(Vector3.Zero, 3) { Layer = new Layer("LIBRARY_LAYER") });
        child.AttributeDefinitions.Add(new AttributeDefinition("TAG") { Value = "default" });
        if (arrows)
        {
            var arrow = new Block("LIBRARY_ARROW"); arrow.Entities.Add(new Line(Vector2.Zero, Vector2.UnitX));
            var style = new DimensionStyle("LIBRARY_DIMENSION") { DimArrow1 = arrow, DimArrow2 = arrow };
            child.Entities.Add(new AlignedDimension(Vector2.Zero, new Vector2(10, 0), 3) { Style = style });
        }
        var parent = new Block("PARENT");
        foreach (string value in new[] { "first", "second" })
        {
            var insert = new Insert(child); insert.Attributes[0].Value = value; parent.Entities.Add(insert);
        }
        input.Entities.Add(new Insert(parent)); var source = new ShadowSession(input); var sourceBefore = source.Current;
        var output = new DxfDocument(); var target = new ShadowSession(output); var editor = new EditorSession(target);
        var patch = DraftingBlockLibrary.Import(target.Current, source.Current, new(Id(parent), "PARENT")).Edit.Patch; var preview = patch.Apply(target.Current);
        Check(!output.Blocks.Contains("PARENT") && preview.Document["Blocks"].Items.Any(b => b["Name"].Text == "PARENT"), "Preview published native definitions");
        editor.Execute("Import captured definition graph", patch);
        var imported = output.Blocks["PARENT"]; var nested = output.Blocks["CHILD"];
        var inserts = imported.Entities.OfType<Insert>().ToArray(); var attributes = inserts.SelectMany(i => i.Attributes).ToArray();
        var terminators = inserts.Select(i => i.EndSequenceRecord).ToArray();
        Check(inserts.Length == 2 && inserts.All(i => i.Block == nested), "Nested definition sharing lost");
        Check(attributes.Select(a => a.Value).SequenceEqual(new[] { "first", "second" }), "Captured values lost");
        for (int n = 0; n < 3; n++)
        {
            editor.Undo(); Check(!output.Blocks.Contains("PARENT") && !output.Blocks.Contains("CHILD") && !output.Layers.Contains("LIBRARY_LAYER"), "Undo left definitions or resources");
            if (arrows) Check(!output.Blocks.Contains("LIBRARY_ARROW") && !output.DimensionStyles.Contains("LIBRARY_DIMENSION"), "Undo retained dimension resources");
            editor.Redo(); Check(output.Blocks["PARENT"] == imported && output.Blocks["CHILD"] == nested && imported.Entities.SequenceEqual(inserts), "Redo replaced definition or entity identities");
            Check(inserts.SelectMany(i => i.Attributes).SequenceEqual(attributes) && inserts.Select(i => i.EndSequenceRecord).SequenceEqual(terminators), "Redo replaced owned attribute identities");
            if (arrows) Check(nested.Entities.OfType<AlignedDimension>().Single().Style == output.DimensionStyles["LIBRARY_DIMENSION"] &&
                output.DimensionStyles["LIBRARY_DIMENSION"].DimArrow1 == output.Blocks["LIBRARY_ARROW"], "Redo lost dimension resource identity");
        }
        Check(ReferenceEquals(source.Refresh(), sourceBefore) && input.Entities.Inserts.Single().Block == parent && parent.Entities.Count == 2, "Import changed source drawing");
    }

    private static ObjectId Id(DxfObject value) => new("H:" + value.Handle.ToUpperInvariant());
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
