using System.Collections.Immutable;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Tables;
using netDxf.Shadow;
using netDxf.Shadow.Editor;
using netDxf.Shadow.NetDxf;
using Att = netDxf.Entities.Attribute;

internal static class AttributeTextVerification
{
    public static void Register(List<(string Name, Action Run)> tests)
    {
        void Test(string name, Action run) => tests.Add(("attribute-text/" + name, run));
        Test("external embedded edits are captured without ephemeral graph identities", () =>
        {
            var (doc, attribute, source, _) = Seed(); var before = source.Current; var id = Id(attribute);
            Check(ReferenceEquals(before, source.Refresh()), "No-op refresh created a new snapshot");
            Check(Packet(before, id)["Content"].Value is ShadowValue.Structure, "Embedded content is not a detached value");
            var style = (ShadowValue.Reference)Packet(before, id)["Content"]["Style"].Value;
            Check(style.Id == Id(doc.TextStyles["BODY"]), "Embedded style did not retain canonical graph identity");
            var data = attribute.GetTextData(); data.Content.Value = "external second"; attribute.SetTextData(data);
            var after = source.Refresh();
            Check(!ReferenceEquals(before, after) && before.Objects.Count == after.Objects.Count, "Embedded edit was omitted or grew the graph");
            Check(Packet(before, id)["Content"]["Value"].Text == "embedded first" && Packet(after, id)["Content"]["Value"].Text == "external second", "Historical packet changed");
            Check(ReferenceEquals(after, source.Refresh()), "Packet recapture is unstable");
        });
        Test("value style and binary auxiliary edits preserve identity through undo redo", () =>
        {
            var (doc, attribute, source, editor) = Seed(); var id = Id(attribute); var before = source.Current;
            var packet = (ShadowValue.Structure)Packet(before, id).Value;
            var content = (ShadowValue.Structure)packet.Fields["Content"];
            var modified = packet with { Fields = packet.Fields.SetItem("Content", content with { Fields = content.Fields
                .SetItem("Value", ShadowValue.From("edited\\PΩ"))
                .SetItem("Style", new ShadowValue.Reference(Id(doc.TextStyles["OTHER"]))) }) };
            editor.SetProperty(id, "TextData", modified);
            Check(attribute.GetMText().Value == "edited\\PΩ" && attribute.GetMText().Style.Name == "OTHER", "Text packet edit did not reach source");
            Check(attribute.Value == "fallback" && attribute.Handle != null && attribute.ProxyGraphics.SequenceEqual(new byte[] { 1, 2, 3 }), "Exact packet edit changed independent fallback/proxy");
            for (int i = 0; i < 3; i++)
            {
                editor.Undo(); Check(ShadowValue.Equivalent(Packet(before, id).Value, Packet(source.Current, id).Value), "Undo lost stored fields");
                Check(ReferenceEquals(attribute, doc.Entities.Inserts.Single().Attributes.Single()), "Undo replaced native attribute");
                editor.Redo(); Check(ShadowValue.Equivalent(modified, Packet(source.Current, id).Value), "Redo lost stored fields");
            }
            Check(doc.TextStyles["OTHER"].GetReferences().Any(), "Embedded style usage missing");
        });
        Test("remove and restore complete packet regenerates derived metadata", () =>
        {
            var (_, attribute, source, editor) = Seed(); var id = Id(attribute); var before = source.Current;
            editor.SetProperty(id, "TextData", ShadowValue.Null);
            Check(!attribute.HasMText && attribute.IsPositionLocked == null, "Packet removal left derived metadata");
            editor.Undo(); Check(attribute.HasMText && attribute.IsPositionLocked == true, "Undo did not restore derived metadata");
            Check(ShadowValue.Equivalent(Packet(before, id).Value, Packet(source.Current, id).Value), "Undo omitted optional text data");
            editor.Redo(); Check(!attribute.HasMText, "Redo failed");
        });
        Test("definition embedded text supports source editing and history", () =>
        {
            var (_, attribute, source, editor) = Seed(); var definition = attribute.Definition; var id = Id(definition);
            var packet = (ShadowValue.Structure)Packet(source.Current, id).Value;
            var content = (ShadowValue.Structure)packet.Fields["Content"];
            editor.SetProperty(id, "TextData", packet with { Fields = packet.Fields.SetItem("Content", content with
                { Fields = content.Fields.SetItem("Value", ShadowValue.From("definition edited")) }) });
            Check(definition.GetMText().Value == "definition edited" && attribute.GetMText().Value == "embedded first", "Definition and instance content are not independent");
            editor.Undo(); Check(definition.GetMText().Value == "definition first", "Definition undo failed");
        });
        foreach (bool binary in new[] { false, true }) Test("stored packet survives DXF roundtrip " + binary, () =>
        {
            var (doc, attribute, source, _) = Seed(); var before = Packet(source.Current, Id(attribute));
            using var stream = new MemoryStream(); Check(doc.Save(stream, binary), "Save failed"); stream.Position = 0;
            var loaded = DxfDocument.Load(stream)!; var restored = loaded.Entities.Inserts.Single().Attributes.Single();
            var after = Packet(new ShadowSession(loaded).Current, Id(restored));
            // Native handles remain stable across this transport, including the style reference.
            Check(ShadowValue.Equivalent(before.Value, after.Value), "Transport changed stored packet");
        });
        Test("detached preview merges as one undoable operation", () =>
        {
            var (_, attribute, source, editor) = Seed(); var id = Id(attribute); var baseline = source.Current;
            var memory = new ShadowMemoryStore(baseline); var branch = new EditorSession(memory);
            var packet = (ShadowValue.Structure)Packet(baseline, id).Value;
            var content = (ShadowValue.Structure)packet.Fields["Content"];
            branch.SetProperty(id, "TextData", packet with { Fields = packet.Fields.SetItem("Content", content with
                { Fields = content.Fields.SetItem("Value", ShadowValue.From("preview")) }) });
            Check(attribute.GetMText().Value == "embedded first", "Preview mutated source");
            var merge = editor.PreviewMerge(baseline, branch.Snapshot); Check(merge.CanApply, "Packet merge conflicted");
            editor.ApplyMerge(merge, "Edit embedded text"); Check(attribute.GetMText().Value == "preview" && editor.State.UndoCount == 1, "Merge was not one operation");
            editor.Undo(); Check(attribute.GetMText().Value == "embedded first", "Merged packet undo failed");
        });
        for (int fault = 0; fault < 5; fault++)
        {
            int f = fault;
            Test("malformed packets leave native state and history intact " + f, () =>
            {
                var (_, attribute, source, editor) = Seed(); var id = Id(attribute); var before = source.Current;
                var packet = (ShadowValue.Structure)Packet(before, id).Value;
                packet = f switch
                {
                    0 => packet with { TypeName = "Unregistered.Type" },
                    1 => packet with { Fields = packet.Fields.Remove("Locked").Add("Unexpected", ShadowValue.Null) },
                    2 => packet with { Fields = packet.Fields.SetItem("Kind", new ShadowValue.Atom("System.Int16", "4")) },
                    3 => packet with { Fields = packet.Fields.SetItem("Content", ShadowValue.Null) },
                    _ => packet with { Fields = packet.Fields.SetItem("Auxiliary", new ShadowValue.Sequence([
                        new ShadowValue.Structure(typeof(DxfTag).FullName!, new Dictionary<string, ShadowValue>
                        { ["Code"] = new ShadowValue.Atom("System.Int16", "1005"), ["Value"] = ShadowValue.From("AB") }.ToImmutableDictionary())])) }
                };
                Throws(() => editor.SetProperty(id, "TextData", packet));
                Check(ReferenceEquals(before, source.Current) && editor.State.UndoCount == 0 && attribute.GetMText().Value == "embedded first", "Failed packet edit changed source/history");
            });
        }
        Test("unobserved embedded changes conflict before unrelated source writes", () =>
        {
            var (doc, attribute, source, editor) = Seed(); var data = attribute.GetTextData(); data.Content.Value = "external"; attribute.SetTextData(data);
            Throws(() => editor.SetProperty(Id(attribute), "Value", ShadowValue.From("changed fallback")));
            Check(attribute.Value == "fallback" && attribute.GetMText().Value == "external" && editor.State.UndoCount == 0, "Source conflict was missed");
        });
    }

    private static (DxfDocument, Att, ShadowSession, EditorSession) Seed()
    {
        var doc = new DxfDocument(DxfVersion.AutoCad2018); var block = new Block("EMBEDDED");
        var definition = new AttributeDefinition("VALUE") { Value = "fallback" };
        definition.SetMText(new AttributeMText { Value = "definition first", Style = new TextStyle("BODY", "romans.shx") });
        block.AttributeDefinitions.Add(definition); var root = new Insert(block); doc.Entities.Add(root);
        doc.TextStyles.Add(new TextStyle("OTHER", "romans.shx")); var attribute = root.Attributes.Single();
        var packet = attribute.GetTextData(); packet.Content.Value = "embedded first"; packet.Locked = true; packet.Version = 0;
        packet.FieldLength = 12; packet.SecondaryFlag = 7; packet.SecondaryAlignment = new Vector3(1, 2, 3);
        packet.Auxiliary.Add(new DxfTag(1, "auxiliary")); packet.Auxiliary.Add(new DxfTag(310, new byte[] { 0, 255 }));
        attribute.SetTextData(packet); attribute.ProxyGraphics = new byte[] { 1, 2, 3 };
        var source = new ShadowSession(doc); return (doc, attribute, source, new EditorSession(source));
    }
    private static ObjectId Id(DxfObject value) => new("H:" + value.Handle.ToUpperInvariant());
    private static ShadowReader Packet(ShadowSnapshot snapshot, ObjectId id) => snapshot.Read(id)["TextData"];
    private static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    private static void Throws(Action action)
    {
        try { action(); } catch (Exception error) when (error is ArgumentException or InvalidOperationException or NotSupportedException) { return; }
        throw new Exception("Invalid text packet was accepted");
    }
}
