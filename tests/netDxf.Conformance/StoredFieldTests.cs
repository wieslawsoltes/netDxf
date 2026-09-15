using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;
using netDxf.Tables;
namespace NetDxf.Conformance;
internal static partial class Program
{
    private static void RegisterStoredFieldTests()
    {
        foreach (DxfVersion version in new[] { DxfVersion.AutoCad2000, DxfVersion.AutoCad2018 })
            foreach (bool binary in new[] { false, true })
                Run($"stored-field/native/{version}/{binary}", () => StoredFieldNative(version, binary));
        foreach (DxfVersion version in SupportedVersions)
            foreach (bool binary in new[] { false, true })
                Run($"stored-field/graph/{version}/{binary}", () => StoredFieldGraph(version, binary));
        foreach (bool binary in new[] { false, true })
        {
            foreach (string variant in new[] { "negative-child", "large-child", "missing-child", "wrong-owner", "wrong-child-type", "duplicate-child", "null-child", "negative-objects", "object-count", "unresolved", "cycle" })
                Run($"stored-field/malformed/{variant}/{binary}", () => StoredFieldMalformed(variant, binary));
            foreach (string variant in new[] { "alias", "private-subclass", "private-control", "private-header", "unrecognized-prefix" })
                Run($"stored-field/opaque/{variant}/{binary}", () => StoredFieldOpaque(variant, binary));
        }
        foreach (bool binary in new[] { false, true })
            Run($"stored-field/table-reference/{binary}", () => StoredFieldTableReference(binary));
        foreach (bool binary in new[] { false, true })
            foreach (string decoy in new[] { "absent", "unknown-entity", "discarded-underlay", "dictionary-entity", "ignored-section" })
                Run($"stored-field/source-identity/{decoy}/{binary}", () => StoredFieldSourceIdentity(decoy, binary));
        foreach (short code in new short[] { 330, 339, 340, 349, 350, 359, 360, 369, 390, 399, 480, 481, 320, 329 })
            Run($"stored-field/exposed-dependency/{code}", () => StoredFieldSemantic(code));
    }
    private static long StoredFieldSeed(DxfDocument doc) => (long)typeof(DxfDocument).GetProperty("NumHandles", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(doc)!;
    private static string StoredFieldProfile(DxfVersion version) => version == DxfVersion.AutoCad2000 ? "AC1015" : "AC1032";
    private static DxfRawDocument StoredFieldSource(DxfVersion version)
    {
        string profile = StoredFieldProfile(version), path = $"tests/fixtures/field-oracle/TS1-{profile}.dxf.gz";
        using var compressed = File.OpenRead(path); using var gzip = new GZipStream(compressed, CompressionMode.Decompress); using var bytes = new MemoryStream(); gzip.CopyTo(bytes);
        using var manifest = JsonDocument.Parse(File.ReadAllText("tests/fixtures/field-oracle/manifest.json"));
        string expected = manifest.RootElement.GetProperty("files").EnumerateArray().Single(f => f.GetProperty("profile").GetString() == profile).GetProperty("sha256").GetString()!;
        Equal(expected, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes.ToArray())).ToLowerInvariant(), "FIELD pinned source bytes");
        bytes.Position = 0; return DxfRawDocument.Load(bytes);
    }
    private static IEnumerable<DxfTag> StoredFieldPayload(DxfRawRecord record) => record.Tags.SkipWhile(t => t.Code != 100).TakeWhile(t => t.Code != 1001);
    private static string StoredFieldHandle(DxfRawRecord record) => (string)record.Tags.First(t => t.Code == 5).Value;
    private static DxfRawRecord StoredFieldRecord(DxfRawDocument raw, string handle) => raw.Sections.SelectMany(s => s.Records).Single(r => r.Tags.TakeWhile(t => t.Code != 100).Any(t => t.Code == 5 && (string)t.Value == handle));
    private static DxfDocument StoredFieldLoad(DxfRawDocument raw, bool binary)
    { using var bytes = new MemoryStream(); DxfRawDocument.Create(raw.Tags, binary).Save(bytes); bytes.Position = 0; return DxfDocument.Load(bytes) ?? throw new Exception("FIELD carrier failed to load."); }
    private static DxfDocument StoredFieldCarrier(DxfVersion version, bool binary, out DxfRawDocument native)
    {
        native = StoredFieldSource(version);
        var doc = new DxfDocument(version); var root = doc.Objects.Root;
        doc.Entities.Add(new Line(Vector3.Zero, Vector3.UnitX)); doc.Entities.Add(new Line(Vector3.UnitY, Vector3.UnitZ));
        using var bytes = new MemoryStream(); Check(doc.Save(bytes, binary), "FIELD carrier base save"); bytes.Position = 0; var raw = DxfRawDocument.Load(bytes);
        for (int i = 0; i < 2; i++)
        {
            var line = raw.Sections.Single(s => s.Name == "ENTITIES").Records.Where(r => r.Name == "LINE").ElementAt(i);
            string host = i == 0 ? "14B" : "15B", extension = i == 0 ? "14C" : "15C";
            var tags = line.Tags.Select(t => t.Code == 5 ? new DxfTag(5, host) : t).ToList();
            int start = tags.FindIndex(t => t.Code == 100);
            tags.InsertRange(start, new[] { new DxfTag(102, "{ACAD_XDICTIONARY"), new DxfTag(360, extension), new DxfTag(102, "}") });
            raw = raw.WithRecord(line, tags);
        }
        var rootRecord = StoredFieldRecord(raw, root.Handle);
        raw = raw.WithRecord(rootRecord, rootRecord.Tags.Concat(new[] { new DxfTag(3, "ACAD_FIELDLIST"), new DxfTag(350, "150") }));
        var selected = new HashSet<string> { "14C", "14D", "14E", "14F", "15C", "15D", "15E", "15F", "150" };
        var records = native.Sections.Single(s => s.Name == "OBJECTS").Records.Where(r => r.Tags.Any(t => t.Code == 5 && selected.Contains((string)t.Value))).ToList();
        var additions = new List<DxfTag>();
        foreach (var record in records)
        {
            bool common = true;
            foreach (var tag in record.Tags)
            {
                if (tag.Code == 100) common = false;
                additions.Add(common && record.Name == "FIELDLIST" && tag.Code == 330 && (string)tag.Value == "C" ? new DxfTag(330, root.Handle) : tag);
            }
        }
        string? section = null; bool sectionName = false; var complete = new List<DxfTag>();
        foreach (var tag in raw.Tags)
        {
            if (tag.Code == 0 && (string)tag.Value == "SECTION") sectionName = true;
            else if (sectionName && tag.Code == 2) { section = (string)tag.Value; sectionName = false; }
            if (tag.Code == 0 && (string)tag.Value == "ENDSEC")
            {
                if (section == "OBJECTS") complete.AddRange(additions);
                if (section == "CLASSES") complete.AddRange(native.Sections.Single(s => s.Name == "CLASSES").Records.Where(r => r.Name == "CLASS" && r.Tags.Any(t => t.Code == 1 && ((string)t.Value == "FIELD" || (string)t.Value == "FIELDLIST"))).SelectMany(r => r.Tags));
                section = null;
            }
            complete.Add(tag);
        }
        return StoredFieldLoad(raw.WithTags(complete), binary);
    }
    private static void StoredFieldNative(DxfVersion version, bool binary)
    {
        var doc = StoredFieldCarrier(version, binary, out var native);
        Equal(4, doc.Objects.Items.OfType<DxfStoredField>().Count(), "native FIELD count");
        foreach (var field in doc.Objects.Items.OfType<DxfStoredField>())
        {
            var expected = StoredFieldPayload(StoredFieldRecord(native, field.Handle));
            Check(OwnershipTagValues(expected).SequenceEqual(OwnershipTagValues(field.Payload)), "exact native FIELD payload");
            Equal(version, field.SourceVersion, "native profile");
            bool parent = field.Handle.EndsWith("E", StringComparison.Ordinal);
            Equal(parent ? "_text" : "AcSm", field.EvaluatorId, "native evaluator");
            Equal(parent ? "%<\\_FldIdx 0>%" : "\\AcSm Sheet.Number \\f \"%tc1\"", field.FieldCode, "native field code");
            Equal(parent ? 1 : 0, field.Children.Count, "native children");
            if (parent) Check(ReferenceEquals(field.Children.Single().Owner, field), "native reciprocal child");
            Equal(0, field.ReferencedObjects.Count, "native object references");
        }
        Equal(0, doc.Objects.Validate().Count, "native scoped graph validity");
        foreach (var host in doc.Entities.Lines.ToList()) Check(!doc.Entities.Remove(host), "native FIELD host was removed");
        StoredFieldSave(doc, version, binary, "native", native);
        foreach (var field in doc.Objects.Items.OfType<DxfStoredField>())
        {
            var data = new XData(new ApplicationRegistry("FIELD_COMMON_EDIT")); data.XDataRecord.Add(new XDataRecord(XDataCode.String, "metadata only")); field.XData.Add(data);
            Throws<NotSupportedException>(() => doc.Objects.EraseOwnedTree(field));
        }
        StoredFieldSave(doc, version, binary, "native-edited", native);
    }
    private static DxfRawDocument StoredFieldSynthetic(DxfVersion version, bool binary, out Dictionary<string,string> handles, short extraCode = 0)
    {
        var doc = new DxfDocument(version); var graph = new DxfDictionary(); doc.Objects.Root.Add("FIELD_GRAPH", graph);
        var parent = new DxfXRecord(); var child = new DxfXRecord(); graph.Add("FIELD", parent); graph.Add("TEMP_CHILD", child);
        var external = new DxfXRecord(); external.Data.Add(new DxfTag(1, "external survives")); doc.Objects.Root.Add("FIELD_EXTERNAL", external);
        var layer = doc.Layers.Add(new Layer("FIELD_LAYER")); var style = doc.TextStyles.Add(new TextStyle("FIELD_STYLE", "txt.shx"));
        var app = doc.ApplicationRegistries.Add(new ApplicationRegistry("FIELD_REGISTRY"));
        var target = new Line(Vector3.Zero, Vector3.UnitX); doc.Entities.Add(target);
        handles = new Dictionary<string,string> { ["parent"] = parent.Handle, ["child"] = child.Handle, ["graph"] = graph.Handle, ["external"] = external.Handle, ["layer"] = layer.Handle, ["style"] = style.Handle, ["app"] = app.Handle, ["line"] = target.Handle };
        using var bytes = new MemoryStream(); Check(doc.Save(bytes, binary), "synthetic base save"); bytes.Position = 0; var raw = DxfRawDocument.Load(bytes);
        var dictionary = StoredFieldRecord(raw, graph.Handle); var dictionaryTags = dictionary.Tags.ToList();
        int childName = dictionaryTags.FindIndex(t => t.Code == 3 && (string)t.Value == "TEMP_CHILD"); dictionaryTags.RemoveRange(childName, 2); raw = raw.WithRecord(dictionary, dictionaryTags);
        var payload = new List<DxfTag> { new(100, "AcDbField"), new(1, "Synthetic"), new(2, "prefix \\U+03"), new(3, "A9 suffix"), new(90, 1), new(360, child.Handle), new(97, 7), new(331, layer.Handle), new(331, style.Handle), new(331, app.Handle), new(331, target.Handle), new(331, external.Handle), new(331, external.Handle), new(331, "0000"), new(91, 63), new(92, 0), new(94, 43), new(95, 32), new(96, 335), new(300, "stored error"), new(93, 0), new(7, "ACFD_FIELD_VALUE"), new(90, 0), new(91, 0), new(301, "####"), new(98, 4), new(310, new byte[] { 0, 1, 255 }) };
        if (extraCode != 0) payload.Add(new DxfTag(extraCode, external.Handle));
        var parentRecord = StoredFieldRecord(raw, parent.Handle);
        raw = raw.WithRecord(parentRecord, parentRecord.Tags.TakeWhile(t => t.Code != 100).Select(t => t.Code == 0 ? new DxfTag(0, "FIELD") : t).Concat(payload));
        var childRecord = StoredFieldRecord(raw, child.Handle);
        raw = raw.WithRecord(childRecord, childRecord.Tags.TakeWhile(t => t.Code != 100).Select(t => t.Code == 0 ? new DxfTag(0, "FIELD") : t.Code == 330 ? new DxfTag(330, parent.Handle) : t).Concat(new DxfTag[] { new(100, "AcDbField"), new(1, "Child"), new(2, "1+1"), new(90, 0), new(97, 0), new(93, 0), new(7, "ACFD_FIELD_VALUE"), new(90, 0) }));
        return raw;
    }
    private static void StoredFieldGraph(DxfVersion version, bool binary)
    {
        var raw = StoredFieldSynthetic(version, binary, out var handles); var doc = StoredFieldLoad(raw, binary);
        var field = (DxfStoredField)doc.GetObjectByHandle(handles["parent"]); var graph = (DxfDictionary)doc.GetObjectByHandle(handles["graph"]);
        Equal("prefix Ω suffix", field.FieldCode, "decode complete ordered chunks"); Equal(1, field.Children.Count, "stored child"); Equal(7, field.ReferencedObjects.Count, "ordered object references");
        Check(ReferenceEquals(field.ReferencedObjects[4], field.ReferencedObjects[5]) && field.ReferencedObjects[6] == null, "repeated and null references");
        Check(!doc.Layers.Remove(doc.Layers["FIELD_LAYER"]), "FIELD layer removed"); Check(!doc.TextStyles.Remove(doc.TextStyles["FIELD_STYLE"]), "FIELD text style removed"); Check(!doc.ApplicationRegistries.Remove(doc.ApplicationRegistries["FIELD_REGISTRY"]), "FIELD APPID removed"); Check(!doc.Entities.Remove(doc.Entities.Lines.Single()), "FIELD LINE removed");
        Throws<InvalidOperationException>(() => doc.Objects.EraseOwnedTree((DxfXRecord)doc.GetObjectByHandle(handles["external"])));
        Throws<NotSupportedException>(() => doc.Objects.EraseOwnedTree(graph));
        var destination = new DxfDocument(version); _ = destination.Objects.Root; long seed = StoredFieldSeed(destination); int count = destination.Objects.Items.Count;
        Throws<NotSupportedException>(() => destination.Objects.Clone(graph, destination.Objects.Root, "FIELD_COPY"));
        Equal(seed, StoredFieldSeed(destination), "FIELD clone allocated handles"); Equal(count, destination.Objects.Items.Count, "FIELD clone registered objects"); Check(!destination.Objects.Root.Contains("FIELD_COPY"), "FIELD clone created dictionary name");
        Throws<ArgumentException>(() => destination.Objects.Root.Add("FIELD_FOREIGN", field)); Equal(seed, StoredFieldSeed(destination), "FIELD foreign adoption allocated handles");
        Throws<NotSupportedException>(() => ((IList<DxfTag>)field.Payload).Add(new DxfTag(1, "mutation")));
        byte[] chunk = (byte[])field.Payload.Single(t => t.Code == 310).Value; chunk[0] = 99; Equal((byte)0, ((byte[])field.Payload.Single(t => t.Code == 310).Value)[0], "immutable binary payload");
        var snapshot = OwnershipTagValues(field.Payload).ToArray(); doc.Layers["FIELD_LAYER"].Name = "FIELD_LAYER_RENAMED"; doc.TextStyles["FIELD_STYLE"].Name = "FIELD_STYLE_RENAMED"; doc.ApplicationRegistries["FIELD_REGISTRY"].Name = "FIELD_REGISTRY_RENAMED";
        Check(OwnershipTagValues(field.Payload).SequenceEqual(snapshot), "resource rename rewrote evaluator/cache data"); Equal(0, doc.Objects.Validate().Count, "FIELD identity after resource rename");
        var other = version == DxfVersion.AutoCad2000 ? DxfVersion.AutoCad2018 : DxfVersion.AutoCad2000; doc.DrawingVariables.AcadVer = other; using var invalid = new MemoryStream();
        Throws<InvalidOperationException>(() => doc.Save(invalid, binary)); Equal(0L, invalid.Length, "FIELD profile conversion wrote output"); doc.DrawingVariables.AcadVer = version;
        StoredFieldSave(doc, version, binary, "graph", null);
        File.WriteAllText(Path.Combine(ArtifactDirectory, $"stored-field-{version}-{binary}-map.json"), JsonSerializer.Serialize(handles));
    }
    private static void StoredFieldSave(DxfDocument doc, DxfVersion version, bool binary, string phase, DxfRawDocument? native)
    {
        using var bytes = new MemoryStream(); Check(doc.Save(bytes, binary), "FIELD output save"); File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"stored-field-{version}-{binary}-{phase}.dxf"), bytes.ToArray()); bytes.Position = 0;
        var raw = DxfRawDocument.Load(bytes);
        foreach (var field in doc.Objects.Items.OfType<DxfStoredField>())
            Check(OwnershipTagValues(field.Payload).SequenceEqual(OwnershipTagValues(StoredFieldPayload(StoredFieldRecord(raw, field.Handle)))), "saved FIELD payload differs");
        if (native != null)
            foreach (string handle in new[] { "14C", "14D", "14E", "14F", "15C", "15D", "15E", "15F", "150" })
                Check(OwnershipTagValues(StoredFieldPayload(StoredFieldRecord(native, handle))).SequenceEqual(OwnershipTagValues(StoredFieldPayload(StoredFieldRecord(raw, handle)))), "native owner/field/list payload differs: " + handle);
        bytes.Position = 0; var again = DxfDocument.Load(bytes)!; Equal(doc.Objects.Items.OfType<DxfStoredField>().Count(), again.Objects.Items.OfType<DxfStoredField>().Count(), "FIELD typed reload"); Equal(0, again.Objects.Validate().Count, "FIELD reload graph validation");
    }
    private static void StoredFieldMalformed(string variant, bool binary)
    {
        var raw = StoredFieldSynthetic(DxfVersion.AutoCad2018, binary, out var handles); var record = StoredFieldRecord(raw, handles["parent"]); var tags = record.Tags.ToList();
        int children = tags.FindIndex(t => t.Code == 90), objects = tags.FindIndex(t => t.Code == 97), child = tags.FindIndex(t => t.Code == 360);
        if (variant == "negative-child") tags[children] = new DxfTag(90, -1);
        if (variant == "large-child") tags[children] = new DxfTag(90, int.MaxValue);
        if (variant == "missing-child") tags.RemoveAt(child);
        if (variant == "null-child") tags[child] = new DxfTag(360, "0000");
        if (variant == "wrong-child-type") tags[child] = new DxfTag(360, handles["external"]);
        if (variant == "duplicate-child") { tags[children] = new DxfTag(90, 2); tags.Insert(child, tags[child]); }
        if (variant == "negative-objects") tags[objects] = new DxfTag(97, -1);
        if (variant == "object-count") tags[objects] = new DxfTag(97, 8);
        if (variant == "unresolved") tags[objects + 1] = new DxfTag(331, "FFFFFF");
        if (variant == "cycle") { tags[child] = new DxfTag(360, handles["parent"]); tags[tags.FindIndex(t => t.Code == 330)] = new DxfTag(330, handles["parent"]); }
        raw = raw.WithRecord(record, tags);
        if (variant == "wrong-owner") { record = StoredFieldRecord(raw, handles["child"]); raw = raw.WithRecord(record, record.Tags.Select(t => t.Code == 330 ? new DxfTag(330, handles["graph"]) : t)); }
        Throws<FormatException>(() => StoredFieldLoad(raw, binary));
    }
    private static void StoredFieldOpaque(string variant, bool binary)
    {
        var raw = StoredFieldSynthetic(DxfVersion.AutoCad2018, binary, out var handles); var record = StoredFieldRecord(raw, handles["parent"]); var tags = record.Tags.ToList();
        if (variant == "alias") tags[0] = new DxfTag(0, "ACAD_FIELD");
        if (variant == "private-subclass") tags.Add(new DxfTag(100, "AcmePrivateField"));
        if (variant == "private-control") tags.AddRange(new DxfTag[] { new(102, "{PRIVATE"), new(1, "preserve"), new(102, "}") });
        if (variant == "private-header") tags.InsertRange(tags.FindIndex(t => t.Code == 100), new DxfTag[] { new(102, "{PRIVATE"), new(1, "preserve"), new(102, "}") });
        if (variant == "unrecognized-prefix") tags[tags.FindIndex(t => t.Code == 1)] = new DxfTag(4, "unknown prefix");
        raw = raw.WithRecord(record, tags); var doc = StoredFieldLoad(raw, binary); Check(doc.GetObjectByHandle(handles["parent"]) is DxfOpaqueObject, "private FIELD was partially typed");
        using var output = new MemoryStream(); Check(doc.Save(output, binary), "opaque FIELD save"); output.Position = 0; var saved = DxfRawDocument.Load(output); var after = StoredFieldRecord(saved, handles["parent"]);
        Check(OwnershipTagValues(tags.SkipWhile(t => t.Code != 100 && t.Code != 102).Select(t => t.ValueType == DxfTagValueType.Handle ? new DxfTag(t.Code, Convert.ToUInt64((string)t.Value, 16).ToString("X")) : t)).SequenceEqual(OwnershipTagValues(after.Tags.SkipWhile(t => t.Code != 100 && t.Code != 102))), "whole opaque FIELD packet differs");
    }
    private static void StoredFieldTableReference(bool binary)
    {
        var raw = StoredFieldSynthetic(DxfVersion.AutoCad2018, binary, out var handles);
        var line = StoredFieldRecord(raw, handles["line"]); var prefix = line.Tags.TakeWhile(t => t.Code != 100 || (string)t.Value == "AcDbEntity").ToList();
        // The following common entity tags remain ordinary metadata before the table subclasses.
        int start = line.Tags.ToList().FindIndex(t => t.Code == 100 && (string)t.Value != "AcDbEntity");
        prefix = line.Tags.Take(start).Select(t => t.Code == 0 ? new DxfTag(0, "ACAD_TABLE") : t).ToList();
        var payload = StoredTableTestPayload(); payload.Insert(payload.FindIndex(t => t.Code == 301), new DxfTag(344, handles["parent"]));
        raw = raw.WithRecord(line, prefix.Concat(payload));
        var doc = StoredFieldLoad(raw, binary); var table = doc.Entities.StoredTables.Single(); var field = (DxfStoredField)doc.GetObjectByHandle(handles["parent"]);
        Check(table.Grid != null && table.Grid.Cells.Single().HasFieldReference && !table.Grid.Cells.Single().HasLiteralValue, "TABLE field precedence");
        Check(table.References.Contains(field) && field.References.Contains(table), "TABLE/FIELD exact cross references");
        Check(!doc.Entities.Remove(table), "referenced TABLE was removed");
        using var bytes = new MemoryStream(); Check(doc.Save(bytes, binary), "TABLE FIELD save"); bytes.Position = 0; var again = DxfDocument.Load(bytes)!;
        Check(again.Entities.StoredTables.Single().References.Contains(again.GetObjectByHandle(handles["parent"])), "TABLE FIELD reference after reload");
    }
    private static void StoredFieldSourceIdentity(string decoy, bool binary)
    {
        var raw = StoredFieldSynthetic(DxfVersion.AutoCad2018, binary, out var handles);
        string missing = (string)raw.Sections.Single(s => s.Name == "HEADER").Records.Single(r => r.Name == "$HANDSEED").Tags.Single(t => t.Code == 5).Value;
        var record = StoredFieldRecord(raw, handles["parent"]); var tags = record.Tags.ToList(); int slot = tags.FindIndex(t => t.Code == 331); tags[slot] = new DxfTag(331, missing);
        raw = SourceReferenceDecoy(raw.WithRecord(record, tags), missing, decoy);
        bool rejected = false;
        try { StoredFieldLoad(raw, binary); }
        catch (FormatException error) { Check(error.Message.Contains("FIELD dependency"), "source rejection must identify FIELD dependency"); rejected = true; }
        Check(rejected, "FIELD accepted generated or discarded source identity");
    }
    private static void StoredFieldSemantic(short code)
    {
        var raw = StoredFieldSynthetic(DxfVersion.AutoCad2018, false, out var handles, code); var parent = StoredFieldRecord(raw, handles["parent"]); var tags = parent.Tags.ToList(); int start = tags.FindIndex(t => t.Code == 97); tags[start] = new DxfTag(97, 0); tags.RemoveAll(t => t.Code == 331); raw = raw.WithRecord(parent, tags);
        var doc = StoredFieldLoad(raw, false); var field = (DxfStoredField)doc.GetObjectByHandle(handles["parent"]); var external = (DxfXRecord)doc.GetObjectByHandle(handles["external"]);
        if (code is 320 or 329) { Check(!field.References.Contains(external), "arbitrary FIELD handle became a dependency"); doc.Objects.EraseOwnedTree(external); }
        else { Check(field.References.Contains(external), "semantic FIELD handle is missing"); Throws<InvalidOperationException>(() => doc.Objects.EraseOwnedTree(external)); }
    }
}
