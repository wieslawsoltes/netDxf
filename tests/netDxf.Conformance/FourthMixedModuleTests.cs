using System.Reflection;
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
    private static void RegisterFourthMixedModuleTests()
    {
        foreach (DxfVersion version in SupportedVersions)
            foreach (bool binary in new[] { false, true })
                Run($"fourth-mixed/clone-erase/{version}/{binary}", () => FourthMixedGraph(version, binary));
    }

    private static readonly string[] FourthNames = { "Missing layer", "missing layer", "Missing layer", "Ω中", @"literal \U+0041" };
    private static readonly byte[][] FourthChunks =
    {
        Array.Empty<byte>(), Enumerable.Range(0, 127).Select(i => (byte)(255 - i)).ToArray(),
        Array.Empty<byte>(), new byte[] { 0, 255, 65, 0, 128 }, Array.Empty<byte>()
    };

    private static void FourthMixedGraph(DxfVersion version, bool binary)
    {
        var source = new DxfDocument(version);
        var external = new DxfDictionaryVariable { Value = "external-survivor" };
        source.Objects.Root.Add("FOURTH_EXTERNAL", external);
        var graph = new DxfDictionary { IsHardOwner = false };
        graph.Add("FILTER", new DxfLayerFilter(FourthNames));
        graph.Add("POINTER", new DxfObjectPointer());
        graph.Add("SPATIAL", new DxfSpatialIndex { Timestamp = 2451545.125 });
        var project = new DxfVbaProject(); project.SetChunks(FourthChunks); graph.Add("PROJECT", project);
        graph.Add("PROJECT_ALIAS", project, false);
        graph.Add("LINKS", new DxfXRecord());
        graph.Add("BUFFER", new DxfIdBuffer());
        Light? sourceLight = null;
        if (version >= DxfVersion.AutoCad2007)
        {
            sourceLight = new Light { Name = "Actual entity light" }; source.Entities.Add(sourceLight);
            var list = new DxfLightList(42);
            foreach (string name in new[] { "Stored light", "", @"literal \U+0042" }) list.Entries.Add(new DxfLightListEntry(sourceLight, name));
            graph.Add("LIGHTS", list);
        }
        source.Objects.Root.Add("FOURTH_GRAPH", graph);
        var extension = new DxfDictionary(); extension.Add("NOTE", new DxfDictionaryVariable { Value = "extension-note" });
        source.Objects.SetExtensionDictionary(graph["POINTER"], extension);
        graph.PersistentReactors.Add(external);
        graph["SPATIAL"].PersistentReactors.Add(graph["POINTER"]);
        project.PersistentReactors.Add(graph["FILTER"]);
        var links = (DxfXRecord)graph["LINKS"];
        links.Data.Add(new DxfTag(340, project.Handle));
        links.Data.Add(new DxfTag(330, external.Handle));
        links.Data.Add(new DxfTag(320, project.Handle)); // An arbitrary value deliberately resembles an identity.
        var buffer = (DxfIdBuffer)graph["BUFFER"];
        buffer.References.Add(project); buffer.References.Add(graph); buffer.References.Add(external); buffer.References.Add(project); buffer.References.Add(null);
        var data = new XData(new ApplicationRegistry("FOURTH_APP"));
        data.XDataRecord.Add(new XDataRecord(XDataCode.DatabaseHandle, project.Handle));
        data.XDataRecord.Add(new XDataRecord(XDataCode.DatabaseHandle, external.Handle));
        graph["POINTER"].XData.Add(data);
        var loadedSource = FourthSaveReload(source, version, binary, "source");
        graph = (DxfDictionary)loadedSource.Objects.Root["FOURTH_GRAPH"];
        external = (DxfDictionaryVariable)loadedSource.Objects.Root["FOURTH_EXTERNAL"];
        sourceLight = loadedSource.Entities.All.OfType<Light>().SingleOrDefault();
        FourthAssertGraph(loadedSource, graph, external, (string)((DxfXRecord)graph["LINKS"]).Data[2].Value);

        var destination = new DxfDocument(version);
        for (int i = 0; i < 64; i++) destination.Layers.Add(new Layer("PADDING_" + i));
        var mappedExternal = new DxfDictionaryVariable { Value = "external-survivor" };
        destination.Objects.Root.Add("FOURTH_EXTERNAL", mappedExternal);
        var mappings = new Dictionary<DxfObject, DxfObject>(ReferenceEqualityComparer.Instance) { [external] = mappedExternal };
        Light? mappedLight = null;
        if (sourceLight != null)
        {
            mappedLight = (Light)sourceLight.Clone(); destination.Entities.Add(mappedLight); mappings.Add(sourceLight, mappedLight);
            Check(sourceLight.Handle != mappedLight.Handle, "mixed light mapping retained source handle");
        }
        var copied = destination.Objects.Clone(graph, destination.Objects.Root, "FOURTH_GRAPH", mappings);
        var originalObjects = FourthPaths(graph);
        var copiedObjects = FourthPaths(copied);
        Equal(originalObjects.Count, copiedObjects.Count, "mixed clone object count");
        var pairs = originalObjects.Select(pair =>
        {
            DxfDatabaseObject copy = copiedObjects[pair.Key];
            Check(!ReferenceEquals(pair.Value, copy) && pair.Value.Handle != copy.Handle, "mixed clone retained source identity");
            return new { path = pair.Key, type = pair.Value.CodeName, source = pair.Value.Handle, destination = copy.Handle };
        }).ToArray();
        var copiedFilter = (DxfLayerFilter)copied["FILTER"];
        copiedFilter.LayerNames[0] = "clone mutation";
        Equal(FourthNames[0], ((DxfLayerFilter)graph["FILTER"]).LayerNames[0], "filter clone shares names");
        copiedFilter.LayerNames[0] = FourthNames[0];
        var copiedProject = (DxfVbaProject)copied["PROJECT"];
        byte[] snapshot = copiedProject.Chunks[1]; snapshot[0] = 0;
        Equal((byte)255, copiedProject.Chunks[1][0], "project exposed mutable storage");
        string arbitrary = ((DxfXRecord)graph["LINKS"]).Data[2].Value.ToString()!;
        FourthAssertGraph(destination, copied, mappedExternal, arbitrary);
        destination.ApplicationRegistries["FOURTH_APP"].Name = "FOURTH_APP_COPY";
        Check(graph["POINTER"].XData.AppIds.Contains("FOURTH_APP"), "destination APPID rename changed source binding");
        destination = FourthSaveReload(destination, version, binary, "copy");
        copied = (DxfDictionary)destination.Objects.Root["FOURTH_GRAPH"];
        mappedExternal = (DxfDictionaryVariable)destination.Objects.Root["FOURTH_EXTERNAL"];
        FourthAssertGraph(destination, copied, mappedExternal, arbitrary, "FOURTH_APP_COPY");

        var blocker = new DxfXRecord(); destination.Objects.Root.Add("FOURTH_BLOCKER", blocker);
        blocker.Data.Add(new DxfTag(340, copied["PROJECT"].Handle));
        var closure = FourthPaths(copied).Values.ToArray();
        long seed = FourthSeed(destination); int count = destination.Objects.Items.Count;
        Throws<InvalidOperationException>(() => destination.Objects.EraseOwnedTree(copied));
        Equal(seed, FourthSeed(destination), "rejected mixed erase changed seed");
        Equal(count, destination.Objects.Items.Count, "rejected mixed erase changed registration");
        foreach (DxfDatabaseObject item in closure) Check(ReferenceEquals(item, destination.GetObjectByHandle(item.Handle)) && !item.IsErased, "rejected mixed erase changed identity");
        blocker.Data.Clear(); destination.Objects.EraseOwnedTree(blocker);
        destination.Objects.EraseOwnedTree(copied);
        Equal(seed, FourthSeed(destination), "successful mixed erase reused or allocated handles");
        foreach (DxfDatabaseObject item in closure)
            Check(item.IsErased && item.Database == null && destination.GetObjectByHandle(item.Handle) == null, "mixed erase retained registration");
        Check(!destination.Objects.Root.Contains("FOURTH_GRAPH") && copied.Owner == null, "mixed erase retained owning alias");
        Check(ReferenceEquals(mappedExternal, destination.Objects.Root["FOURTH_EXTERNAL"]), "mixed erase removed external object");
        Throws<InvalidOperationException>(() => destination.Objects.Root.Add("RESURRECTION", copied));
        Equal(0, destination.Objects.Validate().Count, "mixed graph after erase");
        var erased = FourthSaveReload(destination, version, binary, "erased");
        foreach (string type in new[] { "LAYER_FILTER", "OBJECT_PTR", "SPATIAL_INDEX", "VBA_PROJECT", "LIGHTLIST" })
            Check(!erased.Objects.Items.Any(item => item.CodeName == type), "erased object was written: " + type);
        Equal(sourceLight == null ? 0 : 1, erased.Entities.All.OfType<Light>().Count(), "mixed erase removed a referenced light");
        if (version >= DxfVersion.AutoCad2004)
            foreach (string type in new[] { "LAYER_FILTER", "OBJECT_PTR", "SPATIAL_INDEX" })
                Equal(0, erased.Classes[type].InstanceCount, "erased class retains stale count: " + type);
        if (version >= DxfVersion.AutoCad2007) Equal(0, erased.Classes["LIGHTLIST"].InstanceCount, "erased LIGHTLIST class count");
        File.WriteAllText(Path.Combine(ArtifactDirectory, $"fourth-mixed-{version}-{binary}-map.json"),
            JsonSerializer.Serialize(new { version = version.ToString(), binary, pairs, external = new { source = external.Handle, destination = mappedExternal.Handle }, light = new { source = sourceLight?.Handle, destination = mappedLight?.Handle }, arbitrary }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static DxfDocument FourthSaveReload(DxfDocument doc, DxfVersion version, bool binary, string phase)
    {
        using var bytes = new MemoryStream(); Check(doc.Save(bytes, binary), "mixed save " + phase);
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"fourth-mixed-{version}-{binary}-{phase}.dxf"), bytes.ToArray());
        bytes.Position = 0; return DxfDocument.Load(bytes) ?? throw new Exception("Mixed reload failed: " + phase);
    }

    private static void FourthAssertGraph(DxfDocument doc, DxfDictionary graph, DxfDictionaryVariable external, string arbitrary, string appId = "FOURTH_APP")
    {
        Check(FourthNames.SequenceEqual(((DxfLayerFilter)graph["FILTER"]).LayerNames), "mixed filter values");
        Equal(BitConverter.DoubleToInt64Bits(2451545.125), BitConverter.DoubleToInt64Bits(((DxfSpatialIndex)graph["SPATIAL"]).Timestamp), "mixed spatial timestamp");
        var project = (DxfVbaProject)graph["PROJECT"];
        Equal(FourthChunks.Length, project.Chunks.Count, "mixed project chunk count");
        for (int i = 0; i < FourthChunks.Length; i++) Check(FourthChunks[i].SequenceEqual(project.Chunks[i]), "mixed project chunk bytes");
        Check(ReferenceEquals(project, graph["PROJECT_ALIAS"]), "mixed alias identity");
        var links = (DxfXRecord)graph["LINKS"];
        Equal(project.Handle, (string)links.Data[0].Value, "mixed project pointer");
        Equal(external.Handle, (string)links.Data[1].Value, "mixed external pointer");
        Equal(arbitrary, (string)links.Data[2].Value, "arbitrary handle was remapped");
        var expected = new DxfObject?[] { project, graph, external, project, null };
        var actual = ((DxfIdBuffer)graph["BUFFER"]).References;
        Equal(expected.Length, actual.Count, "mixed buffer count");
        for (int i = 0; i < expected.Length; i++) Check(ReferenceEquals(expected[i], actual[i]), "mixed buffer reference");
        var data = graph["POINTER"].XData[appId].XDataRecord;
        Equal(project.Handle, (string)data[0].Value, "mixed project XData");
        Equal(external.Handle, (string)data[1].Value, "mixed external XData");
        Check(ReferenceEquals(external, graph.PersistentReactors.Single()), "mixed external reactor");
        Check(ReferenceEquals(graph["POINTER"], graph["SPATIAL"].PersistentReactors.Single()), "mixed internal reactor");
        Equal("extension-note", ((DxfDictionaryVariable)graph["POINTER"].ExtensionDictionary["NOTE"]).Value, "mixed extension");
        if (graph.Contains("LIGHTS"))
        {
            var list = (DxfLightList)graph["LIGHTS"]; Equal(42, list.StoredVersion, "mixed explicit light-list version");
            Check(new[] { "Stored light", "", @"literal \U+0042" }.SequenceEqual(list.Entries.Select(entry => entry.Name)), "mixed stored light names");
            Light light = doc.Entities.All.OfType<Light>().Single();
            foreach (DxfLightListEntry entry in list.Entries) Check(ReferenceEquals(light, entry.Light), "mixed actual light reference");
        }
        Equal(0, doc.Objects.Validate().Count, "mixed graph validation");
    }

    private static Dictionary<string, DxfDatabaseObject> FourthPaths(DxfDictionary root)
    {
        var result = new Dictionary<string, DxfDatabaseObject>();
        var seen = new HashSet<DxfDatabaseObject>(ReferenceEqualityComparer.Instance);
        void Visit(string path, DxfDatabaseObject item)
        {
            if (!seen.Add(item)) return;
            result.Add(path, item);
            if (item is DxfDictionary dictionary)
                foreach (DxfDictionaryEntry entry in dictionary.Entries) Visit(path + "/" + entry.Name, (DxfDatabaseObject)entry.Target);
            if (item.ExtensionDictionary != null) Visit(path + "/EXTENSION", item.ExtensionDictionary);
        }
        Visit("ROOT", root); return result;
    }

    private static long FourthSeed(DxfDocument doc) => (long)typeof(DxfDocument).GetProperty("NumHandles", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(doc)!;
}
