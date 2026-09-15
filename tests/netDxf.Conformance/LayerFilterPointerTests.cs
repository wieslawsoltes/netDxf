using System.Text.Json;
using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;
using netDxf.Tables;

namespace NetDxf.Conformance;
internal static partial class Program
{
    private static readonly string[] StoredLayerNames = { "Alpha", "CaseLayer", "Alpha", "caselayer", "東京", @"Literal\U+0041", "Unresolved" };
    private static void RunLayerFilterPointerTests()
    {
        foreach (DxfVersion version in SupportedVersions) foreach (bool binary in new[] { false, true })
        {
            Run($"layer-filter-pointer/authored/{version}/{binary}", () => LayerFilterPointerAuthored(version, binary));
            Run($"layer-filter-pointer/independent/{version}/{binary}", () => LayerFilterPointerIndependent(version, binary));
            Run($"layer-filter-pointer/opaque/{version}/{binary}", () => LayerFilterPointerOpaque(version, binary));
        }
        foreach (string type in new[] { "LAYER_FILTER", "OBJECT_PTR" })
        {
            foreach (bool reactor in new[] { false, true }) foreach (bool privateHeader in new[] { false, true })
                Run($"layer-filter-pointer/duplicate/{type}/{reactor}/{privateHeader}", () => LayerFilterPointerDuplicateMetadata(type, reactor, privateHeader));
            foreach (int mode in Enumerable.Range(0, 4))
                Run($"layer-filter-pointer/classes/{type}/{mode}", () => LayerFilterPointerClasses(type, mode));
        }
        Run("layer-filter-pointer/clone-names-and-metadata", LayerFilterPointerClone);
        Run("layer-filter-pointer/validation-and-classes", LayerFilterPointerValidation);
        foreach (int fault in Enumerable.Range(0, 8))
        { int scenario = fault; Run($"layer-filter-pointer/malformed/{fault}", () => LayerFilterPointerMalformed(scenario)); }
    }
    private static DxfDocument LayerFilterPointerDocument(DxfVersion version)
    {
        var doc = new DxfDocument(version);
        foreach (string name in new[] { "Alpha", "CaseLayer", "東京" }) doc.Layers.Add(new Layer(name));
        var line = new Line(new Vector3(1.25, -2.5, 3.75), new Vector3(4.5, 5.25, -6.125)); doc.Entities.Add(line);
        var app = new DxfDictionary(); var names = new DxfLayerFilter(StoredLayerNames); var pointer = new DxfObjectPointer();
        app.Add("NAMES", names); app.Add("EMPTY_FILTER", new DxfLayerFilter()); app.Add("POINTER", pointer); app.Add("EMPTY_POINTER", new DxfObjectPointer()); app.Add("NAMES_ALIAS", names, false);
        doc.NamedObjects.Add("QA_FILTER_POINTER", app);
        foreach (var item in app.Entries.Select(entry => entry.Target).Distinct()) item.PersistentReactors.Add(app);
        pointer.PersistentReactors.Add(line);
        var filterData = new XData(new ApplicationRegistry("QA_FILTER_POINTER")); filterData.XDataRecord.Add(new XDataRecord(XDataCode.String, "filter metadata")); filterData.XDataRecord.Add(new XDataRecord(XDataCode.DatabaseHandle, pointer.Handle)); names.XData.Add(filterData);
        var pointerData = new XData(new ApplicationRegistry("DC015")); pointerData.XDataRecord.Add(new XDataRecord(XDataCode.String, "uninterpreted application data")); pointerData.XDataRecord.Add(new XDataRecord(XDataCode.DatabaseHandle, names.Handle)); pointer.XData.Add(pointerData);
        var extension = new DxfDictionary(); var record = new DxfXRecord(); record.Data.Add(new DxfTag(1, "owned extension")); record.Data.Add(new DxfTag(330, line.Handle)); extension.Add("PAYLOAD", record);
        doc.Objects.SetExtensionDictionary(pointer, extension);
        return doc;
    }
    private static void CheckLayerFilterPointer(DxfDocument doc)
    {
        var app = (DxfDictionary)doc.NamedObjects["QA_FILTER_POINTER"];
        var filter = (DxfLayerFilter)app["NAMES"]; var pointer = (DxfObjectPointer)app["POINTER"];
        Check(filter.LayerNames.SequenceEqual(StoredLayerNames), "ordered stored names changed");
        Check(ReferenceEquals(filter, app["NAMES_ALIAS"]), "filter alias identity changed");
        Equal(0, ((DxfLayerFilter)app["EMPTY_FILTER"]).LayerNames.Count, "empty filter acquired names");
        Equal(0, app["EMPTY_POINTER"].XData.Count, "empty pointer acquired application data");
        Equal(pointer.Handle, (string)filter.XData["QA_FILTER_POINTER"].XDataRecord[1].Value, "filter XData target changed");
        Equal(filter.Handle, (string)pointer.XData["DC015"].XDataRecord[1].Value, "pointer XData target changed");
        Check(pointer.PersistentReactors.Contains(doc.Entities.Lines.Single()), "external pointer reactor changed");
        Equal(doc.Entities.Lines.Single().Handle, (string)((DxfXRecord)pointer.ExtensionDictionary["PAYLOAD"]).Data[1].Value, "extension XRECORD target changed");
        Equal(0, doc.Objects.Validate().Count, "stored envelope graph validation");
    }
    private static void LayerFilterPointerAuthored(DxfVersion version, bool binary)
    {
        var doc = LayerFilterPointerDocument(version);
        using var stream = new MemoryStream(); Check(doc.Save(stream, binary), "envelope save");
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"layer-filter-pointer-{version}-{binary}.dxf"), stream.ToArray());
        stream.Position = 0; var raw = DxfRawDocument.Load(stream);
        foreach (var record in raw.Sections.SelectMany(section => section.Records).Where(record => record.Name == "OBJECT_PTR"))
            Check(!record.Tags.Any(tag => tag.Code == 100), "OBJECT_PTR acquired an invented subclass");
        stream.Position = 0; var loaded = DxfDocument.Load(stream)!; CheckLayerFilterPointer(loaded);
        Equal("CAseDLPNTableRecord", loaded.Classes["OBJECT_PTR"].CppClassName, "documented pointer C++ identity");
        Equal(1, loaded.Classes["OBJECT_PTR"].ProxyFlags, "documented pointer flags");
        Equal(string.Empty, loaded.Classes["OBJECT_PTR"].ApplicationName, "unspecified application name");
    }
    private static string LayerFilterPointerFixture(DxfVersion version) => Path.Combine("tests", "fixtures", "layer-filter-pointer", $"independent-layer-filter-pointer-R{version.ToString().Substring(7)}.dxf");
    private static void LayerFilterPointerIndependent(DxfVersion version, bool binary)
    {
        string path = LayerFilterPointerFixture(version);
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine("tests", "fixtures", "layer-filter-pointer", "manifest.json")));
        var expected = manifest.RootElement.GetProperty("fixtures").EnumerateArray().Single(value => value.GetProperty("file").GetString() == Path.GetFileName(path));
        var bytes = File.ReadAllBytes(path); Equal(expected.GetProperty("sha256").GetString(), Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant(), "low-level fixture hash");
        using var input = new MemoryStream(bytes); var doc = DxfDocument.Load(input)!; CheckLayerFilterPointer(doc);
        using var stream = new MemoryStream(); Check(doc.Save(stream, binary), "independent envelope save");
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"independent-layer-filter-pointer-{version}-{binary}.dxf"), stream.ToArray());
        stream.Position = 0; CheckLayerFilterPointer(DxfDocument.Load(stream)!);
    }
    private static void LayerFilterPointerOpaque(DxfVersion version, bool binary)
    {
        foreach (int variant in Enumerable.Range(0, 4))
        {
            var doc = LayerFilterPointerDocument(version); var app = (DxfDictionary)doc.NamedObjects["QA_FILTER_POINTER"];
            string pointerHandle = app["POINTER"].Handle, filterHandle = app["NAMES"].Handle;
            using var source = new MemoryStream(); Check(doc.Save(source, binary), "opaque seed"); source.Position = 0; var raw = DxfRawDocument.Load(source);
            raw = ObjectStoreReplaceRecord(raw, pointerHandle, tags =>
            {
                int identity = tags.FindIndex(tag => tag.Code == 5);
                if (variant == 0) tags.Insert(identity + 1, new DxfTag(1, "unknown before owner"));
                else if (variant == 1) tags.InsertRange(identity + 1, new[] { new DxfTag(102, "{PRIVATE_HEADER"), new DxfTag(70, (short)17), new DxfTag(102, "}") });
                else if (variant == 2) { int xdata = tags.FindIndex(tag => tag.Code == 1001); tags.Insert(xdata, new DxfTag(100, "AcDbPrivatePointer")); }
                else { int xdata = tags.FindIndex(tag => tag.Code == 1001); tags.Insert(xdata, new DxfTag(330, doc.Entities.Lines.Single().Handle)); }
                return tags;
            });
            raw = ObjectStoreReplaceRecord(raw, filterHandle, tags =>
            {
                int start = tags.FindIndex(tag => tag.Code == 100 && (string)tag.Value == "AcDbLayerFilter");
                tags.RemoveAll(tag => tag.Code == 8); tags.Insert(start + 1, new DxfTag(330, doc.Layers["Alpha"].Handle)); return tags;
            });
            using var input = new MemoryStream(); raw.Save(input, binary); input.Position = 0; var loaded = DxfDocument.Load(input)!;
            var pointer = (DxfOpaqueObject)loaded.GetObjectByHandle(pointerHandle); var filter = (DxfOpaqueObject)loaded.GetObjectByHandle(filterHandle);
            Check(pointer.Tags.Count > 0 && filter.Tags.Any(tag => tag.Code == 330), "unknown envelope payload was discarded");
            Check(pointer.Owner != null && pointer.ExtensionDictionary != null, "opaque common metadata lost");
            Throws<NotSupportedException>(() => loaded.Objects.CloneObject(pointer, loaded.NamedObjects, "UNSAFE"));
            using var output = new MemoryStream(); Check(loaded.Save(output, binary), "opaque save"); output.Position = 0; var reloaded = DxfDocument.Load(output)!;
            foreach (var original in new[] { pointer, filter })
            {
                var retained = (DxfOpaqueObject)reloaded.GetObjectByHandle(original.Handle);
                Equal(original.Tags.Count, retained.Tags.Count, "opaque tag count");
                for (int i = 0; i < original.Tags.Count; i++) { Equal(original.Tags[i].Code, retained.Tags[i].Code, "opaque code order"); Equal(original.Tags[i].Value, retained.Tags[i].Value, "opaque tag value"); }
            }
        }
    }
    private static void LayerFilterPointerClone()
    {
        var source = LayerFilterPointerDocument(DxfVersion.AutoCad2018); var target = new DxfDocument(DxfVersion.AutoCad2018);
        var destinationLine = new Line(Vector3.Zero, Vector3.UnitY); target.Entities.Add(destinationLine); var destinationLayer = target.Layers.Add(new Layer("DifferentName"));
        var graph = (DxfDictionary)source.NamedObjects["QA_FILTER_POINTER"];
        source.Layers["Alpha"].Name = "RenamedAlpha";
        int count = target.Objects.Items.Count; string seed = target.DrawingVariables.HandleSeed;
        Throws<InvalidOperationException>(() => target.Objects.Clone(graph, target.NamedObjects, "MISSING"));
        Equal(count, target.Objects.Items.Count, "failed clone registered objects"); Equal(seed, target.DrawingVariables.HandleSeed, "failed clone allocated handles");
        var clone = target.Objects.Clone(graph, target.NamedObjects, "COPY", new Dictionary<DxfObject,DxfObject> { { source.Entities.Lines.Single(), destinationLine }, { source.Layers["RenamedAlpha"], destinationLayer } });
        var filter = (DxfLayerFilter)clone["NAMES"]; var pointer = (DxfObjectPointer)clone["POINTER"];
        Check(filter.LayerNames.SequenceEqual(StoredLayerNames), "layer rename/map changed stored names");
        Equal(filter.Handle, (string)pointer.XData["DC015"].XDataRecord[1].Value, "cloned internal XData target");
        Equal(destinationLine.Handle, (string)((DxfXRecord)pointer.ExtensionDictionary["PAYLOAD"]).Data[1].Value, "cloned external target");
        filter.LayerNames[0] = "Edited"; Equal("Alpha", ((DxfLayerFilter)graph["NAMES"]).LayerNames[0], "clone shares name collection");
        foreach (bool binary in new[] { false, true }) { using var output = new MemoryStream(); Check(target.Save(output, binary), "cloned envelope save"); output.Position = 0; Check(DxfDocument.Load(output) != null, "cloned envelope load"); }
    }
    private static void LayerFilterPointerValidation()
    {
        var names = new List<string> { "Original" }; var filter = new DxfLayerFilter(names); names[0] = "Changed"; Equal("Original", filter.LayerNames[0], "constructor retained mutable source");
        foreach (string bad in new[] { "", "a\nb", "a\rb", "a\0b", "\uD800", "\uDC00" })
        { Throws<ArgumentException>(() => filter.LayerNames[0] = bad); Equal("Original", filter.LayerNames[0], "failed edit changed collection"); }
        Throws<ArgumentException>(() => filter.LayerNames.Add(null!)); Throws<ArgumentNullException>(() => new DxfLayerFilter(null!));
        var doc = LayerFilterPointerDocument(DxfVersion.AutoCad2018);
        doc.Classes.Add(new DxfClass("OBJECT_PTR", "CAseDLPNTableRecord", "CallerApplication") { InstanceCount = 99, ProxyFlags = 7 });
        using var output = new MemoryStream(); Check(doc.Save(output), "caller class save"); output.Position = 0; var loaded = DxfDocument.Load(output)!;
        Equal("CAseDLPNTableRecord", loaded.Classes["OBJECT_PTR"].CppClassName, "caller C++ identity changed"); Equal(2, loaded.Classes["OBJECT_PTR"].InstanceCount, "actual pointer count"); Equal(7, loaded.Classes["OBJECT_PTR"].ProxyFlags, "caller class flags changed");
        doc.Classes["OBJECT_PTR"].IsEntity = true; using var rejected = new MemoryStream(); CheckSaveRejected(doc, rejected); Equal(0L, rejected.Length, "conflicting object CLASS wrote bytes");
    }
    private static void LayerFilterPointerDuplicateMetadata(string type, bool reactor, bool privateHeader)
    {
        var doc = LayerFilterPointerDocument(DxfVersion.AutoCad2018);
        string handle = ((DxfDictionary)doc.NamedObjects["QA_FILTER_POINTER"])[type == "LAYER_FILTER" ? "NAMES" : "POINTER"].Handle;
        using var seed = new MemoryStream(); Check(doc.Save(seed, true), "duplicate seed"); seed.Position = 0;
        var raw = ObjectStoreReplaceRecord(DxfRawDocument.Load(seed), handle, tags =>
        {
            if (reactor)
                tags.InsertRange(tags.FindIndex(tag => tag.Code == 5) + 1, new[] { new DxfTag(102, "{ACAD_REACTORS"), new DxfTag(102, "}") });
            else
            {
                int depth = 0;
                int owner = tags.FindIndex(tag => { if (tag.Code == 102) depth += (string)tag.Value == "}" ? -1 : 1; return depth == 0 && tag.Code == 330; });
                tags.Insert(owner + 1, tags[owner]);
            }
            if (privateHeader) tags.Insert(tags.FindIndex(tag => tag.Code == 5) + 1, new DxfTag(1, "private data cannot hide duplicate metadata"));
            return tags;
        });
        foreach (bool binary in new[] { false, true })
        {
            using var input = new MemoryStream(); raw.Save(input, binary); input.Position = 0;
            bool rejected; try { rejected = DxfDocument.Load(input) == null; } catch (FormatException) { rejected = true; }
            Check(rejected, "duplicate common metadata accepted");
        }
    }
    private static void LayerFilterPointerClasses(string type, int mode)
    {
        foreach (bool binary in new[] { false, true })
        {
            var doc = mode == 0 ? new DxfDocument(DxfVersion.AutoCad2018) : LayerFilterPointerDocument(DxfVersion.AutoCad2018);
            if (mode != 0)
            {
                using var seed = new MemoryStream(); Check(doc.Save(seed, binary), "class seed"); seed.Position = 0; var raw = DxfRawDocument.Load(seed);
                var handles = doc.Objects.Items.Where(item => item.CodeName == type).Select(item => item.Handle).ToArray();
                foreach (string handle in mode == 1 ? handles : handles.Take(1))
                    raw = ObjectStoreReplaceRecord(raw, handle, tags => { tags.Insert(tags.FindIndex(tag => tag.Code == 5) + 1, new DxfTag(1, "private header")); return tags; });
                using var input = new MemoryStream(); raw.Save(input, binary); input.Position = 0; doc = DxfDocument.Load(input)!;
            }
            doc.Classes.Remove(type);
            string cpp = mode == 2 ? (type == "OBJECT_PTR" ? "CAseDLPNTableRecord" : "AcDbLayerFilter") : "PrivateStoredClass";
            doc.Classes.Add(new DxfClass(type, cpp, "CallerApplication") { ProxyFlags = 7, InstanceCount = 99, WasProxy = true });
            using var output = new MemoryStream();
            if (mode == 3) { CheckSaveRejected(doc, output); Equal(0L, output.Length, "class conflict wrote bytes"); continue; }
            Check(doc.Save(output, binary), "class metadata preservation save"); output.Position = 0; var loaded = DxfDocument.Load(output)!;
            var definition = loaded.Classes[type]; Equal(cpp, definition.CppClassName, "class C++ identity changed"); Equal("CallerApplication", definition.ApplicationName, "class application changed");
            Equal(7, definition.ProxyFlags, "class flags changed"); Check(definition.WasProxy, "class proxy status changed");
            Equal(mode == 2 ? 2 : 99, definition.InstanceCount, "class physical instance count or private count changed");
        }
    }
    private static void LayerFilterPointerMalformed(int fault)
    {
        var doc = LayerFilterPointerDocument(DxfVersion.AutoCad2018); var app = (DxfDictionary)doc.NamedObjects["QA_FILTER_POINTER"];
        using var source = new MemoryStream(); Check(doc.Save(source, true), "malformed seed"); source.Position = 0; var raw = DxfRawDocument.Load(source);
        string handle = fault < 4 ? app["NAMES"].Handle : app["POINTER"].Handle;
        raw = ObjectStoreReplaceRecord(raw, handle, tags =>
        {
            if (fault < 4) tags[tags.FindIndex(tag => tag.Code == 8)] = new DxfTag(8, new[] { "", "bad\\U+000Aname", "bad\\U+0000name", "bad\\U+D800" }[fault]);
            else if (fault == 4) tags.Insert(tags.FindIndex(tag => tag.Code == 5) + 1, new DxfTag(5, "F00"));
            else if (fault == 5) tags.Insert(tags.FindIndex(tag => tag.Code == 102) + 1, new DxfTag(1, "invalid reactor group"));
            else if (fault == 6) tags.Insert(tags.FindIndex(tag => tag.Code == 1001), new DxfTag(102, "{UNTERMINATED"));
            else tags.Insert(tags.FindIndex(tag => tag.Code == 1001), new DxfTag(102, "}"));
            return tags;
        });
        foreach (bool binary in new[] { false, true })
        { using var malformed = new MemoryStream(); raw.Save(malformed, binary); malformed.Position = 0; bool rejected; try { rejected = DxfDocument.Load(malformed) == null; } catch (FormatException) { rejected = true; } Check(rejected, "malformed public envelope accepted"); }
    }
}
