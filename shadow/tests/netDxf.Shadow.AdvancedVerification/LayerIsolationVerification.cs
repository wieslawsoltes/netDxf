using System.Collections.Immutable;
using System.Text.Json;
using netDxf;
using netDxf.Tables;
using netDxf.Shadow;
using netDxf.Shadow.Drafting;
using netDxf.Shadow.Editor;
using netDxf.Shadow.NetDxf;
using netDxf.Shadow.Studio;

internal static class LayerIsolationVerification
{
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static ObjectId Id(ShadowSnapshot s, string name) => s.Document["Layers"].Items.Single(l => l["Name"].Text == name).Id!.Value;
    private static void Refuse(Action action)
    {
        try { action(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException or ShadowConflictException) { return; }
        throw new InvalidOperationException("Invalid layer isolation was accepted.");
    }
    internal static void Register(List<(string Name, Action Run)> tests)
    {
        foreach (var mode in new[] { "off", "lock" }) tests.Add(("layer-isolation/native flags identity one-undo " + mode, () =>
        {
            var doc = new DxfDocument(); var a = doc.Layers.Add(new Layer("A")); var b = doc.Layers.Add(new Layer("B"));
            var source = new ShadowSession(doc); var editor = new EditorSession(source);
            DraftingLayerIsolation.Plan(source.Current, new("isolate", [Id(source.Current, "A")], mode)).Execute(editor);
            Check(a.IsVisible && !a.IsFrozen && !a.IsLocked && (mode == "off" ? !b.IsVisible : b.IsLocked), "Isolation flags are wrong.");
            Check(editor.State.UndoCount == 1 && DraftingLayerIsolation.Read(source.Current).Active, "Isolation was not one native transaction.");
            editor.Undo(); Check(b.IsVisible && !b.IsLocked && !DraftingLayerIsolation.Read(source.Current).Active, "Undo left isolation flags or metadata.");
            editor.Redo(); Check(DraftingLayerIsolation.Read(source.Current).Active, "Redo lost restore metadata.");
            DraftingLayerIsolation.Plan(source.Current, new("restore")).Execute(editor);
            Check(b.IsVisible && !b.IsLocked && !DraftingLayerIsolation.Read(source.Current).Active, "Unisolate failed.");
            Check(ReferenceEquals(a, doc.Layers["A"]) && ReferenceEquals(b, doc.Layers["B"]), "Isolation replaced native layers.");
            editor.Undo(); Check(DraftingLayerIsolation.Read(source.Current).Active, "Undo of Unisolate did not recover its receipt.");
            editor.Redo(); Check(!DraftingLayerIsolation.Read(source.Current).Active, "Redo of Unisolate failed.");
        }));
        tests.Add(("layer-isolation/repeated modes preserve baseline and later resource changes", () =>
        {
            var doc = new DxfDocument(); var a = doc.Layers.Add(new Layer("A")); var b = doc.Layers.Add(new Layer("B"));
            var source = new ShadowSession(doc); var editor = new EditorSession(source);
            DraftingLayerIsolation.Plan(source.Current, new("isolate", [Id(source.Current, "A")])).Execute(editor);
            DraftingLayers.Plan(source.Current, new(Id(source.Current, "B"), Name: "Renamed", Visible: true, ColorIndex: 2, Plot: false, Locked: true)).Execute(editor);
            DraftingLayers.Plan(source.Current, new(null, "Later", ColorIndex: 3)).Execute(editor);
            DraftingLayerIsolation.Plan(source.Current, new("isolate", [Id(source.Current, "Renamed")], "lock")).Execute(editor);
            Check(a.IsLocked && b.IsVisible && !b.IsLocked, "Repeated isolation did not replace the mode/scope.");
            editor.Undo(); Check(!a.IsLocked && b.IsVisible && b.IsLocked && DraftingLayerIsolation.Read(source.Current).Mode == "off", "Repeated isolate undo lost the previous receipt.");
            editor.Redo();
            DraftingLayerIsolation.Plan(source.Current, new("restore")).Execute(editor);
            Check(a.IsVisible && !a.IsLocked && b.IsVisible && b.IsLocked && b.Color.Index == 2 && !b.Plot, "Restore erased edits after initial isolation.");
            Check(doc.Layers["Later"].IsVisible && !doc.Layers["Later"].IsLocked, "Restore damaged a newly created layer.");
        }));
        foreach (var binary in new[] { false, true }) tests.Add(("layer-isolation/save reload restore " + binary, () =>
        {
            var doc = new DxfDocument(); doc.Layers.Add(new Layer("A")); doc.Layers.Add(new Layer("B") { IsLocked = true });
            var source = new ShadowSession(doc); var editor = new EditorSession(source);
            DraftingLayerIsolation.Plan(source.Current, new("isolate", [Id(source.Current, "A")])).Execute(editor);
            using var stream = new MemoryStream(); Check(doc.Save(stream, binary), "Save failed."); stream.Position = 0;
            var loaded = DxfDocument.Load(stream)!; var reloaded = new ShadowSession(loaded);
            Check(DraftingLayerIsolation.Read(reloaded.Current).Active, "DXF lost isolation ownership.");
            DraftingLayerIsolation.Plan(reloaded.Current, new("restore")).Execute(new EditorSession(reloaded));
            Check(loaded.Layers["B"].IsVisible && loaded.Layers["B"].IsLocked, "Reload restore lost original layer flags.");
        }));
        tests.Add(("layer-isolation/current frozen restoration invalid input and cancellation remain atomic", () =>
        {
            var doc = new DxfDocument(); var a = doc.Layers.Add(new Layer("A") { IsFrozen = true, IsLocked = true });
            var source = new ShadowSession(doc); var editor = new EditorSession(source);
            DraftingLayerIsolation.Plan(source.Current, new("isolate", [Id(source.Current, "A")])).Execute(editor);
            DraftingResources.Plan(source.Current, new("layer", "current", Id(source.Current, "A"))).Execute(editor);
            var before = source.Current; var history = editor.State.UndoCount;
            Refuse(() => DraftingLayerIsolation.Plan(source.Current, new("restore")).Execute(editor));
            Refuse(() => DraftingLayerIsolation.Plan(source.Current, new("isolate", [new("missing")])));
            Refuse(() => DraftingLayerIsolation.Plan(source.Current, new("isolate", [Id(source.Current, "A")], "viewport")));
            try { DraftingLayerIsolation.Plan(source.Current, new("restore"), new(true)); throw new Exception("Cancellation ignored."); }
            catch (OperationCanceledException) { }
            Check(ReferenceEquals(before, source.Current) && editor.State.UndoCount == history && !a.IsFrozen, "Refused restoration changed source or history.");
            DraftingLayers.Plan(source.Current, new(Id(source.Current, "0"), Visible: true)).Execute(editor);
            DraftingResources.Plan(source.Current, new("layer", "current", Id(source.Current, "0"))).Execute(editor);
            DraftingLayerIsolation.Plan(source.Current, new("restore")).Execute(editor); Check(a.IsFrozen && a.IsLocked, "Original frozen state was lost.");
        }));
        tests.Add(("layer-isolation/malformed reserved XDATA refuses restoration without guesses", () =>
        {
            foreach (var payload in new[] { "2|off|1|0", "1|viewport|1|0", "1|off|9|0", "1|off|1|1", "1|off|7|0", "1|lock|01|5", new string('x', 255) })
            {
                var doc = new DxfDocument(); var data = new XData(new ApplicationRegistry(DraftingLayerIsolation.ApplicationName));
                data.XDataRecord.Add(new(XDataCode.String, payload)); doc.Layers["0"].XData.Add(data);
                var source = new ShadowSession(doc); var editor = new EditorSession(source); var before = source.Current;
                Refuse(() => DraftingLayerIsolation.Plan(source.Current, new("restore")).Execute(editor));
                Refuse(() => DraftingLayerIsolation.Plan(source.Current, new("isolate", [Id(source.Current, "0")])).Execute(editor));
                Check(ReferenceEquals(before, source.Current) && editor.State.UndoCount == 0 && doc.Layers["0"].IsVisible, "Malformed receipt changed flags.");
                Check(!DraftingLayerIsolation.Read(source.Current).Valid, "Malformed status pretended restoration was available.");
            }
        }));
        tests.Add(("layer-isolation/native metadata failure rolls back all layer flags and history", () =>
        {
            var doc = new DxfDocument(); doc.Layers.Add(new Layer("A")); var b = doc.Layers.Add(new Layer("B"));
            var source = new ShadowSession(doc); var editor = new EditorSession(source); var before = source.Current; var once = true;
            var seed = doc.DrawingVariables.HandleSeed;
            b.XDataAddAppReg += (_, _) => { if (once) { once = false; throw new InvalidOperationException("Injected metadata failure."); } };
            Refuse(() => DraftingLayerIsolation.Plan(source.Current, new("isolate", [Id(source.Current, "A")])).Execute(editor));
            Check(ReferenceEquals(before, source.Current) && editor.State.UndoCount == 0 && b.IsVisible && doc.Layers["0"].IsVisible, "Source failure left partial flags/history.");
            Check(!DraftingLayerIsolation.Read(source.Current).Active, "Source failure left an active isolation receipt.");
            Check(doc.DrawingVariables.HandleSeed == seed && !doc.ApplicationRegistries.Contains(DraftingLayerIsolation.ApplicationName), "Failure left table/header changes.");
            DraftingLayerIsolation.Plan(source.Current, new("isolate", [Id(source.Current, "A")])).Execute(editor);
            Check(long.Parse(doc.ApplicationRegistries[DraftingLayerIsolation.ApplicationName].Handle, System.Globalization.NumberStyles.HexNumber) >=
                long.Parse(seed, System.Globalization.NumberStyles.HexNumber), "Recovery reused a preexisting handle.");
            editor.Undo(); Check(b.IsVisible && !DraftingLayerIsolation.Read(source.Current).Active, "Session was unusable after rollback.");
        }));
        tests.Add(("layer-isolation/case insensitive reserved APPID collision refuses without replacing foreign data", () =>
        {
            var doc = new DxfDocument(); var data = new XData(new ApplicationRegistry(DraftingLayerIsolation.ApplicationName.ToLowerInvariant()));
            data.XDataRecord.Add(new(XDataCode.String, "foreign data")); doc.Layers["0"].XData.Add(data);
            var source = new ShadowSession(doc); var editor = new EditorSession(source); var before = source.Current;
            Refuse(() => DraftingLayerIsolation.Plan(source.Current, new("isolate", [Id(source.Current, "0")])).Execute(editor));
            Refuse(() => DraftingLayerIsolation.Plan(source.Current, new("restore")).Execute(editor));
            Check(ReferenceEquals(before, source.Current) && editor.State.UndoCount == 0 && ReferenceEquals(data, doc.Layers["0"].XData[DraftingLayerIsolation.ApplicationName]) &&
                (string)data.XDataRecord.Single().Value == "foreign data", "Lowercase collision was overwritten.");
        }));
        tests.Add(("layer-isolation/bare case insensitive APPID is reused and receipts remain restorable", () =>
        {
            var doc = new DxfDocument(); var registry = doc.ApplicationRegistries.Add(new ApplicationRegistry(DraftingLayerIsolation.ApplicationName.ToLowerInvariant()));
            doc.Layers.Add(new Layer("A")); var source = new ShadowSession(doc); var editor = new EditorSession(source);
            DraftingLayerIsolation.Plan(source.Current, new("isolate", [Id(source.Current, "A")])).Execute(editor);
            Check(ReferenceEquals(registry, doc.ApplicationRegistries[DraftingLayerIsolation.ApplicationName]) && DraftingLayerIsolation.Read(source.Current).Valid &&
                DraftingLayerIsolation.Read(source.Current).Active, "Existing APPID reuse lost ownership.");
            DraftingLayerIsolation.Plan(source.Current, new("restore")).Execute(editor);
            Check(doc.Layers["0"].IsVisible && !DraftingLayerIsolation.Read(source.Current).Active, "Case insensitive receipt was not removed.");
        }));
        tests.Add(("layer-isolation/studio recorded replay resolves portable layer identities", () =>
        {
            using var studio = new Studio(false);
            JsonElement J(object value) => JsonSerializer.SerializeToElement(value, StudioJson.CreateOptions());
            long Revision() => J(studio.CadState()).GetProperty("state").GetProperty("revision").GetInt64();
            ObjectId Layer(string name) => new(J(studio.CadResources()).GetProperty("layers").EnumerateArray().Single(l => l.GetProperty("name").GetString() == name).GetProperty("id").GetProperty("value").GetString()!);
            studio.CadWorkflowStart(new(Revision(), "Layer isolation"));
            studio.CadLayer(new(Revision(), new(null, "A")));
            studio.CadLayer(new(Revision(), new(null, "B")));
            studio.CadLayerIsolation(new(Revision(), new("isolate", [Layer("A")], "lock")));
            studio.History("undo", Revision()); studio.History("redo", Revision());
            studio.CadWorkflowRoundTrip(new(Revision()));
            studio.CadLayerIsolation(new(Revision(), new("restore")));
            var replay = studio.CadWorkflowReplay(new(Revision(), studio.CadWorkflowExport()));
            Check(replay.Passed && replay.Complete, J(replay).GetRawText());
        }));
    }
}
