// Copyright (c) netDxf contributors. Licensed under the MIT License.
// Retains PR #224's original assertions under distinct helper/result/fixture names
// so its different corpus can coexist with the subsequent populated-state suite.
using System.Globalization;
using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;
using netDxf.Tables;

namespace NetDxf.Conformance;
internal static partial class Program
{
    private static void RegisterLayerStateRetentionTests()
    {
        foreach (var version in SupportedVersions)
        foreach (bool binary in new[] { false, true })
        {
            foreach (int count in new[] { 1, 3 })
                Run($"layer-state-retention/wire/{version}/{binary}/{count}", () => Ls224Wire(version, binary, count));
            Run($"layer-state-retention/references/{version}/{binary}", () => Ls224References(version, binary));
            Run($"layer-state-retention/owners/{version}/{binary}", () => Ls224Owners(version, binary));
            Run($"layer-state-retention/edits/{version}/{binary}", () => Ls224Edits(version, binary));
        }
        Run("layer-state-retention/unsupported-source", Ls224Unsupported);
        Run("layer-state-retention/duplicate-identities", Ls224Duplicates);
        Run("layer-state-retention/retention-count-boundary", Ls224CountBoundary);
    }

    private static DxfDocument Ls224Seed(DxfVersion version, int count)
    {
        var doc = LsiSeed(version);
        doc.Layers.Add(new Layer("Detailed") { Color = new AciColor(34, 76, 129),
            Transparency = new Transparency(37), Lineweight = Lineweight.W35, IsLocked = true });
        doc.Layers.Add(new Layer("Hidden") { Color = new AciColor(2), IsVisible = false, IsFrozen = true, Plot = false });
        for (int i = 0; i < count; i++)
        {
            doc.Layers.StateManager.AddNew("STATE_" + i, "Résumé " + i);
            var state = doc.Layers.StateManager["STATE_" + i];
            state.CurrentLayer = i % 2 == 0 ? "Detailed" : "Hidden";
            state.PaperSpace = i % 2 != 0;
            state.Properties["0"].Flags = (LayerPropertiesFlags)(i + 4);
        }
        return doc;
    }
    private static string Ls224Packet(DxfRawRecord record) => string.Join("|", record.Tags.Select(t =>
        t.Code.ToString(CultureInfo.InvariantCulture) + ":" + (t.Value is double d
            ? BitConverter.DoubleToInt64Bits(d).ToString("x16") : Convert.ToString(t.Value, CultureInfo.InvariantCulture))));
    private static Dictionary<string, string> Ls224Packets(byte[] bytes)
    {
        using var input = new MemoryStream(bytes); var raw = DxfRawDocument.Load(input);
        var ids = LsiIds(bytes);
        return raw.Sections.SelectMany(s => s.Records).Where(r =>
                r.Name == "XRECORD" || r.Name == "LINE" || r.Name == "LAYER" || r.Name == "LTYPE"
                || r.Name == "DICTIONARY" && (LsiHandle(r) == ids.Outer || LsiHandle(r) == ids.Inner))
            .ToDictionary(LsiHandle, Ls224Packet);
    }
    private static void Ls224Compare(Dictionary<string,string> before, byte[] bytes)
    {
        var after = Ls224Packets(bytes);
        Check(before.Count == after.Count && before.All(p => after.TryGetValue(p.Key, out string? value) && value == p.Value),
            "Layer-state identities, ownership, payload or unrelated geometry changed across save");
    }
    private static void Ls224Wire(DxfVersion version, bool binary, int count)
    {
        var doc = Ls224Seed(version, count);
        byte[] source = LsiSave(doc, binary); var expected = Ls224Packets(source); var ids = LsiIds(source);
        var states = doc.Layers.StateManager.ToDictionary(s => s.Name, s => s.Handle);
        string prefix = $"layer-state-retention-{version}-{binary}-{count}";
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, prefix + "-source.dxf"), source);
        for (int generation = 0; generation < 3; generation++)
        {
            doc = LsiLoad(source); LsiRegistered(doc, ids);
            foreach (var pair in states)
                Check(doc.Layers.StateManager[pair.Key].Handle == pair.Value
                    && ReferenceEquals(doc.GetObjectByHandle(pair.Value), doc.Layers.StateManager[pair.Key]), "State identity not registered");
            source = LsiSave(doc, generation % 2 == 0 ? !binary : binary);
            Ls224Compare(expected, source);
            File.WriteAllBytes(Path.Combine(ArtifactDirectory, prefix + "-g" + generation + ".dxf"), source);
        }
    }
    private static void Ls224References(DxfVersion version, bool binary)
    {
        var doc = Ls224Seed(version, 3); var state = doc.Layers.StateManager["STATE_1"];
        string handle = state.Handle;
        var refs = new DxfXRecord(); refs.Data.Add(new DxfTag(330, handle)); refs.Data.Add(new DxfTag(340, handle));
        doc.NamedObjects.Add("STATE_REFS", refs);
        var line = doc.Entities.Lines.Single(); line.PersistentReactors.Add(state);
        var data = new XData(new ApplicationRegistry("STATE_XDATA"));
        data.XDataRecord.Add(new XDataRecord(XDataCode.DatabaseHandle, handle)); line.XData.Add(data);
        for (int generation = 0; generation < 3; generation++)
        {
            doc = LsiLoad(LsiSave(doc, binary)); state = doc.Layers.StateManager["STATE_1"];
            Check(state.Handle == handle && ReferenceEquals(doc.GetObjectByHandle(handle), state), "Referenced state was reallocated");
            line = doc.Entities.Lines.Single(); refs = (DxfXRecord)doc.NamedObjects["STATE_REFS"];
            Check(ReferenceEquals(line.PersistentReactors.Single(), state) && (string)refs.Data[1].Value == handle, "Incoming reference lost");
            Check(!doc.Layers.StateManager.Remove(state), "Referenced state removed");
        }
        line.PersistentReactors.Clear();
        line.XData["STATE_XDATA"].XDataRecord.Clear();
        Check(!doc.Layers.StateManager.Remove(state), "XRECORD-only referenced state removed");
        refs.Data.Clear();
        line.XData["STATE_XDATA"].XDataRecord.Add(new XDataRecord(XDataCode.DatabaseHandle, handle));
        Check(!doc.Layers.StateManager.Remove(state), "XData-only referenced state removed");
        line.XData["STATE_XDATA"].XDataRecord.Clear();
        Check(doc.Layers.StateManager.Remove(state) && doc.GetObjectByHandle(handle) == null, "Unreferenced removal failed");
        Check(doc.Objects.Validate().Count == 0, "State removal left dangling pointers");
    }
    private static void Ls224Owners(DxfVersion version, bool binary)
    {
        var doc = Ls224Seed(version, 2); string state = doc.Layers.StateManager["STATE_1"].Handle;
        byte[] seed = LsiSave(doc, binary); var ids = LsiIds(seed);
        foreach (string handle in new[] { ids.Outer, ids.Inner, state })
        {
            using var input = new MemoryStream(seed); var raw = DxfRawDocument.Load(input);
            var record = raw.Sections.Single(s => s.Name == "OBJECTS").Records.Single(r => LsiHandle(r) == handle);
            var tags = record.Tags.ToList(); int index = tags.FindIndex(t => t.Code == 100);
            // The last 330 before the subclass is structural, not the reactor pointer.
            int owner = tags.FindLastIndex(index - 1, t => t.Code == 330);
            tags[owner] = new DxfTag(330, "0"); raw = raw.WithRecord(record, tags);
            using var output = new MemoryStream(); raw.Save(output, binary); output.Position = 0;
            bool refused = false;
            try { refused = DxfDocument.Load(output) == null; } catch (FormatException) { refused = true; }
            Check(refused && output.CanRead, "Invalid populated-state owner accepted");
        }
    }
    private static void Ls224Edits(DxfVersion version, bool binary)
    {
        var doc = Ls224Seed(version, 2); byte[] bytes = LsiSave(doc, binary); var ids = LsiIds(bytes);
        string first = doc.Layers.StateManager["STATE_0"].Handle;
        doc = LsiLoad(bytes); var state = doc.Layers.StateManager["STATE_0"];
        state.Name = "Renamed"; state.Description = "Updated"; state.Properties["Detailed"].Color = new AciColor(3);
        doc.Layers.StateManager.AddNew("NEW_STATE");
        Check(doc.Layers.StateManager.Remove("STATE_1"), "Unreferenced source state could not be removed");
        var roundtrip = LsiLoad(LsiSave(doc, !binary));
        Check(roundtrip.Layers.StateManager["Renamed"].Handle == first
            && roundtrip.Layers.StateManager["Renamed"].Description == "Updated"
            && roundtrip.Layers.StateManager["Renamed"].Properties["Detailed"].Color.Index == 3,
            "Edited state lost identity or values");
        Equal(ids, LsiIds(LsiSave(roundtrip, binary)), "Adding/removing states lost dictionary identity");
        var clone = (LayerState)roundtrip.Layers.StateManager["Renamed"].Clone();
        clone.Name = "Independent"; var target = Ls224Seed(version, 0); target.Layers.StateManager.Add(clone);
        Check(!ReferenceEquals(clone, state) && ReferenceEquals(clone.Owner, target.Layers.StateManager), "Clone aliases source owner");
    }
    private static void Ls224Unsupported()
    {
        byte[] seed = LsiSave(Ls224Seed(DxfVersion.AutoCad2018, 1), false); var ids = LsiIds(seed);
        for (int mode = 0; mode < 4; mode++)
        {
            using var stream = new MemoryStream(seed); var raw = DxfRawDocument.Load(stream);
            var record = raw.Sections.Single(s => s.Name == "OBJECTS").Records.Single(r => r.Name == "XRECORD");
            var tags = record.Tags.ToList();
            if (mode == 0) tags.Add(new DxfTag(90, 123));
            else if (mode == 1) tags[tags.FindIndex(t => t.Code == 91)] = new DxfTag(91, 2048);
            else if (mode == 2) tags[tags.FindIndex(t => t.Code == 280)] = new DxfTag(280, (short)2);
            else tags.Insert(tags.FindIndex(t => t.Code == 100), new DxfTag(90, 999));
            raw = raw.WithRecord(record, tags); using var output = new MemoryStream(); raw.Save(output, false);
            var loaded = LsiLoad(output.ToArray());
            Check(LsiIds(LsiSave(loaded, false)) != ids, "Unsupported/lossy payload incorrectly advertised as retained");
        }
    }
    private static void Ls224CountBoundary()
    {
        foreach (int count in new[] { 4096, 4097 })
        {
            var doc = LsiSeed(DxfVersion.AutoCad2018);
            for (int i = 0; i < count; i++) doc.Layers.StateManager.Add(new LayerState("S" + i));
            string stateId = doc.Layers.StateManager["S0"].Handle;
            byte[] bytes = LsiSave(doc, true); var ids = LsiIds(bytes);
            var loaded = LsiLoad(bytes);
            Equal(count, loaded.Layers.StateManager.Count, "Retention budget dropped a converted state");
            bool retained = loaded.Layers.StateManager["S0"].Handle == stateId;
            Equal(count == 4096, retained, "Retention state-count boundary");
            Equal(count == 4096, LsiIds(LsiSave(loaded, false)) == ids, "Dictionary retention budget boundary");
        }
    }
    private static void Ls224Duplicates()
    {
        var doc = Ls224Seed(DxfVersion.AutoCad2018, 2); byte[] seed = LsiSave(doc, false);
        string duplicate = doc.Layers.StateManager["STATE_0"].Handle;
        using var stream = new MemoryStream(seed); var raw = DxfRawDocument.Load(stream);
        var record = raw.Sections.Single(s => s.Name == "OBJECTS").Records.Single(r => r.Name == "XRECORD" && LsiHandle(r) != duplicate);
        raw = raw.WithRecord(record, record.Tags.Select(t => t.Code == 5 ? new DxfTag(5, duplicate) : t));
        using var output = new MemoryStream(); raw.Save(output, false); output.Position = 0;
        bool refused = false;
        try { refused = DxfDocument.Load(output) == null; }
        catch (Exception e) when (e is FormatException || e is ArgumentException) { refused = true; }
        Check(refused && output.CanRead, "Duplicate populated-state source identity admitted");
    }
}
