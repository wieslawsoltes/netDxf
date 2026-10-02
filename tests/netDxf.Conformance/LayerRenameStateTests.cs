// Copyright (c) netDxf contributors. Licensed under the MIT License.
using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.Objects;
using netDxf.Tables;

namespace NetDxf.Conformance;
internal static partial class Program
{
    private static void RegisterLayerRenameStateTests()
    {
        foreach (bool stored in new[] { false, true })
        foreach (bool early in new[] { false, true })
        {
            Run($"layer-rename-state/rename/{stored}/{early}", () => LrsRename(stored, early));
            Run($"layer-rename-state/veto/{stored}/{early}", () => LrsVeto(stored, early));
        }
        for (int mode = 0; mode < 9; mode++)
        { int m = mode; Run("layer-rename-state/callback/" + m, () => LrsCallback(m)); }
        for (int mode = 0; mode < 5; mode++)
        { int m = mode; Run("layer-rename-state/snapshot-refusal/" + m, () => LrsRefusal(m)); }
        for (int mode = 0; mode < 8; mode++)
        { int m = mode; Run("layer-rename-state/extended/" + m, () => LrsExtended(m)); }
        Run("layer-rename-state/aliases-and-live-views", LrsAliases);
        Run("layer-rename-state/noop-and-invalid", LrsNoop);
        Run("layer-rename-state/no-snapshots", LrsNoSnapshots);
        Run("layer-rename-state/unrelated-state", LrsUnrelated);
        foreach (var version in SupportedVersions)
        foreach (bool binary in new[] { false, true })
            Run($"layer-rename-state/wire/{version}/{binary}", () => LrsWire(version, binary));
    }

    private static DxfDocument LrsSeed(DxfVersion version = DxfVersion.AutoCad2018)
    {
        var doc = LsiSeed(version);
        var layer = doc.Layers.Add(new Layer("Walls") { Color = new AciColor(3), IsLocked = true, Lineweight = Lineweight.W30 });
        doc.Entities.Lines.Single().Layer = layer;
        doc.Layers.StateManager.AddNew("Saved", "Keep captured values");
        doc.Layers.StateManager.AddNew("Other", "Keep other current layer");
        doc.Layers.StateManager["Saved"].CurrentLayer = "Walls";
        doc.Layers.StateManager["Saved"].PaperSpace = true;
        doc.DrawingVariables.CLayer = "Walls";
        return doc;
    }

    private static void LrsAssert(DxfDocument doc, Layer layer, string name)
    {
        Check(layer.Name == name && ReferenceEquals(layer.Owner, doc.Layers)
            && ReferenceEquals(doc.Layers[name], layer) && ReferenceEquals(doc.GetObjectByHandle(layer.Handle), layer), "Layer index/name/ownership disagree");
        Equal(name, doc.DrawingVariables.CLayer, "Current layer did not follow rename");
        foreach (LayerState state in doc.Layers.StateManager)
        {
            Check(state.Properties.ContainsKey(name) && state.Properties[name].Name == name, "Saved property key/name did not follow rename");
            Check(!state.Properties.ContainsKey(name == "Walls" ? "Renamed" : "Walls"), "Old or premature snapshot key remains");
        }
        Equal(name, doc.Layers.StateManager["Saved"].CurrentLayer, "Saved current layer did not follow rename");
        Equal("0", doc.Layers.StateManager["Other"].CurrentLayer, "Unrelated saved current layer changed");
    }

    private static void LrsRename(bool stored, bool early)
    {
        var doc = LrsSeed(); if (stored) doc = LsiLoad(LsiSave(doc, false));
        var layer = doc.Layers["Walls"]; var properties = doc.Layers.StateManager["Saved"].Properties;
        var snapshot = properties["Walls"]; var color = snapshot.Color; var alpha = snapshot.Transparency;
        int callbacks = 0, dictionaryCallbacks = 0;
        TableObject.NameChangedEventHandler observer = (s,e) =>
        {
            callbacks++; LrsAssert(doc, layer, "Walls");
            Check(e.OldValue == "Walls" && e.NewValue == "Renamed", "Wrong layer rename event");
        };
        // Early subscription is tested before adoption of an authored/loaded clone.
        if (early)
        {
            layer = (Layer)layer.Clone(); layer.NameChanged += observer;
            doc = LsiSeed(DxfVersion.AutoCad2018); doc.Layers.Add(layer); doc.Entities.Lines.Single().Layer = layer;
            doc.Layers.StateManager.AddNew("Saved"); doc.Layers.StateManager.AddNew("Other");
            doc.Layers.StateManager["Saved"].CurrentLayer = "Walls"; doc.DrawingVariables.CLayer = "Walls";
            properties = doc.Layers.StateManager["Saved"].Properties; snapshot = properties["Walls"]; color = snapshot.Color; alpha = snapshot.Transparency;
        }
        else layer.NameChanged += observer;
        layer.Color = new AciColor(7); layer.IsLocked = false;
        properties.BeforeAddItem += (s,e) => dictionaryCallbacks++;
        properties.BeforeRemoveItem += (s,e) => dictionaryCallbacks++;
        properties.AddItem += (s,e) => dictionaryCallbacks++;
        properties.RemoveItem += (s,e) => dictionaryCallbacks++;
        Check(doc.Objects.Validate().Count == 0, "Initial graph");
        string handle = layer.Handle, seed = doc.DrawingVariables.HandleSeed;
        var stateIds = LspIds(doc); var refs = layer.GetReferences().Select(r => (r.Reference,r.Uses)).ToArray();
        var keys = properties.Keys; var values = properties.Values;
        layer.Name = "Renamed"; LrsAssert(doc, layer, "Renamed");
        Equal(1, callbacks, "Observer count"); Equal(0, dictionaryCallbacks, "Rename emitted membership events");
        Check(layer.Handle == handle && doc.DrawingVariables.HandleSeed == seed, "Rename allocated/replaced identity");
        Check(ReferenceEquals(properties, doc.Layers.StateManager["Saved"].Properties)
            && ReferenceEquals(snapshot, properties["Renamed"]) && ReferenceEquals(color, snapshot.Color)
            && ReferenceEquals(alpha, snapshot.Transparency) && snapshot.Color.Index == 3
            && snapshot.Flags.HasFlag(LayerPropertiesFlags.Locked), "Rename replaced or recaptured snapshot");
        Check(ReferenceEquals(keys, properties.Keys) && ReferenceEquals(values, properties.Values)
            && keys.Contains("Renamed") && !keys.Contains("Walls"), "Live key/value views became stale");
        var after = layer.GetReferences(); Equal(refs.Length, after.Count, "Reference source count");
        foreach (var r in refs) Equal(r.Uses, after.Single(x => ReferenceEquals(x.Reference,r.Reference)).Uses, "Reference multiplicity changed");
        LspRegistered(doc, stateIds);
        layer.NameChanged -= observer;
        doc.Layers.StateManager.Restore("Saved");
        Check(layer.Color.Index == 3 && layer.IsLocked, "Saved state no longer restores renamed layer");
        doc = LsiLoad(LsiSave(doc, true)); LrsAssert(doc, doc.Layers["Renamed"], "Renamed");
        Equal(handle, doc.Layers["Renamed"].Handle, "Rename changed persisted identity");
    }

    private static void LrsVeto(bool stored, bool early)
    {
        var doc = LrsSeed(); if (stored) doc = LsiLoad(LsiSave(doc,false));
        Layer layer = doc.Layers["Walls"];
        TableObject.NameChangedEventHandler veto = (s,e) => throw new ApplicationException("veto");
        if (early)
        {
            layer = (Layer)layer.Clone(); layer.NameChanged += veto; doc = LsiSeed(DxfVersion.AutoCad2018); doc.Layers.Add(layer);
            doc.Layers.StateManager.AddNew("Saved"); doc.Layers.StateManager.AddNew("Other");
            doc.Layers.StateManager["Saved"].CurrentLayer = "Walls"; doc.DrawingVariables.CLayer = "Walls";
        }
        else layer.NameChanged += veto;
        var property = doc.Layers.StateManager["Saved"].Properties["Walls"];
        using var iterator = doc.Layers.StateManager["Saved"].Properties.GetEnumerator(); Check(iterator.MoveNext(),"Empty seed");
        Throws<ApplicationException>(() => layer.Name = "Renamed"); LrsAssert(doc,layer,"Walls");
        Check(ReferenceEquals(property, doc.Layers.StateManager["Saved"].Properties["Walls"]) && iterator.MoveNext(),"Veto mutated saved properties");
        layer.NameChanged -= veto; layer.Name = "Renamed"; LrsAssert(doc,layer,"Renamed");
    }

    private static void LrsCallback(int mode)
    {
        var doc = LrsSeed(); var layer = doc.Layers["Walls"]; var state = doc.Layers.StateManager["Saved"];
        var foreign = LsiSeed(DxfVersion.AutoCad2018); var extra = doc.Layers.Add(new Layer("Extra"));
        int notifications = 0;
        TableObject.NameChangedEventHandler callback = (s,e) =>
        {
            // Bound the old-library negative run too: a missing recursion guard
            // must be a recorded assertion failure, not a process stack overflow.
            Check(++notifications == 1, "Recursive callback escaped the rename guard");
            switch (mode)
            {
                case 0: doc.Layers.Add(new Layer("Renamed")); break;
                case 1: state.Properties["Walls"].Color = new AciColor(5); break;
                case 2: state.CurrentLayer = "0"; doc.DrawingVariables.CLayer = "0"; break;
                case 3: layer.Name = "Nested"; break;
                case 4: Throws<InvalidOperationException>(() => layer.Name = "Nested"); break;
                case 5: extra.Name = "ExtraNew"; break;
                case 6: state.Description = "Callback change"; throw new ApplicationException("veto");
                case 7: state.Properties.Remove("Walls"); state.CurrentLayer = "0"; break;
                case 8:
                    doc.Layers.StateManager.RemoveAll(); doc.Entities.Lines.Single().Layer = doc.Layers["0"]; doc.DrawingVariables.CLayer = "0";
                    Check(doc.Layers.Remove(layer), "Callback cannot detach unreferenced layer"); foreign.Layers.Add(layer);
                    foreign.Layers.StateManager.AddNew("Foreign"); foreign.Layers.StateManager["Foreign"].CurrentLayer = "Walls";
                    foreign.DrawingVariables.CLayer = "Walls"; break;
            }
        };
        layer.NameChanged += callback;
        if (mode == 0) Throws<ArgumentException>(() => layer.Name = "Renamed");
        else if (mode == 3) Throws<InvalidOperationException>(() => layer.Name = "Renamed");
        else if (mode == 6) Throws<ApplicationException>(() => layer.Name = "Renamed");
        else layer.Name = "Renamed";
        layer.NameChanged -= callback;
        Equal(1, notifications, "Recursive rename notified observers");
        if (mode == 0 || mode == 3 || mode == 6)
        {
            Check(layer.Name == "Walls" && ReferenceEquals(doc.Layers["Walls"],layer), "Refused callback corrupted layer index");
            Check(state.Properties.ContainsKey("Walls") && !state.Properties.ContainsKey("Renamed"), "Refused callback rekeyed snapshot");
            if (mode == 6) Equal("Callback change",state.Description,"Caller side effect rolled back");
        }
        else if (mode == 8)
        {
            Check(ReferenceEquals(layer.Owner,foreign.Layers) && ReferenceEquals(foreign.Layers["Renamed"],layer),"Rename used former owner");
            Equal("Renamed",foreign.Layers.StateManager["Foreign"].Properties["Renamed"].Name,"Adopted snapshot not rekeyed");
            Equal("Renamed",foreign.DrawingVariables.CLayer,"Adopted current layer not renamed");
            Check(doc.Layers["Walls"] == null && doc.Layers["Renamed"] == null,"Former owner gained renamed layer");
        }
        else
        {
            Equal("Renamed",layer.Name,"Callback prevented accepted rename");
            if (mode == 1) Equal((short)5,state.Properties["Renamed"].Color.Index,"Callback property update lost");
            if (mode == 2) { Equal("0",state.CurrentLayer,"Callback current layer overwritten"); Equal("0",doc.DrawingVariables.CLayer,"Callback CLAYER overwritten"); }
            if (mode == 5) Check(ReferenceEquals(doc.Layers["ExtraNew"],extra),"Other-object rename failed");
            if (mode == 7) Check(!state.Properties.ContainsKey("Renamed"),"Removed callback snapshot was resurrected");
        }
        Check(doc.Objects.Validate().Count == 0 && foreign.Objects.Validate().Count == 0,"Callback graph invalid");
    }

    private static void LrsRefusal(int mode)
    {
        var doc = LrsSeed(); var layer = doc.Layers["Walls"]; var state = doc.Layers.StateManager["Other"];
        if (mode == 0 || mode == 1)
        {
            var p = new LayerStateProperties(mode == 0 ? "Renamed" : "RENAMED");
            state.Properties.BeforeAddItem += (sender,args) => args.Cancel = false;
            state.Properties.Add(p.Name,p);
        }
        if (mode == 2) state.Properties.Add("walls",state.Properties["Walls"]);
        if (mode == 3) { var p=state.Properties["Walls"]; state.Properties.Remove("Walls"); state.Properties.Remove("0"); state.Properties.Add("0",p); }
        if (mode == 4) { state.Properties.Remove("Walls"); state.Properties.Add("Walls",state.Properties["0"]); }
        var first = doc.Layers.StateManager["Saved"].Properties["Walls"]; var keySet=doc.Layers.StateManager["Saved"].Properties.Keys.ToArray();
        bool rejected=false;try{layer.Name="Renamed";}catch(ArgumentException){rejected=true;}catch(InvalidOperationException){rejected=true;}
        Check(rejected && layer.Name=="Walls" && ReferenceEquals(doc.Layers["Walls"],layer),"Ambiguous snapshot accepted or layer index mutated");
        Check(ReferenceEquals(first,doc.Layers.StateManager["Saved"].Properties["Walls"])
            && keySet.SequenceEqual(doc.Layers.StateManager["Saved"].Properties.Keys),"Late invalid snapshot partially changed earlier state");
    }

    private static void LrsExtended(int mode)
    {
        var doc = LrsSeed(); var layer = doc.Layers["Walls"]; var state = doc.Layers.StateManager["Saved"];
        var properties = state.Properties; var property = properties["Walls"];
        string renamed = mode == 2 ? "Ściany Δ" : "Renamed";
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            if (mode == 0)
            {
                properties.Remove("Walls"); properties.Add("wAlLs", property);
                state.CurrentLayer = "wALLs"; doc.DrawingVariables.CLayer = "WaLLs";
            }
            if (mode == 1) properties.Remove("Walls"); // CurrentLayer-only reference is not a new snapshot.
            if (mode == 2) System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("tr-TR");
            if (mode == 3) layer.NameChanged += (s,e) => doc.Layers.StateManager.AddNew("DuringCallback");
            if (mode == 4) layer.NameChanged += (s,e) => Check(doc.Layers.StateManager.Remove(state), "Callback state detachment failed");
            if (mode == 5)
            {
                layer.NameChanged += (s,e) => { property.Color = new AciColor(6); throw new ApplicationException("changed then vetoed"); };
                Throws<ApplicationException>(() => layer.Name = renamed);
                LrsAssert(doc, layer, "Walls"); Equal((short)6, property.Color.Index, "Callback side effect rolled back");
                return;
            }
            if (mode == 6) layer.NameChanged += (s,e) =>
            {
                doc.Layers.StateManager.RemoveAll(); doc.Entities.Lines.Single().Layer = doc.Layers["0"];
                doc.DrawingVariables.CLayer = "0"; Check(doc.Layers.Remove(layer), "Callback layer detachment failed");
            };
            var data = new XData(new ApplicationRegistry("LAYER_RENAME_DATA"));
            data.XDataRecord.Add(new XDataRecord(XDataCode.String, "Walls"));
            data.XDataRecord.Add(new XDataRecord(XDataCode.DatabaseHandle, layer.Handle));
            if (mode == 7) doc.Entities.Lines.Single().XData.Add(data);
            var proxy = new byte[] {1, 7, 33, 255}; doc.Entities.Lines.Single().ProxyGraphics = proxy;
            string handle = layer.Handle;
            layer.Name = renamed;
            Equal(renamed, layer.Name, "Extended rename failed");
            if (mode == 6)
            {
                Check(layer.Owner == null && layer.Handle == null && doc.Layers["Walls"] == null
                    && doc.Layers[renamed] == null, "Detach was ignored");
                return;
            }
            Check(ReferenceEquals(doc.Layers[renamed], layer) && layer.Handle == handle, "Resource identity changed");
            Check(proxy.SequenceEqual(doc.Entities.Lines.Single().ProxyGraphics), "Name-only edit invalidated geometry proxy");
            if (mode == 4)
            {
                Check(state.Owner == null && property.Name == "Walls" && properties.ContainsKey("Walls"), "Detached state was rewritten");
                return;
            }
            Equal(renamed, state.CurrentLayer, "Saved current-layer name lost");
            if (mode == 1) Check(!properties.ContainsKey(renamed) && properties.Count == 1, "CurrentLayer-only rename manufactured a snapshot");
            else Check(ReferenceEquals(property, properties[renamed]) && property.Name == renamed, "Snapshot identity/name changed incorrectly");
            if (mode == 3) Equal(renamed, doc.Layers.StateManager["DuringCallback"].Properties[renamed].Name, "New callback state was skipped");
            if (mode == 7)
            {
                Equal("Walls", (string)data.XDataRecord[0].Value, "Private literal was rewritten");
                Equal(handle, (string)data.XDataRecord[1].Value, "Handle data changed");
            }
            var loaded = LsiLoad(LsiSave(doc, false));
            Equal(handle, loaded.Layers[renamed].Handle, "Extended rename persistence changed identity");
            Equal(renamed, loaded.Layers.StateManager["Saved"].CurrentLayer, "Extended saved name did not round trip");
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = culture; }
    }

    private static void LrsAliases()
    {
        var doc=LrsSeed();var first=doc.Layers.StateManager["Saved"].Properties["Walls"];
        doc.Layers.StateManager["Other"].Properties["Walls"]=first;
        doc.Layers["Walls"].Name="Renamed";
        Check(ReferenceEquals(first,doc.Layers.StateManager["Saved"].Properties["Renamed"])
            && ReferenceEquals(first,doc.Layers.StateManager["Other"].Properties["Renamed"]),"Shared snapshot identity replaced");
        Equal("Renamed",first.Name,"Alias name not synchronized");
    }
    private static void LrsNoop()
    {
        var doc=LrsSeed();var layer=doc.Layers["Walls"];int events=0;layer.NameChanged+=(s,e)=>events++;
        using var iter=doc.Layers.StateManager["Saved"].Properties.GetEnumerator();Check(iter.MoveNext(),"Noop setup");
        layer.Name="wAlLs";Equal(0,events,"Case-only assignment notified");Check(iter.MoveNext(),"Noop invalidated snapshot enumeration");
        foreach(string value in new[]{"","A/B","Bad*"})Throws<ArgumentException>(()=>layer.Name=value);
        Throws<ArgumentException>(()=>doc.Layers["0"].Name="Zero");LrsAssert(doc,layer,"Walls");Equal(0,events,"Invalid name notified");
    }
    private static void LrsNoSnapshots()
    {
        var doc=LsiSeed(DxfVersion.AutoCad2018);var layer=doc.Layers.Add(new Layer("Walls"));doc.DrawingVariables.CLayer="Walls";
        layer.Name="Renamed";Check(ReferenceEquals(doc.Layers["Renamed"],layer) && doc.DrawingVariables.CLayer=="Renamed","Empty manager rename failed");
        Check(doc.Layers.StateManager.Count==0,"Rename manufactured snapshot");
        var detached=(Layer)layer.Clone();detached.Name="Detached";Equal("Renamed",layer.Name,"Detached rename crossed documents");
    }
    private static void LrsUnrelated()
    {
        var doc=LrsSeed();doc.Layers.StateManager["Other"].Properties.Remove("Walls");var props=doc.Layers.StateManager["Other"].Properties;
        using var iter=props.GetEnumerator();Check(iter.MoveNext(),"Unrelated snapshot empty");doc.Layers["Walls"].Name="Renamed";
        Check(!iter.MoveNext() && props.Count==1 && !props.ContainsKey("Renamed"),"Unrelated snapshot was modified");
    }
    private static void LrsWire(DxfVersion version,bool binary)
    {
        var doc=LrsSeed(version);byte[] source=LsiSave(doc,binary);doc=LsiLoad(source);
        var layer=doc.Layers["Walls"];string id=layer.Handle;var stateIds=LspIds(doc);
        string prefix=$"layer-rename-state-{version}-{(binary ? "binary" : "text")}";
        File.WriteAllBytes(Path.Combine(ArtifactDirectory,prefix+"-source.dxf"),source);
        layer.Name="Renamed";LrsAssert(doc,layer,"Renamed");
        for(int stage=0;stage<2;stage++)
        {
            byte[] saved=LsiSave(doc,stage==0 ? !binary : binary);
            File.WriteAllBytes(Path.Combine(ArtifactDirectory,prefix+(stage==0 ? "-output.dxf" : "-resave.dxf")),saved);
            doc=LsiLoad(saved);LrsAssert(doc,doc.Layers["Renamed"],"Renamed");LspRegistered(doc,stateIds);
            Equal(id,doc.Layers["Renamed"].Handle,"Wire layer identity changed");
        }
        string path=Path.Combine(ArtifactDirectory,prefix+".las");doc.Layers.StateManager.ExportAtomic(path,"Saved");
        var imported=LayerState.Load(path)!;Check(imported.CurrentLayer=="Renamed" && imported.Properties.ContainsKey("Renamed"),"LAS export used stale name");
        doc.Layers["Renamed"].Color=new AciColor(7);doc.Layers.StateManager.Restore("Saved");
        Equal((short)3,doc.Layers["Renamed"].Color.Index,"Restore recaptured current settings");
    }
}
