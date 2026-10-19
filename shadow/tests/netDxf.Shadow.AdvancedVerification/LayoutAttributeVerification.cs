using System.Text.Json;
using netDxf;
using netDxf.Entities;
using netDxf.Shadow;
using netDxf.Shadow.Drafting;
using netDxf.Shadow.Editor;
using netDxf.Shadow.Geometry;
using netDxf.Shadow.NetDxf;
using netDxf.Shadow.Studio;

internal static class LayoutAttributeVerification
{
    internal static void Register(List<(string Name, Action Run)> tests)
    {
        void Test(string name, Action run) => tests.Add(("layout-attributes/" + name, run));
        foreach (string layout in new[] { "Model", "Layout1" })
        foreach (bool multiline in new[] { false, true })
        foreach (bool binary in new[] { false, true })
            Test($"create move erase history and DXF {layout}/{multiline}/{binary}", () =>
            {
                var doc = new DxfDocument(netDxf.Header.DxfVersion.AutoCad2018);
                if (layout != "Model") doc.Layouts.Add(new netDxf.Objects.Layout(layout));
                var source = new ShadowSession(doc); var editor = new EditorSession(source);
                var owner = source.Current.Layout(layout)["AssociatedBlock"].Id!.Value;
                var plan = DraftingAttributeDefinitions.Plan(source.Current, owner, new(Add: [new("equipment", Value: multiline ? "Cooling\nwater" : "P-101", Position: new(4, 6), Multiline: multiline, Invisible: true)]));
                var id = plan.Created.Single(); var proposed = plan.Patch.Apply(source.Current);
                Check(new CadSceneBuilder().Build(proposed, new(layout)).Occurrences.Single().Geometry.Text!.Content == "EQUIPMENT", "Detached ATTDEF tag was not rendered");
                plan.Execute(editor); var definition = doc.Layouts[layout].AssociatedBlock.AttributeDefinitions["EQUIPMENT"];
                Check(definition.Position == new Vector3(4, 6, 0), "Creation changed insertion point");
                var planner = new DraftingPlanner();
                planner.Plan(source.Current, new("MOVE", [Vec3.Zero, new(10, 20)], [id]), layout).Execute(editor);
                Check(CadTextProjection.Read(source.Current.Read(id)).Text.Origin == new Vec3(14, 26), "Move did not transform visible text");
                Check(definition.Position == new Vector3(14, 26, 0), "Move did not transform fallback text");
                editor.Undo(); Check(definition.Position == new Vector3(4, 6, 0), "Move undo failed"); editor.Redo();
                using var output = new MemoryStream(); editor.SynchronizeSource(() => source.MutateSource(d => Check(d.Save(output, binary), "Save failed")));
                output.Position = 0; var loaded = DxfDocument.Load(output)!; var saved = loaded.Layouts[layout].AssociatedBlock.AttributeDefinitions["EQUIPMENT"];
                Check(saved.HasMText == multiline && saved.Position == definition.Position && saved.Flags.HasFlag(AttributeFlags.Hidden), "DXF lost standalone definition data");
                if (multiline) Check(saved.GetMText().Position == new Vector3(14, 26, 0) && saved.GetMText().Value == "Cooling\\Pwater", "DXF lost embedded frame or content");
                planner.Plan(source.Current, new("ERASE", Entities: [id]), layout).Execute(editor);
                Check(doc.Layouts[layout].AssociatedBlock.AttributeDefinitions.Count == 0, "Erase retained the last layout definition");
                editor.Undo(); Check(ReferenceEquals(definition, doc.Layouts[layout].AssociatedBlock.AttributeDefinitions["EQUIPMENT"]), "Erase undo replaced definition identity");
                editor.Redo(); Check(doc.Layouts[layout].AssociatedBlock.AttributeDefinitions.Count == 0, "Erase redo failed");
            });
        foreach (bool multiline in new[] { false, true })
        foreach (var disposition in Enum.GetValues<BlockSourceDisposition>().Distinct())
            Test($"block creation includes selected definitions {multiline}/{disposition}", () =>
            {
                var doc = new DxfDocument(netDxf.Header.DxfVersion.AutoCad2018);
                doc.Entities.Add(new Line(new Vector2(10, 20), new Vector2(40, 20)));
                using var data = new MemoryStream(); Check(doc.Save(data), "Seed save failed"); data.Position = 0;
                using var studio = new Studio(false); studio.Load(data);
                var layout = J(studio.CadReadAttributeDefinitions(studio.CadRevision, currentSpace: true)); var owner = Id(layout.GetProperty("block"));
                studio.CadEditAttributeDefinitions(new(studio.CadRevision, new(owner, new(Add: [new("TAG", Value: multiline ? "A\nB" : "P-101", Position: new(15, 25), Multiline: multiline)]))));
                var definition = Id(J(studio.CadReadAttributeDefinitions(studio.CadRevision, currentSpace: true)).GetProperty("definitions")[0].GetProperty("appearance").GetProperty("id"));
                studio.CadSelect(new(studio.CadRevision, "all"));
                studio.CadWorkflowStart(new(studio.CadRevision, "Create block with layout attributes"));
                studio.CadDefineBlock(new(studio.CadRevision, new("PUMP", new(10, 20), [], disposition)));
                using var result = new MemoryStream(studio.Save()); var loaded = DxfDocument.Load(result)!;
                var block = loaded.Blocks["PUMP"]; var attribute = block.AttributeDefinitions["TAG"];
                Check(attribute.Position == new Vector3(5, 5, 0) && block.Entities.Count == 1, "BLOCK did not localize definition and geometry together");
                Check(loaded.Layouts["Model"].AssociatedBlock.AttributeDefinitions.Count == (disposition == BlockSourceDisposition.Retain ? 1 : 0), "BLOCK source disposition ignored definitions");
                if (multiline) Check(attribute.GetMText().Position == new Vector3(5, 5, 0), "BLOCK did not localize embedded text");
                if (disposition == BlockSourceDisposition.ConvertToInsert)
                    Check(loaded.Entities.Inserts.Single().Attributes.Single().Value == (multiline ? "A B" : "P-101"), "Converted reference omitted its attribute defaults");
                studio.History("undo", studio.CadRevision);
                Check(Id(J(studio.CadReadAttributeDefinitions(studio.CadRevision, currentSpace: true)).GetProperty("definitions")[0].GetProperty("appearance").GetProperty("id")) == definition, "Undo replaced source definition identity");
                studio.History("redo", studio.CadRevision); studio.CadWorkflowRoundTrip(new(studio.CadRevision));
                var replay = studio.CadWorkflowReplay(new(studio.CadRevision, studio.CadWorkflowExport()));
                Check(replay.Passed && replay.Complete, J(replay).GetRawText());
            });
        foreach (var normal in new[] { Vector3.UnitZ, Vector3.UnitY, new Vector3(1, 2, 3) })
        foreach (var transform in new[] { Affine3.Move(new(7, -3, 2)), Affine3.RotateZ(Math.PI / 3), Affine3.Scale(new(2, 2, 2)) })
            Test($"embedded text affine frame {normal}/{transform}", () =>
            {
                var doc = new DxfDocument(netDxf.Header.DxfVersion.AutoCad2018); var owner = doc.Layouts["Model"].AssociatedBlock;
                var definition = new AttributeDefinition("TEXT") { Value = "independent fallback", Position = new(3, 5, 7), Height = 3, Normal = normal, Rotation = 27 };
                definition.SetMText(new AttributeMText { Value = "First\\Psecond", Position = new(11, 13, 17), Height = 4, RectangleWidth = 30,
                    Normal = normal, Rotation = 35, AttachmentPoint = MTextAttachmentPoint.MiddleCenter });
                owner.AttributeDefinitions.Add(definition); var source = new ShadowSession(doc); var editor = new EditorSession(source);
                var id = new ObjectId("H:" + definition.Handle); var before = source.Current; var text = CadTextProjection.Read(before.Read(id)).Text;
                var patch = AffineEditOperations.Plan(before, [id], transform); editor.Execute("Transform definition", patch);
                var actual = CadTextProjection.Read(source.Current.Read(id)).Text;
                Check((actual.Origin - transform.Point(text.Origin)).Length < 1e-9, "Embedded origin transform differs");
                Check((actual.XAxis.Normalized - transform.Direction(text.XAxis).Normalized).Length < 1e-9 &&
                    (actual.YAxis.Normalized - transform.Direction(text.YAxis).Normalized).Length < 1e-9, "Embedded orientation transform differs");
                Check(Math.Abs(actual.Height - text.Height * transform.Direction(text.XAxis).Length) < 1e-9 &&
                    actual.Content == text.Content && actual.Alignment == text.Alignment && definition.Value == "independent fallback", "Embedded layout/default changed");
                using var bytes = new MemoryStream(); editor.SynchronizeSource(() => source.MutateSource(d => Check(d.Save(bytes, true), "Affine DXF save failed")));
                bytes.Position = 0; var loaded = DxfDocument.Load(bytes)!; var roundTrip = new ShadowSession(loaded).Current;
                var loadedId = roundTrip.Layout()["AssociatedBlock"]["AttributeDefinitions"].Items.Single()["Value"].Id!.Value;
                var reloaded = CadTextProjection.Read(roundTrip.Read(loadedId)).Text;
                Check((reloaded.Origin - actual.Origin).Length < 1e-9 && (reloaded.XAxis - actual.XAxis).Length < 1e-9, "DXF changed transformed text frame");
                editor.Undo(); Check(CadTextProjection.Read(source.Current.Read(id)).Text == text, "Affine undo lost original text packet");
                editor.Redo(); Check(CadTextProjection.Read(source.Current.Read(id)).Text == actual, "Affine redo lost text packet");
            });
        Test("definition grip follows the embedded frame and preserves the fallback offset", () =>
        {
            var doc = new DxfDocument(netDxf.Header.DxfVersion.AutoCad2018);
            var definition = new AttributeDefinition("TAG") { Value = "Fallback", Position = new(3, 5, 7) };
            definition.SetMText(new AttributeMText { Value = "Visible", Position = new(11, 13, 17), Height = 4 });
            doc.Layouts["Model"].AssociatedBlock.AttributeDefinitions.Add(definition);
            var source = new ShadowSession(doc); var editor = new EditorSession(source); var id = new ObjectId("H:" + definition.Handle);
            var grip = DraftingGrips.Read(source.Current, [id]).Single();
            Check(grip.Position == new Vec3(11, 13, 17) && grip.Kind == "Move", "Grip did not follow the visible text frame");
            DraftingGrips.Move(source.Current, id, grip.Id, new(21, 33, 47)).Execute(editor);
            Check(definition.GetMText().Position == new Vector3(21, 33, 47) && definition.Position == new Vector3(13, 25, 37), "Grip changed the embedded/fallback offset");
            editor.Undo(); Check(definition.Position == new Vector3(3, 5, 7) && definition.GetMText().Position == new Vector3(11, 13, 17), "Grip undo lost text frames");
            editor.Redo(); Check(definition.GetMText().Position == new Vector3(21, 33, 47), "Grip redo failed");
            doc.Layers["0"].IsLocked = true; source.Refresh();
            Check(DraftingGrips.Read(source.Current, [id]).IsEmpty, "Locked definition exposed an editable grip");
        });
        Test("layout preview is detached and refuses reference synchronization", () =>
        {
            using var studio = new Studio(false); var owner = Id(J(studio.CadReadAttributeDefinitions(studio.CadRevision, currentSpace: true)).GetProperty("block"));
            var operation = new CadAttributeDefinitionOperation(owner, new(Add: [new("PREVIEW", Value: "Value", PositionLocked: false)]));
            var revision = studio.CadRevision; var preview = J(studio.CadEditPreview(new(revision, "Place ATTDEF", [new("attribute-definitions", AttributeDefinitions: operation)])));
            Check(studio.CadRevision == revision, "Preview changed the source");
            studio.CadEditPreviewApply(new(revision, preview.GetProperty("previewId").GetString()!));
            Check(J(studio.CadReadAttributeDefinitions(studio.CadRevision, currentSpace: true)).GetProperty("definitions").GetArrayLength() == 1, "Preview omitted definition");
            revision = studio.CadRevision;
            try { studio.CadEditAttributeDefinitions(new(revision, operation with { UpdateExisting = true, Changes = new() })); throw new Exception("Layout synchronization accepted"); } catch (ArgumentException) { }
            Check(studio.CadRevision == revision, "Rejected synchronization changed the source");
        });
    }
    private static JsonElement J(object value) => JsonSerializer.SerializeToElement(value, StudioJson.CreateOptions());
    private static ObjectId Id(JsonElement value) => new(value.GetProperty("value").GetString()!);
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
