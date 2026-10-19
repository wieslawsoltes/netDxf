using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Shadow;
using netDxf.Shadow.Editor;
using netDxf.Shadow.NetDxf;
using Att = netDxf.Entities.Attribute;

internal static class AttributeMembershipVerification
{
    internal static void Register(List<(string Name, Action Run)> tests)
    {
        void Test(string name, Action run) => tests.Add(("attribute-membership/" + name, run));
        foreach (bool empty in new[] { false, true }) Test("membership history retains native objects and sequence identities " + empty, () =>
        {
            var (_, root, source, editor) = Seed(empty); var rootId = Id(root); var original = root.Attributes.ToArray(); var originalEnd = root.EndSequenceRecord;
            var edit = new ShadowGraphEdit(source.Current);
            var added = edit.Create("netDxf.Entities.Attribute", ShadowObjectKind.Object, new Dictionary<string, ShadowValue>
            {
                ["Definition"] = new ShadowValue.Reference(Id(root.Block.AttributeDefinitions.Values.First())),
                ["Owner"] = new ShadowValue.Reference(rootId), ["Value"] = ShadowValue.From("new instance")
            });
            edit.ReplaceSequence(rootId, "Attributes", new[] { new ShadowValue.Reference(added) });
            edit.PruneUnreachable(); editor.Execute("Replace attributes", edit.Build());
            var instance = root.Attributes.Single(); var newEnd = root.EndSequenceRecord;
            Check(instance.Value == "new instance" && source.Current.Read(added).Id == added, "New attribute did not bind to authored identity");
            for (int i = 0; i < 3; i++)
            {
                editor.Undo(); Check(root.Attributes.SequenceEqual(original) && ReferenceEquals(root.EndSequenceRecord, originalEnd), "Undo replaced native objects or sequence identity");
                editor.Redo(); Check(root.Attributes.Single() == instance && ReferenceEquals(root.EndSequenceRecord, newEnd)
                    && source.Current.Read(added).Id == added && instance.Owner == root, "Redo lost owned identity");
            }
        });
        Test("reorder and definition rebinding form one reversible transaction", () =>
        {
            var (doc, root, source, editor) = Seed(false); var a = root.Attributes[0]; var b = root.Attributes[1]; var definition = a.Definition;
            var replacement = new AttributeDefinition("A") { Value = "new default" };
            root.Block.AttributeDefinitions.Remove("A"); root.Block.AttributeDefinitions.Add(replacement); source.Refresh(); editor = new EditorSession(source);
            var edit = new ShadowGraphEdit(source.Current); edit.Set(Id(a), "Definition", new ShadowValue.Reference(Id(replacement)));
            edit.ReplaceSequence(Id(root), "Attributes", new[] { new ShadowValue.Reference(Id(b)), new ShadowValue.Reference(Id(a)) });
            edit.PruneUnreachable(); editor.Execute("Rebind and reorder", edit.Build());
            Check(a.Definition == replacement && a.Value == "authored A" && root.Attributes[0] == b, "Replacement changed authored values");
            editor.Undo(); Check(a.Definition == definition && root.Attributes[0] == a, "Undo did not restore orphaned original definition");
            editor.Redo(); Check(a.Definition == replacement && root.Attributes[0] == b, "Redo did not restore binding");
            using var stream = new MemoryStream(); Check(doc.Save(stream, true), "Native save failed"); stream.Position = 0;
            Check(DxfDocument.Load(stream)!.Entities.Inserts.Single().Attributes.First().Tag == "B", "Binary output lost attribute ordering");
        });
        Test("foreign ownership and invalid rebinding fail before mutation", () =>
        {
            var (doc, root, source, editor) = Seed(false); var other = new Insert(root.Block); doc.Entities.Add(other); source.Refresh(); editor = new EditorSession(source); var before = source.Current;
            var edit = new ShadowGraphEdit(before); edit.ReplaceSequence(Id(root), "Attributes", new[] { new ShadowValue.Reference(Id(other.Attributes[0])) });
            Reject(() => editor.Execute("Foreign attribute", edit.Build()));
            edit = new ShadowGraphEdit(before); edit.Set(Id(root.Attributes[0]), "Definition", new ShadowValue.Reference(Id(root.Attributes[1].Definition)));
            Reject(() => editor.Execute("Wrong tag", edit.Build()));
            Check(ReferenceEquals(before, source.Current) && editor.State.UndoCount == 0 && root.Attributes.Count == 2, "Preflight refusal changed source");
        });
        Test("membership preview merges without replacing retained attributes", () =>
        {
            var (_, root, source, editor) = Seed(false); var a = root.Attributes[0]; var b = root.Attributes[1]; var baseline = source.Current;
            var edit = new ShadowGraphEdit(baseline); edit.ReplaceSequence(Id(root), "Attributes", new[] { new ShadowValue.Reference(Id(b)) }); edit.PruneUnreachable();
            var memory = new ShadowMemoryStore(baseline); new EditorSession(memory).Execute("Remove attribute", edit.Build());
            Check(root.Attributes[0] == a, "Preview mutated native membership");
            var merge = editor.PreviewMerge(baseline, memory.Current); editor.ApplyMerge(merge, "Apply membership preview");
            Check(root.Attributes.Single() == b && a.Owner == null, "Preview application replaced retained identity");
            editor.Undo(); Check(root.Attributes[0] == a && a.Owner == root, "Preview undo lost removed identity");
        });
        Test("failed notifications cannot leave a source session silently usable after allocator changes", () =>
        {
            var (_, root, source, editor) = Seed(false); var originals = root.Attributes.ToArray(); var before = source.Current;
            bool once = true; root.AttributeRemoved += (_, _) => { if (once) { once = false; throw new InvalidOperationException("callback failure"); } };
            var edit = new ShadowGraphEdit(before); edit.ReplaceSequence(Id(root), "Attributes", Array.Empty<ShadowValue>()); edit.PruneUnreachable();
            Reject(() => editor.Execute("Rejected callback", edit.Build()));
            Check(root.Attributes.SequenceEqual(originals) && originals.All(a => a.Owner == root) && editor.State.UndoCount == 0, "Journal failed to restore native membership");
            // Native handles are monotonic. If callback rollback reassigned them, the
            // adapter must report the unreconciled public graph instead of continuing.
            Reject(() => source.Refresh());
        });
    }
    private static (DxfDocument, Insert, ShadowSession, EditorSession) Seed(bool empty)
    {
        var doc = new DxfDocument(); var block = new Block("ATTRIBUTES");
        block.AttributeDefinitions.Add(new AttributeDefinition("A")); block.AttributeDefinitions.Add(new AttributeDefinition("B"));
        var root = empty ? Insert.CreateWithAttributes(block, Array.Empty<Att>()) : new Insert(block);
        doc.Entities.Add(root); foreach (var a in root.Attributes) a.Value = "authored " + a.Tag;
        var source = new ShadowSession(doc); return (doc, root, source, new EditorSession(source));
    }
    private static ObjectId Id(DxfObject value) => new("H:" + value.Handle.ToUpperInvariant());
    private static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    private static void Reject(Action run) { try { run(); } catch { return; } throw new Exception("Expected a refused operation."); }
}
