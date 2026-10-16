using netDxf;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterLayerIndexOwnershipInputTests()
    {
        foreach (DxfVersion version in SupportedVersions)
        foreach (bool binary in new[] { false, true })
        foreach (string form in new[] { "null", "absent", "mixed" })
            Run($"layer-index/owner-input/{version}/{binary}/{form}", () => LayerIndexOwnerInput(version, binary, form));
        foreach (bool binary in new[] { false, true })
        foreach (string defect in new[] { "foreign", "second-index", "soft-owner", "hard-owner", "private-owner", "reciprocal-conflict", "missing", "wrong-type", "count" })
            Run($"layer-index/owner-rejection/{binary}/{defect}", () => LayerIndexOwnerRejection(binary, defect));
        Run("layer-index/owner-api-atomic", LayerIndexOwnerApiAtomic);
    }

    private static DxfRawDocument LayerIndexClearOwner(DxfRawDocument raw, string handle, bool absent)
    {
        return ObjectStoreReplaceRecord(raw, handle, tags =>
        {
            int subclass = tags.FindIndex(tag => tag.Code == 100);
            int owner = tags.FindIndex(0, subclass, tag => tag.Code == 330);
            Check(owner >= 0, "Synthetic IDBUFFER lacks a common owner field");
            if (absent) tags.RemoveAt(owner);
            else tags[owner] = new DxfTag(330, "0");
            return tags;
        });
    }

    private static void LayerIndexOwnerInput(DxfVersion version, bool binary, string form)
    {
        var source = LayerIndexDocument(version);
        var sourceIndex = (DxfLayerIndex)((DxfDictionary)source.NamedObjects["QA_LAYER_INDEX"])["INDEX"];
        string indexHandle = sourceIndex.Handle;
        string[] handles = sourceIndex.Entries.Select(entry => entry.Buffer.Handle).ToArray();
        using var input = new MemoryStream(LayerIndexSave(source, binary));
        var raw = DxfRawDocument.Load(input);
        for (int i = 0; i < handles.Length; i++)
        {
            if (form == "mixed" && i % 3 == 2) continue;
            raw = LayerIndexClearOwner(raw, handles[i], form == "absent" || form == "mixed" && i % 3 == 1);
        }
        using var authored = new MemoryStream(); raw.Save(authored, binary);
        var loaded = LayerIndexLoad(authored.ToArray());
        for (int cycle = 0; cycle < 3; cycle++)
        {
            LayerIndexCheck(loaded);
            var index = (DxfLayerIndex)((DxfDictionary)loaded.NamedObjects["QA_LAYER_INDEX"])["INDEX"];
            Equal(indexHandle, index.Handle, "Ownership inference changed the index identity");
            Check(index.Entries.Select(entry => entry.Buffer.Handle).SequenceEqual(handles), "Ownership inference changed buffer identities");
            Equal(handles.Length, loaded.Objects.Items.OfType<DxfIdBuffer>().Count(), "Ownership inference created or removed buffers");
            byte[] bytes = LayerIndexSave(loaded, cycle % 2 == 0 ? !binary : binary);
            using var storedInput = new MemoryStream(bytes); var stored = DxfRawDocument.Load(storedInput);
            foreach (string handle in handles)
            {
                var record = stored.Sections.Single(section => section.Name == "OBJECTS").Records.Single(record =>
                    record.Tags.TakeWhile(tag => tag.Code != 100).Any(tag => tag.Code == 5 && Equals(tag.Value, handle)));
                Equal(indexHandle, (string)record.Tags.TakeWhile(tag => tag.Code != 100).Single(tag => tag.Code == 330).Value,
                    "Saved IDBUFFER lacks the inferred reciprocal owner");
            }
            loaded = LayerIndexLoad(bytes);
        }
        LayerIndexCheck(source);
    }

    private static void LayerIndexOwnerRejection(bool binary, string defect)
    {
        var source = LayerIndexDocument(DxfVersion.AutoCad2018);
        var parent = (DxfDictionary)source.NamedObjects["QA_LAYER_INDEX"];
        var index = (DxfLayerIndex)parent["INDEX"];
        var empty = (DxfLayerIndex)parent["EMPTY"];
        var claimant = new DxfXRecord(); parent.Add("CLAIMANT", claimant);
        source.Classes.Add(new DxfClass("SYNTHETIC_OBJECT", "AcmeOwnership", "SyntheticApplication") { WasProxy = true });
        string target = index.Entries[0].Buffer.Handle;
        using var input = new MemoryStream(LayerIndexSave(source, binary)); var raw = DxfRawDocument.Load(input);
        foreach (var entry in index.Entries)
            if (defect != "reciprocal-conflict") raw = LayerIndexClearOwner(raw, entry.Buffer.Handle, false);
        if (defect == "foreign")
            raw = ObjectStoreReplaceRecord(raw, target, tags =>
            {
                tags[tags.FindIndex(tag => tag.Code == 330)] = new DxfTag(330, parent.Handle); return tags;
            });
        else if (defect == "second-index")
            raw = ObjectStoreReplaceRecord(raw, empty.Handle, tags =>
            {
                tags.AddRange(new[] { new DxfTag(8, "ClaimedTwice"), new DxfTag(360, target), new DxfTag(90, index.Entries[0].Count) });
                return tags;
            });
        else if (defect == "soft-owner" || defect == "hard-owner" || defect == "reciprocal-conflict")
            raw = ObjectStoreReplaceRecord(raw, claimant.Handle, tags =>
            {
                tags.Add(new DxfTag(defect == "soft-owner" ? (short)350 : (short)360, target)); return tags;
            });
        else if (defect == "private-owner")
            raw = ObjectStoreReplaceRecord(raw, claimant.Handle, tags =>
            {
                tags = tags.TakeWhile(tag => tag.Code != 100).ToList();
                tags[0] = new DxfTag(0, "SYNTHETIC_OBJECT");
                tags.AddRange(new[] { new DxfTag(100, "AcmeOwnership"), new DxfTag(360, target) }); return tags;
            });
        else
            raw = ObjectStoreReplaceRecord(raw, index.Handle, tags =>
            {
                int name = tags.FindIndex(tag => tag.Code == 8);
                if (defect == "count") tags[name + 2] = new DxfTag(90, index.Entries[0].Count + 1);
                else tags[name + 1] = new DxfTag(360, defect == "missing" ? "FFF001" : source.Entities.Lines.First().Handle);
                return tags;
            });
        using var rejected = new MemoryStream(); raw.Save(rejected, binary);
        byte[] before = rejected.ToArray(); rejected.Position = 0;
        bool failed;
        try { failed = DxfDocument.Load(rejected) == null; }
        catch (FormatException) { failed = true; }
        Check(failed, "Conflicting or incomplete source ownership was accepted: " + defect);
        Check(rejected.CanRead && rejected.ToArray().SequenceEqual(before), "Rejected ownership load altered or closed its input");
        LayerIndexCheck(source);
    }

    private static void LayerIndexOwnerApiAtomic()
    {
        var source = LayerIndexDocument(DxfVersion.AutoCad2018);
        var parent = (DxfDictionary)source.NamedObjects["QA_LAYER_INDEX"];
        var index = (DxfLayerIndex)parent["INDEX"];
        var other = new DxfLayerIndex(); var foreign = new DxfIdBuffer();
        other.SetEntries(new[] { new DxfLayerIndexEntry("Foreign", foreign) }); parent.Add("OTHER", other);
        var entries = index.Entries.ToArray(); string seed = source.DrawingVariables.HandleSeed;
        Throws<ArgumentException>(() => index.SetEntries(entries.Concat(new[] { new DxfLayerIndexEntry("Invalid", foreign) })));
        Check(index.Entries.SequenceEqual(entries) && ReferenceEquals(foreign.Owner, other), "Rejected public adoption partially changed ownership");
        Equal(seed, source.DrawingVariables.HandleSeed, "Rejected public adoption allocated an identity");
        LayerIndexCheck(source);
    }
}
