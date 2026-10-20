using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using netDxf;
using netDxf.Objects;
using netDxf.Tables;
using netDxf.Shadow;
using netDxf.Shadow.Drafting;
using netDxf.Shadow.Editor;
using netDxf.Shadow.NetDxf;
using netDxf.Shadow.Studio;

internal static class LayerStateTransferVerification
{
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Reject(Action action, string message)
    {
        try { action(); } catch (Exception error) when (error is ArgumentException or InvalidOperationException or FormatException or IOException or InvalidDataException or OperationCanceledException) { return; }
        throw new InvalidOperationException(message);
    }
    internal static void Register(List<(string Name, Action Run)> tests)
    {
        tests.Add(("layer-state-transfer/native mask strict LAS and legacy compatibility", () =>
        {
            var state = new LayerState("Transfer", [new Layer("0"), new Layer("Pipes") { Color = AciColor.FromTrueColor(0x1278AB) }])
            { RestoreMask = (LayerStateRestoreMask)unchecked((int)0x81010221), Description = "A preserved state" };
            state.Properties["Pipes"].Flags |= (LayerPropertiesFlags)0x1000;
            state.Properties["Pipes"].Transparency = Transparency.FromLayerStateAlphaValue(0);
            var text = state.ToLasText(); var parsed = LayerState.FromLasText(text);
            Check(parsed.RestoreMask == state.RestoreMask && parsed.Properties["Pipes"].Flags == state.Properties["Pipes"].Flags, "Mask/entry unknown bits changed.");
            Check(parsed.Properties["Pipes"].Transparency.Value == 0 && parsed.Properties["Pipes"].Transparency.StoredAlphaValue == 0, "Opaque saved alpha0 changed.");
            Check(Transparency.FromAlphaValue(0).IsByBlock, "Generic transparency semantics changed.");
            Check(((LayerState)parsed.Clone()).RestoreMask == parsed.RestoreMask, "Clone dropped mask.");
            using var bytes = new MemoryStream(); parsed.WriteLas(bytes); Check(bytes.CanWrite, "Writer closed caller stream.");
            bytes.Position = 0; Check(LayerState.ReadLas(bytes).RestoreMask == state.RestoreMask && bytes.CanRead, "Stream transfer changed mask or ownership.");
            var path = Path.Combine(Path.GetTempPath(), "layer-state-" + Guid.NewGuid().ToString("N") + ".las");
            try { File.WriteAllText(path, text); Check(LayerState.Load(path).RestoreMask == state.RestoreMask, "Legacy Load lost supported mask."); }
            finally { File.Delete(path); }
        }));
        tests.Add(("layer-state-transfer/strict bounded packets reject ambiguous or unsupported input", () =>
        {
            var text = new LayerState("Valid", [new Layer("0")]).ToLasText();
            Reject(() => LayerState.FromLasText(text + "\n8"), "Dangling group accepted.");
            Reject(() => LayerState.FromLasText(text + "  0\nLAYERSTATE\n  1\nSecond\n"), "Second state accepted.");
            Reject(() => LayerState.FromLasText(text + "  2\nNamedPlotStyle\n"), "Unsupported plot-style loss accepted.");
            Reject(() => LayerState.FromLasText(text + "  8\n0\n 90\n8\n"), "Case-collapsed duplicate layer accepted.");
            Reject(() => LayerState.FromLasText(new string('X', LayerState.MaximumLasBytes + 1)), "Unbounded input accepted.");
            Reject(() => new LayerState("Empty") { Description = new string('X', LayerState.MaximumLasBytes + 1) }.ToLasText(), "Empty state escaped output bound.");
            Reject(() => new LayerState("Empty") { Description = new string('界', LayerState.MaximumLasBytes / 2) }.ToLasText(), "Multibyte output escaped byte bound.");
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            Reject(() => LayerState.FromLasText(text, cancelled.Token), "Cancelled LAS parse succeeded.");
            using var target = new MemoryStream([42]);
            Reject(() => new LayerState("Bad") { Description = "line\nbreak" }.WriteLas(target), "Invalid text was exported.");
            Check(target.ToArray().SequenceEqual(new byte[] { 42 }), "Failed validation wrote partial packet.");
        }));
        tests.Add(("layer-state-transfer/native DXF text and binary retain mask identities", () =>
        {
            foreach (var binary in new[] { false, true })
            {
                var doc = new DxfDocument(); var state = new LayerState("Saved", doc.Layers) { RestoreMask = (LayerStateRestoreMask)0x43210221 };
                doc.Layers.StateManager.Add(state);
                using var bytes = new MemoryStream(); Check(doc.Save(bytes, binary), "DXF save failed."); bytes.Position = 0;
                var restored = DxfDocument.Load(bytes).Layers.StateManager["Saved"];
                Check(restored.RestoreMask == state.RestoreMask && restored.Handle == state.Handle, "DXF dropped mask or native identity.");
            }
        }));
        tests.Add(("layer-state-transfer/edit saved entries preserves live layers identity and inverse", () =>
        {
            var doc = new DxfDocument(); var layer = doc.Layers.Add(new Layer("Pipes")); doc.Layers.Add(new Layer("Notes"));
            var state = new LayerState("Saved", [doc.Layers["0"], layer]) { RestoreMask = (LayerStateRestoreMask)0x400003FF };
            doc.Layers.StateManager.Add(state); state.Properties["Pipes"].Flags |= (LayerPropertiesFlags)0x4000;
            var saved = state.Properties["Pipes"]; var source = new ShadowSession(doc); var editor = new EditorSession(source);
            var id = DraftingLayerStates.Read(source.Current).Single().Id;
            DraftingLayerStates.Plan(source.Current, new("edit", id,
                Entries: [new("Pipes", Visible: false, Locked: true, Color: "#ABCDEF", Transparency: 35)],
                AddLayers: ["Notes"], RemoveLayers: ["0"], CurrentLayer: "Notes")).Execute(editor);
            Check(ReferenceEquals(state.Properties["Pipes"], saved) && state.Properties.ContainsKey("Notes") && !state.Properties.ContainsKey("0"), "Saved membership/identity failed.");
            Check(layer.IsVisible && !layer.IsLocked && layer.Color.Index == 7 && layer.Transparency.Value == 0, "Saved edit changed drawing.");
            Check(((int)saved.Flags & 0x4000) != 0 && saved.Color.R == 0xAB && saved.Transparency.Value == 35 && state.CurrentLayer == "Notes", "Saved edit lost fields.");
            editor.Undo(); Check(state.Properties.ContainsKey("0") && !state.Properties.ContainsKey("Notes") && state.CurrentLayer == "0" && saved.Color.Index == 7, "Entry undo failed.");
            editor.Redo(); Check(state.Properties.ContainsKey("Notes"), "Entry redo failed.");
            DraftingLayerStates.Plan(source.Current, new("options", id, Fields: LayerStateFields.Color)).Execute(editor);
            Check((int)state.RestoreMask == (0x400003FF & ~(1|2|4|8|16|32|64|128|1024)) + 32, "Mask mapping changed unknown/viewport bits.");
            Check(DraftingLayerStates.Read(source.Current).Single().Fields == LayerStateFields.Color, "Mask projection mismatch.");
            editor.Undo(); Check((int)state.RestoreMask == 0x400003FF, "Mask undo failed.");
        }));
        tests.Add(("layer-state-transfer/resource mappings and overwrite remain explicit atomic edits", () =>
        {
            var doc = new DxfDocument(); var layer = doc.Layers.Add(new Layer("Destination")); var source = new ShadowSession(doc); var editor = new EditorSession(source);
            var portable = new LayerStateTransfer("Imported", "", "Missing", false, 0x40000021,
                [new("Missing", 0x800C, "#123456", 7, true, "Unknown", "W35", 0, 0)]);
            var analysis = DraftingLayerStates.AnalyzeImport(source.Current, portable, new());
            Check(analysis.Conflicts.Select(c => c.Kind).ToHashSet().SetEquals(["layer", "linetype"]), "Missing resource diagnostics incomplete.");
            var options = new LayerStateImportOptions(Layers: [new("Missing", "Destination")], Linetypes: [new("Unknown", "Continuous")]);
            var before = source.Current; DraftingLayerStates.PlanImport(before, portable, options).Execute(editor);
            var state = doc.Layers.StateManager["Imported"]; var handle = state.Handle;
            Check(state.Properties["Destination"].Transparency.Value == 0 && state.Properties["Destination"].Transparency.StoredAlphaValue == 0, "Import changed alpha0.");
            Check(layer.IsVisible && !layer.IsLocked && layer.Lineweight == Lineweight.Default, "Import restored live properties implicitly.");
            Reject(() => DraftingLayerStates.PlanImport(source.Current, portable, options), "Silent name overwrite accepted.");
            var oldProperty = state.Properties["Destination"];
            DraftingLayerStates.PlanImport(source.Current, portable with { Description = "Replacement" }, options with { Overwrite = true }).Execute(editor);
            Check(ReferenceEquals(state, doc.Layers.StateManager["Imported"]) && state.Handle == handle && ReferenceEquals(oldProperty, state.Properties["Destination"]), "Overwrite replaced native identities.");
            editor.Undo(); Check(state.Description == "", "Overwrite undo failed.");
            editor.Undo(); Check(!doc.Layers.StateManager.Contains("Imported") && doc.Layers.Contains("Destination"), "Import undo damaged resource ownership.");
            editor.Redo(); Check(doc.Layers.StateManager.Contains("Imported"), "Import redo failed.");
        }));
        tests.Add(("layer-state-transfer/source collection rejection rolls back saved entry edit", () =>
        {
            var doc = new DxfDocument(); doc.Layers.Add(new Layer("New")); var state = new LayerState("Saved", [doc.Layers["0"]]); doc.Layers.StateManager.Add(state);
            var source = new ShadowSession(doc); var editor = new EditorSession(source); var before = source.Current; bool fail = true;
            state.Properties.BeforeAddItem += (_, _) => { if (fail) { fail = false; throw new InvalidOperationException("Injected rejection"); } };
            Reject(() => DraftingLayerStates.Plan(source.Current, new("edit", DraftingLayerStates.Read(source.Current).Single().Id, AddLayers: ["New"])).Execute(editor), "Injected failure did not reject.");
            Check(ReferenceEquals(before, source.Current) && state.Properties.Count == 1 && !state.Properties.ContainsKey("New"), "Rejected edit leaked saved properties.");
            Check(editor.State.UndoCount == 0, "Rejected edit added history.");
        }));
        tests.Add(("layer-state-transfer/Studio preview is guarded nonmutating and consumed once", () =>
        {
            using var studio = new Studio(false);
            var text = new LayerState("Import", [new Layer("0")]).ToLasText(); var revision = studio.CadRevision;
            var preview = studio.CadLayerStatePreview(new(revision, text));
            Check(preview.Token is not null && studio.CadRevision == revision, "Preview mutated drawing or failed.");
            studio.CadLayerStateImport(new(revision, preview.Token!));
            Check(studio.CadRevision > revision, "Import did not commit.");
            var after = studio.CadRevision;
            Reject(() => studio.CadLayerStateImport(new(after, preview.Token!)), "Consumed preview was reused.");
            JsonElement Resources() => JsonSerializer.SerializeToElement(studio.CadResources(), StudioJson.CreateOptions());
            var exported = studio.CadLayerStateExport(Resources().GetProperty("layerStates")[0].GetProperty("id").GetProperty("value").GetString()!);
            Check(LayerState.FromLasText(exported.Text).Name == "Import", "Studio export differs.");
            studio.History("undo", studio.CadRevision);
            Check(Resources().GetProperty("layerStates").GetArrayLength() == 0, "Import did not use one undo entry.");
            var stale = studio.CadLayerStatePreview(new(studio.CadRevision, text));
            studio.CadLayer(new(studio.CadRevision, new(null, Name: "Changed")));
            Reject(() => studio.CadLayerStateImport(new(studio.CadRevision, stale.Token!)), "Changed-resource preview accepted.");
        }));
    }
}
