using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Objects;
using netDxf.Shadow;
using netDxf.Shadow.Editor;
using netDxf.Shadow.NetDxf;
using netDxf.Shadow.Studio;
using System.Text.Json;
using netDxf.Tables;
using netDxf.Units;
using Att = netDxf.Entities.Attribute;

internal static class AttributeSynchronizationVerification
{
    internal static void Register(List<(string Name, Action Run)> tests)
    {
        void Test(string name, Action run) => tests.Add(("attribute-sync/" + name, run));
        Test("all references reset appearance and retain values through preview undo redo and save", () =>
        {
            var (doc, block, roots) = Seed(); var old = roots.Select(r => r.Attributes.ToArray()).ToArray();
            var oldEnds = roots.Select(r => r.EndSequenceRecord).ToArray();
            foreach (var root in roots)
            {
                root.Attributes[0].Value = "authored " + root.Handle;
                root.Attributes[0].Layer = new Layer("OVERRIDE"); root.Attributes[0].Color = AciColor.Red;
                root.Attributes[0].Height = 23; root.Attributes[0].ProxyGraphics = [1]; root.ProxyGraphics = [2];
                root.XData.Add(new XData(new ApplicationRegistry("CUSTOM")));
            }
            var a = block.AttributeDefinitions["A"]; a.Height = 5; a.Layer = new Layer("DEFINITION");
            a.Color = AciColor.Blue; a.Alignment = TextAlignment.MiddleCenter;
            block.AttributeDefinitions.Remove("B"); block.AttributeDefinitions.Add(new AttributeDefinition("C") { Value = "new default", Position = new(3, 4, 0) });
            var source = new ShadowSession(doc); var editor = new EditorSession(source); var before = source.Current;
            var plan = AttributeSynchronization.Plan(before, Id(block));
            Check(plan.References.Length == 3 && plan.References.All(r => r.Added.Length == 1 && r.Removed.Single().Tag == "B"
                && r.RemovedXData.SequenceEqual(new[] { "CUSTOM" })), "Review report omitted reference or data removal");
            Check(roots.All(r => r.Attributes[0].Height == 23) && ReferenceEquals(before, source.Current), "Planning mutated source");
            var preview = editor.Preview("ATTSYNC", plan.Patch); Check(preview.Proposed.Read(Id(roots[0]))["XData"]["Values"].Items.Count() == 0, "Preview still shows removed XData");
            editor.ApplyPreview(preview); Check(editor.State.UndoCount == 1, "Sync was not one history entry");
            var added = roots.Select(r => r.Attributes[1]).ToArray();
            for (int cycle = 0; cycle < 3; cycle++)
            {
                for (int i = 0; i < roots.Length; i++)
                {
                    var root = roots[i]; var attribute = root.Attributes[0];
                    Check(attribute == old[i][0] && attribute.Value == "authored " + root.Handle, "Retained identity or assigned value changed");
                    Check(attribute.Layer == a.Layer && attribute.Color == a.Color && attribute.ProxyGraphics == null && root.ProxyGraphics == null, "Appearance/proxy reset failed");
                    Check(root.Attributes[1] == added[i] && added[i].Value == "new default" && root.XData.Count == 0, "Added values or metadata reset failed");
                }
                editor.Undo();
                for (int i = 0; i < roots.Length; i++) Check(roots[i].Attributes.SequenceEqual(old[i]) && roots[i].Attributes[0].Height == 23
                    && roots[i].XData.ContainsAppId("CUSTOM") && ReferenceEquals(roots[i].EndSequenceRecord, oldEnds[i]), "Undo did not restore original state");
                editor.Redo();
            }
            foreach (bool binary in new[] { false, true })
            {
                using var data = new MemoryStream(); Check(doc.Save(data, binary), "Save failed"); data.Position = 0; var loaded = DxfDocument.Load(data)!;
                var loadedRoots = loaded.Blocks.SelectMany(b => b.Entities.OfType<Insert>()).Where(r => r.Block.Name == block.Name).ToArray();
                Check(loadedRoots.Length == roots.Length && loadedRoots.All(r => r.Attributes.Select(x => x.Tag).SequenceEqual(new[] { "A", "C" }) && r.XData.Count == 0), "DXF reload lost synchronized attributes");
            }
            Check(AttributeSynchronization.Plan(source.Current, Id(block)).Patch.Changes.IsEmpty, "Repeated sync should be a no-op");
        });
        foreach (bool mirrorText in new[] { false, true }) Test("native geometry uses insertion units tilted normals and mirror policy " + mirrorText, () =>
        {
            var (doc, block, roots) = Seed(); doc.DrawingVariables.MirrText = mirrorText; doc.DrawingVariables.InsUnits = DrawingUnits.Millimeters;
            block.Record.Units = DrawingUnits.Inches; block.Origin = new(1, 2, 3);
            var definition = block.AttributeDefinitions["A"]; definition.Position = new(4, 5, 2); definition.Normal = new(1, 2, 3); definition.Rotation = 21;
            definition.WidthFactor = .8; definition.ObliqueAngle = 8; definition.Alignment = TextAlignment.TopLeft;
            foreach (var root in roots) { root.Normal = new(2, 3, 4); root.Rotation = 35; root.Scale = new(-2, 3, 4); }
            var expected = roots.Select(r =>
            {
                var clone = new Insert(block) { Position = r.Position, Normal = r.Normal, Rotation = r.Rotation, Scale = r.Scale };
                // Bind the expected insertion to the same owning drawing for MIRRTEXT and units.
                r.Owner.Entities.Add(clone); clone.TransformAttributes(); var value = clone.Attributes[0]; r.Owner.Entities.Remove(clone); return value;
            }).ToArray();
            var source = new ShadowSession(doc); var editor = new EditorSession(source); editor.Execute("ATTSYNC", AttributeSynchronization.Plan(source.Current, Id(block)).Patch);
            for (int i = 0; i < roots.Length; i++)
            {
                var actual = roots[i].Attributes[0]; var target = expected[i];
                Check(Vector3.Distance(actual.Position, target.Position) < 1e-10 && Vector3.Distance(actual.Normal, target.Normal) < 1e-10
                    && Math.Abs(actual.Height - target.Height) < 1e-10 && Math.Abs(actual.Rotation - target.Rotation) < 1e-10
                    && actual.IsBackward == target.IsBackward && actual.Alignment == target.Alignment, "Projected geometry differs from native owned transformation");
            }
        });
        Test("multiline resets layout and style while retaining independent fallback and embedded values", () =>
        {
            var (doc, block, roots) = Seed();
            var definition = block.AttributeDefinitions["A"];
            definition.SetMText(new AttributeMText { Value = "default", Position = new(2, 3, 0), Height = 2, RectangleWidth = 50,
                Style = new TextStyle("MULTI", "simplex.shx"), AttachmentPoint = MTextAttachmentPoint.BottomRight });
            foreach (var root in roots)
            {
                root.Attributes[0].SetMText(new AttributeMText { Value = "first\\Psecond", Height = 6, RectangleWidth = 90 });
                root.Attributes[0].Value = "fallback only";
            }
            var source = new ShadowSession(doc); var editor = new EditorSession(source);
            editor.Execute("ATTSYNC", AttributeSynchronization.Plan(source.Current, Id(block)).Patch);
            foreach (var root in roots)
            {
                var attribute = root.Attributes[0]; var text = attribute.GetMText();
                Check(attribute.Value == "fallback only" && text.Value == "first\\Psecond" && !ReferenceEquals(text.Style, doc.TextStyles["MULTI"]),
                    "Independent values changed or detached getter unexpectedly shares styles");
                Check(attribute.GetTextData(false).Content.Style == doc.TextStyles["MULTI"] && text.RectangleWidth == 50 && text.AttachmentPoint == MTextAttachmentPoint.BottomRight,
                    "Embedded formatting/style reset failed");
            }
            editor.Undo(); Check(roots.All(r => r.Attributes[0].GetMText().RectangleWidth == 90), "Undo lost original embedded layout");
        });
        Test("text-mode conversion requires explicit policy and reports loss of formatting", () =>
        {
            var (doc, block, roots) = Seed(); var root = roots[0];
            root.Attributes[0].SetMText(new AttributeMText { Value = "visible\\Ptext" }); root.Attributes[0].Value = "independent fallback";
            var source = new ShadowSession(doc); var before = source.Current;
            Reject(() => AttributeSynchronization.Plan(before, Id(block)));
            Check(ReferenceEquals(before, source.Refresh()) && root.Attributes[0].HasMText, "Refused conversion mutated source");
            var plan = AttributeSynchronization.Plan(before, Id(block), new(ConvertTextModes: true));
            Check(plan.References.Sum(r => r.TextConversions.Length) == 1, "Conversion review is missing");
            var editor = new EditorSession(source); editor.Execute("Convert attribute", plan.Patch);
            Check(!root.Attributes[0].HasMText && root.Attributes[0].Value == "visible text", "Visible multiline value not retained");
            editor.Undo(); Check(root.Attributes[0].HasMText && root.Attributes[0].Value == "independent fallback", "Undo lost independent fallback");
        });
        Test("single-line literal escaping survives promotion to multiline", () =>
        {
            var (doc, block, roots) = Seed(); var value = "{literal} \\P";
            foreach (var root in roots) root.Attributes[0].Value = value;
            block.AttributeDefinitions["A"].SetMText(new AttributeMText { Value = "default" });
            var source = new ShadowSession(doc); new EditorSession(source).Execute("Convert", AttributeSynchronization.Plan(source.Current, Id(block), new(ConvertTextModes: true)).Patch);
            Check(roots.All(r => r.Attributes[0].Value == value && r.Attributes[0].GetMText().ToMText().PlainText() == value), "Single-line special characters became MTEXT commands");
        });
        Test("duplicate tags keep independent values in definition order", () =>
        {
            var (doc, block, roots) = Seed(); var root = roots[0]; var first = root.Attributes[0];
            var duplicate = new Att(block.AttributeDefinitions["A"]) { Value = "duplicate" }; var second = root.Attributes[1];
            root.ReplaceAttributes(new[] { second, duplicate, first });
            var source = new ShadowSession(doc); var editor = new EditorSession(source);
            editor.Execute("ATTSYNC", AttributeSynchronization.Plan(source.Current, Id(block)).Patch);
            Check(root.Attributes.SequenceEqual(new[] { duplicate, first, second }) && duplicate.Value == "duplicate", "Duplicate identity or ordering changed");
            editor.Undo(); Check(root.Attributes.SequenceEqual(new[] { second, duplicate, first }), "Original ordering not restored");
        });
        Test("all-or-nothing preflight protects locked references incoming references and stale plans", () =>
        {
            var (doc, block, roots) = Seed(); var locked = new Layer("LOCKED") { IsLocked = true }; roots[2].Layer = locked;
            var source = new ShadowSession(doc); Reject(() => AttributeSynchronization.Plan(source.Current, Id(block)));
            locked.IsLocked = false; block.AttributeDefinitions.Remove("B");
            var line = new Line(Vector2.Zero, Vector2.UnitX); doc.Entities.Add(line); line.PersistentReactors.Add(roots[0].Attributes[1]); source.Refresh();
            var before = source.Current; var plan = AttributeSynchronization.Plan(before, Id(block)); var editor = new EditorSession(source);
            Reject(() => editor.Execute("Referenced removal", plan.Patch));
            Check(ReferenceEquals(before, source.Refresh()) && roots.All(r => r.Attributes.Count == 2) && editor.State.UndoCount == 0, "Preflight failed after partial mutation");
            line.PersistentReactors.Clear(); source.Refresh(); Reject(() => editor.Execute("Stale", plan.Patch));
        });
        Test("empty definitions remove all attributes and restore exact originals", () =>
        {
            var (doc, block, roots) = Seed(); var original = roots.Select(r => r.Attributes.ToArray()).ToArray(); block.AttributeDefinitions.Clear();
            var source = new ShadowSession(doc); var editor = new EditorSession(source);
            editor.Execute("Remove all", AttributeSynchronization.Plan(source.Current, Id(block)).Patch);
            Check(roots.All(r => r.Attributes.Count == 0), "Orphan attributes remain"); editor.Undo();
            Check(roots.Select((r, i) => r.Attributes.SequenceEqual(original[i])).All(v => v), "Removed objects were recreated");
        });
        Test("branch definition changes project without source access and merge in one history operation", () =>
        {
            var (doc, block, roots) = Seed(); var source = new ShadowSession(doc); var before = source.Current;
            var branch = new ShadowMemoryStore(before); var edit = new ShadowGraphEdit(before);
            edit.Set(Id(block.AttributeDefinitions["A"]), "Height", ShadowValue.From(7.0)); branch.Apply(edit.Build());
            branch.Apply(AttributeSynchronization.Plan(branch.Current, Id(block)).Patch);
            Check(roots[0].Attributes[0].Height != 7, "Branch mutated native attributes");
            var editor = new EditorSession(source); editor.ApplyMerge(editor.PreviewMerge(before, branch.Current), "Definition and sync");
            Check(roots.All(r => r.Attributes[0].Height == 7) && editor.State.UndoCount == 1, "Branch ignored new definition or split history");
            editor.Undo(); Check(block.AttributeDefinitions["A"].Height != 7 && roots.All(r => r.Attributes[0].Height != 7), "Branch undo failed");
        });
        Test("Studio reset review renders applies records undo redo and replays isolated workflow", () =>
        {
            var (doc, block, roots) = Seed();
            foreach (var root in roots) { root.Attributes[0].Height = 12; root.Attributes[0].Value = "INSTANCE"; root.XData.Add(new XData(new ApplicationRegistry("REVIEW"))); }
            block.AttributeDefinitions.Remove("B"); block.AttributeDefinitions.Add(new AttributeDefinition("C") { Value = "new" });
            using var data = new MemoryStream(); doc.Save(data); data.Position = 0;
            using var studio = new Studio(false); studio.Load(data);
            JsonElement J(object value) => JsonSerializer.SerializeToElement(value, StudioJson.CreateOptions());
            long Revision() => studio.CadRevision;
            var blockId = new ObjectId(J(studio.CadState()).GetProperty("blocks").EnumerateArray().Single(b => b.GetProperty("name").GetString() == "SYNC").GetProperty("id").GetString()!);
            studio.CadWorkflowStart(new(Revision(), "Synchronize all block attributes"));
            var before = J(studio.State());
            var receipt = J(studio.CadEditPreview(new(Revision(), "ATTSYNC SYNC", [new("attribute-sync", AttributeSync: new(blockId))])));
            var summary = receipt.GetProperty("attributeSynchronizations")[0];
            Check(summary.GetProperty("referenceCount").GetInt32() == 3 && summary.GetProperty("removed").GetInt32() == 3
                && summary.GetProperty("removedXData").GetInt32() == 3 && summary.GetProperty("added").GetInt32() == 3, "Review summary lost reset scope");
            var id = receipt.GetProperty("previewId").GetString()!;
            Check(!studio.CadEditPreviewRender(id, false).Bytes.SequenceEqual(studio.CadEditPreviewRender(id, true).Bytes), "Preview did not show appearance reset");
            Check(J(studio.State()).GetProperty("undoCount").GetInt32() == before.GetProperty("undoCount").GetInt32(), "Preview created undo entry");
            studio.CadEditPreviewApply(new(Revision(), id)); studio.History("undo", Revision()); studio.History("redo", Revision());
            studio.CadWorkflowRoundTrip(new(Revision()));
            var replay = studio.CadWorkflowReplay(new(Revision(), studio.CadWorkflowExport()));
            Check(replay.Complete && replay.Passed, J(replay).GetRawText());
        });
        Test("cancellation budgets and unsupported embedded frames reject the entire proposal", () =>
        {
            var (doc, block, roots) = Seed(); var source = new ShadowSession(doc); var before = source.Current;
            using var cancel = new CancellationTokenSource(); cancel.Cancel();
            try { AttributeSynchronization.Plan(before, Id(block), cancellationToken: cancel.Token); throw new Exception("Cancellation ignored"); }
            catch (OperationCanceledException) { }
            Reject(() => AttributeSynchronization.Plan(before, Id(block), new(MaximumReferences: 2)));
            Reject(() => AttributeSynchronization.Plan(before, Id(block), new(MaximumAttributes: 2)));
            block.AttributeDefinitions["A"].SetMText(new AttributeMText { Value = "default" });
            foreach (var root in roots) root.Attributes[0].SetMText(new AttributeMText { Value = "assigned" });
            roots[2].Scale = new(2, 3, 4); source.Refresh(); before = source.Current;
            Reject(() => AttributeSynchronization.Plan(before, Id(block)));
            Check(ReferenceEquals(before, source.Refresh()) && roots.All(r => r.Attributes[0].GetMText().Value == "assigned"), "Rejected plan changed earlier references");
        });
    }

    private static (DxfDocument, Block, Insert[]) Seed()
    {
        var doc = new DxfDocument(); doc.DrawingVariables.InsUnits = DrawingUnits.Unitless;
        var block = new Block("SYNC"); block.Record.Units = DrawingUnits.Unitless;
        block.AttributeDefinitions.Add(new AttributeDefinition("A") { Value = "A default" });
        block.AttributeDefinitions.Add(new AttributeDefinition("B") { Value = "B default", Position = new(0, 3, 0) });
        var model = new Insert(block, new Vector3(10, 20, 0)); doc.Entities.Add(model);
        var paperLayout = new Layout("Sheet"); doc.Layouts.Add(paperLayout);
        var paper = new Insert(block, new Vector3(30, 40, 0)); paperLayout.AssociatedBlock.Entities.Add(paper);
        var container = new Block("CONTAINER"); container.Record.Units = DrawingUnits.Unitless;
        var nested = new Insert(block, new Vector3(50, 60, 0)); container.Entities.Add(nested); doc.Blocks.Add(container);
        return (doc, block, new[] { model, paper, nested });
    }
    private static ObjectId Id(DxfObject value) => new("H:" + value.Handle.ToUpperInvariant());
    private static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    private static void Reject(Action run) { try { run(); } catch { return; } throw new Exception("Expected refusal."); }
}
