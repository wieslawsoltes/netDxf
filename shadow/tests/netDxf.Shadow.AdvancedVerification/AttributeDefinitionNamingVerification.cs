using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Shadow;
using netDxf.Shadow.Editor;
using netDxf.Shadow.Drafting;
using netDxf.Shadow.NetDxf;

internal static class AttributeDefinitionNamingVerification
{
    internal static void Register(List<(string Name, Action Run)> tests)
    {
        tests.Add(("attribute-definition-names/preview swap and sync preserve values handles and history", () =>
        {
            var (doc, block, root) = Seed(); var definitions = block.AttributeDefinitions.Values.ToArray(); var attributes = root.Attributes.ToArray();
            var handles = definitions.Select(d => d.Handle).Concat(attributes.Select(a => a.Handle)).ToArray(); var source = new ShadowSession(doc); var editor = new EditorSession(source);
            var branch = new ShadowMemoryStore(source.Current); branch.Apply(Rename(branch.Current, block, new() { ["A"] = "B", ["B"] = "A" }, true));
            branch.Apply(AttributeSynchronization.Plan(branch.Current, Id(block)).Patch);
            var preview = editor.PreviewMerge(source.Current, branch.Current); Check(definitions[0].Tag == "A", "Preview renamed native definitions"); editor.ApplyMerge(preview, "Swap and sync");
            for (int cycle = 0; cycle < 3; cycle++)
            {
                Check(block.AttributeDefinitions["B"] == definitions[0] && root.Attributes[0] == attributes[0] && attributes[0].Tag == "B" && attributes[0].Value == "assigned A", "Swap changed identities or assigned values");
                Check(handles.SequenceEqual(definitions.Select(d => d.Handle).Concat(attributes.Select(a => a.Handle))), "Tag swap changed native handles");
                editor.Undo(); Check(block.AttributeDefinitions["A"] == definitions[0] && attributes[0].Tag == "A" && attributes[0].Definition == definitions[0], "Undo lost original tags or bindings"); editor.Redo();
            }
        }));
        tests.Add(("attribute-definition-names/new synchronized attributes bind renamed retained definitions", () =>
        {
            var (doc, block, root) = Seed(); var empty = Insert.CreateWithAttributes(block, Array.Empty<netDxf.Entities.Attribute>()); doc.Entities.Add(empty);
            var source = new ShadowSession(doc); var branch = new ShadowMemoryStore(source.Current);
            branch.Apply(Rename(branch.Current, block, new() { ["A"] = "RENAMED" }, true)); branch.Apply(AttributeSynchronization.Plan(branch.Current, Id(block)).Patch);
            var editor = new EditorSession(source); editor.ApplyMerge(editor.PreviewMerge(source.Current, branch.Current), "Rename and add defaults"); var added = empty.Attributes.ToArray();
            Check(added[0].Tag == "RENAMED" && added[0].Definition == block.AttributeDefinitions["RENAMED"] && added[0].Value == "default A", "New instance used the old definition tag");
            editor.Undo(); Check(empty.Attributes.Count == 0 && root.Attributes[0].Tag == "A", "Undo did not restore empty reference"); editor.Redo();
            Check(empty.Attributes.SequenceEqual(added) && added[0].Definition.Tag == "RENAMED", "Redo recreated instances or lost bindings");
        }));
        tests.Add(("attribute-definition-names/definition-only rename keeps original instance bindings", () =>
        {
            var (doc, block, root) = Seed(); var definition = block.AttributeDefinitions["A"]; var attribute = root.Attributes[0]; var source = new ShadowSession(doc); var editor = new EditorSession(source);
            editor.Execute("Definition only", Rename(source.Current, block, new() { ["A"] = "NEW" }, false));
            Check(definition.Tag == "NEW" && attribute.Tag == "A" && attribute.Definition == definition && attribute.Value == "assigned A", "Definition-only edit changed instances");
            editor.Undo(); Check(definition.Tag == "A" && attribute.Tag == "A", "Definition-only undo failed");
        }));
        tests.Add(("attribute-definition-names/invalid dictionary rename refuses before any source change", () =>
        {
            var (doc, block, root) = Seed(); var source = new ShadowSession(doc); var before = source.Current;
            var edit = new ShadowGraphEdit(before); edit.Set(Id(block.AttributeDefinitions["A"]), "Tag", ShadowValue.From("NEW"));
            Reject(() => source.Apply(edit.Build())); Reject(() => source.Apply(Rename(before, block, new() { ["A"] = "B" }, true)));
            Check(ReferenceEquals(before, source.Refresh()) && root.Attributes[0].Tag == "A", "Invalid rename mutated or stopped source");
        }));
        tests.Add(("attribute-definition-names/failed derived assertion rolls back exact tags and bindings", () =>
        {
            var (doc, block, root) = Seed(); var source = new ShadowSession(doc); var before = source.Current; var branch = new ShadowMemoryStore(before);
            branch.Apply(Rename(before, block, new() { ["A"] = "NEW" }, true)); var edit = new ShadowGraphEdit(branch.Current);
            // HasMText is a derived public assertion, not a writable formatting field.
            edit.Set(Id(root.Attributes[0]), "HasMText", ShadowValue.From(true)); branch.Apply(edit.Build());
            Reject(() => source.Apply(ShadowGraphPatch.Between(before, branch.Current)));
            Check(ReferenceEquals(before, source.Refresh()) && block.AttributeDefinitions["A"].Tag == "A" && root.Attributes[0].Tag == "A", "Rollback lost native bindings or indexes");
        }));
        tests.Add(("attribute-definition-names/rename can take a deleted definition tag in one transaction", () =>
        {
            var (doc, block, root) = Seed(); var a = block.AttributeDefinitions["A"]; var b = block.AttributeDefinitions["B"];
            var handle = a.Handle; var originalAttributes = root.Attributes.ToArray(); var source = new ShadowSession(doc); var branch = new ShadowMemoryStore(source.Current);
            var edit = new ShadowGraphEdit(branch.Current); var collection = branch.Current.Read(Id(block))["AttributeDefinitions"];
            edit.ReplaceSequence(collection.Id!.Value, "$items", collection.Items.Where(p => p["Key"].Text == "A").Select(p => p.Value));
            edit.ReplaceSequence(collection.Id.Value, "Values", new[] { new ShadowValue.Reference(Id(a)) });
            edit.ReplaceSequence(collection.Id.Value, "Tags", new[] { ShadowValue.From("A") });
            edit.ReplaceSequence(Id(root), "Attributes", new[] { new ShadowValue.Reference(Id(originalAttributes[0])) });
            edit.PruneUnreachable(); branch.Apply(edit.Build()); branch.Apply(Rename(branch.Current, block, new() { ["A"] = "B" }, true));
            var editor = new EditorSession(source); editor.ApplyMerge(editor.PreviewMerge(source.Current, branch.Current), "Remove and rename");
            for (int cycle = 0; cycle < 3; cycle++)
            {
                Check(block.AttributeDefinitions["B"] == a && a.Handle == handle && b.Owner == null && root.Attributes.Single() == originalAttributes[0] && root.Attributes[0].Value == "assigned A", "Rename into removed tag lost identity or value");
                editor.Undo(); Check(block.AttributeDefinitions["A"] == a && block.AttributeDefinitions["B"] == b && root.Attributes.SequenceEqual(originalAttributes) && a.Handle == handle, "Undo collided with restored definition"); editor.Redo();
            }
        }));
        tests.Add(("attribute-definition-names/new definition can take a retained definition's old tag", () =>
        {
            var (doc, block, root) = Seed(); var a = block.AttributeDefinitions["A"]; var handle = a.Handle; var source = new ShadowSession(doc); var branch = new ShadowMemoryStore(source.Current);
            branch.Apply(Rename(branch.Current, block, new() { ["A"] = "RENAMED" }, true));
            var edit = new ShadowGraphEdit(branch.Current); var collection = branch.Current.Read(Id(block))["AttributeDefinitions"];
            var id = edit.Create(typeof(AttributeDefinition).FullName!, ShadowObjectKind.Object, new Dictionary<string, ShadowValue>
            { ["Tag"] = ShadowValue.From("A"), ["Value"] = ShadowValue.From("new default"), ["Owner"] = new ShadowValue.Reference(Id(block)) });
            var pair = (ShadowValue.Structure)collection.Items.First().Value; pair = pair with { Fields = pair.Fields.SetItem("Key", ShadowValue.From("A")).SetItem("Value", new ShadowValue.Reference(id)) };
            edit.ReplaceSequence(collection.Id!.Value, "$items", collection.Items.Select(p => p.Value).Append(pair));
            edit.ReplaceSequence(collection.Id.Value, "Values", collection["Values"].Items.Select(p => p.Value).Append(new ShadowValue.Reference(id)));
            edit.ReplaceSequence(collection.Id.Value, "Tags", collection["Tags"].Items.Select(p => p.Value).Append(ShadowValue.From("A"))); branch.Apply(edit.Build());
            var editor = new EditorSession(source); editor.ApplyMerge(editor.PreviewMerge(source.Current, branch.Current), "Rename and reuse tag"); var added = block.AttributeDefinitions["A"];
            Check(block.AttributeDefinitions["RENAMED"] == a && a.Handle == handle && added != a && added.Value == "new default", "Reused tag replaced the retained definition");
            editor.Undo(); Check(block.AttributeDefinitions["A"] == a && a.Handle == handle && added.Owner == null && root.Attributes[0].Tag == "A", "Undo failed to free reused tag");
            editor.Redo(); Check(block.AttributeDefinitions["A"] == added && block.AttributeDefinitions["RENAMED"] == a, "Redo replaced a definition");
        }));
        tests.Add(("attribute-definition-names/history restores a binding that predates a definition-only rename", () =>
        {
            var (doc, block, root) = Seed(); var definition = block.AttributeDefinitions["A"]; var attribute = root.Attributes[0];
            block.RenameAttributeDefinitions(new Dictionary<string, string> { ["A"] = "NEW" });
            var source = new ShadowSession(doc); var editor = new EditorSession(source); var old = source.Current;
            var edit = new ShadowGraphEdit(old); edit.Set(Id(attribute), "Tag", ShadowValue.From("NEW")); editor.Execute("Follow definition rename", edit.Build());
            for (int cycle = 0; cycle < 3; cycle++)
            {
                Check(attribute.Tag == "NEW" && attribute.Definition == definition, "Following a definition lost the binding");
                editor.Undo(); Check(attribute.Tag == "A" && attribute.Definition == definition && definition.Tag == "NEW", "Undo refused an exact historical binding"); editor.Redo();
            }
            // A known historical snapshot cannot authorize a different mismatched tag.
            var current = source.Current; var invalid = new ShadowGraphEdit(current); invalid.Set(Id(attribute), "Tag", ShadowValue.From("FORGED"));
            Reject(() => source.Apply(invalid.Build() with { HistoricalTarget = old }));
            Check(ReferenceEquals(current, source.Refresh()) && attribute.Tag == "NEW", "History hint authorized an unrelated binding");
        }));
        tests.Add(("attribute-definition-names/insert in the same batch uses proposed names defaults and geometry", () =>
        {
            var (doc, block, root) = Seed(); var definition = block.AttributeDefinitions["A"]; var source = new ShadowSession(doc);
            var branch = new ShadowMemoryStore(source.Current); branch.Apply(Rename(branch.Current, block, new() { ["A"] = "NEW" }, true));
            var edit = new ShadowGraphEdit(branch.Current); edit.Set(Id(definition), "Value", ShadowValue.From("new default"));
            edit.Set(Id(definition), "Height", ShadowValue.From(7.0)); edit.Set(Id(definition), "Position", ShadowValue.From(new Vec3(2, 3, 0))); branch.Apply(edit.Build());
            branch.Apply(new DraftingPlanner().Plan(branch.Current, new("INSERT", [new(10, 20)], Reference: Id(block))).Patch);
            var editor = new EditorSession(source); editor.ApplyMerge(editor.PreviewMerge(source.Current, branch.Current), "Definition and new reference");
            var created = doc.Entities.Inserts.Single(i => i != root); var attribute = created.Attributes.First();
            Check(attribute.Tag == "NEW" && attribute.Value == "new default" && attribute.Definition == definition && attribute.Height == 7 && Vector3.Distance(attribute.Position, new(12, 23, 0)) < 1e-10,
                "New insertion used the source definition before proposed edits");
            editor.Undo(); Check(doc.Entities.Inserts.Single() == root && definition.Tag == "A", "Combined insertion undo failed"); editor.Redo();
            Check(doc.Entities.Inserts.Any(i => i == created) && created.Attributes.First() == attribute && attribute.Tag == "NEW", "Combined insertion redo lost identities");
        }));
        tests.Add(("attribute-definition-names/new insertion uses final text mode and canonical renamed resources", () =>
        {
            var (doc, block, root) = Seed(); var definition = block.AttributeDefinitions["A"];
            var style = new netDxf.Tables.TextStyle("ORIGINAL_STYLE", "txt.shx"); definition.Style = style;
            definition.SetMText(new AttributeMText { Value = "old multiline", Style = style }); var source = new ShadowSession(doc);
            var branch = new ShadowMemoryStore(source.Current); var edit = new ShadowGraphEdit(branch.Current);
            edit.Set(Id(definition), "TextData", ShadowValue.Null); edit.Set(Id(definition), "HasMText", ShadowValue.From(false));
            edit.Set(Id(style), "Name", ShadowValue.From("RENAMED_STYLE")); branch.Apply(edit.Build());
            branch.Apply(new DraftingPlanner().Plan(branch.Current, new("INSERT", [new(10, 20)], Reference: Id(block), Scale: new(2, 3, 1))).Patch);
            var editor = new EditorSession(source); editor.ApplyMerge(editor.PreviewMerge(source.Current, branch.Current), "Single-line and insert");
            var attribute = doc.Entities.Inserts.Single(i => i != root).Attributes.First();
            Check(!attribute.HasMText && attribute.Style == style && style.Name == "RENAMED_STYLE" && !doc.TextStyles.Contains("ORIGINAL_STYLE"), "Insertion used an obsolete text mode or duplicated a renamed style");
        }));
        tests.Add(("attribute-definition-names/unsupported implicit insertion frame rejects before definition changes", () =>
        {
            var (doc, block, root) = Seed(); var definition = block.AttributeDefinitions["A"]; definition.SetMText(new AttributeMText { Value = "multiline" });
            var source = new ShadowSession(doc); var before = source.Current; var branch = new ShadowMemoryStore(before); var edit = new ShadowGraphEdit(branch.Current);
            edit.Set(Id(definition), "Value", ShadowValue.From("pending default")); branch.Apply(edit.Build());
            branch.Apply(new DraftingPlanner().Plan(branch.Current, new("INSERT", [new(10, 20)], Reference: Id(block), Scale: new(2, 3, 1))).Patch);
            Reject(() => source.Apply(ShadowGraphPatch.Between(before, branch.Current)));
            Check(ReferenceEquals(before, source.Refresh()) && definition.Value == "default A" && doc.Entities.Inserts.Single() == root, "Insertion frame failure partially mutated or stopped the source");
        }));
        foreach (bool mirror in new[] { false, true }) tests.Add(("attribute-definition-names/new insertion uses proposed units and drawing mirror policy " + mirror, () =>
        {
            var (doc, block, root) = Seed(); var definition = block.AttributeDefinitions["A"]; definition.Position = new(2, 3, 0); definition.Alignment = TextAlignment.TopLeft;
            var source = new ShadowSession(doc); var branch = new ShadowMemoryStore(source.Current); var edit = new ShadowGraphEdit(branch.Current);
            edit.Set(Id(block.Record), "Units", new ShadowValue.Atom("netDxf.Units.DrawingUnits", "Inches"));
            var variables = branch.Current.Document["DrawingVariables"].Id!.Value;
            edit.Set(variables, "InsUnits", new ShadowValue.Atom("netDxf.Units.DrawingUnits", "Millimeters")); edit.Set(variables, "MirrText", ShadowValue.From(mirror)); branch.Apply(edit.Build());
            branch.Apply(new DraftingPlanner().Plan(branch.Current, new("INSERT", [new(10, 20)], Reference: Id(block), Scale: new(-2, 3, 1), Rotation: 25)).Patch);
            source.Apply(ShadowGraphPatch.Between(source.Current, branch.Current)); var actual = doc.Entities.Inserts.Single(i => i != root).Attributes.First();
            var expectedDoc = new DxfDocument(); expectedDoc.DrawingVariables.InsUnits = netDxf.Units.DrawingUnits.Millimeters; expectedDoc.DrawingVariables.MirrText = mirror;
            var expectedRoot = new Insert((Block)block.Clone()) { Position = new(10, 20, 0), Scale = new(-2, 3, 1), Rotation = 25 };
            expectedDoc.Entities.Add(expectedRoot); expectedRoot.TransformAttributes(); var expected = expectedRoot.Attributes.First();
            Check(Vector3.Distance(actual.Position, expected.Position) < 1e-9 && Math.Abs(actual.Height - expected.Height) < 1e-9 &&
                actual.IsBackward == expected.IsBackward && actual.IsUpsideDown == expected.IsUpsideDown && actual.Alignment == expected.Alignment,
                "Implicit attributes ignored proposed unit conversion or MIRRTEXT");
        }));
        tests.Add(("attribute-definition-names/custom insertion factories retain authored defaults", () =>
        {
            var (doc, block, root) = Seed(); var source = new ShadowSession(doc);
            source.ObjectFactories.Register<Insert>((node, resolver) =>
            {
                var result = new Insert((Block)resolver.Resolve(((ShadowValue.Reference)node["Block"]).Id)); result.Attributes[0].Value = "factory default"; return result;
            });
            source.Apply(new DraftingPlanner().Plan(source.Current, new("INSERT", [new(10, 20)], Reference: Id(block))).Patch);
            Check(doc.Entities.Inserts.Single(i => i != root).Attributes[0].Value == "factory default", "Implicit initialization overrode the registered factory");
        }));
        tests.Add(("attribute-definition-names/implicit multiline defaults use proposed embedded style names", () =>
        {
            var (doc, block, root) = Seed(); var definition = block.AttributeDefinitions["A"];
            definition.SetMText(new AttributeMText { Value = "embedded", Style = new netDxf.Tables.TextStyle("OLD_EMBEDDED", "txt.shx") });
            var style = definition.GetTextData(false).Content.Style; var source = new ShadowSession(doc); var branch = new ShadowMemoryStore(source.Current);
            var edit = new ShadowGraphEdit(branch.Current); edit.Set(Id(style), "Name", ShadowValue.From("NEW_EMBEDDED")); branch.Apply(edit.Build());
            branch.Apply(new DraftingPlanner().Plan(branch.Current, new("INSERT", [new(10, 20)], Reference: Id(block))).Patch);
            var editor = new EditorSession(source); editor.ApplyMerge(editor.PreviewMerge(source.Current, branch.Current), "Embedded style rename and insert");
            var added = doc.Entities.Inserts.Single(i => i != root).Attributes[0];
            Check(added.GetTextData(false).Content.Style == style && !doc.TextStyles.Contains("OLD_EMBEDDED") && added.GetMText().Value == "embedded", "Embedded defaults resurrected the old style name");
            for (int cycle = 0; cycle < 2; cycle++)
            {
                editor.Undo(); Check(style.Name == "OLD_EMBEDDED" && !doc.TextStyles.Contains("NEW_EMBEDDED") && doc.Entities.Inserts.Single() == root, "Undo retained the inserted reference or renamed style");
                editor.Redo(); Check(added.GetTextData(false).Content.Style == style && style.Name == "NEW_EMBEDDED" && !doc.TextStyles.Contains("OLD_EMBEDDED"), "Redo duplicated the embedded style");
            }
        }));
    }

    private static ShadowGraphPatch Rename(ShadowSnapshot snapshot, Block block, Dictionary<string, string> mapping, bool instances)
    {
        var edit = new ShadowGraphEdit(snapshot); var collection = snapshot.Read(Id(block))["AttributeDefinitions"];
        var renamed = new Dictionary<ObjectId, string>(); var pairs = collection.Items.Select(entry =>
        {
            var definition = entry["Value"]; var old = definition["Tag"].Text;
            if (!mapping.TryGetValue(old, out var tag)) return entry.Value;
            renamed.Add(definition.Id!.Value, tag); edit.Set(definition.Id.Value, "Tag", ShadowValue.From(tag));
            var pair = (ShadowValue.Structure)entry.Value; return pair with { Fields = pair.Fields.SetItem("Key", ShadowValue.From(tag)) };
        }).ToArray();
        edit.ReplaceSequence(collection.Id!.Value, "$items", pairs);
        edit.ReplaceSequence(collection.Id.Value, "Tags", collection["Values"].Items.Select(d => ShadowValue.From(renamed.GetValueOrDefault(d.Id!.Value, d["Tag"].Text))));
        if (instances) foreach (var node in snapshot.Objects.Values.Where(n => n.TypeName == typeof(netDxf.Entities.Attribute).FullName))
            if (node["Definition"] is ShadowValue.Reference definition && renamed.TryGetValue(definition.Id, out var tag)) edit.Set(node.Id, "Tag", ShadowValue.From(tag));
        return edit.Build();
    }
    private static (DxfDocument, Block, Insert) Seed()
    {
        var block = new Block("NAMES"); foreach (var tag in new[] { "A", "B" }) block.AttributeDefinitions.Add(new AttributeDefinition(tag) { Value = "default " + tag });
        var root = new Insert(block); foreach (var attribute in root.Attributes) attribute.Value = "assigned " + attribute.Tag;
        var doc = new DxfDocument(); doc.Entities.Add(root); return (doc, block, root);
    }
    private static ObjectId Id(DxfObject value) => new("H:" + value.Handle);
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Reject(Action action) { try { action(); } catch (Exception) { return; } throw new InvalidOperationException("Expected rejection"); }
}
