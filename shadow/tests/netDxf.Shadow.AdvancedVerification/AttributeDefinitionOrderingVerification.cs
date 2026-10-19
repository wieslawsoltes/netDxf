using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Shadow;
using netDxf.Shadow.Editor;
using netDxf.Shadow.NetDxf;

internal static class AttributeDefinitionOrderingVerification
{
    internal static void Register(List<(string Name, Action Run)> tests)
    {
        tests.Add(("attribute-definition-order/preview history preserves native handles and protected references", () =>
        {
            var (doc, block, definitions) = Seed(); var line = new Line(Vector2.Zero, Vector2.UnitX);
            line.PersistentReactors.Add(definitions[1]); doc.Entities.Add(line);
            var source = new ShadowSession(doc); var handles = definitions.Select(d => d.Handle).ToArray();
            var target = CaptureOrder(source, block, definitions.Reverse()); var editor = new EditorSession(source);
            var preview = editor.Preview("Reorder definitions", ShadowGraphPatch.Between(source.Current, target));
            Check(block.AttributeDefinitions.Values.SequenceEqual(definitions), "Preview mutated source order"); editor.ApplyPreview(preview);
            Check(editor.State.UndoCount == 1, "Order was not one undo step");
            for (int cycle = 0; cycle < 4; cycle++)
            {
                Verify(definitions.Reverse()); editor.Undo(); Verify(definitions); editor.Redo();
            }
            void Verify(IEnumerable<AttributeDefinition> expected)
            {
                Check(block.AttributeDefinitions.Values.SequenceEqual(expected), "Native order differs from history");
                Check(definitions.Select(d => d.Handle).SequenceEqual(handles) && definitions.All(d => d.Owner == block), "Retained definitions were detached or re-registered");
                Check(line.PersistentReactors.Single() == definitions[1] && ReferenceEquals(source.Current, source.Refresh()), "Protected references or stable capture changed");
            }
        }));
        tests.Add(("attribute-definition-order/branch merge and synchronization keep instance values", () =>
        {
            var (doc, block, definitions) = Seed(); var root = doc.Entities.Inserts.Single();
            foreach (var item in root.Attributes) item.Value = "assigned " + item.Tag;
            var original = root.Attributes.ToArray(); var source = new ShadowSession(doc);
            var reordered = CaptureOrder(source, block, definitions.Reverse()); var baseline = source.Current;
            var branch = new ShadowMemoryStore(baseline); branch.Apply(ShadowGraphPatch.Between(baseline, reordered));
            branch.Apply(AttributeSynchronization.Plan(branch.Current, new ObjectId("H:" + block.Handle)).Patch);
            var editor = new EditorSession(source); editor.ApplyMerge(editor.PreviewMerge(baseline, branch.Current), "Order and sync");
            Check(block.AttributeDefinitions.Values.SequenceEqual(definitions.Reverse()) && root.Attributes.SequenceEqual(original.Reverse()), "Combined merge ignored prompt order");
            Check(root.Attributes.All(a => a.Value == "assigned " + a.Tag), "Synchronization lost assigned values");
            editor.Undo(); Check(block.AttributeDefinitions.Values.SequenceEqual(definitions) && root.Attributes.SequenceEqual(original), "Combined undo lost original objects");
        }));
        tests.Add(("attribute-definition-order/cancelled membership restores order without replacing retained objects", () =>
        {
            var (doc, block, definitions) = Seed(); var source = new ShadowSession(doc);
            block.AttributeDefinitions.Remove("B"); block.AttributeDefinitions.Reorder(new[] { definitions[2], definitions[0] });
            var target = source.Refresh(); block.AttributeDefinitions.Add(definitions[1]); block.AttributeDefinitions.Reorder(definitions); source.Refresh();
            var handles = definitions.Select(d => d.Handle).ToArray(); var before = source.Current;
            block.AttributeDefinitions.BeforeRemoveItem += (_, e) => e.Cancel = true;
            var editor = new EditorSession(source); bool rejected = false;
            try { editor.Execute("Refused removal and order", ShadowGraphPatch.Between(before, target)); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected && editor.State.UndoCount == 0 && block.AttributeDefinitions.Values.SequenceEqual(definitions), "Refused removal partially changed order");
            Check(definitions.Select(d => d.Handle).SequenceEqual(handles) && ReferenceEquals(before, source.Refresh()), "Rollback changed retained definitions or source graph");
        }));
        tests.Add(("attribute-definition-order/mixed membership and order preserve surviving handles through history", () =>
        {
            var (doc, block, definitions) = Seed(); var source = new ShadowSession(doc);
            var snapshot = source.Current; var blockId = new ObjectId("H:" + block.Handle);
            var collection = snapshot.Read(blockId)["AttributeDefinitions"]; var pairs = collection.Items.ToArray();
            var edit = new ShadowGraphEdit(snapshot);
            var addedId = edit.Create(typeof(AttributeDefinition).FullName!, ShadowObjectKind.Object, new Dictionary<string, ShadowValue>
            {
                ["Tag"] = ShadowValue.From("D"), ["Value"] = ShadowValue.From("new default"), ["Owner"] = new ShadowValue.Reference(blockId)
            });
            var addedPair = (ShadowValue.Structure)pairs[0].Value;
            addedPair = addedPair with { Fields = addedPair.Fields.SetItem("Key", ShadowValue.From("D")).SetItem("Value", new ShadowValue.Reference(addedId)) };
            edit.ReplaceSequence(collection.Id!.Value, "$items", new[] { pairs[2].Value, addedPair, pairs[0].Value });
            edit.ReplaceSequence(collection.Id.Value, "Values", new[] { pairs[2]["Value"].Value, new ShadowValue.Reference(addedId), pairs[0]["Value"].Value });
            edit.ReplaceSequence(collection.Id.Value, "Tags", new[] { ShadowValue.From("C"), ShadowValue.From("D"), ShadowValue.From("A") });
            edit.Set(new ObjectId("H:" + definitions[1].Handle), "Owner", ShadowValue.Null);
            var retainedHandles = new[] { definitions[0].Handle, definitions[2].Handle };
            var editor = new EditorSession(source); editor.Execute("Replace and order", edit.Build());
            var added = block.AttributeDefinitions["D"];
            for (int cycle = 0; cycle < 3; cycle++)
            {
                Check(block.AttributeDefinitions.Values.SequenceEqual(new[] { definitions[2], added, definitions[0] }) && added.Owner == block && definitions[1].Owner == null, "Replacement membership differs");
                Check(retainedHandles.SequenceEqual(new[] { definitions[0].Handle, definitions[2].Handle }), "Reordering reallocated surviving handles");
                editor.Undo(); Check(block.AttributeDefinitions.Values.SequenceEqual(definitions) && added.Owner == null, "Undo did not restore definition objects");
                Check(retainedHandles.SequenceEqual(new[] { definitions[0].Handle, definitions[2].Handle }), "Undo reallocated surviving handles"); editor.Redo();
            }
        }));
        tests.Add(("attribute-definition-order/duplicate and mismatched keys fail before native membership changes", () =>
        {
            var (doc, block, definitions) = Seed(); var source = new ShadowSession(doc); var baseline = source.Current;
            var collection = baseline.Read(new ObjectId("H:" + block.Handle))["AttributeDefinitions"];
            var pairs = collection.Items.Select(item => item.Value).ToArray(); var invalid = (ShadowValue.Structure)pairs[0];
            invalid = invalid with { Fields = invalid.Fields.SetItem("Key", ShadowValue.From("WRONG")) };
            var handles = definitions.Select(d => d.Handle).ToArray(); var events = 0;
            block.AttributeDefinitionRemoved += (_, _) => events++; var editor = new EditorSession(source);
            foreach (var items in new[] { new[] { pairs[2], pairs[0], pairs[0] }, new[] { pairs[2], invalid, pairs[0] } })
            {
                var edit = new ShadowGraphEdit(baseline); edit.ReplaceSequence(collection.Id!.Value, "$items", items);
                bool refused = false; try { editor.Execute("Invalid membership", edit.Build()); } catch (ArgumentException) { refused = true; }
                Check(refused && events == 0 && block.AttributeDefinitions.Values.SequenceEqual(definitions) && definitions.Select(d => d.Handle).SequenceEqual(handles), "Invalid dictionary caused native detachment");
                Check(ReferenceEquals(baseline, source.Refresh()) && editor.State.UndoCount == 0, "Invalid plan stopped the session or changed history");
            }
        }));
    }

    private static ShadowSnapshot CaptureOrder(ShadowSession source, Block block, IEnumerable<AttributeDefinition> order)
    {
        var original = block.AttributeDefinitions.Values.ToArray(); block.AttributeDefinitions.Reorder(order);
        var target = source.Refresh(); block.AttributeDefinitions.Reorder(original); source.Refresh(); return target;
    }
    private static (DxfDocument Document, Block Block, AttributeDefinition[] Definitions) Seed()
    {
        var doc = new DxfDocument(); var block = new Block("ORDER");
        var definitions = new[] { "A", "B", "C" }.Select(tag => new AttributeDefinition(tag) { Value = "default " + tag }).ToArray();
        block.AttributeDefinitions.AddRange(definitions); doc.Entities.Add(new Insert(block)); return (doc, block, definitions);
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
