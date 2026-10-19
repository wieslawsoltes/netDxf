using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Objects;
using netDxf.Shadow;
using netDxf.Shadow.Drafting;
using netDxf.Shadow.Editor;
using netDxf.Shadow.NetDxf;
using netDxf.Tables;
using netDxf.Units;

internal static class BlockLibraryVerification
{
    internal static void Register(List<(string Name, Action Run)> tests)
    {
        void Test(string name, Action action) => tests.Add(("block-library/" + name, action));
        foreach (var policy in Enum.GetValues<BlockLibraryResourceConflict>())
        foreach (bool binary in new[] { false, true }) Test("resource-policy/" + policy + "/" + binary, () => Resources(policy, binary));
        foreach (bool binary in new[] { false, true })
        {
            Test("redefine-retains-references-and-assigned-attributes/" + binary, () => Redefine(binary));
            Test("model-space-base-units-and-paper-exclusion/" + binary, () => Model(binary));
            Test("associative-hatch-and-end-metadata/" + binary, () => Hatch(binary));
            Test("shared-hatch-boundary-and-reordered-entities/" + binary, () => Hatch(binary, true));
            Test("anonymous-dependency-is-never-reused-by-name/" + binary, () => Anonymous(binary));
        }
        Test("root-collision-reuse-rename-and-reject", () =>
        {
            var (input, root) = Seed(); var output = new DxfDocument(); output.Blocks.Add(new Block("Imported"));
            var source = new ShadowSession(input); var target = new ShadowSession(output);
            Reject<ArgumentException>(() => DraftingBlockLibrary.Import(target.Current, source.Current, new(Id(root), "Imported")));
            var reused = DraftingBlockLibrary.Import(target.Current, source.Current, new(Id(root), "Imported", BlockLibraryConflict.Reuse));
            Check(reused.Edit.Patch.Changes.IsEmpty && reused.Block == Id(output.Blocks["Imported"]), "Reuse edited the existing definition");
            var renamed = DraftingBlockLibrary.Import(target.Current, source.Current, new(Id(root), "Imported", BlockLibraryConflict.Rename));
            renamed.Edit.Execute(new(target)); Check(output.Blocks.Contains("Imported$1") && output.Blocks["Imported"].Entities.Count == 0, "Rename overwrote the existing block");
        });
        Test("budgets-cancellation-and-stale-plan", () =>
        {
            var (input, root) = Seed(); var output = new DxfDocument(); var source = new ShadowSession(input); var target = new ShadowSession(output); var before = target.Current;
            var request = new BlockLibraryImport(Id(root), "Imported");
            Reject<InvalidOperationException>(() => DraftingBlockLibrary.Import(before, source.Current, request, new(MaximumObjects: 1)));
            Reject<InvalidOperationException>(() => DraftingBlockLibrary.Import(before, source.Current, request, new(MaximumValues: 1)));
            Reject<OperationCanceledException>(() => DraftingBlockLibrary.Import(before, source.Current, request, cancellationToken: new(true)));
            var plan = DraftingBlockLibrary.Import(before, source.Current, request);
            target.MutateSource(d => d.Entities.Add(new Line(Vector2.Zero, Vector2.UnitX)));
            Reject<ShadowConflictException>(() => plan.Edit.Execute(new(target)));
            Check(!output.Blocks.Contains("Imported") && output.Entities.All.Count() == 1, "Rejected plan published geometry");
        });
        Test("invalid-library-membership-is-refused-before-native-adoption", () =>
        {
            var input = new DxfDocument(); var block = new Block("HATCHED"); var circle = new Circle(Vector3.Zero, 5);
            block.Entities.Add(new Hatch(HatchPattern.Solid, [new HatchBoundaryPath(new[] { circle })], true));
            input.Entities.Add(new Insert(block)); var source = new ShadowSession(input);
            var output = new DxfDocument(); var target = new ShadowSession(output); var before = target.Current; var editor = new EditorSession(target);
            var plan = DraftingBlockLibrary.Import(before, source.Current, new(Id(block), "Imported"));
            var proposed = plan.Edit.Patch.Apply(before); var bad = new ShadowGraphEdit(proposed);
            bad.Set(before.Document["Layers"].Id!.Value, "Count", ShadowValue.From(-1));
            Reject<ArgumentException>(() => editor.Execute("Rejected import", ShadowGraphPatch.Between(before, bad.Build().Apply(proposed))));
            Check(ReferenceEquals(target.Current, before) && !output.Blocks.Contains("Imported") && editor.State.UndoCount == 0, "Rejected import changed source/history");
            plan.Edit.Execute(editor); editor.Undo(); editor.Redo();
            Check(output.Blocks["Imported"].Entities.OfType<Hatch>().Single().Associative, "Session was unusable after rejection");
        });
    }

    private static (DxfDocument Document, Block Root) Seed()
    {
        var doc = new DxfDocument(); var child = new Block("CHILD"); child.Record.Units = DrawingUnits.Millimeters;
        child.Entities.Add(new Circle(Vector3.Zero, 2) { Layer = new Layer("EQUIPMENT") { Color = AciColor.Red } });
        child.AttributeDefinitions.Add(new AttributeDefinition("TAG") { Value = "source default" });
        var root = new Block("SOURCE") { Origin = new(3, 4, 0), Description = "Library definition" }; root.Record.Units = DrawingUnits.Inches;
        root.AttributeDefinitions.Add(new AttributeDefinition("TAG") { Value = "new default" });
        root.AttributeDefinitions.Add(new AttributeDefinition("NEW") { Value = "new field" });
        foreach (string value in new[] { "A", "B" })
        {
            var insert = new Insert(child, new Vector3(value == "A" ? 0 : 20, 10, 0)); insert.Attributes[0].Value = value; root.Entities.Add(insert);
        }
        doc.Entities.Add(new Insert(root)); return (doc, root);
    }

    private static void Resources(BlockLibraryResourceConflict policy, bool binary)
    {
        var (input, root) = Seed(); var output = new DxfDocument();
        var existing = new Block("CHILD"); existing.Entities.Add(new Circle(Vector3.Zero, 99));
        existing.AttributeDefinitions.Add(new AttributeDefinition("TAG") { Value = "destination default" }); output.Blocks.Add(existing);
        var layer = new Layer("EQUIPMENT") { Color = AciColor.Blue }; output.Layers.Add(layer);
        var source = new ShadowSession(input); var before = source.Current; var target = new ShadowSession(output); var editor = new EditorSession(target);
        var request = new BlockLibraryImport(Id(root), "Imported", ResourceConflict: policy);
        if (policy == BlockLibraryResourceConflict.Reject)
        {
            Reject<ArgumentException>(() => DraftingBlockLibrary.Import(target.Current, before, request));
            Check(!output.Blocks.Contains("Imported"), "Rejected collision published a definition"); return;
        }
        var plan = DraftingBlockLibrary.Import(target.Current, before, request); var preview = plan.Edit.Patch.Apply(target.Current);
        Check(!output.Blocks.Contains("Imported") && preview.Read(plan.Block)["Origin"].Vector() == new Vec3(3, 4), "Preview was not detached or lost the base point");
        plan.Edit.Execute(editor); var imported = output.Blocks["Imported"]; var children = imported.Entities.OfType<Insert>().ToArray();
        Check(children[0].Block == children[1].Block && children.Select(i => i.Attributes[0].Value).SequenceEqual(new[] { "A", "B" }), "Shared child or assigned values lost");
        if (policy == BlockLibraryResourceConflict.Reuse)
            Check(children[0].Block == existing && children.All(i => i.Attributes[0].Definition == existing.AttributeDefinitions["TAG"]), "Reuse did not bind destination definitions");
        else
            Check(children[0].Block != existing && children[0].Block.Name == "CHILD$1" && children[0].Block.Entities.Single().Layer.Name == "EQUIPMENT$1" &&
                children[0].Block.Entities.Single().Layer.Color.Index == AciColor.Red.Index, "Rename lost source resources");
        Check(imported.Record.Units == DrawingUnits.Inches && imported.Description == "Library definition" && layer.Color.Index == AciColor.Blue.Index, "Import changed unrelated resources or metadata");
        editor.Undo(); Check(!output.Blocks.Contains("Imported") && output.Blocks["CHILD"] == existing && output.Layers["EQUIPMENT"] == layer, "Undo changed destination resources");
        editor.Redo(); Check(output.Blocks["Imported"] == imported && imported.Entities.SequenceEqual(children), "Redo replaced imported identities");
        var loaded = RoundTrip(output, binary); Check(loaded.Blocks["Imported"].Entities.OfType<Insert>().Select(i => i.Attributes[0].Value).SequenceEqual(new[] { "A", "B" }), "Roundtrip changed values");
        Check(ReferenceEquals(source.Refresh(), before), "Import mutated the source document");
    }

    private static void Redefine(bool binary)
    {
        var (input, root) = Seed(); var output = new DxfDocument(); var block = new Block("Imported");
        var tag = new AttributeDefinition("TAG") { Value = "old default" }; var obsolete = new AttributeDefinition("OLD") { Value = "old only" };
        block.AttributeDefinitions.Add(tag); block.AttributeDefinitions.Add(obsolete); var line = new Line(Vector2.Zero, Vector2.UnitX); block.Entities.Add(line);
        var instance = new Insert(block); instance.Attributes[0].Value = "authored"; output.Entities.Add(instance); var attributes = instance.Attributes.ToArray();
        var source = new ShadowSession(input); var target = new ShadowSession(output); var editor = new EditorSession(target);
        var plan = DraftingBlockLibrary.Import(target.Current, source.Current, new(Id(root), "Imported", BlockLibraryConflict.Redefine));
        plan.Edit.Execute(editor);
        Check(output.Blocks["Imported"] == block && instance.Block == block && block.AttributeDefinitions["TAG"] == tag && tag.Value == "new default", "Redefinition replaced shared identities");
        Check(instance.Attributes.SequenceEqual(attributes) && instance.Attributes[0].Value == "authored" && obsolete.Owner == null, "Redefinition reset instance values or retained stale ownership");
        Check(block.Entities.OfType<Insert>().Count() == 2 && block.AttributeDefinitions.ContainsTag("NEW"), "New contents missing");
        editor.Undo(); Check(block.Entities.Single() == line && block.AttributeDefinitions["OLD"] == obsolete && tag.Value == "old default", "Undo did not restore definition contents");
        editor.Redo(); Check(instance.Attributes.SequenceEqual(attributes) && block.AttributeDefinitions["TAG"] == tag, "Redo replaced retained attributes");
        var loaded = RoundTrip(output, binary); Check(loaded.Entities.Inserts.Single().Attributes[0].Value == "authored" && loaded.Blocks["Imported"].Entities.OfType<Insert>().Count() == 2, "Redefined DXF differs");
    }

    private static void Model(bool binary)
    {
        var input = new DxfDocument(); input.DrawingVariables.InsBase = new(7, 8, 9); input.DrawingVariables.InsUnits = DrawingUnits.Meters;
        input.Entities.Add(new Circle(new Vector3(10, 20, 0), 2)); input.Layouts.Add(new Layout("Sheet")); input.Entities.ActiveLayout = "Sheet";
        input.Entities.Add(new Line(Vector2.Zero, Vector2.UnitX)); var source = new ShadowSession(input); var output = new DxfDocument(); var target = new ShadowSession(output);
        var plan = DraftingBlockLibrary.Import(target.Current, source.Current, new(source.Current.Layout()["AssociatedBlock"].Id!.Value, "Drawing"));
        plan.Edit.Execute(new(target)); var block = output.Blocks["Drawing"];
        Check(block.Entities.Single() is Circle && block.Origin == new Vector3(7, 8, 9) && block.Record.Units == DrawingUnits.Meters && block.Record.Layout == null, "Drawing import included paper space or lost insertion metadata");
        Check(RoundTrip(output, binary).Blocks["Drawing"].Entities.Single() is Circle, "Drawing import could not roundtrip");
    }

    private static void Hatch(bool binary, bool shared = false)
    {
        var input = new DxfDocument(); var block = new Block("HATCHED"); var circle = new Circle(Vector3.Zero, 5);
        var hatch = new Hatch(HatchPattern.Solid, [new HatchBoundaryPath(new[] { circle })], true);
        if (shared)
        {
            block.Entities.Add(hatch); // Native adoption puts its boundary after the hatch.
            block.Entities.Add(new Hatch(HatchPattern.Line, [new HatchBoundaryPath(new[] { circle })], true));
        }
        else { block.Entities.Add(circle); block.Entities.Add(hatch); }
        block.EndRecord.XData.Add(new XData(new ApplicationRegistry("LIBRARY_META")));
        block.EndRecord.XData["LIBRARY_META"].XDataRecord.Add(new XDataRecord(XDataCode.String, "end metadata"));
        input.Entities.Add(new Insert(block)); var source = new ShadowSession(input); var before = source.Current;
        var output = new DxfDocument(); var target = new ShadowSession(output); var editor = new EditorSession(target);
        var plan = DraftingBlockLibrary.Import(target.Current, before, new(Id(block), "Imported")); plan.Edit.Execute(editor);
        var copy = output.Blocks["Imported"]; var fill = copy.Entities.OfType<Hatch>().First(); var boundary = copy.Entities.OfType<Circle>().Single();
        Check(fill.Associative && fill.BoundaryPaths.Single().Entities.Single() == boundary && boundary.Reactors.Contains(fill), "Hatch association was dropped");
        Check((string)copy.EndRecord.XData["LIBRARY_META"].XDataRecord.Single().Value == "end metadata", "ENDBLK metadata was dropped");
        for (int i = 0; i < 3; i++)
        {
            editor.Undo(); editor.Redo(); Check(copy.Entities.OfType<Hatch>().First() == fill && fill.BoundaryPaths.Single().Entities.Single() == boundary, "Hatch history replaced identities");
            Check(boundary.Reactors.Count == (shared ? 2 : 1), "History duplicated or lost source links");
        }
        var loaded = RoundTrip(output, binary).Blocks["Imported"]; Check(loaded.Entities.OfType<Hatch>().All(h => h.Associative) && loaded.EndRecord.XData.ContainsAppId("LIBRARY_META"), "Metadata roundtrip failed");
        Check(ReferenceEquals(source.Refresh(), before), "Hatch source was mutated");
    }

    private static void Anonymous(bool binary)
    {
        var input = new DxfDocument(); var child = Block.CreateAnonymous("*U1"); child.Entities.Add(new Circle(Vector3.Zero, 2));
        var root = new Block("ROOT"); root.Entities.Add(new Insert(child)); input.Entities.Add(new Insert(root));
        var output = new DxfDocument(); var existing = Block.CreateAnonymous("*U1"); existing.Entities.Add(new Circle(Vector3.Zero, 99)); output.Blocks.Add(existing);
        var source = new ShadowSession(input); var target = new ShadowSession(output); var editor = new EditorSession(target);
        var plan = DraftingBlockLibrary.Import(target.Current, source.Current, new(Id(root), "Imported")); plan.Edit.Execute(editor);
        var imported = output.Blocks["Imported"].Entities.OfType<Insert>().Single().Block;
        Check(imported != existing && imported.Name == "*U2" && imported.Flags.HasFlag(BlockTypeFlags.AnonymousBlock), "Anonymous names were treated as reusable definitions");
        editor.Undo(); editor.Redo(); Check(output.Blocks["*U1"] == existing && output.Blocks["*U2"] == imported, "Anonymous history changed identities");
        Check(RoundTrip(output, binary).Blocks["Imported"].Entities.OfType<Insert>().Single().Block.Entities.OfType<Circle>().Single().Radius == 2, "Anonymous transport changed geometry");
    }

    private static DxfDocument RoundTrip(DxfDocument doc, bool binary) { using var stream = new MemoryStream(); Check(doc.Save(stream, binary), "Save failed"); stream.Position = 0; return DxfDocument.Load(stream)!; }
    private static ObjectId Id(DxfObject value) => new("H:" + value.Handle.ToUpperInvariant());
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Reject<T>(Action run) where T : Exception { try { run(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
