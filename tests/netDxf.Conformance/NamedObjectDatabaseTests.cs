using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;
using netDxf.Tables;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RunNamedObjectDatabaseTests()
    {
        foreach (DxfVersion version in SupportedVersions)
            foreach (bool binary in new[] { false, true })
                Run($"named-objects/roundtrip/{version}/{binary}", () => NamedObjectRoundTrip(version, binary));
        Run("named-objects/defensive-data", NamedObjectDefensiveData);
        Run("named-objects/graph-clone", NamedObjectClone);
        Run("named-objects/cross-document-clone", NamedObjectCrossDocumentClone);
        Run("named-objects/invalid-graphs", NamedObjectInvalidGraphs);
        Run("named-objects/soft-reference-cycle", NamedObjectSoftCycle);
        Run("named-objects/placeholder-xdata", NamedObjectPlaceholderXData);
        Run("named-objects/exposed-pointer-reservation", NamedObjectPointerReservation);
        Run("named-objects/extended-source-payload", NamedObjectExtendedSourcePayload);
        Run("named-objects/combined-reactors", NamedObjectCombinedReactors);
        Run("named-objects/foreign-xdata-registry", NamedObjectForeignXDataRegistry);
        Run("named-objects/managed-layer-states", NamedObjectManagedLayerStates);
        Run("named-objects/literal-escapes", NamedObjectLiteralEscapes);
        Run("named-objects/text-framing-preflight", NamedObjectTextFramingPreflight);
        Run("named-objects/mapping-snapshot", NamedObjectMappingSnapshot);
    }
    private static DxfDictionary BuildNamedObjectGraph(DxfDocument doc)
    {
        var graph = new DxfDictionary { IsHardOwner = true, Cloning = DictionaryCloningFlags.UseClone };
        var record = new DxfXRecord { Cloning = DictionaryCloningFlags.Name };
        record.Data.Add(new DxfTag(1, "Zażółć 測試"));
        record.Data.Add(new DxfTag(100, "PayloadSubclass"));
        record.Data.Add(new DxfTag(102, "{PayloadGroup"));
        record.Data.Add(new DxfTag(280, (short)7));
        record.Data.Add(new DxfTag(102, "}"));
        record.Data.Add(new DxfTag(310, new byte[] { 0, 1, 255 }));
        record.Data.Add(new DxfTag(160, long.MaxValue - 1));
        record.Data.Add(new DxfTag(40, 1.25));
        record.Data.Add(new DxfTag(290, true));
        record.Data.Add(new DxfTag(320, "FFABC"));
        graph.Add("PAYLOAD", record);
        graph.Add("ALIAS", record, false);
        graph.Add("VAR", new DxfDictionaryVariable { Schema = 0, Value = "Mode ✓" });
        var fallback = new DxfDictionaryWithDefault();
        fallback.Add("Fallback", new DxfXRecord());
        fallback.Default = fallback["Fallback"];
        graph.Add("DEFAULT", fallback);
        doc.NamedObjects.Add("APP_DATA", graph);
        record.Data.Add(new DxfTag(330, graph["VAR"].Handle));
        record.PersistentReactors.Add(graph["VAR"]);
        var ext = new DxfDictionary(); ext.Add("Nested", new DxfDictionaryVariable { Value = "extension" });
        doc.Objects.SetExtensionDictionary(record, ext);
        var line = new Line(Vector3.Zero, Vector3.UnitX); doc.Entities.Add(line);
        var lineExt = new DxfDictionary(); lineExt.Add("OnLine", new DxfXRecord()); doc.Objects.SetExtensionDictionary(line, lineExt);
        var layer = doc.Layers.Add(new Layer("ObjectMetadata"));
        var layerExt = new DxfDictionary(); layerExt.Add("OnLayer", new DxfDictionaryVariable { Value = "layer extension" }); doc.Objects.SetExtensionDictionary(layer, layerExt);
        return graph;
    }
    private static void NamedObjectRoundTrip(DxfVersion version, bool binary)
    {
        var doc = new DxfDocument(version); DxfDictionary graph = BuildNamedObjectGraph(doc);
        Equal(0, doc.Objects.Validate().Count, "valid object graph");
        string rootHandle = doc.NamedObjects.Handle;
        using var stream = new MemoryStream(); Check(doc.Save(stream, binary), "object save");
        stream.Position = 0; var loaded = DxfDocument.Load(stream) ?? throw new InvalidOperationException("Object load failed.");
        var result = (DxfDictionary)loaded.NamedObjects["APP_DATA"];
        Equal(rootHandle, loaded.NamedObjects.Handle, "stable root handle");
        Equal(graph.Handle, result.Handle, "stable graph handle");
        Equal(4, result.Count, "names including aliases");
        Check(ReferenceEquals(result["PAYLOAD"], result["ALIAS"]), "Alias identity was lost.");
        Check(result.Entries.Single(e => e.Name == "PAYLOAD").IsHardOwner, "Hard-owner code lost.");
        Check(!result.Entries.Single(e => e.Name == "ALIAS").IsHardOwner, "Soft-owner code lost.");
        var record = (DxfXRecord)result["PAYLOAD"];
        Check(ReferenceEquals(record, loaded.GetObjectByHandle(record.Handle)), "Database registration missing.");
        Equal("Zażółć 測試", (string)record.Data[0].Value, "Unicode payload");
        Equal((short)100, record.Data[1].Code, "payload subclass code");
        Equal((short)7, (short)record.Data[3].Value, "payload duplicate code280");
        Check(((byte[])record.Data[5].Value).SequenceEqual(new byte[] { 0, 1, 255 }), "Binary data differs.");
        Equal(long.MaxValue - 1, (long)record.Data[6].Value, "64-bit data");
        Equal(result["VAR"].Handle, (string)record.Data.Last().Value, "resolved reference handle");
        Check(ReferenceEquals(result["VAR"], record.PersistentReactors.Single()), "Persistent reactor identity differs.");
        Check(record.ExtensionDictionary?["Nested"] is DxfDictionaryVariable, "Object extension lost.");
        Check(loaded.Entities.Lines.Single().ExtensionDictionary?["OnLine"] is DxfXRecord, "Entity extension lost.");
        Check(loaded.Layers["ObjectMetadata"].ExtensionDictionary?["OnLayer"] is DxfDictionaryVariable, "Layer extension lost.");
        var fallback = (DxfDictionaryWithDefault)result["DEFAULT"];
        Check(ReferenceEquals(fallback["missing"], fallback["Fallback"]), "Default lookup differs.");
        Equal(0, loaded.Objects.Validate().Count, "loaded graph validity");
        using var second = new MemoryStream(); Check(loaded.Save(second, !binary), "cross-transport save");
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"objects-{version}-{binary}.dxf"), stream.ToArray());
    }
    private static void NamedObjectDefensiveData()
    {
        byte[] bytes = { 1, 2, 3 }; var record = new DxfXRecord(); record.Data.Add(new DxfTag(310, bytes));
        bytes[0] = 90; Equal((byte)1, ((byte[])record.Data[0].Value)[0], "input isolation");
        ((byte[])record.Data[0].Value)[0] = 91; Equal((byte)1, ((byte[])record.Data[0].Value)[0], "output isolation");
        Throws<ArgumentException>(() => record.Data.Add(new DxfTag(0, "LINE")));
        Throws<ArgumentException>(() => record.Data.Add(new DxfTag(5, "FF")));
        Throws<ArgumentException>(() => record.Data.Add(new DxfTag(1001, "APP")));
        Throws<ArgumentNullException>(() => record.Data.Add(null!));
        Throws<ArgumentException>(() => record.Data.Add(new DxfTag(370, (short)25)));
        Throws<ArgumentException>(() => record.Data[0] = new DxfTag(440, 42));
        Throws<ArgumentException>(() => record.Data.Add(new DxfTag(310, new byte[128])));
    }
    private static void NamedObjectClone()
    {
        var doc = new DxfDocument(); var source = BuildNamedObjectGraph(doc);
        var xdata = new XData(new ApplicationRegistry("OBJECT_CLONE"));
        xdata.XDataRecord.Add(new XDataRecord(XDataCode.DatabaseHandle, source["VAR"].Handle)); source.XData.Add(xdata);
        var clone = doc.Objects.Clone(source, doc.NamedObjects, "COPY");
        Check(clone.Handle != source.Handle, "Clone identity shared.");
        Check(ReferenceEquals(clone["PAYLOAD"], clone["ALIAS"]), "Cloned aliases differ.");
        var record = (DxfXRecord)clone["PAYLOAD"];
        Equal(clone["VAR"].Handle, (string)record.Data.Last().Value, "pointer remap");
        Equal("FFABC", (string)record.Data[9].Value, "arbitrary handle preserved");
        Equal(clone["VAR"].Handle, (string)clone.XData["OBJECT_CLONE"].XDataRecord[0].Value, "XData remap");
        Check(ReferenceEquals(clone["VAR"], record.PersistentReactors.Single()), "Reactor remap failed.");
        Check(!ReferenceEquals(record.ExtensionDictionary, source["PAYLOAD"].ExtensionDictionary), "Extension dictionary shared.");
        Check(ReferenceEquals(record.ExtensionDictionary?.Owner, record), "Extension owner not remapped.");
        ((DxfDictionaryVariable)clone["VAR"]).Value = "changed";
        Equal("Mode ✓", ((DxfDictionaryVariable)source["VAR"]).Value, "clone independent mutation");
        Equal(0, doc.Objects.Validate().Count, "clone validity");
    }
    private static void NamedObjectCrossDocumentClone()
    {
        var source = new DxfDocument(); var graph = BuildNamedObjectGraph(source);
        var sourceLine = source.Entities.Lines.Single(); ((DxfXRecord)graph["PAYLOAD"]).Data.Add(new DxfTag(340, sourceLine.Handle));
        var target = new DxfDocument(); int count = target.Objects.Items.Count;
        Throws<InvalidOperationException>(() => target.Objects.Clone(graph, target.NamedObjects, "COPY"));
        Equal(count, target.Objects.Items.Count, "failed clone changed registration");
        var targetLine = new Line(Vector3.Zero, Vector3.UnitY); target.Entities.Add(targetLine);
        var mappings = new Dictionary<DxfObject, DxfObject> { [sourceLine] = targetLine };
        var clone = target.Objects.Clone(graph, target.NamedObjects, "COPY", mappings);
        Equal(targetLine.Handle, (string)((DxfXRecord)clone["PAYLOAD"]).Data.Last().Value, "external pointer mapping");
        Equal(0, target.Objects.Validate().Count, "cross-document validity");
    }
    private static void NamedObjectInvalidGraphs()
    {
        var doc = new DxfDocument(); var dictionary = new DxfDictionary(); doc.NamedObjects.Add("A", dictionary);
        var record = new DxfXRecord(); dictionary.Add("B", record);
        Throws<ArgumentException>(() => dictionary.Add("b", new DxfXRecord()));
        Throws<ArgumentException>(() => dictionary.Add("Loop", doc.NamedObjects));
        Throws<ArgumentException>(() => doc.NamedObjects.Add("ACAD_GROUP", new DxfDictionary()));
        Throws<ArgumentException>(() => doc.NamedObjects.Add("SecondOwner", record));
        var other = new DxfDocument(); Throws<ArgumentException>(() => other.NamedObjects.Add("Cross", dictionary));
        record.Data.Add(new DxfTag(340, "DEADBEEF"));
        Check(doc.Objects.Validate().Count > 0, "Dangling pointer accepted.");
        using var stream = new MemoryStream(); bool rejected;
        try { rejected = !doc.Save(stream); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "Invalid graph was saved."); Equal(0L, stream.Length, "save wrote before graph validation");
    }
    private static void NamedObjectSoftCycle()
    {
        var doc = new DxfDocument(); var graph = new DxfDictionary { IsHardOwner = false }; doc.NamedObjects.Add("GRAPH", graph);
        var first = new DxfXRecord(); var second = new DxfXRecord();
        graph.Add("FIRST", first, false); graph.Add("SECOND", second, false);
        first.Data.Add(new DxfTag(330, second.Handle)); second.Data.Add(new DxfTag(330, first.Handle));
        Equal(0, doc.Objects.Validate().Count, "soft pointer cycle");
        var clone = doc.Objects.Clone(graph, doc.NamedObjects, "COPY");
        Equal(clone["SECOND"].Handle, (string)((DxfXRecord)clone["FIRST"]).Data[0].Value, "first cyclic pointer remap");
        Equal(clone["FIRST"].Handle, (string)((DxfXRecord)clone["SECOND"]).Data[0].Value, "second cyclic pointer remap");
        Throws<ArgumentException>(() => graph.Add("SELF", graph, false));
        var other = new DxfDictionary { IsHardOwner = false }; doc.NamedObjects.Add("OTHER", other);
        Throws<ArgumentException>(() => other.Add("FOREIGN_ALIAS", first, false));
    }
    private static void NamedObjectPlaceholderXData()
    {
        var doc = new DxfDocument(); var placeholder = new DxfPlaceholder();
        var data = new XData(new ApplicationRegistry("PLACEHOLDER_META")); data.XDataRecord.Add(new XDataRecord(XDataCode.String, "metadata"));
        placeholder.XData.Add(data); doc.NamedObjects.Add("PLACEHOLDER", placeholder);
        foreach (bool binary in new[] { false, true })
        {
            using var stream = new MemoryStream(); Check(doc.Save(stream, binary), "placeholder save"); stream.Position = 0;
            var loaded = DxfDocument.Load(stream) ?? throw new InvalidOperationException("Placeholder load failed.");
            var result = (DxfPlaceholder)loaded.NamedObjects["PLACEHOLDER"];
            Equal("metadata", (string)result.XData["PLACEHOLDER_META"].XDataRecord[0].Value, "placeholder XData");
        }
        Throws<InvalidOperationException>(() => doc.Objects.SetExtensionDictionary(doc.Layers, new DxfDictionary()));
    }
    private static void NamedObjectPointerReservation()
    {
        var doc = new DxfDocument(); var record = new DxfXRecord();
        long seed = Convert.ToInt64(doc.DrawingVariables.HandleSeed, 16);
        record.Data.Add(new DxfTag(330, (seed + 1).ToString("X")));
        doc.NamedObjects.Add("RECORD", record);
        Check(doc.GetObjectByHandle((seed + 1).ToString("X")) == null, "Adding a graph captured its unresolved pointer.");
        record.Data.Add(new DxfTag(340, "FFFF"));
        var line = new Line(Vector3.Zero, Vector3.UnitX); doc.Entities.Add(line);
        Check(line.Handle != "FFFF", "A later entity captured an exposed pointer.");
        Check(doc.Objects.Validate().Count >= 2, "Unresolved pointer diagnostics disappeared.");
    }
    private static void NamedObjectExtendedSourcePayload()
    {
        var doc = new DxfDocument(); var graph = new DxfDictionary(); graph.Add("DATA", new DxfXRecord()); doc.NamedObjects.Add("EXTENDED", graph);
        using var source = new MemoryStream(); Check(doc.Save(source), "extended fixture save");
        var text = System.Text.Encoding.UTF8.GetString(source.ToArray());
        string marker = "AcDbXrecord\n";
        int start = text.IndexOf(marker, StringComparison.Ordinal);
        Check(start >= 0, "Fixture XRECORD not found.");
        // Insert after the leading cloning flag, before the next object.
        int codeEnd = text.IndexOf('\n', start + marker.Length);
        int valueEnd = text.IndexOf('\n', codeEnd + 1);
        text = text.Insert(valueEnd + 1, "370\n25\n440\n42\n");
        using var input = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text));
        var loaded = DxfDocument.Load(input) ?? throw new InvalidOperationException("Extended source load failed.");
        var record = (DxfXRecord)((DxfDictionary)loaded.NamedObjects["EXTENDED"])["DATA"];
        Equal((short)370, record.Data[0].Code, "retained source370"); Equal(42, (int)record.Data[1].Value, "retained source440");
        Throws<ArgumentException>(() => record.Data[0] = new DxfTag(370, (short)30));
        var clone = loaded.Objects.Clone((DxfDictionary)loaded.NamedObjects["EXTENDED"], loaded.NamedObjects, "EXTENDED_COPY");
        Equal((short)370, ((DxfXRecord)clone["DATA"]).Data[0].Code, "preserved extended clone");
    }

    private static void NamedObjectCombinedReactors()
    {
        var doc = new DxfDocument(); var line = new Line(Vector3.Zero, Vector3.UnitX); doc.Entities.Add(line);
        var group = new Group("Grouped"); group.Entities.Add(line); doc.Groups.Add(group);
        var record = new DxfXRecord(); doc.NamedObjects.Add("REACTOR", record); line.PersistentReactors.Add(record);
        foreach (bool binary in new[] { false, true })
        {
            using var stream = new MemoryStream(); Check(doc.Save(stream, binary), "combined reactors save");
            stream.Position = 0; var raw = DxfRawDocument.Load(stream);
            var tags = raw.Sections.Single(s => s.Name == "ENTITIES").Content;
            Equal(1, tags.Count(t => t.Code == 102 && (string)t.Value == "{ACAD_REACTORS"), "one merged reactor group");
            stream.Position = 0; var loaded = DxfDocument.Load(stream) ?? throw new InvalidOperationException("Combined reactor load failed.");
            Check(loaded.Entities.Lines.Single().PersistentReactors.Any(r => r is DxfXRecord), "Application reactor dropped.");
        }
    }
    private static void NamedObjectForeignXDataRegistry()
    {
        var source = new DxfDocument(); var registry = source.ApplicationRegistries.Add(new ApplicationRegistry("FOREIGN_REGISTRY"));
        string handle = registry.Handle; DxfObject owner = registry.Owner;
        var data = new XData(registry); data.XDataRecord.Add(new XDataRecord(XDataCode.String, "source"));
        var record = new DxfXRecord(); record.XData.Add(data);
        var target = new DxfDocument(); target.NamedObjects.Add("FOREIGN", record);
        Check(ReferenceEquals(owner, registry.Owner) && registry.Handle == handle, "Adoption transferred source registry.");
        Check(ReferenceEquals(source.GetObjectByHandle(handle), registry), "Source registry database identity changed.");
        Check(!ReferenceEquals(record.XData[registry.Name], data), "Foreign XData object shared.");
        var addedLater = new DxfXRecord(); target.NamedObjects.Add("LATER", addedLater); addedLater.XData.Add(data);
        Check(ReferenceEquals(owner, registry.Owner) && registry.Handle == handle, "Later XData transferred source registry.");
        using var stream = new MemoryStream(); Check(target.Save(stream), "foreign registry save");
        Check(ReferenceEquals(owner, registry.Owner) && registry.Handle == handle, "Save transferred source registry.");
    }
    private static void NamedObjectManagedLayerStates()
    {
        var doc = new DxfDocument(); doc.Layers.StateManager.AddNew("Snapshot");
        doc.NamedObjects.Add("APP", new DxfDictionaryVariable { Value = "custom" });
        using var stream = new MemoryStream(); Check(doc.Save(stream), "layer-state save"); stream.Position = 0;
        var loaded = DxfDocument.Load(stream) ?? throw new InvalidOperationException("Layer-state load failed.");
        Equal(1, loaded.Layers.StateManager.Count, "managed layer-state count");
        Equal("custom", ((DxfDictionaryVariable)loaded.NamedObjects["APP"]).Value, "coexisting custom data");
        using var output = new MemoryStream(); Check(loaded.Save(output), "layer-state resave");
    }

    private static void NamedObjectLiteralEscapes()
    {
        foreach (DxfVersion version in SupportedVersions)
        foreach (bool binary in new[] { false, true })
        {
            var doc = new DxfDocument(version); var graph = new DxfDictionary();
            var variable = new DxfDictionaryVariable { Value = @"literal \U+0041 / C:\path\file" };
            var record = new DxfXRecord(); record.Data.Add(new DxfTag(1, variable.Value));
            graph.Add(@"Name\U+0041", variable); graph.Add("DATA", record); doc.NamedObjects.Add("LITERALS", graph);
            using var stream = new MemoryStream(); Check(doc.Save(stream, binary), "literal save"); stream.Position = 0;
            var loaded = DxfDocument.Load(stream) ?? throw new InvalidOperationException("Literal load failed.");
            var result = (DxfDictionary)loaded.NamedObjects["LITERALS"];
            Equal(variable.Value, ((DxfDictionaryVariable)result[@"Name\U+0041"]).Value, "literal variable");
            Equal(variable.Value, (string)((DxfXRecord)result["DATA"]).Data[0].Value, "literal record");
        }
    }
    private static void NamedObjectTextFramingPreflight()
    {
        var doc = new DxfDocument(); var record = new DxfXRecord(); record.Data.Add(new DxfTag(1, "line1\nline2")); doc.NamedObjects.Add("TEXT", record);
        int layouts = doc.Layouts.Count, registries = doc.ApplicationRegistries.Count; string seed = doc.DrawingVariables.HandleSeed;
        using var stream = new MemoryStream(); bool rejected;
        try { rejected = !doc.Save(stream); } catch (InvalidDataException) { rejected = true; }
        Check(rejected && stream.Length == 0, "Text framing was not rejected before output.");
        Equal(layouts, doc.Layouts.Count, "preflight layout stability"); Equal(registries, doc.ApplicationRegistries.Count, "preflight registry stability"); Equal(seed, doc.DrawingVariables.HandleSeed, "preflight seed stability");
        using var binary = new MemoryStream(); Check(doc.Save(binary, true), "binary linebreak save"); binary.Position = 0;
        var loaded = DxfDocument.Load(binary) ?? throw new InvalidOperationException("Binary linebreak load failed.");
        Equal("line1\nline2", (string)((DxfXRecord)loaded.NamedObjects["TEXT"]).Data[0].Value, "binary linebreak value");
        var soft = new DxfDictionary { IsHardOwner = false }; doc.NamedObjects.Add("SOFT", soft);
        var line = new Line(Vector3.Zero, Vector3.UnitX); doc.Entities.Add(line);
        Throws<ArgumentException>(() => soft.Add("GRAPHIC", line, false));
    }
    private static void NamedObjectMappingSnapshot()
    {
        var source = new DxfDocument(); var graph = BuildNamedObjectGraph(source); var line = source.Entities.Lines.Single();
        ((DxfXRecord)graph["PAYLOAD"]).Data.Add(new DxfTag(340, line.Handle));
        var target = new DxfDocument(); var replacement = new Line(Vector3.Zero, Vector3.UnitY); target.Entities.Add(replacement);
        var mapping = new ObjectMappingWithThrowingLookup(line, replacement);
        var clone = target.Objects.Clone(graph, target.NamedObjects, "COPY", mapping);
        Equal(0, mapping.LookupCalls, "external lookup callbacks after enumeration");
        Equal(replacement.Handle, (string)((DxfXRecord)clone["PAYLOAD"]).Data.Last().Value, "snapshotted mapping");
    }
    private sealed class ObjectMappingWithThrowingLookup : IReadOnlyDictionary<DxfObject, DxfObject>
    {
        private readonly Dictionary<DxfObject, DxfObject> values;
        internal ObjectMappingWithThrowingLookup(DxfObject key, DxfObject value) { this.values = new() { [key] = value }; }
        internal int LookupCalls { get; private set; }
        public int Count => this.values.Count;
        public IEnumerable<DxfObject> Keys => this.values.Keys;
        public IEnumerable<DxfObject> Values => this.values.Values;
        public DxfObject this[DxfObject key] => this.values[key];
        public bool ContainsKey(DxfObject key) => this.values.ContainsKey(key);
        public bool TryGetValue(DxfObject key, out DxfObject value) { this.LookupCalls++; throw new IOException("Caller lookup must not run during clone mutation."); }
        public IEnumerator<KeyValuePair<DxfObject, DxfObject>> GetEnumerator() => this.values.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => this.GetEnumerator();
    }

}
