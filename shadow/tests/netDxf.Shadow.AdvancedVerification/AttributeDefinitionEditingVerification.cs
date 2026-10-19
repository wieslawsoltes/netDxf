using System.Collections.Immutable;
using System.Text.Json;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Shadow;
using netDxf.Shadow.Drafting;
using netDxf.Shadow.Editor;
using netDxf.Shadow.NetDxf;
using netDxf.Shadow.Studio;

internal static class AttributeDefinitionEditingVerification
{
    internal static void Register(List<(string Name, Action Run)> tests)
    {
        void Test(string name, Action run) => tests.Add(("definition-editor/" + name, run));
        Test("definition fields and tag swaps retain assigned values and exact history", () =>
        {
            var (doc, block, insert, source, editor) = Seed(); var a = block.AttributeDefinitions["A"]; var b = block.AttributeDefinitions["B"];
            var attributes = insert.Attributes.ToArray(); var before = source.Current;
            var changes = new AttributeDefinitionChanges(Edits: [new(Id(a), Tag: "B", Prompt: "New prompt", Verify: true,
                Appearance: new(Id(a), Value: "new default", Height: 7, Color: "#123456")), new(Id(b), Tag: "A")], Order: [Id(b), Id(a)]);
            var plan = DraftingAttributeDefinitions.Plan(before, Id(block), changes, true); var next = plan.Patch.Apply(before);
            Check(a.Tag == "A" && a.Value == "default A" && insert.Attributes.SequenceEqual(attributes), "Planning changed native state");
            next = AttributeSynchronization.Plan(next, Id(block)).Patch.Apply(next); editor.Execute("Edit and sync", ShadowGraphPatch.Between(before, next));
            for (int i = 0; i < 3; i++)
            {
                Check(block.AttributeDefinitions.Values.SequenceEqual(new[] { b, a }) && a.Tag == "B" && a.Height == 7 && a.Prompt == "New prompt", "Definition changes differ");
                Check(insert.Attributes.SequenceEqual(attributes.Reverse()) && attributes[0].Value == "assigned A" && attributes[0].Tag == "B" && attributes[0].Height == 7, "Sync lost assigned values or identities");
                editor.Undo(); Check(a.Tag == "A" && a.Value == "default A" && insert.Attributes.SequenceEqual(attributes), "Undo lost original state"); editor.Redo();
            }
        });
        Test("definition-only constant and multiline defaults do not edit instances", () =>
        {
            var (_, block, insert, source, editor) = Seed(); var a = block.AttributeDefinitions["A"];
            a.Flags = AttributeFlags.Constant; a.SetMText(new AttributeMText { Value = "embedded" }); source.Refresh(); editor = new(source);
            DraftingAttributeDefinitions.Plan(source.Current, Id(block), new(Edits: [new(Id(a), Appearance: new(Id(a), Value: "changed\\Pdefault", Height: 9))])).Execute(editor);
            Check(a.GetMText().Value == "changed\\Pdefault" && a.GetMText().Height == 9 && a.Value == "default A", "Embedded edit overwrote fallback");
            Check(insert.Attributes[0].Value == "assigned A" && insert.Attributes[0].Height != 9, "Definition edit changed an instance");
        });
        foreach (bool multiline in new[] { false, true }) foreach (bool binary in new[] { false, true })
            Test("create defaults sync and text/binary transport " + multiline + "/" + binary, () =>
            {
                var (doc, block, insert, source, editor) = Seed(); var before = source.Current;
                var plan = DraftingAttributeDefinitions.Plan(before, Id(block), new(Add: [new("service", "Service", multiline ? "Water\nStorage" : "Water",
                    new(4, 6, 0), 3, Multiline: multiline, Preset: true)]));
                var proposed = plan.Patch.Apply(before); var created = DraftingAttributeDefinitions.Read(proposed, Id(block)).Single(d => d.Appearance.Tag == "SERVICE");
                Check(created.Appearance.Multiline == multiline && created.Preset && created.Position == new Vec3(4, 6, 0), "Detached creation is incomplete");
                proposed = AttributeSynchronization.Plan(proposed, Id(block)).Patch.Apply(proposed);
                editor.Execute("ATTDEF and sync", ShadowGraphPatch.Between(before, proposed)); var definition = block.AttributeDefinitions["SERVICE"];
                Check(insert.Attributes.Last().Definition == definition && insert.Attributes.Last().IsPositionLocked == true, "New attributes did not bind proposed defaults");
                if (multiline) Check(definition.Value == "Water Storage" && definition.GetMText().Value == "Water\\PStorage", "Multiline default has invalid fallback framing or lost paragraphs");
                using var data = new MemoryStream(); editor.SynchronizeSource(() => source.MutateSource(d => Check(d.Save(data, binary), "Save failed")));
                data.Position = 0; var loaded = DxfDocument.Load(data)!;
                Check(loaded.Blocks[block.Name].AttributeDefinitions["SERVICE"].HasMText == multiline && loaded.Entities.Inserts.Single().Attributes.Last().HasMText == multiline, "Transport changed text mode");
                editor.Undo(); Check(block.AttributeDefinitions.Count == 2 && insert.Attributes.Count == 2, "Undo retained new definitions or attributes");
                editor.Redo(); Check(block.AttributeDefinitions["SERVICE"] == definition && insert.Attributes.Count == 3, "Redo replaced the authored definition");
            });
        Test("definition removal can preserve independent existing attributes", () =>
        {
            var (_, block, insert, source, editor) = Seed(); var removed = block.AttributeDefinitions["A"]; var attribute = insert.Attributes[0];
            DraftingAttributeDefinitions.Plan(source.Current, Id(block), new(Remove: [Id(removed)])).Execute(editor);
            Check(!block.AttributeDefinitions.ContainsTag("A") && attribute.Value == "assigned A" && attribute.Definition == removed && removed.Owner is null, "Definition-only removal changed assigned data");
            editor.Undo(); Check(block.AttributeDefinitions["A"] == removed && removed.Owner == block, "Undo lost removed definition identity");
        });
        Test("old profiles reject unsupported modes but save an unlocked single-line definition", () =>
        {
            var (doc, block, _, source, _) = Seed(); doc.DrawingVariables.AcadVer = netDxf.Header.DxfVersion.AutoCad2000; source.Refresh(); var before = source.Current;
            foreach (var creation in new[] { new AttributeDefinitionCreation("LOCK"), new AttributeDefinitionCreation("MULTI", Multiline: true, PositionLocked: false) })
                try { DraftingAttributeDefinitions.Plan(before, Id(block), new(Add: [creation])); throw new Exception("Unsupported profile accepted"); } catch (NotSupportedException) { }
            Check(ReferenceEquals(before, source.Refresh()), "Profile refusal changed source");
            try { DraftingAttributeDefinitions.Plan(before, Id(block), new(Edits: [new(Id(block.AttributeDefinitions["A"]),
                Appearance: new(Id(block.AttributeDefinitions["A"]), PositionLocked: true))])); throw new Exception("Unsupported lock edit accepted"); } catch (NotSupportedException) { }
            var editor = new EditorSession(source); DraftingAttributeDefinitions.Plan(before, Id(block), new(Add: [new("PLAIN", PositionLocked: false)])).Execute(editor);
            using var data = new MemoryStream(); Check(doc.Save(data), "Plain definition cannot be saved to its original profile");
        });
        Test("empty block accepts its first definition and detached appearance edits", () =>
        {
            var doc = new DxfDocument(netDxf.Header.DxfVersion.AutoCad2018); var block = new Block("EMPTY"); doc.Entities.Add(new Insert(block));
            var source = new ShadowSession(doc); var before = source.Current; var first = DraftingAttributeDefinitions.Plan(before, Id(block), new(Add: [new("FIRST", Value: "initial")]));
            var branch = first.Patch.Apply(before); var definition = DraftingAttributeDefinitions.Read(branch, Id(block)).Single();
            branch = DraftingAttributeDefinitions.Plan(branch, Id(block), new(Edits: [new(definition.Appearance.Id, Appearance: new(definition.Appearance.Id, Value: "changed"))])).Patch.Apply(branch);
            source.Apply(ShadowGraphPatch.Between(before, branch)); Check(block.AttributeDefinitions["FIRST"].Value == "changed", "Detached first definition could not be edited");
        });
        Test("Studio preview and direct edits record portable definition references and replay", () =>
        {
            var (doc, block, insert, _, _) = Seed(); using var seed = new MemoryStream(); Check(doc.Save(seed), "Seed save failed"); seed.Position = 0;
            using var studio = new Studio(false); studio.Load(seed);
            JsonElement J(object value) => JsonSerializer.SerializeToElement(value, StudioJson.CreateOptions());
            long Revision() => J(studio.CadState()).GetProperty("state").GetProperty("revision").GetInt64();
            studio.CadSelect(new(Revision(), "entities", Entities: [Id(insert)]));
            var read = J(studio.CadReadAttributeDefinitions(Revision())); var first = new ObjectId(read.GetProperty("definitions")[0].GetProperty("appearance").GetProperty("id").GetProperty("value").GetString()!);
            studio.CadWorkflowStart(new(Revision(), "Definition authoring"));
            var operation = new CadAttributeDefinitionOperation(Id(block), new(Edits: [new(first, Tag: "NEW_TAG", Prompt: "Equipment", Appearance: new(first, Height: 4))]), true);
            var revision = Revision(); var preview = J(studio.CadEditPreview(new(revision, "Edit definitions", [new("attribute-definitions", AttributeDefinitions: operation)])));
            Check(Revision() == revision && preview.GetProperty("attributeSynchronizations")[0].GetProperty("referenceCount").GetInt32() == 1, "Preview mutated or omitted reset report");
            studio.CadEditPreviewApply(new(revision, preview.GetProperty("previewId").GetString()!));
            try { studio.CadEditAttributeDefinitions(new(revision, operation)); throw new Exception("Stale revision accepted"); } catch (ShadowConflictException) { }
            studio.CadEditAttributeDefinitions(new(Revision(), new(Id(block), new(Add: [new("ADDED", Value: "new default")]))));
            studio.History("undo", Revision()); studio.History("redo", Revision()); studio.CadWorkflowRoundTrip(new(Revision()));
            var replay = studio.CadWorkflowReplay(new(Revision(), studio.CadWorkflowExport())); Check(replay.Passed && replay.Complete, J(replay).GetRawText());
        });
        foreach (bool save in new[] { false, true }) Test("definition workspace saves or discards authoring " + save, () =>
        {
            var (doc, block, _, _, _) = Seed(); using var seed = new MemoryStream(); Check(doc.Save(seed), "Seed save failed"); seed.Position = 0;
            using var studio = new Studio(false); studio.Load(seed);
            JsonElement J(object value) => JsonSerializer.SerializeToElement(value, StudioJson.CreateOptions());
            int Count() => J(studio.CadReadAttributeDefinitions(studio.CadRevision, Id(block).Value)).GetProperty("definitions").GetArrayLength();
            var sourceRevision = J(studio.CadState()).GetProperty("state").GetProperty("sourceRevision").GetInt64();
            studio.CadBeginBlock(new(studio.CadRevision, Id(block).Value));
            Check(J(studio.CadReadAttributeDefinitions(studio.CadRevision)).GetProperty("name").GetString() == block.Name, "Active definition was not resolved");
            studio.CadEditAttributeDefinitions(new(studio.CadRevision, new(Id(block), new(Add: [new("BRANCH", Value: "Branch default")]))));
            Check(Count() == 3, "Branch definition was not created");
            studio.CadFinishBlock(new(studio.CadRevision, sourceRevision, save, !save));
            Check(Count() == (save ? 3 : 2), "Finishing the definition workspace lost its save/discard policy");
            if (save)
            {
                studio.History("undo", studio.CadRevision); Check(Count() == 2, "Saved definition did not undo");
                studio.History("redo", studio.CadRevision); Check(Count() == 3, "Saved definition did not redo");
            }
            using var output = new MemoryStream(studio.Save()); var loaded = DxfDocument.Load(output)!;
            Check(loaded.Blocks[block.Name].AttributeDefinitions.Count == (save ? 3 : 2) && loaded.Entities.Inserts.Single().Attributes.Count == 2,
                "Definition workspace changed unsynchronized instance values or saved incorrect definitions");
        });
        for (int fault = 0; fault < 7; fault++)
        {
            int f = fault; Test("invalid batch is rejected without mutation " + f, () =>
            {
                var (_, block, _, source, _) = Seed(); var a = block.AttributeDefinitions["A"]; var b = block.AttributeDefinitions["B"]; var before = source.Current;
                var input = f switch
                {
                    0 => new AttributeDefinitionChanges(Edits: [new(Id(a), Tag: "B")]),
                    1 => new AttributeDefinitionChanges(Add: [new("bad tag")]),
                    2 => new AttributeDefinitionChanges(Remove: [Id(a), Id(b)]),
                    3 => new AttributeDefinitionChanges(Order: [Id(a), Id(a)]),
                    4 => new AttributeDefinitionChanges(Edits: [new(Id(a), Appearance: new(Id(b), Value: "wrong"))]),
                    5 => new AttributeDefinitionChanges(Add: [new("NAN", Height: double.NaN)]),
                    _ => new AttributeDefinitionChanges(Remove: [Id(a)], Edits: [new(Id(a), Tag: "C")])
                };
                Reject(() => DraftingAttributeDefinitions.Plan(before, Id(block), input)); Check(ReferenceEquals(before, source.Refresh()), "Invalid batch changed source");
            });
        }
    }
    private static (DxfDocument, Block, Insert, ShadowSession, EditorSession) Seed()
    {
        var block = new Block("EDIT_DEFINITIONS"); foreach (var tag in new[] { "A", "B" }) block.AttributeDefinitions.Add(new AttributeDefinition(tag) { Value = "default " + tag });
        var insert = new Insert(block); foreach (var a in insert.Attributes) a.Value = "assigned " + a.Tag;
        var doc = new DxfDocument(netDxf.Header.DxfVersion.AutoCad2018); doc.Entities.Add(insert); var source = new ShadowSession(doc); return (doc, block, insert, source, new(source));
    }
    private static ObjectId Id(DxfObject value) => new("H:" + value.Handle);
    private static void Check(bool result, string message) { if (!result) throw new InvalidOperationException(message); }
    private static void Reject(Action action) { try { action(); } catch (ArgumentException) { return; } throw new InvalidOperationException("Expected rejection"); }
}
