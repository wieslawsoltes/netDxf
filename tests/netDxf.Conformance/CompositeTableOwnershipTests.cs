using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using netDxf;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterCompositeTableOwnershipTests()
    {
        foreach (bool binary in new[] { false, true })
        {
            foreach (string handle in new[] { "13DF", "13F2" })
                Run($"composite-table/native/{handle}/{binary}", () => CompositeTableNative(handle, binary));
            Run($"composite-table/reversed-order/{binary}", () => CompositeTableOrder(binary));
            foreach (string variant in new[] { "wrong-content", "wrong-geometry", "wrong-table", "null-table", "absent-table", "wrong-owner", "undeclared-child", "cycle" })
                Run($"composite-table/malformed/{variant}/{binary}", () => CompositeTableMalformed(variant, binary));
            foreach (string variant in new[] { "private-marker", "reordered-markers", "extra-marker", "trailing-value", "duplicate-owner-slot", "missing-cell", "unknown-scalar-code", "missing-section" })
                Run($"composite-table/unbound/{variant}/{binary}", () => CompositeTableUnknown(variant, binary));
            foreach (string decoy in new[] { "absent", "unknown-entity", "discarded-underlay", "dictionary-entity", "ignored-section" })
                Run($"composite-table/source-identity/{decoy}/{binary}", () => CompositeTableSourceIdentity(decoy, binary));
        }
        Run("composite-table/bind-atomicity", CompositeTableBindAtomicity);
    }

    private static DxfRawRecord CompositeTableRecord(DxfRawDocument raw, string handle) => raw.Sections.Single(s => s.Name == "OBJECTS").Records.Single(r => r.Tags.Any(t => t.Code == 5 && (string)t.Value == handle));
    private static IEnumerable<DxfTag> CompositeTablePayload(DxfRawRecord record)
    {
        int start = record.Tags.ToList().FindIndex(t => t.Code == 100);
        return record.Tags.Skip(start).TakeWhile(t => t.Code != 1001);
    }
    private static DxfRawDocument CompositeTableSource()
    {
        const string file = "sample_AC1018_ascii.dxf";
        using var compressed = File.OpenRead(Path.Combine("tests", "fixtures", "table-oracle", file + ".gz"));
        using var gzip = new GZipStream(compressed, CompressionMode.Decompress); using var bytes = new MemoryStream(); gzip.CopyTo(bytes);
        using var manifest = JsonDocument.Parse(File.ReadAllText("tools/table_oracle/fixtures.json"));
        string hash = manifest.RootElement.GetProperty("files").EnumerateArray().Single(v => v.GetProperty("file").GetString() == file).GetProperty("sha256").GetString()!;
        Equal(hash, Convert.ToHexString(SHA256.HashData(bytes.ToArray())).ToLowerInvariant(), "composite source hash");
        bytes.Position = 0; return DxfRawDocument.Load(bytes);
    }
    private static (DxfRawDocument Carrier, DxfRawDocument Source, string Root, List<string> Handles) CompositeTableCarrier(string wrapperHandle, bool binary)
    {
        var source = CompositeTableSource(); var wrapper = CompositeTableRecord(source, wrapperHandle);
        var handles = new List<string> { wrapperHandle };
        handles.AddRange(CompositeTablePayload(wrapper).Where(t => t.Code is 360 or 361).Select(t => (string)t.Value));
        var table = CompositeTableRecord(source, handles[3]);
        handles.AddRange(CompositeTablePayload(table).Where(t => t.Code == 360).Select(t => (string)t.Value));
        var doc = new DxfDocument(DxfVersion.AutoCad2004); var owner = new DxfDictionary(); doc.Objects.Root.Add("COMPOSITE_OWNER", owner); string root = owner.Handle;
        using var bytes = new MemoryStream(); Check(doc.Save(bytes, binary), "composite base save"); bytes.Position = 0; var raw = DxfRawDocument.Load(bytes);
        var dictionary = CompositeTableRecord(raw, root);
        raw = raw.WithRecord(dictionary, dictionary.Tags.Concat(new[] { new DxfTag(3, "NATIVE_COMPOSITE"), new DxfTag(360, wrapperHandle) }));
        int boundary = raw.Sections.Single(s => s.Name == "OBJECTS").EndTagIndex - 1;
        // The wrapper's external dictionary owner/reactor is explicitly rebound to
        // this carrier root. Every subclass packet and descendant owner is native.
        var packet = handles.SelectMany(h => h == wrapperHandle ? CompositeTableRecord(source, h).Tags.Select(t => t.Code == 330 ? new DxfTag(330, root) : t) : CompositeTableRecord(source, h).Tags);
        return (raw.WithTags(raw.Tags.Take(boundary).Concat(packet).Concat(raw.Tags.Skip(boundary))), source, root, handles);
    }
    private static DxfDocument? CompositeTableTryLoad(DxfRawDocument raw, bool binary)
    { using var bytes = new MemoryStream(); DxfRawDocument.Create(raw.Tags, binary).Save(bytes); bytes.Position = 0; return DxfDocument.Load(bytes); }
    private static DxfDocument CompositeTableLoad(DxfRawDocument raw, bool binary) => CompositeTableTryLoad(raw, binary) ?? throw new Exception("Composite TABLE load failed.");
    private static void CompositeTableReject(DxfRawDocument raw, bool binary)
    {
        bool rejected;
        try { rejected = CompositeTableTryLoad(raw, binary) == null; } catch (FormatException) { rejected = true; }
        Check(rejected, "A malformed composite TABLE ownership graph loaded");
    }
    private static DxfDatabaseObject[] CompositeTableChildren(DxfDatabaseObject value) => ((IEnumerable<DxfDatabaseObject>)typeof(DxfDatabaseObject).GetProperty("DeclaredOwnedObjects", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value)!).ToArray();

    private static void CompositeTableNative(string wrapperHandle, bool binary)
    {
        var fixture = CompositeTableCarrier(wrapperHandle, binary); var doc = CompositeTableLoad(fixture.Carrier, binary);
        var wrapper = (DxfXRecord)doc.GetObjectByHandle(wrapperHandle); var table = (DxfDataTable)doc.GetObjectByHandle(fixture.Handles[3]);
        Check(wrapper.IsSchemaManaged, "native composite wrapper is unbound"); Equal(15, wrapper.Data.Count, "native wrapper tag count");
        Check(CompositeTableChildren(wrapper).Select(c => c.Handle).SequenceEqual(fixture.Handles.Skip(1).Take(3)), "composite direct child order");
        Equal(wrapperHandle == "13DF" ? 21 : 20, table.RowCount, "native DATATABLE row count");
        Check(CompositeTableChildren(table).Select(c => c.Handle).SequenceEqual(fixture.Handles.Skip(4)), "native DATATABLE owned child sequence");
        foreach (string handle in fixture.Handles.Skip(1)) Equal(fixture.Handles.Skip(1).Take(3).Contains(handle) ? wrapperHandle : table.Handle, doc.GetObjectByHandle(handle).Owner.Handle, "composite reciprocal owner");
        Equal(0, doc.Objects.Validate().Count, "composite validation");
        long seed = OwnershipSeed(doc); int count = doc.Objects.Items.Count();
        foreach (Action change in new Action[] { () => wrapper.Data.Add(new DxfTag(1, "new")), () => wrapper.Data[14] = new DxfTag(360, "FFFFFF"), () => wrapper.Data.Clear() })
            Throws<InvalidOperationException>(change);
        Equal(seed, OwnershipSeed(doc), "composite rejected edit reserved handle");
        Throws<NotSupportedException>(() => doc.Objects.EraseOwnedTree(wrapper));
        var destination = new DxfDocument(DxfVersion.AutoCad2004); int destinationCount = destination.Objects.Items.Count(); long destinationSeed = OwnershipSeed(destination);
        Throws<NotSupportedException>(() => destination.Objects.Clone((DxfDictionary)doc.GetObjectByHandle(fixture.Root), destination.Objects.Root, "COPY"));
        Equal(destinationSeed, OwnershipSeed(destination), "opaque descendant clone allocated"); Equal(destinationCount, destination.Objects.Items.Count(), "opaque descendant clone registered objects");
        Throws<ArgumentException>(() => destination.Objects.Root.Add("FOREIGN", wrapper));
        Equal(destinationSeed, OwnershipSeed(destination), "foreign composite adoption allocated");
        Check(fixture.Handles.All(h => !((DxfDatabaseObject)doc.GetObjectByHandle(h)).IsErased), "rejected erasure changed descendants");
        Equal(count, doc.Objects.Items.Count(), "rejected erasure changed database count");
        CompositeTableSave(doc, fixture.Source, fixture.Handles, wrapperHandle, binary, "native");
        var data = new XData(new netDxf.Tables.ApplicationRegistry("COMPOSITE_TEST")); data.XDataRecord.Add(new XDataRecord(XDataCode.String, "common metadata only")); wrapper.XData.Add(data);
        CompositeTableSave(doc, fixture.Source, fixture.Handles, wrapperHandle, binary, "edited");
        File.WriteAllText(Path.Combine(ArtifactDirectory, $"composite-table-{wrapperHandle}-{binary}-map.json"), JsonSerializer.Serialize(new { source = "sample_AC1018_ascii.dxf", wrapper = wrapperHandle, sourceOwner = wrapperHandle == "13DF" ? "13DE" : "13F1", carrierOwner = fixture.Root, content = fixture.Handles[1], geometry = fixture.Handles[2], dataTable = fixture.Handles[3], rowObjects = fixture.Handles.Skip(4).ToArray() }));
    }
    private static void CompositeTableSave(DxfDocument doc, DxfRawDocument source, List<string> handles, string wrapper, bool binary, string phase)
    {
        using var bytes = new MemoryStream(); Check(doc.Save(bytes, binary), "composite output save");
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"composite-table-{wrapper}-{binary}-{phase}.dxf"), bytes.ToArray()); bytes.Position = 0; var raw = DxfRawDocument.Load(bytes);
        foreach (string handle in handles)
            Check(OwnershipTagValues(CompositeTablePayload(CompositeTableRecord(source, handle))).SequenceEqual(OwnershipTagValues(CompositeTablePayload(CompositeTableRecord(raw, handle)))), "native composite subclass packet differs at " + handle);
        bytes.Position = 0; var loaded = DxfDocument.Load(bytes)!; Check(((DxfXRecord)loaded.GetObjectByHandle(wrapper)).IsSchemaManaged, "composite ownership lost on reload"); Equal(0, loaded.Objects.Validate().Count, "composite reload validation");
    }
    private static void CompositeTableOrder(bool binary)
    {
        var fixture = CompositeTableCarrier("13DF", binary); var raw = fixture.Carrier; var section = raw.Sections.Single(s => s.Name == "OBJECTS");
        int start = section.Records.First().StartTagIndex, end = section.EndTagIndex - 1;
        var native = section.Records.Where(r => r.Tags.Any(t => t.Code == 5 && fixture.Handles.Contains((string)t.Value))).ToArray();
        var prefix = section.Records.Where(r => !native.Contains(r));
        // Keep the required first named-object dictionary; reverse only the tested ownership closure.
        raw = raw.WithTags(raw.Tags.Take(start).Concat(prefix.Concat(native.Reverse()).SelectMany(r => r.Tags)).Concat(raw.Tags.Skip(end)));
        var doc = CompositeTableLoad(raw, binary); Check(((DxfXRecord)doc.GetObjectByHandle("13DF")).IsSchemaManaged, "composite object order changed binding"); Equal(0, doc.Objects.Validate().Count, "reversed composite graph validation");
    }
    private static void CompositeTableMalformed(string variant, bool binary)
    {
        var fixture = CompositeTableCarrier("13DF", binary); var raw = fixture.Carrier; var wrapper = CompositeTableRecord(raw, "13DF"); var tags = wrapper.Tags.ToList();
        int first = tags.FindIndex(t => t.Code == 102 && (string)t.Value == "ACAD_ROUNDTRIP_2008_TABLE_ENTITY");
        if (variant == "wrong-content") tags[first + 1] = new DxfTag(360, "143D");
        if (variant == "wrong-geometry") tags[first + 9] = new DxfTag(361, "143D");
        if (variant == "wrong-table") tags[first + 14] = new DxfTag(360, "747");
        if (variant == "null-table") tags[first + 14] = new DxfTag(360, "0");
        if (variant == "absent-table") tags[first + 14] = new DxfTag(360, "FFFFFF");
        raw = raw.WithRecord(wrapper, tags);
        if (variant == "wrong-owner") { var table = CompositeTableRecord(raw, "143D"); raw = raw.WithRecord(table, table.Tags.Select(t => t.Code == 330 ? new DxfTag(330, fixture.Root) : t)); }
        if (variant == "undeclared-child")
        {
            int boundary = raw.Sections.Single(s => s.Name == "OBJECTS").EndTagIndex - 1;
            raw = raw.WithTags(raw.Tags.Take(boundary).Concat(new DxfTag[] { new(0, "XRECORD"), new(5, "FFFFFF"), new(330, "13DF"), new(100, "AcDbXrecord"), new(280, (short)1), new(1, "unlisted") }).Concat(raw.Tags.Skip(boundary)));
        }
        if (variant == "cycle") { wrapper = CompositeTableRecord(raw, "13DF"); raw = raw.WithRecord(wrapper, wrapper.Tags.Select(t => t.Code == 330 ? new DxfTag(330, "143D") : t)); }
        CompositeTableReject(raw, binary);
    }
    private static void CompositeTableUnknown(string variant, bool binary)
    {
        var fixture = CompositeTableCarrier("13DF", binary); var raw = fixture.Carrier; var wrapper = CompositeTableRecord(raw, "13DF"); var tags = wrapper.Tags.ToList(); int first = tags.FindIndex(t => t.Code == 102 && (string)t.Value == "ACAD_ROUNDTRIP_2008_TABLE_ENTITY");
        if (variant == "private-marker") tags[first + 10] = new DxfTag(102, "PRIVATE_TABLE");
        if (variant == "reordered-markers") (tags[first + 10], tags[first + 13]) = (tags[first + 13], tags[first + 10]);
        if (variant == "extra-marker") tags.Add(new DxfTag(102, "PRIVATE_TAIL"));
        if (variant == "trailing-value") tags.Add(new DxfTag(1, "tail"));
        if (variant == "duplicate-owner-slot") tags.Add(new DxfTag(360, "143D"));
        if (variant == "missing-cell") tags.RemoveAt(first + 14);
        if (variant == "unknown-scalar-code") tags[first + 11] = new DxfTag(92, 7);
        if (variant == "missing-section") tags.RemoveRange(first + 10, 3);
        raw = raw.WithRecord(wrapper, tags); var doc = CompositeTableLoad(raw, binary); var result = (DxfXRecord)doc.GetObjectByHandle("13DF");
        Check(!result.IsSchemaManaged, "unknown composite grammar became bound");
        using var bytes = new MemoryStream(); Check(doc.Save(bytes, binary), "unknown composite save"); bytes.Position = 0; var saved = DxfRawDocument.Load(bytes);
        Check(OwnershipTagValues(CompositeTablePayload(CompositeTableRecord(raw, "13DF"))).SequenceEqual(OwnershipTagValues(CompositeTablePayload(CompositeTableRecord(saved, "13DF")))), "unknown composite payload changed");
    }
    private static void CompositeTableSourceIdentity(string decoy, bool binary)
    {
        var fixture = CompositeTableCarrier("13DF", binary); var raw = fixture.Carrier;
        string missing = (string)raw.Sections.Single(s => s.Name == "HEADER").Records.Single(r => r.Name == "$HANDSEED").Tags.Single(t => t.Code == 5).Value;
        var wrapper = CompositeTableRecord(raw, "13DF"); var tags = wrapper.Tags.ToList(); tags[tags.FindLastIndex(t => t.Code == 360)] = new DxfTag(360, missing);
        CompositeTableReject(SourceReferenceDecoy(raw.WithRecord(wrapper, tags), missing, decoy), binary);
    }
    private static void CompositeTableBindAtomicity()
    {
        var wrapper = OwnershipRecord(); foreach (var tag in new DxfTag[] { new(102, "ACAD_ROUNDTRIP_PRE2007_TABLE"), new(90, 7), new(91, 3), new(102, "ACAD_ROUNDTRIP_PRE2007_TABLECELL"), new(360, "0") }) wrapper.Data.Add(tag);
        var content = OwnershipChild("TABLECONTENT"); var geometry = OwnershipChild("TABLEGEOMETRY"); var data = new DxfDataTable(); var foreign = new DxfDocument(DxfVersion.AutoCad2004); foreign.Objects.Root.Add("DATA", data);
        long seed = OwnershipSeed(foreign);
        Throws<ArgumentException>(() => OwnershipInvoke(wrapper, "BindCompositeTableRoundtripChildren", content, geometry, data));
        Check(!wrapper.IsSchemaManaged && content.Owner == null && geometry.Owner == null, "late invalid child partially bound the composite graph");
        Check(ReferenceEquals(data.Owner, foreign.Objects.Root), "rejected binding changed the foreign owner"); Equal(seed, OwnershipSeed(foreign), "rejected binding allocated handles");
    }
}
