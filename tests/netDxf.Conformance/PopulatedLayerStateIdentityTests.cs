// Copyright (c) netDxf contributors. Licensed under the MIT License.
using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;
using netDxf.Tables;

namespace NetDxf.Conformance;
internal static partial class Program
{
    private static void RegisterPopulatedLayerStateIdentityTests()
    {
        RegisterLayerStateMutationTests();
        RegisterLayerStateReferenceQueryTests();
        RegisterLayerStateRetentionTests();
        foreach (var version in SupportedVersions)
        foreach (bool binary in new[] { false, true })
        {
            foreach (int count in new[] { 1, 3 })
                Run($"layer-state-populated/wire/{version}/{binary}/{count}", () => LspWire(version, binary, count));
            Run($"layer-state-populated/references/{version}/{binary}", () => LspReferences(version, binary));
            Run($"layer-state-populated/edits/{version}/{binary}", () => LspEdits(version, binary));
            Run($"layer-state-populated/invalid-owner/{version}/{binary}", () => LspInvalid(version, binary));
        }
        Run("layer-state-populated/empty-state-and-scope", LspScope);
        Run("layer-state-populated/removal-references", LspRemoval);
        Run("layer-state-populated/clone-and-isolation", LspClone);
    }

    private static DxfDocument LspSeed(DxfVersion version, int count)
    {
        var doc = LsiSeed(version);
        doc.Layers.Add(new Layer("Walls") { Color = new AciColor(3), Lineweight = Lineweight.W30, Transparency = new Transparency(25), IsVisible = false });
        doc.Layers.Add(new Layer("Details") { Color = new AciColor(20, 70, 130), IsFrozen = true, Plot = false });
        for (int i = 0; i < count; i++)
        {
            doc.Layers.StateManager.AddNew("State_" + i, "Saved Δ layer state " + i);
            var state = doc.Layers.StateManager["State_" + i];
            state.PaperSpace = i % 2 != 0;
            state.CurrentLayer = i % 2 == 0 ? "Walls" : "Details";
            state.Properties["Walls"].Color = new AciColor((short)(3 + i));
        }
        return doc;
    }

    private static Dictionary<string, string> LspIds(DxfDocument doc) =>
        doc.Layers.StateManager.Items.ToDictionary(s => s.Name, s => s.Handle);

    private static string LspValues(LayerState state) => state.Description + "|" + state.CurrentLayer + "|" + state.PaperSpace
        + string.Join(";", state.Properties.Values.Select(p => $"{p.Name}:{(int)p.Flags}:{p.Color.Index}:{AciColor.ToTrueColor(p.Color)}:{p.Color.UseTrueColor}:{p.Lineweight}:{p.LinetypeName}:{p.Transparency.Value}"));

    private static void LspRegistered(DxfDocument doc, Dictionary<string, string> expected)
    {
        Equal(expected.Count, doc.Layers.StateManager.Count, "Saved state count");
        foreach (var pair in expected)
        {
            var state = doc.Layers.StateManager[pair.Key];
            Equal(pair.Value, state.Handle, "Populated state handle changed");
            Check(ReferenceEquals(doc.GetObjectByHandle(pair.Value), state)
                && ReferenceEquals(state.Owner, doc.Layers.StateManager), "Populated state registration/owner changed");
        }
        Check(doc.Objects.Validate().Count == 0, "Populated layer-state graph invalid");
    }

    private static void LspWire(DxfVersion version, bool binary, int count)
    {
        var doc = LspSeed(version, count);
        var stateIds = LspIds(doc);
        var values = doc.Layers.StateManager.Items.ToDictionary(s => s.Name, LspValues);
        byte[] bytes = LsiSave(doc, binary);
        var pair = LsiIds(bytes);
        string prefix = $"layer-state-populated-{version}-{(binary ? "binary" : "text")}-{count}";
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, prefix + "-source.dxf"), bytes);
        for (int i = 0; i < 3; i++)
        {
            doc = LsiLoad(bytes);
            LspRegistered(doc, stateIds); LsiRegistered(doc, pair);
            foreach (var item in values) Equal(item.Value, LspValues(doc.Layers.StateManager[item.Key]), "State property projection changed");
            bytes = LsiSave(doc, i == 0 ? !binary : binary);
            Equal(pair, LsiIds(bytes), "Populated dictionary identities changed");
            File.WriteAllBytes(Path.Combine(ArtifactDirectory, prefix + new[] { "-output.dxf", "-resave.dxf", "-repeat.dxf" }[i]), bytes);
        }
    }

    private static void LspReferences(DxfVersion version, bool binary)
    {
        var doc = LspSeed(version, 3); var ids = LspIds(doc);
        var pairs = LsiIds(LsiSave(doc, binary));
        var reference = new DxfXRecord();
        foreach (string id in ids.Values) { reference.Data.Add(new DxfTag(330, id)); reference.Data.Add(new DxfTag(340, id)); }
        reference.Data.Add(new DxfTag(340, pairs.Inner));
        doc.NamedObjects.Add("LSP_REFERENCES", reference);
        doc.Entities.Lines.Single().PersistentReactors.Add(doc.Layers.StateManager["State_0"]);
        var xd = new XData(new ApplicationRegistry("LSP_REF")); xd.XDataRecord.Add(new XDataRecord(XDataCode.DatabaseHandle, ids["State_1"]));
        doc.Entities.Lines.Single().XData.Add(xd);
        for (int i = 0; i < 3; i++)
        {
            doc = LsiLoad(LsiSave(doc, i % 2 == 0 ? binary : !binary)); LspRegistered(doc, ids); LsiRegistered(doc, pairs);
            Check(ReferenceEquals(doc.Entities.Lines.Single().PersistentReactors.Single(), doc.Layers.StateManager["State_0"]), "Persistent reactor did not bind retained state");
            foreach (var tag in ((DxfXRecord)doc.NamedObjects["LSP_REFERENCES"]).Data)
                Check(doc.GetObjectByHandle((string)tag.Value) != null, "Dangling state XRECORD pointer");
            Check(!doc.Layers.StateManager.Remove("State_2"), "Referenced state removal accepted");
        }
    }

    private static void LspEdits(DxfVersion version, bool binary)
    {
        var doc = LsiLoad(LsiSave(LspSeed(version, 3), binary));
        var ids = LspIds(doc); var pair = LsiIds(LsiSave(doc, binary));
        var state = doc.Layers.StateManager["State_0"];
        state.Name = "Renamed"; ids.Add(state.Name, ids["State_0"]); ids.Remove("State_0");
        state.Description = "Updated saved properties";
        state.Properties["Walls"].Color = new AciColor(6);
        doc.Layers.StateManager.Restore(state.Name);
        Equal((short)6, doc.Layers["Walls"].Color.Index, "Restored state lost editable properties");
        doc.Layers["Walls"].Lineweight = Lineweight.W50;
        doc.Layers.StateManager.Update(state.Name);
        string value = LspValues(state);
        doc = LsiLoad(LsiSave(doc, !binary)); LspRegistered(doc, ids);
        Equal(value, LspValues(doc.Layers.StateManager["Renamed"]), "Updated state values changed");
        Check(doc.Layers.StateManager.Remove("State_2"), "Unreferenced state could not be removed"); ids.Remove("State_2");
        doc.Layers.StateManager.AddNew("Added"); ids.Add("Added", doc.Layers.StateManager["Added"].Handle);
        doc = LsiLoad(LsiSave(doc, binary)); LspRegistered(doc, ids); LsiRegistered(doc, pair);
    }

    private static void LspInvalid(DxfVersion version, bool binary)
    {
        byte[] bytes = LsiSave(LspSeed(version, 3), binary);
        for (int mode = 0; mode < 3; mode++)
        {
            using var input = new MemoryStream(bytes); var raw = DxfRawDocument.Load(input);
            var state = raw.Sections.Single(s => s.Name == "OBJECTS").Records.Last(r => r.Name == "XRECORD" && r.Tags.Any(t => t.Code == 301));
            var tags = state.Tags.ToList(); int subclass = tags.FindIndex(t => t.Code == 100);
            var owners = Enumerable.Range(0, subclass).Where(i => tags[i].Code == 330).ToArray();
            if (mode < 2) tags[owners[mode]] = new DxfTag(330, "0");
            else tags.Insert(1, new DxfTag(5, LsiHandle(state))); // repeated physical identity
            raw = raw.WithRecord(state, tags);
            using var altered = new MemoryStream(); raw.Save(altered, binary); altered.Position = 0;
            bool refused = false;
            try { refused = DxfDocument.Load(altered) == null; }
            catch (FormatException) { refused = true; }
            catch (ArgumentException) { refused = true; }
            Check(refused && altered.CanRead, "Invalid state ownership/identity accepted or stream closed");
        }
    }

    private static void LspScope()
    {
        var doc = LsiSeed(DxfVersion.AutoCad2018); doc.Layers.StateManager.Add(new LayerState("Empty"));
        var ids = LspIds(doc); doc = LsiLoad(LsiSave(doc, false)); LspRegistered(doc, ids);
        byte[] bytes = LsiSave(LspSeed(DxfVersion.AutoCad2018, 3), false);
        using var input = new MemoryStream(bytes); var raw = DxfRawDocument.Load(input);
        var state = raw.Sections.Single(s => s.Name == "OBJECTS").Records.First(r => r.Name == "XRECORD" && r.Tags.Any(t => t.Code == 301));
        string id = LsiHandle(state);
        // Unknown payload must not be promoted to a supposedly retained canonical state.
        raw = raw.WithRecord(state, state.Tags.Concat(new[] { new DxfTag(1, "private-suffix") }));
        using var output = new MemoryStream(); raw.Save(output, false); doc = LsiLoad(output.ToArray());
        Check(doc.GetObjectByHandle(id) == null, "Converted private state was admitted as retained identity");
    }

    private static void LspRemoval()
    {
        for (int mode = 0; mode < 4; mode++)
        {
            var doc = LspSeed(DxfVersion.AutoCad2018, 1); var state = doc.Layers.StateManager["State_0"]; string id = state.Handle;
            var reference = new DxfXRecord(); doc.NamedObjects.Add("REFERENCES", reference);
            if (mode == 0) reference.Data.Add(new DxfTag(330, id));
            if (mode == 1) reference.Data.Add(new DxfTag(340, id));
            if (mode == 2) doc.Entities.Lines.Single().PersistentReactors.Add(state);
            if (mode == 3) { var xd = new XData(new ApplicationRegistry("LS_XD")); xd.XDataRecord.Add(new XDataRecord(XDataCode.DatabaseHandle, id)); doc.Entities.Lines.Single().XData.Add(xd); }
            Check(!doc.Layers.StateManager.Remove(state) && state.Handle == id && ReferenceEquals(doc.GetObjectByHandle(id), state), "Referenced state erased");
            reference.Data.Clear(); doc.Entities.Lines.Single().PersistentReactors.Clear(); doc.Entities.Lines.Single().XData.Clear();
            Check(doc.Layers.StateManager.Remove(state) && doc.GetObjectByHandle(id) == null, "Unreferenced state not removable");
            Check(doc.Objects.Validate().Count == 0, "State removal damaged database");
        }
    }

    private static void LspClone()
    {
        var source = LsiLoad(LsiSave(LspSeed(DxfVersion.AutoCad2018, 1), false));
        var state = source.Layers.StateManager["State_0"]; string before = LspValues(state);
        var clone = (LayerState)state.Clone(); var other = LspSeed(DxfVersion.AutoCad2018, 3);
        clone.Name = "Clone"; other.Layers.StateManager.Add(clone);
        Check(!ReferenceEquals(clone, state) && !ReferenceEquals(clone.Properties["Walls"], state.Properties["Walls"]), "Clone aliases source state");
        clone.Properties["Walls"].Color = new AciColor(7);
        Equal(before, LspValues(state), "Foreign state edit mutated source");
        var ids = LspIds(other); other = LsiLoad(LsiSave(other, true)); LspRegistered(other, ids);
    }
}
