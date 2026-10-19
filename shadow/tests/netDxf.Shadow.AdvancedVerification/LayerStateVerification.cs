using netDxf;
using netDxf.Objects;
using netDxf.Tables;
using netDxf.Shadow;
using netDxf.Shadow.Drafting;
using netDxf.Shadow.Editor;
using netDxf.Shadow.NetDxf;
using netDxf.Shadow.Studio;
using System.Text.Json;
using System.Collections.Immutable;

internal static class LayerStateVerification
{
    private static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    private static ObjectId State(ShadowSession s, string name) => DraftingLayerStates.Read(s.Current).Single(x => x.Name == name).Id;
    internal static void Register(List<(string Name, Action Run)> tests)
    {
        tests.Add(("layer-states/capture restore selective fields and native identities", () =>
        {
            var doc = new DxfDocument(); var layer = doc.Layers.Add(new Layer("Process")
                { Color = AciColor.FromTrueColor(0x345678), Lineweight = Lineweight.W35, Transparency = new Transparency(35), IsLocked = true, IsFrozenInNewViewports = true });
            doc.DrawingVariables.CLayer = "Process";
            var source = new ShadowSession(doc); var editor = new EditorSession(source);
            DraftingLayerStates.Plan(source.Current, new("save", Name: "Normal", Description: "Process layers")).Execute(editor);
            var id = State(source, "Normal"); var saved = doc.Layers.StateManager["Normal"];
            Check(saved.Properties.Count == 2 && saved.CurrentLayer == "Process" && saved.Description == "Process layers", "State was not saved into the native manager.");
            Check(!ReferenceEquals(saved.Properties["Process"].Color, layer.Color), "Saved color aliases live layer color.");
            DraftingLayers.Plan(source.Current, new(source.Current.Document["Layers"].Items.Single(l => l["Name"].Text == "Process").Id,
                ColorIndex: 1, Visible: false, Locked: false, Transparency: 70, FrozenInNewViewports: false)).Execute(editor);
            DraftingLayerStates.Plan(source.Current, new("restore", id, Fields: LayerStateFields.Hidden | LayerStateFields.Color)).Execute(editor);
            Check(layer.IsVisible && layer.Color.UseTrueColor && layer.Color.R == 0x34 && !layer.IsLocked && layer.Transparency.Value == 70, "Selective restoration overwrote an unselected property.");
            editor.Undo(); Check(!layer.IsVisible && layer.Color.Index == 1, "Restore undo failed."); editor.Redo();
            DraftingLayerStates.Plan(source.Current, new("restore", id)).Execute(editor);
            Check(layer.IsLocked && layer.IsFrozenInNewViewports && layer.Transparency.Value == 35 && layer.Lineweight == Lineweight.W35, "Full global properties were not restored.");
            Check(ReferenceEquals(doc.Layers["Process"], layer) && ReferenceEquals(doc.Layers.StateManager["Normal"], saved), "Restoration replaced native layer/state identity.");
            foreach (var binary in new[] { false, true })
            {
                using var stream = new MemoryStream(); Check(doc.Save(stream, binary), "State DXF save failed."); stream.Position = 0;
                var loaded = DxfDocument.Load(stream)!; var state = loaded.Layers.StateManager["Normal"];
                Check(state.CurrentLayer == "Process" && state.Properties.Count == 2 && state.Properties["Process"].Color.R == 0x34, "Layer state did not survive DXF transport.");
            }
        }));
        tests.Add(("layer-states/update rename delete and undo preserve saved payload", () =>
        {
            var doc = new DxfDocument(); var source = new ShadowSession(doc); var editor = new EditorSession(source);
            DraftingLayerStates.Plan(source.Current, new("save", Name: "Initial")).Execute(editor);
            var id = State(source, "Initial"); var saved = doc.Layers.StateManager["Initial"];
            DraftingLayers.Plan(source.Current, new(null, "New", ColorIndex: 2)).Execute(editor);
            DraftingLayerStates.Plan(source.Current, new("save", id, "Initial", "Updated")).Execute(editor);
            Check(saved.Properties.ContainsKey("New"), "State update omitted a newly created layer.");
            editor.Undo(); Check(!saved.Properties.ContainsKey("New"), "State update undo lost previous membership."); editor.Redo();
            DraftingLayerStates.Plan(source.Current, new("rename", id, "Saved")).Execute(editor);
            Check(ReferenceEquals(doc.Layers.StateManager["Saved"], saved), "Rename changed state identity.");
            DraftingLayerStates.Plan(source.Current, new("delete", id)).Execute(editor);
            Check(!doc.Layers.StateManager.Contains("Saved"), "State deletion was not applied.");
            editor.Undo(); Check(ReferenceEquals(doc.Layers.StateManager["Saved"], saved) && saved.Properties.Count == 2, "Delete undo lost state or properties."); editor.Redo();
        }));
        tests.Add(("layer-states/invalid restore and cancellation have no source effects", () =>
        {
            var doc = new DxfDocument(); var state = new LayerState("Imported", doc.Layers);
            doc.Layers.StateManager.Add(state); state.Properties["0"].LinetypeName = "Missing";
            var source = new ShadowSession(doc); var editor = new EditorSession(source); var before = source.Current;
            try { DraftingLayerStates.Plan(source.Current, new("restore", State(source, "Imported"))).Execute(editor); throw new Exception("Accepted missing resource."); }
            catch (ArgumentException) { }
            Check(ReferenceEquals(before, source.Current) && editor.State.UndoCount == 0, "Invalid state changed source/history.");
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            try { DraftingLayerStates.Plan(source.Current, new("save", Name: "Cancelled"), cancellationToken: cancelled.Token); throw new Exception("Ignored cancellation."); }
            catch (OperationCanceledException) { }
            Check(!doc.Layers.StateManager.Contains("Cancelled"), "Cancelled save created a state.");
        }));
        tests.Add(("layer-states/unlisted visibility and stored viewport flags remain independent", () =>
        {
            var doc = new DxfDocument(); var native = new LayerState("Original", doc.Layers);
            native.Properties["0"].Flags |= LayerPropertiesFlags.VpFrozen; doc.Layers.StateManager.Add(native);
            var source = new ShadowSession(doc); var editor = new EditorSession(source); var id = State(source, "Original");
            DraftingLayerStates.Plan(source.Current, new("save", id, "Original")).Execute(editor);
            Check(native.Properties["0"].Flags.HasFlag(LayerPropertiesFlags.VpFrozen), "Capturing global flags erased stored viewport-only data.");
            DraftingLayers.Plan(source.Current, new(null, "Later")).Execute(editor);
            DraftingLayerStates.Plan(source.Current, new("restore", id, TurnOffUnlistedLayers: true)).Execute(editor);
            Check(!doc.Layers["Later"].IsVisible && !doc.Layers["0"].IsFrozen, "Viewport-only flag was applied globally or an unlisted layer stayed on.");
            editor.Undo(); Check(doc.Layers["Later"].IsVisible, "Unlisted visibility was not undoable.");
        }));
        tests.Add(("layer-states/studio workflow portable selectors survive rename reload and deletion", () =>
        {
            using var studio = new Studio(false);
            JsonElement J(object value) => JsonSerializer.SerializeToElement(value, StudioJson.CreateOptions());
            long Revision() => J(studio.CadState()).GetProperty("state").GetProperty("revision").GetInt64();
            ObjectId Named(string kind, string name) => new(J(studio.CadResources()).GetProperty(kind).EnumerateArray()
                .Single(x => x.GetProperty("name").GetString() == name).GetProperty("id").GetProperty("value").GetString()!);
            studio.CadWorkflowStart(new(Revision(), "Layer state regression"));
            studio.CadLayer(new(Revision(), new(null, "Process", ColorIndex: 3)));
            studio.CadLayerState(new(Revision(), new("save", Name: "Normal")));
            studio.CadLayer(new(Revision(), new(Named("layers", "Process"), ColorIndex: 1, Visible: false)));
            studio.CadLayerState(new(Revision(), new("restore", Named("layerStates", "Normal"))));
            studio.CadLayerState(new(Revision(), new("rename", Named("layerStates", "Normal"), "Renamed")));
            studio.History("undo", Revision()); studio.History("redo", Revision());
            studio.CadWorkflowRoundTrip(new(Revision()));
            studio.CadLayerState(new(Revision(), new("delete", Named("layerStates", "Renamed"))));
            studio.History("undo", Revision());
            var scenario = studio.CadWorkflowExport();
            Check(scenario.Steps.Length == 10, J(studio.CadWorkflowStatus()).GetRawText());
            var replay = studio.CadWorkflowReplay(new(Revision(), scenario));
            Check(replay.Passed && replay.Complete, J(replay).GetRawText());
        }));
        tests.Add(("layer-states/definition-independent state edits merge from an immutable branch", () =>
        {
            var doc = new DxfDocument(); var source = new ShadowSession(doc); var live = new EditorSession(source);
            var baseline = source.Current; var branch = new ShadowMemoryStore(baseline); var branchEditor = new EditorSession(branch);
            DraftingLayerStates.Plan(branch.Current, new("save", Name: "Branch state")).Execute(branchEditor);
            Check(!doc.Layers.StateManager.Contains("Branch state"), "Detached state editing changed live source.");
            var merge = live.PreviewMerge(baseline, branch.Current); Check(merge.CanApply, "Uncontended state merge conflicted.");
            live.ApplyMerge(merge, "Merge state"); Check(doc.Layers.StateManager.Contains("Branch state"), "Merged state was not adopted natively.");
            live.Undo(); Check(!doc.Layers.StateManager.Contains("Branch state"), "Merged state undo failed.");
        }));
        tests.Add(("layer-states/resource usage includes saved name references before removal", () =>
        {
            var doc = new DxfDocument(); var layer = doc.Layers.Add(new Layer("Process"));
            var line = doc.Linetypes.Add(new Linetype("Stored"));
            var state = new LayerState("Normal", doc.Layers); doc.Layers.StateManager.Add(state);
            state.Properties["Process"].LinetypeName = line.Name;
            var source = new ShadowSession(doc); var editor = new EditorSession(source); var before = source.Current;
            foreach (var (table, kind, name) in new[] { ("Layers", "layer", "Process"), ("Linetypes", "linetype", "Stored") })
            {
                var id = source.Current.Document[table].Items.Single(l => l["Name"].Text == name).Id!.Value;
                Check(DraftingResources.Uses(source.Current, id) > 0, "Saved named resource reference was not counted.");
                try { DraftingResources.Plan(source.Current, new(kind, "delete", id)); throw new Exception("Allowed deleting a saved resource reference."); }
                catch (ArgumentException) { }
            }
            Check(ReferenceEquals(before, source.Current) && editor.State.UndoCount == 0, "Resource validation changed the document.");
        }));
        tests.Add(("layer-states/missing saved layer is refused before non-undoable native creation", () =>
        {
            var doc = new DxfDocument(); var state = new LayerState("Imported", doc.Layers);
            doc.Layers.StateManager.Add(state);
            var source = new ShadowSession(doc); var editor = new EditorSession(source); var before = source.Current;
            // An imported/detached snapshot may have unresolved named resources.
            var edit = new ShadowGraphEdit(before); var saved = before.Read(State(source, "Imported"));
            var value = saved["Properties"].Items.First()["Value"].Object!;
            var missing = edit.Create(value.TypeName, value.Kind, value.Properties.SetItem("Name", ShadowValue.From("Missing")));
            edit.ReplaceSequence(saved.Id!.Value, "Properties", saved["Properties"].Items.Select(p => p.Value).Append(
                new ShadowValue.Structure("System.Collections.Generic.KeyValuePair`2", new Dictionary<string, ShadowValue>
                { ["Key"] = ShadowValue.From("Missing"), ["Value"] = new ShadowValue.Reference(missing) }.ToImmutableDictionary())));
            var detached = edit.Build().Apply(before);
            try { DraftingLayerStates.Plan(detached, new("restore", State(source, "Imported"))); throw new Exception("Created an unremovable layer."); }
            catch (ArgumentException error) { Check(error.Message.Contains("Missing"), "Missing layer was not identified."); }
            Check(ReferenceEquals(before, source.Current) && editor.State.UndoCount == 0 && !doc.Layers.Contains("Missing"), "Invalid restoration changed the document.");
        }));
        tests.Add(("layer-states/current-layer transitions reject frozen targets without partial changes", () =>
        {
            var doc = new DxfDocument(); var a = doc.Layers.Add(new Layer("A")); var b = doc.Layers.Add(new Layer("B") { IsFrozen = true });
            doc.DrawingVariables.CLayer = "A";
            var source = new ShadowSession(doc); var editor = new EditorSession(source);
            DraftingLayerStates.Plan(source.Current, new("save", Name: "A current")).Execute(editor);
            var state = State(source, "A current");
            ObjectId Layer(string name) => source.Current.Document["Layers"].Items.Single(l => l["Name"].Text == name).Id!.Value;
            DraftingLayers.Plan(source.Current, new(Layer("B"), Frozen: false)).Execute(editor);
            DraftingResources.Plan(source.Current, new("layer", "current", Layer("B"))).Execute(editor);
            DraftingLayers.Plan(source.Current, new(Layer("A"), Frozen: true)).Execute(editor);
            var before = source.Current; var history = editor.State.UndoCount;
            foreach (var request in new[] {
                new LayerStateEdit("restore", state, Fields: LayerStateFields.Color),
                new LayerStateEdit("restore", state, RestoreCurrentLayer: false) })
            {
                try { DraftingLayerStates.Plan(source.Current, request).Execute(editor); throw new Exception("Accepted a frozen current layer."); }
                catch (ArgumentException) { }
                Check(ReferenceEquals(before, source.Current) && editor.State.UndoCount == history, "Rejected restore changed source or history.");
            }
            DraftingLayerStates.Plan(source.Current, new("restore", state)).Execute(editor);
            Check(doc.DrawingVariables.CLayer == "A" && !a.IsFrozen && b.IsFrozen, "Restore did not thaw the saved current layer and restore the previous current layer.");
            editor.Undo(); Check(doc.DrawingVariables.CLayer == "B" && a.IsFrozen && !b.IsFrozen, "Current-layer transition was not atomic on undo.");
        }));
        tests.Add(("layer-states/resource rename preserves stored references through undo and redo", () =>
        {
            var doc = new DxfDocument(); var line = doc.Linetypes.Add(new Linetype("ProcessDash"));
            var layer = doc.Layers.Add(new Layer("Process") { Linetype = line }); doc.DrawingVariables.CLayer = "Process";
            var source = new ShadowSession(doc); var editor = new EditorSession(source);
            DraftingLayerStates.Plan(source.Current, new("save", Name: "Process state")).Execute(editor);
            var state = doc.Layers.StateManager["Process state"];
            var layerId = source.Current.Document["Layers"].Items.Single(l => l["Name"].Text == "Process").Id!.Value;
            DraftingLayers.Plan(source.Current, new(layerId, "Renamed")).Execute(editor);
            Check(state.CurrentLayer == "Renamed" && state.Properties.ContainsKey("Renamed"), "Layer rename did not follow stored state references.");
            editor.Undo(); Check(state.CurrentLayer == "Process" && state.Properties.ContainsKey("Process"), "Layer rename undo lost saved references.");
            editor.Redo();
            var lineId = source.Current.Document["Linetypes"].Items.Single(l => l["Name"].Text == "ProcessDash").Id!.Value;
            DraftingResources.Plan(source.Current, new("linetype", "save", lineId, Linetype: new("RenamedDash"))).Execute(editor);
            Check(state.Properties["Renamed"].LinetypeName == "RenamedDash", "Linetype rename did not follow saved references.");
            editor.Undo(); Check(state.Properties["Renamed"].LinetypeName == "ProcessDash", "Linetype rename undo lost saved references.");
            editor.Redo();
        }));
    }
}
