using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;
using netDxf.Tables;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static string StoredFixture(string kind, DxfVersion version, bool binary) => Path.Combine("tests", "fixtures", "stored-envelopes",
        $"independent-stored-{kind}-R{version.ToString().Replace("AutoCad", "")}-{(binary ? "binary" : "ascii")}.dxf");
    private static void RegisterStoredEnvelopeTests()
    {
        Run("stored-envelopes/model-transactional", StoredEnvelopeModel);
        Run("stored-envelopes/model-admission-limits", StoredEnvelopeModelLimits);
        foreach (bool binary in new[] { false, true })
        {
            bool b = binary;
            Run($"stored-envelopes/version-preflight/{b}", () => StoredEnvelopeVersion(b));
            Run($"stored-envelopes/class-preflight/{b}", () => StoredEnvelopeClass(b));
            Run($"stored-envelopes/input-limits/{b}", () => StoredEnvelopeInputLimits(b));
            foreach (int scenario in Enumerable.Range(0, 3))
            {
                int c = scenario;
                Run($"stored-envelopes/private-class/{b}/{c}", () => StoredEnvelopePrivateClass(b, c));
            }
            foreach (string kind in new[] { "spatial", "vba" })
            {
                string k = kind;
                Run($"stored-envelopes/clone/{k}/{b}", () => StoredEnvelopeClone(k, b));
                foreach (int index in Enumerable.Range(0, kind == "spatial" ? 9 : 12))
                {
                    int i = index;
                    Run($"stored-envelopes/malformed/{k}/{b}/{i}", () => StoredEnvelopeMalformed(k, b, i));
                }
                foreach (int index in Enumerable.Range(0, 5))
                {
                    int i = index;
                    Run($"stored-envelopes/opaque/{k}/{b}/{i}", () => StoredEnvelopeOpaque(k, b, i));
                }
            }
            foreach (DxfVersion version in SupportedVersions)
            {
                DxfVersion v = version;
                Run($"stored-envelopes/authored/{v}/{b}", () => StoredEnvelopeAuthor(v, b));
                foreach (string kind in new[] { "spatial", "vba" })
                {
                    if (kind == "vba" && version == DxfVersion.AutoCad2000) continue;
                    string k = kind;
                    foreach (bool inputBinary in new[] { false, true })
                    {
                        bool source = inputBinary;
                        Run($"stored-envelopes/independent/{k}/{v}/{source}/{b}", () => StoredEnvelopeIndependent(k, v, source, b));
                    }
                }
            }
        }
    }
    private static IEnumerable<byte[]> StoredThrowingChunks()
    {
        yield return new byte[] { 9 };
        throw new InvalidOperationException("deliberate enumeration failure");
    }
    private static void StoredChunksEqual(DxfVbaProject first, DxfVbaProject second)
    {
        Equal(first.DataLength, second.DataLength, "VBA byte count");
        Check(first.Data.SequenceEqual(second.Data), "VBA payload bytes changed.");
        Check(first.Chunks.Select(Convert.ToHexString).SequenceEqual(second.Chunks.Select(Convert.ToHexString)), "VBA physical chunks changed.");
    }
    private static void StoredEnvelopeModel()
    {
        var index = new DxfSpatialIndex(); Equal(0.0, index.Timestamp, "Default timestamp");
        foreach (double value in new[] { double.MinValue, double.MaxValue, double.Epsilon, -0.0, -1.25, 2451544.5000000005 })
        { index.Timestamp = value; Equal(BitConverter.DoubleToInt64Bits(value), BitConverter.DoubleToInt64Bits(index.Timestamp), "Timestamp bits"); }
        double old = index.Timestamp;
        foreach (double value in new[] { double.NaN, double.NegativeInfinity, double.PositiveInfinity }) Throws<ArgumentOutOfRangeException>(() => index.Timestamp = value);
        Equal(old, index.Timestamp, "Rejected timestamp changed value");
        var project = new DxfVbaProject(); Equal(0, project.DataLength, "Default VBA length"); Equal(0, project.Chunks.Count, "Default physical chunks");
        var input = Enumerable.Range(0, 300).Select(i => (byte)i).ToArray(); project.Data = input; input[0] = 9;
        Equal((byte)0, project.Data[0], "Data setter aliases input"); Check(project.Chunks.Select(c => c.Length).SequenceEqual(new[] { 127, 127, 46 }), "Canonical chunk sizes changed.");
        var chunks = new[] { new byte[] { 0, 255, 1 }, Array.Empty<byte>(), new byte[] { 7 } }; project.SetChunks(chunks); chunks[0][0] = 88;
        var snapshot = project.Chunks; snapshot[0][0] = 77; var bytes = project.Data; bytes[0] = 66;
        Equal((byte)0, project.Data[0], "Payload getter or chunk setter aliases data");
        Throws<ArgumentNullException>(() => project.Data = null!);
        Throws<ArgumentNullException>(() => project.SetChunks(null!));
        Throws<ArgumentException>(() => project.SetChunks(new[] { new byte[] { 4 }, null! }));
        Throws<ArgumentOutOfRangeException>(() => project.SetChunks(new[] { new byte[] { 4 }, new byte[128] }));
        Throws<InvalidOperationException>(() => project.SetChunks(StoredThrowingChunks()));
        Check(project.Data.SequenceEqual(new byte[] { 0, 255, 1, 7 }) && project.Chunks.Select(c => c.Length).SequenceEqual(new[] { 3, 0, 1 }), "Rejected setter changed payload or chunks.");
        project.SetChunks(new[] { Array.Empty<byte>(), Array.Empty<byte>() }); Equal(2, project.Chunks.Count, "Empty chunks lost");
        project.Data = Array.Empty<byte>(); Equal(0, project.Chunks.Count, "Empty Data assignment must canonicalize physical chunks");
    }
    private static void StoredEnvelopeModelLimits()
    {
        var project = new DxfVbaProject();
        project.Data = new byte[DxfVbaProject.MaximumDataLength]; Equal(DxfVbaProject.MaximumDataLength, project.DataLength, "Exact maximum bytes");
        Throws<ArgumentOutOfRangeException>(() => project.Data = new byte[DxfVbaProject.MaximumDataLength + 1]);
        Equal(DxfVbaProject.MaximumDataLength, project.DataLength, "Oversized Data changed prior value");
        project.SetChunks(project.Chunks); Equal(DxfVbaProject.MaximumDataLength, project.DataLength, "Maximum SetChunks length");
        Throws<ArgumentOutOfRangeException>(() => project.SetChunks(project.Chunks.Concat(new[] { new byte[] { 1 } })));
        Equal(DxfVbaProject.MaximumDataLength, project.DataLength, "Oversized chunk sum changed prior value");
        project.SetChunks(Enumerable.Repeat(Array.Empty<byte>(), DxfVbaProject.MaximumChunkCount)); Equal(DxfVbaProject.MaximumChunkCount, project.Chunks.Count, "Exact maximum chunks");
        Throws<ArgumentOutOfRangeException>(() => project.SetChunks(Enumerable.Repeat(Array.Empty<byte>(), DxfVbaProject.MaximumChunkCount + 1)));
        Equal(DxfVbaProject.MaximumChunkCount, project.Chunks.Count, "Too many chunks changed prior sequence");
    }
    private static DxfDocument StoredEnvelopeCreate(DxfVersion version)
    {
        var doc = new DxfDocument(version); var line = new Line(new Vector3(2, 3, 5), new Vector3(7, 11, 13)); doc.Entities.Add(line);
        var graph = new DxfDictionary(); doc.NamedObjects.Add("STORED_AUTHORED", graph);
        var index = new DxfSpatialIndex { Timestamp = -1234.125 }; graph.Add("INDEX", index);
        if (version >= DxfVersion.AutoCad2004)
        {
            var project = new DxfVbaProject(); project.SetChunks(new[] { Enumerable.Range(0, 127).Select(i => (byte)i).ToArray(), Array.Empty<byte>(), new byte[] { 0, 255, 17 } });
            graph.Add("VBA", project);
        }
        foreach (DxfDatabaseObject item in graph.Entries.Select(e => e.Target).Cast<DxfDatabaseObject>().ToArray())
        {
            item.PersistentReactors.Add(graph);
            var extension = new DxfDictionary(); extension.Add("NOTE", new DxfDictionaryVariable { Value = "stored \\U+0041" }); doc.Objects.SetExtensionDictionary(item, extension);
            var data = new XData(new ApplicationRegistry("STORED_AUTHORED")); data.XDataRecord.Add(new(XDataCode.DatabaseHandle, line.Handle)); data.XDataRecord.Add(new(XDataCode.String, "inert bytes")); item.XData.Add(data);
        }
        return doc;
    }
    private static void StoredEnvelopeAuthor(DxfVersion version, bool binary)
    {
        var doc = StoredEnvelopeCreate(version); Equal(0, doc.Objects.Validate().Count, "Authored graph validation");
        using var stream = new MemoryStream(); Check(doc.Save(stream, binary), "Authored save failed.");
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"stored-authored-R{version.ToString().Replace("AutoCad", "")}-{(binary ? "binary" : "ascii")}.dxf"), stream.ToArray());
        stream.Position = 0; var loaded = DxfDocument.Load(stream) ?? throw new InvalidOperationException("Authored reload failed.");
        var graph = (DxfDictionary)loaded.NamedObjects["STORED_AUTHORED"]; var original = (DxfDictionary)doc.NamedObjects["STORED_AUTHORED"];
        Equal(-1234.125, ((DxfSpatialIndex)graph["INDEX"]).Timestamp, "Authored timestamp");
        if (version >= DxfVersion.AutoCad2004) StoredChunksEqual((DxfVbaProject)original["VBA"], (DxfVbaProject)graph["VBA"]);
        foreach (DxfDictionaryEntry entry in graph.Entries)
        {
            var item = (DxfDatabaseObject)entry.Target;
            Check(ReferenceEquals(item.Owner, graph) && ReferenceEquals(item.PersistentReactors.Single(), graph), "Authored owner/reactor changed.");
            Equal("stored \\U+0041", ((DxfDictionaryVariable)item.ExtensionDictionary["NOTE"]).Value, "Extension metadata");
            Equal(loaded.Entities.Lines.Single().Handle, (string)item.XData["STORED_AUTHORED"].XDataRecord[0].Value, "Authored XData pointer");
        }
        Equal(0, loaded.Objects.Validate().Count, "Reloaded authored validation");
        Equal("AcDbSpatialIndex", loaded.Classes["SPATIAL_INDEX"].CppClassName, "Spatial CLASS metadata");
        Check(!loaded.Classes.Contains("VBA_PROJECT"), "Invented a VBA CLASS definition.");
    }
    private static void StoredEnvelopeIndependent(string kind, DxfVersion version, bool sourceBinary, bool binary)
    {
        string path = StoredFixture(kind, version, sourceBinary);
        var doc = DxfDocument.Load(path) ?? throw new InvalidOperationException("Independent stored input failed.");
        var graph = (DxfDictionary)doc.NamedObjects["QA_STORED_ENVELOPES"];
        Equal(kind == "spatial" ? 3 : 4, graph.Count, "Independent graph count");
        if (kind == "spatial")
        {
            Equal(BitConverter.DoubleToInt64Bits(2451544.5000000005), BitConverter.DoubleToInt64Bits(((DxfSpatialIndex)graph["ITEM_0"]).Timestamp), "Independent fractional timestamp");
            Equal(long.MinValue, BitConverter.DoubleToInt64Bits(((DxfSpatialIndex)graph["ITEM_1"]).Timestamp), "Independent negative zero timestamp");
        }
        else
        {
            var project = (DxfVbaProject)graph["ITEM_0"];
            Check(project.Data.SequenceEqual(Enumerable.Range(0, 300).Select(i => (byte)((i * 37 + 11) % 256))), "Independent VBA bytes changed.");
            Check(project.Chunks.Select(c => c.Length).SequenceEqual(new[] { 0, 127, 3, 0, 127, 43, 0 }), "Independent physical chunks changed.");
            Equal(0, ((DxfVbaProject)graph["ITEM_1"]).Chunks.Count, "No chunks versus empty chunks");
            Equal(2, ((DxfVbaProject)graph["ITEM_2"]).Chunks.Count, "Empty physical chunks");
        }
        using var stream = new MemoryStream(); Check(doc.Save(stream, binary), "Independent save failed.");
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, Path.GetFileNameWithoutExtension(path) + $"-roundtrip-{(binary ? "binary" : "ascii")}.dxf"), stream.ToArray());
        stream.Position = 0; var after = DxfDocument.Load(stream) ?? throw new InvalidOperationException("Independent reload failed.");
        Equal(0, after.Objects.Validate().Count, "Independent graph validation");
        var loaded = (DxfDictionary)after.NamedObjects["QA_STORED_ENVELOPES"];
        foreach (DxfDictionaryEntry entry in graph.Entries.Where(e => e.Name.StartsWith("ITEM_", StringComparison.Ordinal)))
        {
            var item = (DxfDatabaseObject)loaded[entry.Name]; var original = (DxfDatabaseObject)entry.Target;
            Equal(original.Handle, item.Handle, "Independent handle persistence"); Check(ReferenceEquals(item.Owner, loaded) && ReferenceEquals(item.PersistentReactors.Single(), loaded), "Independent ownership/reactor changed.");
            if (item is DxfVbaProject project) StoredChunksEqual((DxfVbaProject)original, project);
            else Equal(BitConverter.DoubleToInt64Bits(((DxfSpatialIndex)original).Timestamp), BitConverter.DoubleToInt64Bits(((DxfSpatialIndex)item).Timestamp), "Independent timestamp bits");
            Equal(original.ExtensionDictionary.Handle, item.ExtensionDictionary.Handle, "Independent extension identity");
            Equal(after.Entities.Lines.Single().Handle, (string)item.XData["QA_STORED_ENVELOPES"].XDataRecord[1].Value, "Independent XData pointer");
        }
        Equal("following envelope", (string)((DxfXRecord)loaded["FOLLOWING"]).Data[0].Value, "Following object swallowed");
    }
    private static void StoredEnvelopeClone(string kind, bool binary)
    {
        var source = StoredEnvelopeCreate(DxfVersion.AutoCad2018); var graph = (DxfDictionary)source.NamedObjects["STORED_AUTHORED"];
        var item = (DxfDatabaseObject)graph[kind == "spatial" ? "INDEX" : "VBA"];
        var target = new DxfDocument(DxfVersion.AutoCad2018);
        for (int i = 0; i < 30; i++) target.Entities.Add(new Line(Vector3.Zero, new Vector3(i, 1, 0)));
        var targetLine = target.Entities.Lines.Last(); var owner = new DxfDictionary(); target.NamedObjects.Add("CLONE", owner);
        Check(targetLine.Handle != source.Entities.Lines.Single().Handle && owner.Handle != graph.Handle, "Clone remapping would be vacuous.");
        int count = target.Objects.Items.Count; string seed = target.DrawingVariables.HandleSeed;
        Throws<InvalidOperationException>(() => target.Objects.CloneObject(item, owner, "FAILED"));
        Equal(count, target.Objects.Items.Count, "Failed clone registered shells"); Equal(seed, target.DrawingVariables.HandleSeed, "Failed clone changed seed"); Equal(0, owner.Count, "Failed clone added entry");
        var clone = target.Objects.CloneObject(item, owner, "COPY", new Dictionary<DxfObject, DxfObject> { [source.Entities.Lines.Single()] = targetLine });
        Check(clone.Handle != item.Handle && clone.ExtensionDictionary.Handle != item.ExtensionDictionary.Handle, "Cloned identity did not change.");
        Check(ReferenceEquals(clone.Owner, owner) && ReferenceEquals(clone.PersistentReactors.Single(), owner), "Clone retained source owner or reactor.");
        Check(ReferenceEquals(clone.ExtensionDictionary.Owner, clone), "Clone extension owner leaked.");
        Equal(targetLine.Handle, (string)clone.XData["STORED_AUTHORED"].XDataRecord[0].Value, "Clone XData was not remapped");
        if (clone is DxfVbaProject project)
        {
            StoredChunksEqual((DxfVbaProject)item, project); project.Data = new byte[] { 91 };
            Equal(130, ((DxfVbaProject)item).DataLength, "Clone payload edit leaked");
        }
        else { Equal(((DxfSpatialIndex)item).Timestamp, ((DxfSpatialIndex)clone).Timestamp, "Clone timestamp"); ((DxfSpatialIndex)clone).Timestamp = 9; Equal(-1234.125, ((DxfSpatialIndex)item).Timestamp, "Clone timestamp edit leaked"); }
        ((DxfDictionaryVariable)clone.ExtensionDictionary["NOTE"]).Value = "clone only";
        Equal("stored \\U+0041", ((DxfDictionaryVariable)item.ExtensionDictionary["NOTE"]).Value, "Clone extension edit leaked");
        var sibling = target.Objects.CloneObject(clone, owner, "SECOND"); Check(sibling.Handle != clone.Handle && ReferenceEquals(sibling.PersistentReactors.Single(), owner), "Same-document clone metadata changed.");
        Equal(0, target.Objects.Validate().Count, "Cloned graph validation");
        using var stream = new MemoryStream(); Check(target.Save(stream, binary), "Clone save failed."); stream.Position = 0;
        var loaded = DxfDocument.Load(stream) ?? throw new InvalidOperationException("Clone reload failed."); Equal(0, loaded.Objects.Validate().Count, "Cloned graph reload validation");
    }
    private static DxfRawDocument StoredEnvelopeRaw(string kind, Func<List<DxfTag>, List<DxfTag>> mutate)
    {
        using var input = File.OpenRead(StoredFixture(kind, DxfVersion.AutoCad2018, false)); var raw = DxfRawDocument.Load(input);
        var store = DxfRawObjectStore.Open(raw); string handle = store.Objects.First(o => o.TypeName == (kind == "spatial" ? "SPATIAL_INDEX" : "VBA_PROJECT")).Handle;
        return ObjectStoreReplaceRecord(raw, handle, mutate);
    }
    private static void StoredEnvelopeReject(DxfRawDocument raw, bool binary)
    {
        using var stream = new MemoryStream(); raw.Save(stream, binary); stream.Position = 0;
        bool rejected = false;
        try { rejected = DxfDocument.Load(stream) == null; } catch (FormatException) { rejected = true; } catch (ArgumentException) { rejected = true; }
        Check(rejected, "Malformed public stored envelope was accepted."); Check(stream.CanRead, "Rejected input closed caller stream.");
    }
    private static void StoredEnvelopeMalformed(string kind, bool binary, int scenario)
    {
        var raw = StoredEnvelopeRaw(kind, tags =>
        {
            int start = tags.FindIndex(t => t.Code == 100); int end = tags.FindIndex(t => t.Code == 1001);
            int value = tags.FindIndex(start, t => t.Code == (kind == "spatial" ? 40 : 90));
            if (scenario == 0) tags.RemoveAt(value);
            else if (scenario == 1) tags.Insert(value, tags[value]);
            else if (scenario == 2) tags.Insert(start, tags[start]);
            else if (kind == "spatial")
            {
                if (scenario == 3) tags.RemoveAt(start + 2);
                if (scenario == 4) tags.Insert(start + 2, tags[start + 2]);
                if (scenario == 5) { DxfTag saved = tags[value]; tags.RemoveAt(value); tags.Insert(start + 2, saved); }
                if (scenario == 6) { tags.Insert(end, new(40, 17.25)); tags.Insert(end, new(91, 42)); }
                if (scenario == 7) { tags.Insert(end, new(100, "AcDbIndex")); tags.Insert(end, new(100, "PrivateExtension")); }
                if (scenario == 8) tags.RemoveAt(start);
            }
            else
            {
                if (scenario == 3) tags[value] = new(90, -1);
                if (scenario == 4) tags[value] = new(90, 299);
                if (scenario == 5) tags[value] = new(90, 301);
                if (scenario == 6) tags[value] = new(90, DxfVbaProject.MaximumDataLength + 1);
                if (scenario == 7) tags.Insert(value, new(310, Array.Empty<byte>()));
                if (scenario == 8) { tags[value] = new(90, 428); tags.Insert(end, new(310, new byte[128])); }
                if (scenario == 9) { tags.Insert(end, new(90, 300)); tags.Insert(end, new(91, 42)); }
                if (scenario == 10) { tags.Insert(end, new(100, "AcDbVbaProject")); tags.Insert(end, new(91, 42)); }
                if (scenario == 11) tags.RemoveRange(start, end - start);
            }
            return tags;
        });
        StoredEnvelopeReject(raw, binary);
    }
    private static string StoredTagKey(DxfTag tag) => tag.Code + ":" + (tag.Value is byte[] bytes ? Convert.ToHexString(bytes) : tag.Value is double number ? BitConverter.DoubleToInt64Bits(number).ToString("X16") : tag.Value.ToString());
    private static void StoredEnvelopeOpaque(string kind, bool binary, int scenario)
    {
        var raw = StoredEnvelopeRaw(kind, tags =>
        {
            int start = tags.FindIndex(t => t.Code == 100), end = tags.FindIndex(t => t.Code == 1001);
            if (scenario == 0) tags.Insert(end, new(91, 42));
            if (scenario == 1) { tags.Insert(end, new(100, "PrivateExtension")); tags.Insert(end + 1, new(kind == "spatial" ? (short)40 : (short)90, kind == "spatial" ? (object)17.25 : 17)); }
            if (scenario == 2) tags[start] = new(100, "PrivateEnvelope");
            if (scenario == 3) tags.Insert(end, new(1, "literal \\U+0041"));
            if (scenario == 4) { tags.Insert(end, new(100, "PrivateExtension")); tags.Insert(end + 1, new(310, new byte[128])); }
            return tags;
        });
        using var input = new MemoryStream(); raw.Save(input, binary); input.Position = 0;
        var doc = DxfDocument.Load(input) ?? throw new InvalidOperationException("Unknown extension failed opaque load.");
        var item = (DxfOpaqueObject)((DxfDictionary)doc.NamedObjects["QA_STORED_ENVELOPES"])["ITEM_0"];
        var expected = item.Tags.ToArray();
        using var output = new MemoryStream(); Check(doc.Save(output, !binary), "Opaque save failed."); output.Position = 0;
        var loaded = DxfDocument.Load(output) ?? throw new InvalidOperationException("Opaque reload failed.");
        var actual = (DxfOpaqueObject)((DxfDictionary)loaded.NamedObjects["QA_STORED_ENVELOPES"])["ITEM_0"];
        Check(expected.Select(StoredTagKey).SequenceEqual(actual.Tags.Select(StoredTagKey)), "Opaque extension tags changed.");
        Throws<NotSupportedException>(() => doc.Objects.CloneObject(item, doc.NamedObjects, "PRIVATE_CLONE"));
    }
    private static void StoredEnvelopeInputLimits(bool binary)
    {
        foreach (bool tooMany in new[] { false, true })
        {
            var raw = StoredEnvelopeRaw("vba", tags =>
            {
                int count = tags.FindIndex(t => t.Code == 90), end = tags.FindIndex(t => t.Code == 1001);
                tags.RemoveRange(count + 1, end - count - 1); tags[count] = new(90, 0);
                tags.InsertRange(count + 1, Enumerable.Repeat(new DxfTag(310, Array.Empty<byte>()), DxfVbaProject.MaximumChunkCount + (tooMany ? 1 : 0)));
                return tags;
            });
            if (tooMany) StoredEnvelopeReject(raw, binary);
            else
            {
                using var stream = new MemoryStream(); raw.Save(stream, binary); stream.Position = 0;
                var doc = DxfDocument.Load(stream) ?? throw new InvalidOperationException("Exact input chunk limit rejected.");
                Equal(DxfVbaProject.MaximumChunkCount, ((DxfVbaProject)((DxfDictionary)doc.NamedObjects["QA_STORED_ENVELOPES"])["ITEM_0"]).Chunks.Count, "Exact input physical chunk limit");
            }
        }
    }
    private static void StoredEnvelopeVersion(bool binary)
    {
        var doc = new DxfDocument(DxfVersion.AutoCad2000) { Name = "original" }; var project = new DxfVbaProject { Data = new byte[] { 7 } }; doc.NamedObjects.Add("VBA", project);
        Check(doc.Objects.Validate().Any(e => e.Contains("VBA_PROJECT", StringComparison.Ordinal)), "VBA profile validation missing.");
        using var stream = new MemoryStream(); stream.Write(new byte[] { 1, 2, 3 }); long position = stream.Position; string seed = doc.DrawingVariables.HandleSeed;
#if DEBUG
        Throws<InvalidOperationException>(() => doc.Save(stream, binary));
#else
        Check(!doc.Save(stream, binary), "Unqualified VBA profile saved.");
#endif
        Check(stream.ToArray().SequenceEqual(new byte[] { 1, 2, 3 }) && stream.Position == position, "VBA preflight touched output.");
        Equal(seed, doc.DrawingVariables.HandleSeed, "VBA preflight changed seed");
        WithAtomicDirectory(path =>
        {
            AtomicPrepare(path, true); Throws<InvalidOperationException>(() => doc.SaveAtomic(path, binary)); AtomicUnchanged(path, true); Equal("original", doc.Name, "Rejected atomic VBA changed name");
        });
        var supported = new DxfDocument(DxfVersion.AutoCad2004); supported.NamedObjects.Add("VBA", new DxfVbaProject());
        int count = doc.Objects.Items.Count;
        Throws<InvalidOperationException>(() => doc.Objects.CloneObject((DxfVbaProject)supported.NamedObjects["VBA"], doc.NamedObjects, "FAILED"));
        Equal(count, doc.Objects.Items.Count, "Unqualified clone registered objects"); Equal(seed, doc.DrawingVariables.HandleSeed, "Unqualified clone changed seed");
        using var fixture = File.OpenRead(StoredFixture("vba", DxfVersion.AutoCad2004, false)); var raw = DxfRawDocument.Load(fixture); var tags = raw.Tags.ToList();
        int version = tags.FindIndex(t => t.Code == 9 && (string)t.Value == "$ACADVER") + 1; tags[version] = new(1, "AC1015");
        using var input = new MemoryStream(); DxfRawDocument.Create(tags).Save(input, binary); input.Position = 0;
        var opaque = DxfDocument.Load(input) ?? throw new InvalidOperationException("Unqualified input lost opaque preservation.");
        Check(((DxfDictionary)opaque.NamedObjects["QA_STORED_ENVELOPES"])["ITEM_0"] is DxfOpaqueObject, "Unqualified input was partially typed.");
        using var saved = new MemoryStream(); Check(opaque.Save(saved, binary), "Unqualified opaque save failed.");
    }
    private static void StoredEnvelopePrivateClass(bool binary, int scenario)
    {
        DxfDocument doc;
        if (scenario == 0) doc = new DxfDocument(DxfVersion.AutoCad2018);
        else
        {
            using var input = File.OpenRead(StoredFixture("spatial", DxfVersion.AutoCad2018, false)); var raw = DxfRawDocument.Load(input);
            string[] handles = DxfRawObjectStore.Open(raw).Objects.Where(o => o.TypeName == "SPATIAL_INDEX").Select(o => o.Handle).ToArray();
            foreach (string handle in handles.Take(scenario == 1 ? 2 : 1))
                raw = ObjectStoreReplaceRecord(raw, handle, tags => { tags[tags.FindIndex(t => t.Code == 100)] = new(100, "PrivateEnvelope"); return tags; });
            using var stream = new MemoryStream(); raw.Save(stream, binary); stream.Position = 0;
            doc = DxfDocument.Load(stream) ?? throw new InvalidOperationException("Private CLASS probe input failed.");
            doc.Classes.Remove("SPATIAL_INDEX");
        }
        string cpp = scenario == 2 ? "AcDbSpatialIndex" : "PrivateSpatialClass";
        var definition = new DxfClass("SPATIAL_INDEX", cpp, "Private application") { InstanceCount = 73, ProxyFlags = 123, WasProxy = true };
        doc.Classes.Add(definition);
        using var output = new MemoryStream(); Check(doc.Save(output, binary), "Private or unused CLASS save failed.");
        Equal(73, definition.InstanceCount, "Save mutated original CLASS instance count");
        output.Position = 0; var loaded = DxfDocument.Load(output) ?? throw new InvalidOperationException("Private CLASS reload failed.");
        DxfClass actual = loaded.Classes["SPATIAL_INDEX"];
        Equal(cpp, actual.CppClassName, "Private CLASS identity changed"); Equal("Private application", actual.ApplicationName, "Private CLASS application changed");
        Equal(123, actual.ProxyFlags, "Private CLASS proxy flags changed"); Check(actual.WasProxy, "Private CLASS proxy state changed.");
        Equal(scenario == 2 ? 2 : 73, actual.InstanceCount, "Typed/opaque physical CLASS counting");
        Equal(scenario == 0 ? 0 : scenario == 1 ? 2 : 1, loaded.Objects.Items.OfType<DxfOpaqueObject>().Count(o => o.CodeName == "SPATIAL_INDEX"), "Opaque spatial instance inventory");
    }
    private static void StoredEnvelopeClass(bool binary)
    {
        var doc = new DxfDocument(DxfVersion.AutoCad2018); doc.NamedObjects.Add("INDEX", new DxfSpatialIndex());
        doc.Classes.Add(new DxfClass("SPATIAL_INDEX", "PrivateClass", "Private application"));
        using var stream = new MemoryStream();
#if DEBUG
        Throws<InvalidDataException>(() => doc.Save(stream, binary));
#else
        Check(!doc.Save(stream, binary), "Conflicting spatial class accepted.");
#endif
        Equal(0L, stream.Length, "Class preflight wrote bytes");
    }
}
