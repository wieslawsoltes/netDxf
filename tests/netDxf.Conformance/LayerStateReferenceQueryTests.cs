// Copyright (c) netDxf contributors. Licensed under the MIT License.
using netDxf;
using netDxf.Collections;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;
using netDxf.Tables;

namespace NetDxf.Conformance;
internal static partial class Program
{
    private static void RegisterLayerStateReferenceQueryTests()
    {
        foreach (var version in SupportedVersions)
        foreach (bool binary in new[] { false, true })
        {
            Run($"layer-state-queries/wire/{version}/{binary}", () => LsqWire(version, binary));
            Run($"layer-state-queries/header-removal/{version}/{binary}", () => LsqHeader(version, binary));
        }
        Run("layer-state-queries/dictionary-fallback-and-alias-refusal", LsqAliases);
        Run("layer-state-queries/foreign-target-and-snapshots", LsqIsolation);
        Run("layer-state-queries/malformed-header-refusal", LsqMalformedHeader);
        Run("layer-state-queries/collapsed-name-admission", LsqNames);
    }

    private static void LsqCheck(DxfDocument doc, LayerState state, params (DxfObject Source, int Uses)[] expected)
    {
        TableObjects<LayerState> manager = doc.Layers.StateManager;
        foreach (var actual in new[] { manager.GetReferences(state), manager.GetReferences(state.Name.ToUpperInvariant()) })
        {
            Equal(expected.Length, actual.Count, "Layer-state source inventory");
            foreach (var item in expected)
                Equal(item.Uses, actual.Single(r => ReferenceEquals(r.Reference, item.Source)).Uses, "Layer-state reference occurrence count");
        }
        Equal(expected.Length != 0, manager.HasReferences(state), "Object HasReferences");
        Equal(expected.Length != 0, manager.HasReferences(state.Name.ToUpperInvariant()), "Name HasReferences");
    }

    private static void LsqWire(DxfVersion version, bool binary)
    {
        var doc = LspSeed(version, 1); var state = doc.Layers.StateManager["State_0"];
        string id = state.Handle; var line = doc.Entities.Lines.Single();
        var pointer = new DxfXRecord(); doc.NamedObjects.Add("LSQ_POINTERS", pointer);
        foreach (short code in new short[] { 330, 340, 350, 360 }) pointer.Data.Add(new DxfTag(code, id));
        pointer.Data.Add(new DxfTag(320, id)); pointer.Data.Add(new DxfTag(1, id)); // not references
        line.PersistentReactors.Add(state);
        var data = new XData(new ApplicationRegistry("LSQ_REF"));
        data.XDataRecord.Add(new XDataRecord(XDataCode.DatabaseHandle, id)); line.XData.Add(data);
        doc.DrawingVariables.AddCustomVariable(new HeaderVariable("$LSQ_POINTER", 340, id));
        LsqCheck(doc, state, (pointer, 4), (line, 2), (doc, 1));
        string prefix = $"layer-state-queries-{version}-{(binary ? "binary" : "text")}";
        byte[] bytes = LsiSave(doc, binary);
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, prefix + "-source.dxf"), bytes);
        for (int i = 0; i < 2; i++)
        {
            doc = LsiLoad(bytes); state = (LayerState)doc.GetObjectByHandle(id); line = doc.Entities.Lines.Single();
            pointer = (DxfXRecord)doc.NamedObjects["LSQ_POINTERS"];
            LsqCheck(doc, state, (pointer, 4), (line, 2), (doc, 1));
            Check(!doc.Layers.StateManager.Remove(state), "A reported incoming reference did not protect the state");
            string seed = doc.DrawingVariables.HandleSeed;
            state.Name = "Renamed";
            LsqCheck(doc, state, (pointer, 4), (line, 2), (doc, 1));
            Equal(seed, doc.DrawingVariables.HandleSeed, "Query or rename allocated a handle");
            bytes = LsiSave(doc, i == 0 ? !binary : binary);
            File.WriteAllBytes(Path.Combine(ArtifactDirectory, prefix + (i == 0 ? "-output.dxf" : "-resave.dxf")), bytes);
        }
        pointer.Data.Clear(); line.PersistentReactors.Clear(); line.XData.Clear();
        doc.DrawingVariables.RemoveCustomVariable("$LSQ_POINTER");
        LsqCheck(doc, state);
        Check(doc.Layers.StateManager.Remove(state) && doc.GetObjectByHandle(id) == null, "Cleared references still prevent removal");
    }

    private static void LsqHeader(DxfVersion version, bool binary)
    {
        foreach (short code in new short[] { 330, 340, 350, 360 })
        {
            var doc = LspSeed(version, 1); var state = doc.Layers.StateManager["State_0"]; string id = state.Handle;
            doc.DrawingVariables.AddCustomVariable(new HeaderVariable("$LSQ_ONLY", code, id.PadLeft(16, '0').ToLowerInvariant()));
            LsqCheck(doc, state, (doc, 1));
            Check(!doc.Layers.StateManager.Remove(state) && ReferenceEquals(doc.GetObjectByHandle(id), state), "Header-only reference was erased");
            doc = LsiLoad(LsiSave(doc, binary)); state = (LayerState)doc.GetObjectByHandle(id);
            LsqCheck(doc, state, (doc, 1));
            Check(!doc.Layers.StateManager.Remove(state), "Reload lost header reference protection");
            doc.DrawingVariables.ClearCustomVariables();
            doc.DrawingVariables.AddCustomVariable(new HeaderVariable("$LSQ_ARBITRARY", 320, id));
            LsqCheck(doc, state);
            Check(doc.Layers.StateManager.Remove(state), "Arbitrary handle incorrectly protects deletion");
        }
    }

    private static void LsqAliases()
    {
        var doc = LspSeed(DxfVersion.AutoCad2018, 1); var state = doc.Layers.StateManager["State_0"];
        var refs = new DxfDictionaryWithDefault(); doc.NamedObjects.Add("LSQ_ALIASES", refs);
        // A state already has a manager owner. Keep the existing alias-adoption refusal;
        // the fallback is a non-owning hard pointer and is allowed independently.
        Throws<ArgumentException>(() => refs.Add("one", state, false));
        Throws<ArgumentException>(() => refs.Add("two", state, true));
        refs.Default = state;
        LsqCheck(doc, state, (refs, 1));
        Check(!doc.Layers.StateManager.Remove(state), "Dictionary fallback target was erased");
        refs.Default = null; LsqCheck(doc, state);
    }

    private static void LsqIsolation()
    {
        var doc = LspSeed(DxfVersion.AutoCad2018, 1); var state = doc.Layers.StateManager["State_0"];
        var refs = new DxfXRecord(); refs.Data.Add(new DxfTag(330, state.Handle)); doc.NamedObjects.Add("Q", refs);
        var manager = doc.Layers.StateManager; var snapshot = manager.GetReferences(state);
        Equal(1, snapshot.Count, "Query snapshot missing source"); snapshot.Clear();
        LsqCheck(doc, state, (refs, 1));
        refs.Data.Add(new DxfTag(330, state.Handle)); LsqCheck(doc, state, (refs, 2));
        var foreign = LspSeed(DxfVersion.AutoCad2018, 1).Layers.StateManager["State_0"];
        Check(!manager.HasReferences(foreign) && manager.GetReferences(foreign).Count == 0, "Same-name foreign target matched local references");
        refs.Data.Clear(); LsqCheck(doc, state);
        var plain = new LayerState("State_0");
        Check(!manager.HasReferences(plain) && manager.GetReferences(plain).Count == 0, "Detached target matched local references");
    }

    private static void LsqMalformedHeader()
    {
        var doc = LspSeed(DxfVersion.AutoCad2018, 1); var state = doc.Layers.StateManager["State_0"]; string id = state.Handle;
        doc.DrawingVariables.AddCustomVariable(new HeaderVariable("$LSQ_INVALID", 340, 123));
        Throws<InvalidOperationException>(() => doc.Layers.StateManager.GetReferences(state));
        Throws<InvalidOperationException>(() => doc.Layers.StateManager.Remove(state));
        Check(ReferenceEquals(doc.GetObjectByHandle(id), state), "Uninspectable header removal mutated identity");
    }

    private static void LsqNames()
    {
        var doc = LspSeed(DxfVersion.AutoCad2018, 3); var ids = LspIds(doc);
        byte[] source = LsiSave(doc, false);
        using var input = new MemoryStream(source); var raw = DxfRawDocument.Load(input);
        var child = raw.Sections.Single(s => s.Name == "OBJECTS").Records.Single(r => r.Name == "DICTIONARY"
            && r.Tags.Any(t => t.Code == 3 && Equals(t.Value, "State_0")));
        raw = raw.WithRecord(child, child.Tags.Select(t => t.Code == 3 && Equals(t.Value, "State_1") ? new DxfTag(3, "state_0") : t));
        using var altered = new MemoryStream(); raw.Save(altered, false);
        var loaded = LsiLoad(altered.ToArray());
        Check(ids.Values.All(id => loaded.GetObjectByHandle(id) == null), "Collapsed names were promoted to source identities");
    }
}
