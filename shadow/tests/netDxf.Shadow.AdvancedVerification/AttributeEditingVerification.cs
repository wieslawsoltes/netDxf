using System.Collections.Immutable;
using System.Text.Json;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.Shadow;
using netDxf.Shadow.Drafting;
using netDxf.Shadow.Editor;
using netDxf.Shadow.NetDxf;
using netDxf.Shadow.Studio;
using netDxf.Tables;
using Att = netDxf.Entities.Attribute;

internal static class AttributeEditingVerification
{
    public static void Register(List<(string Name, Action Run)> tests)
    {
        void Test(string name, Action run) => tests.Add(("attribute-editor/" + name, run));
        Test("single-line text options and appearance are one reversible native transaction", () =>
        {
            var (doc, root, source, editor) = Seed(); var a = root.Attributes[0]; var before = source.Current;
            var attributeId = Id(a); var rootId = Id(root);
            DraftingAttributes.Plan(before, rootId, [new(attributeId, Value: "P-101", TextStyle: "Other", Height: 4, Rotation: 30,
                WidthFactor: 1.4, ObliqueAngle: 10, Backward: true, Layer: "Process", Linetype: "ByBlock", Lineweight: "W35", Color: "#123456", PositionLocked: true)]).Execute(editor);
            Check(a.Value == "P-101" && a.Style.Name == "Other" && a.Layer.Name == "Process" && a.Color.R == 0x12 && a.IsPositionLocked == true, "Native appearance edit failed");
            Check(a.Height == 4 && a.Rotation == 30 && a.WidthFactor == 1.4 && a.IsBackward, "Native text options failed");
            Check(editor.State.UndoCount == 1 && a.ProxyGraphics == null && root.ProxyGraphics == null, "Batch history or proxy invalidation failed");
            for (int i = 0; i < 3; i++)
            {
                editor.Undo(); Check(a.Value == "default" && a.Height == 2 && a.Layer.Name == "0", "Undo did not restore text/appearance");
                Check(a.ProxyGraphics.SequenceEqual(new byte[] { 1 }) && root.ProxyGraphics.SequenceEqual(new byte[] { 2 }), "Undo lost proxy data");
                Check(Id(a) == attributeId && Id(root) == rootId, "History replaced native identities"); editor.Redo();
            }
        });
        Test("multiline edits preserve fallback and packet metadata and replace orientation explicitly", () =>
        {
            var (_, root, source, editor) = Seed(true); var a = root.Attributes[0]; var original = a.GetTextData();
            DraftingAttributes.Plan(source.Current, Id(root), [new(Id(a), Value: "edited\\Psecond", TextStyle: "Other", Height: 6,
                Rotation: 45, Width: 40, Alignment: "MiddleCenter", PositionLocked: true, LineSpacing: 1.5, ExactSpacing: true)]).Execute(editor);
            var text = a.GetMText(); var packet = a.GetTextData();
            Check(text.Value == "edited\\Psecond" && text.Style.Name == "Other" && text.Height == 6 && text.RectangleWidth == 40
                && text.Rotation == 45 && text.TextDirection == null && text.AttachmentPoint == MTextAttachmentPoint.MiddleCenter, "Embedded layout edit failed");
            Check(a.Value == "default" && a.Height == 2 && a.Rotation == 0 && packet.FieldLength == original.FieldLength && packet.SecondaryFlag == original.SecondaryFlag, "Embedded edit overwrote fallback/metadata");
            Check(a.IsPositionLocked == true && packet.Locked == true, "Packet overwrote position-lock edit");
            editor.Undo(); Check(a.GetMText().Value == original.Content.Value && a.GetMText().TextDirection != null && a.IsPositionLocked == null, "Multiline undo lost original orientation/metadata");
            editor.Redo(); Check(a.GetMText().Rotation == 45 && a.IsPositionLocked == true, "Multiline redo failed");
        });
        Test("multi-attribute edits preview on an immutable branch without changing definitions", () =>
        {
            var (_, root, source, editor) = Seed(); var baseline = source.Current;
            var plan = DraftingAttributes.Plan(baseline, Id(root), root.Attributes.Select(a => new AttributeEdit(Id(a), Value: "instance " + a.Tag)).ToImmutableArray());
            var memory = new ShadowMemoryStore(baseline); plan.Execute(new EditorSession(memory));
            Check(root.Attributes[0].Value == "default", "Preview changed native instance");
            var merge = editor.PreviewMerge(baseline, memory.Current); editor.ApplyMerge(merge, "Edit attributes");
            Check(root.Attributes.All(a => a.Value == "instance " + a.Tag) && root.Block.AttributeDefinitions.Values.All(d => d.Value == "default"), "Instance edit changed definition values");
            editor.Undo(); Check(root.Attributes.All(a => a.Value == "default"), "Batch undo failed");
        });
        Test("unchanged edit does not invalidate proxies or create history", () =>
        {
            var (_, root, source, editor) = Seed(); var before = source.Current;
            DraftingAttributes.Plan(before, Id(root), [new(Id(root.Attributes[0]), Value: "default", Height: 2, TextStyle: "Standard")]).Execute(editor);
            Check(ReferenceEquals(before, source.Current) && editor.State.UndoCount == 0 && root.ProxyGraphics != null, "No-op edited source");
        });
        for (int fault = 0; fault < 8; fault++)
        {
            int f = fault;
            Test("invalid edits are refused before any attribute changes " + f, () =>
            {
                var (doc, root, source, editor) = Seed(f == 5); var a = root.Attributes[0];
                if (f == 6) a.Layer = doc.Layers.Add(new Layer("Locked") { IsLocked = true });
                if (f == 7) a.Flags = AttributeFlags.Constant;
                source.Refresh(); var before = source.Current;
                var request = f switch
                {
                    0 => new AttributeEdit(Id(a), Height: double.NaN), 1 => new AttributeEdit(Id(a), TextStyle: "missing"),
                    2 => new AttributeEdit(Id(a), Color: "bad"), 3 => new AttributeEdit(new("missing"), Value: "bad"),
                    4 => new AttributeEdit(Id(a), Alignment: "not alignment"), 5 => new AttributeEdit(Id(a), WidthFactor: 2),
                    _ => new AttributeEdit(Id(a), Value: "bad")
                };
                Throws(() => DraftingAttributes.Plan(before, Id(root), [new(Id(root.Attributes[1]), Value: "must remain unchanged"), request]).Execute(editor));
                Check(ReferenceEquals(before, source.Current) && editor.State.UndoCount == 0 && root.Attributes.All(x => x.Value == "default"), "Refused edit partially changed source");
            });
        }
        Test("cancellation and duplicate targets have no side effects", () =>
        {
            var (_, root, source, editor) = Seed(); var before = source.Current; var request = new AttributeEdit(Id(root.Attributes[0]), Value: "change");
            Throws(() => DraftingAttributes.Plan(before, Id(root), [request, request]));
            using var c = new CancellationTokenSource(); c.Cancel();
            try { DraftingAttributes.Plan(before, Id(root), [request], c.Token); throw new Exception("Cancellation ignored"); } catch (OperationCanceledException) { }
            Check(ReferenceEquals(before, source.Current) && editor.State.UndoCount == 0, "Cancelled edit changed source");
        });
        foreach (bool binary in new[] { false, true }) Test("edited packet and resources survive DXF transport " + binary, () =>
        {
            var (doc, root, source, editor) = Seed(true);
            DraftingAttributes.Plan(source.Current, Id(root), [new(Id(root.Attributes[0]), Value: "Ω\\Pline", Color: "ByBlock", TextStyle: "Other")]).Execute(editor);
            using var stream = new MemoryStream(); Check(doc.Save(stream, binary), "Save failed"); stream.Position = 0;
            var loaded = DxfDocument.Load(stream)!; var a = loaded.Entities.Inserts.Single().Attributes[0];
            Check(a.GetMText().Value == "Ω\\Pline" && a.GetMText().Style.Name == "Other" && a.Color.IsByBlock && a.Value == "default", "Saved attribute lost content or resources");
        });
        Test("Studio selection guards and portable workflow replay include owned attributes", () =>
        {
            var (doc, root, _, _) = Seed(true); using var stream = new MemoryStream(); doc.Save(stream); stream.Position = 0;
            using var studio = new Studio(false); studio.Load(stream);
            JsonElement J(object value) => JsonSerializer.SerializeToElement(value, StudioJson.CreateOptions());
            long Revision() => J(studio.CadState()).GetProperty("state").GetProperty("revision").GetInt64();
            studio.CadSelect(new(Revision(), "entities", Entities: [Id(root)]));
            var info = J(studio.CadReadAttributes(Revision()));
            var id = new ObjectId(info.GetProperty("attributes")[0].GetProperty("id").GetProperty("value").GetString()!);
            studio.CadWorkflowStart(new(Revision(), "Attribute editor regression"));
            studio.CadEditAttributes(new(Revision(), Id(root), [new(id, Value: "workflow\\Ptext", Height: 4)]));
            studio.History("undo", Revision()); studio.History("redo", Revision()); studio.CadWorkflowRoundTrip(new(Revision()));
            var scenario = studio.CadWorkflowExport(); var replay = studio.CadWorkflowReplay(new(Revision(), scenario));
            Check(replay.Passed && replay.Complete, J(replay).GetRawText());
            studio.CadSelect(new(Revision(), "clear"));
            Throws(() => studio.CadEditAttributes(new(Revision(), Id(root), [new(id, Value: "stale")])));
        });
    }
    private static (DxfDocument, Insert, ShadowSession, EditorSession) Seed(bool multiline = false)
    {
        var doc = new DxfDocument(DxfVersion.AutoCad2018); var block = new Block("ATTRIBUTES"); block.Entities.Add(new Line(Vector3.Zero, new Vector3(20, 0, 0)));
        foreach (var tag in new[] { "TAG", "DETAIL" }) block.AttributeDefinitions.Add(new AttributeDefinition(tag) { Value = "default", Height = 2, Position = new Vector3(0, tag == "TAG" ? 4 : 8, 0) });
        var root = new Insert(block); doc.Entities.Add(root); doc.TextStyles.Add(new TextStyle("Other", "DejaVuSans.ttf")); doc.Layers.Add(new Layer("Process"));
        if (multiline)
        {
            root.Attributes[0].SetMText(new AttributeMText { Value = "original\\Ptext", Position = new Vector3(0, 4, 0), TextDirection = Vector3.UnitX });
            var data = root.Attributes[0].GetTextData(); data.Version = 0; data.FieldLength = 12; data.SecondaryFlag = 7; root.Attributes[0].SetTextData(data);
        }
        root.Attributes[0].ProxyGraphics = [1]; root.ProxyGraphics = [2];
        var source = new ShadowSession(doc); return (doc, root, source, new EditorSession(source));
    }
    private static ObjectId Id(DxfObject value) => new("H:" + value.Handle.ToUpperInvariant());
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Throws(Action action) { try { action(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException or NotSupportedException) { return; } throw new Exception("Invalid attribute edit was accepted"); }
}
