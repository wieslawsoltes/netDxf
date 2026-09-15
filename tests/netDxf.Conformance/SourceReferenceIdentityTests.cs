using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RunSourceReferenceIdentityTests()
    {
        foreach (bool binary in new[] { false, true })
        {
            foreach (string path in new[] { "idbuffer", "dictionary-entry", "dictionary-default", "reactor", "extension" })
            {
                foreach (string decoy in new[] { "absent", "unknown-entity", "discarded-underlay", "dictionary-entity", "ignored-section" })
                    Run($"source-reference/reject/{path}/{decoy}/{binary}", () => SourceReferenceReject(path, decoy, binary));
                if (path != "extension")
                    Run($"source-reference/reject/{path}/generated-table/{binary}", () => SourceReferenceReject(path, "generated-table", binary));
                foreach (bool normalized in new[] { false, true })
                    Run($"source-reference/retained/{path}/{normalized}/{binary}", () => SourceReferenceRetained(path, normalized, binary));
            }
            Run($"source-reference/consumed-extension-wrong-host/{binary}", () => SourceReferenceConsumedWrongHost(binary));
            Run($"source-reference/null-idbuffer/{binary}", () => SourceReferenceNull(binary));
            Run($"source-reference/consumed-layer-state-extension/{binary}", () => SourceReferenceLayerStates(binary));
        }
    }

    private sealed class SourceReferenceFixture
    {
        internal DxfRawDocument Raw = null!;
        internal string Carrier = null!;
        internal string Target = null!;
        internal string Missing = null!;
    }

    private static SourceReferenceFixture SourceReferenceSeed(string path)
    {
        var doc = new DxfDocument(DxfVersion.AutoCad2018);
        doc.Comments.Clear();
        doc.Entities.Add(new Line(Vector3.Zero, Vector3.UnitX));
        var graph = new DxfDictionary();
        var target = new DxfPlaceholder();
        var carrier = new DxfPlaceholder();
        var buffer = new DxfIdBuffer();
        var fallback = new DxfDictionaryWithDefault();
        graph.Add("TARGET", target);
        graph.Add("CARRIER", carrier);
        graph.Add("BUFFER", buffer);
        graph.Add("DEFAULT", fallback);
        graph.Add("ENTRY", target, false);
        buffer.References.Add(target);
        buffer.References.Add(null!);
        fallback.Default = target;
        carrier.PersistentReactors.Add(target);
        doc.NamedObjects.Add("SOURCE_REFERENCES", graph);
        var extension = new DxfDictionary();
        extension.Add("VALUE", new DxfDictionaryVariable { Value = "retained extension" });
        doc.Objects.SetExtensionDictionary(carrier, extension);
        using var stream = new MemoryStream();
        Check(doc.Save(stream), "source-reference seed save");
        stream.Position = 0;
        var raw = DxfRawDocument.Load(stream);
        string missing = (string)raw.Sections.Single(s => s.Name == "HEADER").Records
            .Single(r => r.Name == "$HANDSEED").Tags.Single(t => t.Code == 5).Value;
        Check(!raw.Sections.Where(s => s.Name != "HEADER").SelectMany(s => s.Records)
            .Any(r => r.Tags.TakeWhile(t => t.Code != 100).Any(t => (t.Code == 5 || t.Code == 105) && Equals(t.Value, missing))),
            "HANDSEED must be absent from physical record identities");
        return new SourceReferenceFixture
        {
            Raw = raw,
            Carrier = path == "idbuffer" ? buffer.Handle : path == "dictionary-entry" ? graph.Handle
                : path == "dictionary-default" ? fallback.Handle : carrier.Handle,
            Target = path == "extension" ? extension.Handle : target.Handle,
            Missing = missing
        };
    }

    private static DxfRawRecord SourceReferenceRecord(DxfRawDocument raw, string handle) =>
        raw.Sections.Where(s => s.Name != "HEADER").SelectMany(s => s.Records).Single(r =>
            r.Tags.TakeWhile(t => t.Code != 100).Any(t => (t.Code == 5 || t.Code == 105) && Equals(t.Value, handle)));

    private static DxfRawDocument SourceReferenceReplace(SourceReferenceFixture fixture, string path, string value)
    {
        DxfRawRecord record = SourceReferenceRecord(fixture.Raw, fixture.Carrier);
        var tags = record.Tags.ToList();
        int index;
        if (path == "dictionary-entry")
            index = tags.FindIndex(t => t.Code == 3 && Equals(t.Value, "ENTRY")) + 1;
        else if (path == "dictionary-default")
            index = tags.FindIndex(t => t.Code == 340);
        else if (path == "idbuffer")
            index = tags.FindIndex(tags.FindIndex(t => t.Code == 100) + 1, t => t.Code == 330);
        else
        {
            string group = path == "reactor" ? "{ACAD_REACTORS" : "{ACAD_XDICTIONARY";
            index = tags.FindIndex(t => t.Code == 102 && Equals(t.Value, group)) + 1;
        }
        Check(index > 0 && Equals(tags[index].Value, fixture.Target), "expected reference packet location");
        tags[index] = new DxfTag(tags[index].Code, value);
        return fixture.Raw.WithRecord(record, tags);
    }

    private static DxfRawDocument SourceReferenceDecoy(DxfRawDocument raw, string handle, string decoy)
    {
        if (decoy == "absent") return raw;
        var tags = raw.Tags.ToList();
        if (decoy == "ignored-section")
        {
            // The ignored packet deliberately has the generated manager's DICTIONARY kind.
            // Its mere lexical presence must not authorize a runtime object at this handle.
            int eof = tags.FindIndex(t => t.Code == 0 && Equals(t.Value, "EOF"));
            tags.InsertRange(eof, new[] {
                new DxfTag(0, "SECTION"), new DxfTag(2, "FUTURE_SOURCE_SECTION"),
                new DxfTag(0, "DICTIONARY"), new DxfTag(5, handle),
                new DxfTag(100, "AcDbDictionary"), new DxfTag(0, "ENDSEC") });
        }
        else
        {
            string kind = decoy == "discarded-underlay" ? "PDFUNDERLAY" : decoy == "dictionary-entity" ? "DICTIONARY" : "FUTURE_ENTITY";
            string subclass = decoy == "discarded-underlay" ? "AcDbUnderlayReference" : decoy == "dictionary-entity" ? "AcDbDictionary" : "AcDbFutureEntity";
            var packet = new List<DxfTag> {
                new DxfTag(0, kind), new DxfTag(5, handle), new DxfTag(100, "AcDbEntity"),
                new DxfTag(8, "0"), new DxfTag(100, subclass) };
            if (decoy == "discarded-underlay") packet.Add(new DxfTag(340, "0"));
            tags.InsertRange(raw.Sections.Single(s => s.Name == "ENTITIES").ContentStartTagIndex, packet);
            if (decoy == "generated-table")
            {
                // A successfully constructed TABLE collection can still have a generated
                // identity when its original record omitted group 5. Accepted-instance
                // tracking must retain that distinction even if another record has 5.
                var table = raw.Sections.Single(s => s.Name == "TABLES").Records.Single(r => r.Name == "TABLE" && r.Tags.Any(t => t.Code == 2 && Equals(t.Value, "LAYER")));
                int identity = table.Tags.ToList().FindIndex(t => t.Code == 5);
                Check(identity >= 0, "source LAYER table identity");
                tags.RemoveAt(table.StartTagIndex + identity);
            }
        }
        return raw.WithTags(tags);
    }

    private static void SourceReferenceReject(string path, string decoy, bool binary)
    {
        SourceReferenceFixture fixture = SourceReferenceSeed(path);
        DxfRawDocument raw = SourceReferenceDecoy(SourceReferenceReplace(fixture, path, fixture.Missing), fixture.Missing, decoy);
        using var stream = new MemoryStream();
        raw.Save(stream, binary);
        stream.Position = 0;
        bool rejected = false;
        try { rejected = DxfDocument.Load(stream) == null; }
        catch (FormatException exception)
        {
            string expected = path switch {
                "idbuffer" => "IDBUFFER", "dictionary-entry" => "dictionary entry",
                "dictionary-default" => "dictionary default", "reactor" => "persistent reactor",
                _ => "extension dictionary" };
            Check(exception.Message.Contains(expected, StringComparison.OrdinalIgnoreCase), "rejection must identify the dangling reference");
            rejected = true;
        }
        Check(rejected, "a missing or discarded source object resolved to a synthesized runtime object");
        Check(stream.CanRead, "rejection closed the caller stream");
    }

    private static DxfObject? SourceReferenceValue(DxfDocument doc, string handle, string path)
    {
        DxfObject carrier = doc.GetObjectByHandle(handle);
        return path switch {
            "idbuffer" => ((DxfIdBuffer)carrier).References[0],
            "dictionary-entry" => ((DxfDictionary)carrier)["ENTRY"],
            "dictionary-default" => ((DxfDictionaryWithDefault)carrier).Default,
            "reactor" => carrier.PersistentReactors.Single(),
            _ => carrier.ExtensionDictionary };
    }

    private static void SourceReferenceRetained(string path, bool normalized, bool binary)
    {
        SourceReferenceFixture fixture = SourceReferenceSeed(path);
        string spelling = normalized ? "000" + fixture.Target.ToLowerInvariant() : fixture.Target;
        DxfRawDocument raw = SourceReferenceReplace(fixture, path, spelling);
        if (normalized)
        {
            DxfRawRecord target = SourceReferenceRecord(raw, fixture.Target);
            var tags = target.Tags.ToList();
            int identity = tags.FindIndex(t => t.Code == 5);
            tags[identity] = new DxfTag(5, spelling);
            raw = raw.WithRecord(target, tags);
        }
        using var stream = new MemoryStream();
        raw.Save(stream, binary);
        stream.Position = 0;
        var loaded = DxfDocument.Load(stream) ?? throw new InvalidOperationException("retained source load failed");
        DxfObject expected = loaded.GetObjectByHandle(fixture.Target);
        Check(expected != null && ReferenceEquals(expected, SourceReferenceValue(loaded, fixture.Carrier, path)), "retained source identity was replaced or lost");
        Equal(0, loaded.Objects.Validate().Count, "retained source graph");
        using var output = new MemoryStream();
        Check(loaded.Save(output, !binary), "retained source cross-transport save");
        output.Position = 0;
        var again = DxfDocument.Load(output) ?? throw new InvalidOperationException("retained source reload failed");
        Check(ReferenceEquals(again.GetObjectByHandle(fixture.Target), SourceReferenceValue(again, fixture.Carrier, path)), "retained source identity lost on reload");
    }

    private static void SourceReferenceConsumedWrongHost(bool binary)
    {
        SourceReferenceFixture fixture = SourceReferenceSeed("extension");
        DxfRawRecord layer = fixture.Raw.Sections.Single(s => s.Name == "TABLES").Records
            .Single(r => r.Name == "TABLE" && r.Tags.Any(t => t.Code == 2 && Equals(t.Value, "LAYER")));
        string consumed = (string)layer.Tags.Single(t => t.Code == 360).Value;
        Check(SourceReferenceRecord(fixture.Raw, consumed).Name == "DICTIONARY", "consumed extension source exists");
        using var stream = new MemoryStream();
        SourceReferenceReplace(fixture, "extension", consumed).Save(stream, binary);
        stream.Position = 0;
        bool rejected = false;
        try { rejected = DxfDocument.Load(stream) == null; }
        catch (FormatException error)
        {
            Check(error.Message.Contains("extension dictionary", StringComparison.OrdinalIgnoreCase), "wrong host diagnostic");
            rejected = true;
        }
        Check(rejected, "the consumed LAYER dictionary must not silently satisfy an unrelated object's extension reference");
        Check(stream.CanRead, "wrong host rejection closed the caller stream");
    }

    private static void SourceReferenceNull(bool binary)
    {
        SourceReferenceFixture fixture = SourceReferenceSeed("idbuffer");
        using var stream = new MemoryStream();
        SourceReferenceReplace(fixture, "idbuffer", "000").Save(stream, binary);
        stream.Position = 0;
        var doc = DxfDocument.Load(stream) ?? throw new InvalidOperationException("null reference load failed");
        Check(((DxfIdBuffer)doc.GetObjectByHandle(fixture.Carrier)).References.All(r => r == null), "null reference acquired an identity");
        Equal(0, doc.Objects.Validate().Count, "null reference graph");
    }

    private static void SourceReferenceLayerStates(bool binary)
    {
        var doc = new DxfDocument(DxfVersion.AutoCad2018);
        doc.Layers.StateManager.AddNew("SOURCE_SNAPSHOT");
        doc.NamedObjects.Add("APP", new DxfDictionaryVariable { Value = "source metadata" });
        using var stream = new MemoryStream();
        Check(doc.Save(stream, binary), "managed source save");
        stream.Position = 0;
        var raw = DxfRawDocument.Load(stream);
        var layerTable = raw.Sections.Single(s => s.Name == "TABLES").Records.Single(r => r.Name == "TABLE" && r.Tags.Any(t => t.Code == 2 && Equals(t.Value, "LAYER")));
        string extension = (string)layerTable.Tags.Single(t => t.Code == 360).Value;
        Check(SourceReferenceRecord(raw, extension).Name == "DICTIONARY", "managed extension must have a real source dictionary");
        stream.Position = 0;
        var loaded = DxfDocument.Load(stream) ?? throw new InvalidOperationException("managed source load failed");
        Equal(1, loaded.Layers.StateManager.Count, "consumed layer-state mapping");
        Check(loaded.Layers.StateManager.Contains("SOURCE_SNAPSHOT"), "consumed snapshot name lost");
        Equal("source metadata", ((DxfDictionaryVariable)loaded.NamedObjects["APP"]).Value, "ordinary source object beside consumed mapping");
        Equal(0, loaded.Objects.Validate().Count, "managed source graph");
        using var output = new MemoryStream();
        Check(loaded.Save(output, !binary), "managed source cross-transport save");
        output.Position = 0;
        var again = DxfDocument.Load(output) ?? throw new InvalidOperationException("managed source reload failed");
        Check(again.Layers.StateManager.Contains("SOURCE_SNAPSHOT"), "consumed mapping lost on reload");
    }
}
