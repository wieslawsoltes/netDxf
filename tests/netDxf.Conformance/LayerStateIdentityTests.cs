// Copyright (c) netDxf contributors. Licensed under the MIT License.
using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;

namespace NetDxf.Conformance;
internal static partial class Program
{
    private static void RegisterLayerStateIdentityTests()
    {
        foreach (var version in SupportedVersions)
        foreach (bool binary in new[] { false, true })
        {
            foreach (bool populated in new[] { false, true })
                Run($"layer-state-identity/same-instance/{version}/{binary}/{populated}", () => LsiSameInstance(version, binary, populated));
            Run($"layer-state-identity/wire/{version}/{binary}", () => LsiWire(version, binary));
            Run($"layer-state-identity/references/{version}/{binary}", () => LsiReferences(version, binary));
            Run($"layer-state-identity/invalid-owner/{version}/{binary}", () => LsiOwners(version, binary));
        }
        Run("layer-state-identity/document-isolation", LsiIsolation);
        Run("layer-state-identity/noncanonical-scope", LsiNoncanonical);
        Run("layer-state-identity/custom-metadata-refusal", LsiMetadataRefusal);
        Run("layer-state-identity/collision-refusal", LsiCollisions);
    }

    private static byte[] LsiSave(DxfDocument doc, bool binary)
    {
        using var output = new MemoryStream();
        Check(doc.Save(output, binary) && output.CanWrite, "Layer-state save/stream lifetime");
        return output.ToArray();
    }
    private static DxfDocument LsiLoad(byte[] bytes)
    {
        byte[] before = (byte[])bytes.Clone();
        using var input = new MemoryStream(bytes);
        var result = DxfDocument.Load(input) ?? throw new InvalidOperationException("Layer-state load failed");
        Check(input.CanRead && bytes.SequenceEqual(before), "Layer-state load changed caller input");
        return result;
    }
    private static (DxfRawRecord Table, DxfRawRecord Outer, DxfRawRecord Inner) LsiRecords(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        var raw = DxfRawDocument.Load(stream);
        var table = raw.Sections.Single(s => s.Name == "TABLES").Records.Single(r => r.Name == "TABLE"
            && r.Tags.Any(t => t.Code == 2 && Equals(t.Value, "LAYER")));
        string outerId = (string)table.Tags.Single(t => t.Code == 360).Value;
        var objects = raw.Sections.Single(s => s.Name == "OBJECTS").Records;
        var outer = objects.Single(r => r.Name == "DICTIONARY" && (string)r.Tags.Single(t => t.Code == 5).Value == outerId);
        var inner = objects.Single(r => r.Name == "DICTIONARY" && (string)r.Tags.Single(t => t.Code == 5).Value == (string)outer.Tags.Single(t => t.Code == 360).Value);
        return (table, outer, inner);
    }
    private static string LsiHandle(DxfRawRecord record) => (string)record.Tags.Single(t => t.Code == 5).Value;
    private static (string Outer, string Inner) LsiIds(byte[] bytes)
    { var pair = LsiRecords(bytes); return (LsiHandle(pair.Outer), LsiHandle(pair.Inner)); }
    private static DxfDocument LsiSeed(DxfVersion version)
    {
        var doc = new DxfDocument(version);
        doc.Entities.Add(new Line(new Vector3(1, 2, 3), new Vector3(4, 5, 6)));
        return doc;
    }
    private static void LsiRegistered(DxfDocument doc, (string Outer, string Inner) ids)
    {
        Check(ReferenceEquals(doc.GetObjectByHandle(ids.Outer), doc.Layers.StateManager), "Layer-state outer identity unregistered");
        var child = doc.GetObjectByHandle(ids.Inner);
        Check(child != null && child.CodeName == "DICTIONARY" && ReferenceEquals(child.Owner, doc.Layers.StateManager), "Layer-state child identity/owner unregistered");
        Check(doc.Objects.Validate().Count == 0, "Layer-state graph validation failed");
    }
    private static void LsiSameInstance(DxfVersion version, bool binary, bool populated)
    {
        var doc = LsiSeed(version);
        if (populated) doc.Layers.StateManager.AddNew("retained");
        byte[] first = LsiSave(doc, binary); var ids = LsiIds(first);
        LsiRegistered(doc, ids);
        var identity = doc.GetObjectByHandle(ids.Inner);
        string seed = doc.DrawingVariables.HandleSeed;
        for (int i = 0; i < 3; i++)
        {
            Equal(ids, LsiIds(LsiSave(doc, i % 2 == 0 ? !binary : binary)), "Repeated save reallocated dictionary identity");
            Equal(seed, doc.DrawingVariables.HandleSeed, "Repeated save allocated a new handle");
            Check(ReferenceEquals(identity, doc.GetObjectByHandle(ids.Inner)), "Repeated save replaced registered identity");
        }
        doc.Layers.StateManager.AddNew("added");
        Equal(ids, LsiIds(LsiSave(doc, binary)), "Adding a layer state changed dictionary identity");
        doc.Layers.StateManager.RemoveAll();
        Equal(ids, LsiIds(LsiSave(doc, !binary)), "Removing layer states changed dictionary identity");
    }
    private static void LsiWire(DxfVersion version, bool binary)
    {
        var doc = LsiSeed(version);
        string lineHandle = doc.Entities.Lines.Single().Handle;
        string prefix = $"layer-state-identity-{version}-{(binary ? "binary" : "text")}";
        byte[] source = LsiSave(doc, binary); var ids = LsiIds(source);
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, prefix + "-source.dxf"), source);
        var repeated = LsiSave(doc, binary);
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, prefix + "-repeat.dxf"), repeated);
        Equal(ids, LsiIds(repeated), "Same-document wire identities changed");
        doc = LsiLoad(source); LsiRegistered(doc, ids);
        for (int i = 0; i < 2; i++)
        {
            byte[] saved = LsiSave(doc, i == 0 ? !binary : binary);
            File.WriteAllBytes(Path.Combine(ArtifactDirectory, prefix + (i == 0 ? "-output.dxf" : "-resave.dxf")), saved);
            Equal(ids, LsiIds(saved), "Loaded dictionary identities changed");
            doc = LsiLoad(saved); LsiRegistered(doc, ids);
            var line = (Line)doc.GetObjectByHandle(lineHandle);
            Check(line.StartPoint == new Vector3(1,2,3) && line.EndPoint == new Vector3(4,5,6), "Following line changed");
        }
    }
    private static void LsiReferences(DxfVersion version, bool binary)
    {
        var doc = LsiSeed(version); var ids = LsiIds(LsiSave(doc, binary));
        var target = doc.GetObjectByHandle(ids.Inner);
        var reference = new DxfXRecord();
        reference.Data.Add(new DxfTag(330, ids.Outer)); reference.Data.Add(new DxfTag(340, ids.Inner));
        doc.NamedObjects.Add("LAYER_IDENTITY_REFERENCE", reference);
        doc.Entities.Lines.Single().PersistentReactors.Add(target);
        for (int i = 0; i < 3; i++)
        {
            doc = LsiLoad(LsiSave(doc, i % 2 == 0 ? binary : !binary)); LsiRegistered(doc, ids);
            var record = (DxfXRecord)doc.NamedObjects["LAYER_IDENTITY_REFERENCE"];
            Check((string)record.Data[0].Value == ids.Outer && (string)record.Data[1].Value == ids.Inner, "Exposed pointers changed");
            Check(ReferenceEquals(doc.Entities.Lines.Single().PersistentReactors.Single(), doc.GetObjectByHandle(ids.Inner)), "Persistent reactor lost source identity");
        }
    }
    private static void LsiOwners(DxfVersion version, bool binary)
    {
        byte[] bytes = LsiSave(LsiSeed(version), binary);
        foreach (bool outer in new[] { false, true })
        {
            using var source = new MemoryStream(bytes); var raw = DxfRawDocument.Load(source);
            var ids = LsiIds(bytes);
            var record = raw.Sections.Single(s => s.Name == "OBJECTS").Records.Single(r => r.Name == "DICTIONARY"
                && LsiHandle(r) == (outer ? ids.Outer : ids.Inner));
            raw = raw.WithRecord(record, record.Tags.Select(t => t.Code == 330 ? new DxfTag(330, "0") : t));
            using var output = new MemoryStream(); raw.Save(output, binary); output.Position = 0;
            bool refused = false;
            try { refused = DxfDocument.Load(output) == null; }
            catch (FormatException) { refused = true; }
            Check(refused && output.CanRead, "Invalid canonical dictionary ownership accepted or stream closed");
        }
    }
    private static void LsiIsolation()
    {
        var a = LsiSeed(DxfVersion.AutoCad2018); var b = LsiSeed(DxfVersion.AutoCad2018);
        var x = LsiIds(LsiSave(a, false)); var y = LsiIds(LsiSave(b, false));
        Check(!ReferenceEquals(a.GetObjectByHandle(x.Inner), b.GetObjectByHandle(y.Inner)), "Two documents share an identity object");
        a.Layers.StateManager.AddNew("a-only");
        Equal(y, LsiIds(LsiSave(b, true)), "Foreign document save changed identifiers");
        Check(b.Layers.StateManager.Count == 0, "Layer states crossed documents");
    }
    private static void LsiMetadataRefusal()
    {
        var doc = LsiSeed(DxfVersion.AutoCad2018); var ids = LsiIds(LsiSave(doc, false));
        var child = doc.GetObjectByHandle(ids.Inner); var line = doc.Entities.Lines.Single();
        string seed = doc.DrawingVariables.HandleSeed;
        child.PersistentReactors.Add(line);
        using var output = new MemoryStream();
        bool refused = false;
        try { refused = !doc.Save(output, false); }
        catch (NotSupportedException) { refused = true; }
        Check(refused && output.Length == 0 && output.CanWrite, "Unsupported metadata was silently dropped or partially written");
        Check(ReferenceEquals(child.PersistentReactors.Single(), line), "Refusal altered caller metadata");
        Equal(seed, doc.DrawingVariables.HandleSeed, "Refusal allocated a replacement identity");
        child.PersistentReactors.Clear();
        Equal(ids, LsiIds(LsiSave(doc, false)), "Clearing unsupported metadata lost identity");
        var data = new XData(new netDxf.Tables.ApplicationRegistry("LSI_METADATA"));
        data.XDataRecord.Add(new XDataRecord(XDataCode.String, "do-not-drop")); child.XData.Add(data);
        using var second = new MemoryStream(); refused = false;
        try { refused = !doc.Save(second, true); }
        catch (NotSupportedException) { refused = true; }
        Check(refused && second.Length == 0 && second.CanWrite && child.XData.Count == 1,
            "Custom child XData was silently lost or partially written");
    }
    private static void LsiCollisions()
    {
        byte[] bytes = LsiSave(LsiSeed(DxfVersion.AutoCad2018), false);
        for (int mode = 0; mode < 3; mode++)
        {
            using var stream = new MemoryStream(bytes); var raw = DxfRawDocument.Load(stream);
            var pair = LsiRecords(bytes); var ids = LsiIds(bytes);
            var child = raw.Sections.Single(s => s.Name == "OBJECTS").Records.Single(r => r.Name == "DICTIONARY" && LsiHandle(r) == ids.Inner);
            var line = raw.Sections.Single(s => s.Name == "ENTITIES").Records.Single(r => r.Name == "LINE");
            string collision = mode == 0 ? ids.Outer : mode == 1 ? LsiHandle(pair.Table) : LsiHandle(line);
            raw = raw.WithRecord(child, child.Tags.Select(t => t.Code == 5 ? new DxfTag(5, collision) : t));
            var outer = raw.Sections.Single(s => s.Name == "OBJECTS").Records.Single(r => r.Name == "DICTIONARY"
                && r.Tags.Any(t => t.Code == 3 && Equals(t.Value, "ACAD_LAYERSTATES")));
            raw = raw.WithRecord(outer, outer.Tags.Select(t => t.Code == 360 ? new DxfTag(360, collision) : t));
            using var output = new MemoryStream(); raw.Save(output, false); output.Position = 0;
            bool refused = false;
            try { refused = DxfDocument.Load(output) == null; }
            catch (FormatException) { refused = true; }
            catch (ArgumentException) { refused = true; } // Existing early duplicate-dictionary admission.
            Check(refused && output.CanRead, "Duplicate dictionary source identity accepted or stream closed");
        }
    }
    private static void LsiNoncanonical()
    {
        byte[] bytes = LsiSave(LsiSeed(DxfVersion.AutoCad2018), false);
        using var stream = new MemoryStream(bytes); var raw = DxfRawDocument.Load(stream);
        var ids = LsiIds(bytes);
        var child = raw.Sections.Single(s => s.Name == "OBJECTS").Records.Single(r => r.Name == "DICTIONARY" && LsiHandle(r) == ids.Inner);
        raw = raw.WithRecord(child, child.Tags.Select(t => t.Code == 280 ? new DxfTag(280, (short)0) : t));
        using var changed = new MemoryStream(); raw.Save(changed, false);
        var loaded = LsiLoad(changed.ToArray());
        Check(LsiIds(LsiSave(loaded, false)) != ids, "Noncanonical data was mislabeled as retained canonical identity");
    }
}
