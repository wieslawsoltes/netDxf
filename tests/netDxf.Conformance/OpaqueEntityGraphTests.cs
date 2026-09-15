using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Tables;

namespace NetDxf.Conformance;
internal static partial class Program
{
    private static void RegisterOpaqueEntityGraphTests()
    {
        foreach (DxfVersion version in SupportedVersions) foreach (bool binary in new[] { false, true })
        {
            foreach (string place in new[] { "paper", "unused-block" })
                Run($"opaque-entity/placement/{place}/{version}/{binary}", () => OpaquePlacement(version, binary, place));
            Run($"opaque-entity/class-literal/{version}/{binary}", () => OpaqueClassLiteral(version, binary));
            Run($"opaque-entity/xdata-layer/{version}/{binary}", () => OpaqueXDataLayer(version, binary));
            foreach (string table in new[] { "LAYER", "LTYPE", "STYLE" })
                Run($"opaque-entity/table-reference/{table}/{version}/{binary}", () => OpaqueTableReference(version, binary, table));
        }
        foreach (bool binary in new[] { false, true })
        {
            Run($"opaque-entity/aggregate-budget/{binary}", () => OpaqueAggregateBudget(binary));
            Run($"opaque-entity/binary-chunk/{binary}", () => OpaqueBinaryChunk(binary));
            foreach (string value in new[] { "class-nul", "class-crlf", "xdata-nul" })
                Run($"opaque-entity/text-preflight/{value}/{binary}", () => OpaqueTextPreflight(value, binary));
        }
    }
    private static void OpaquePlacement(DxfVersion version, bool binary, string placement)
    {
        var document = OpaqueLoad(OpaqueFixture(version, placement), binary);
        var entity = document.Blocks.SelectMany(block => block.Entities).OfType<DxfOpaqueEntity>().Single();
        Check(ReferenceEquals(document.GetObjectByHandle(entity.SourceHandle), entity), "Block/paper actual entity identity");
        Check(ReferenceEquals(entity.Owner.Record, document.GetObjectByHandle((string)entity.SourceTags.First(tag => tag.Code == 330).Value)), "Block/paper exact source owner");
        Check(entity.References.OfType<Line>().Single().Owner == entity.Owner, "Block/paper standard source dependency");
        if (placement == "paper") { document.Entities.ActiveLayout = "OpaquePaper"; Check(document.Entities.OpaqueEntities.Single() == entity, "Active paper enumeration"); }
        else Check(!document.Entities.OpaqueEntities.Any(), "Unused block omitted from active layout enumeration");
        DxfVersion target = SupportedVersions.First(other => other != version);
        Check(document.AnalyzeVersionCompatibility(target).Diagnostics.Any(item => ReferenceEquals(item.SourceObject, entity) && item.Code == "STORED_SOURCE_PROFILE"), "All-block report traversal");
        var state = new CompatibilityState(document); var foreign = new DxfDocument(version); var foreignState = new CompatibilityState(foreign);
        bool rejected = false; try { foreign.Entities.Add(entity); } catch (ArgumentException) { rejected = true; } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "Foreign entity adoption rejected"); state.CheckUnchanged(); foreignState.CheckUnchanged();
        var otherBlock = new Block("OTHER_OWNER"); document.Blocks.Add(otherBlock); state = new CompatibilityState(document);
        rejected = false; try { otherBlock.Entities.Add(entity); } catch (ArgumentException) { rejected = true; } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "Opaque owner change rejected"); state.CheckUnchanged();
        var snapshot = entity.SourceTags.ToArray(); using var output = new MemoryStream(); Check(document.Save(output, !binary), "Block/paper opaque save"); output.Position = 0;
        var restored = DxfDocument.Load(output) ?? throw new Exception("Block/paper reload");
        OpaqueSameTags(snapshot, restored.Blocks.SelectMany(block => block.Entities).OfType<DxfOpaqueEntity>().Single().SourceTags, "Block/paper packet preservation");
    }
    private static DxfRawDocument OpaqueClassApplication(DxfRawDocument raw, string encodedValue)
    {
        var definition = raw.Sections.Single(section => section.Name == "CLASSES").Records.Single(record => record.Tags.Any(tag => tag.Code == 1 && (string)tag.Value == OpaqueName));
        var all = raw.Tags.ToList(); int at = definition.StartTagIndex + definition.Tags.ToList().FindIndex(tag => tag.Code == 3);
        all[at] = new DxfTag(3, encodedValue); return DxfRawDocument.Create(all);
    }
    private static void OpaqueClassLiteral(DxfVersion version, bool binary)
    {
        var raw = OpaqueClassApplication(OpaqueFixture(version), @"literal \U+005CU+0041 \U+03A9");
        using (var source = new MemoryStream()) { raw.Save(source, binary); File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"opaque-class-source-{version}-{binary}.dxf"), source.ToArray()); }
        var document = OpaqueLoad(raw, binary); const string expected = @"literal \U+0041 Ω";
        Equal(expected, document.Classes[OpaqueName].ApplicationName, "Loaded literal CLASS text");
        foreach (bool outputFormat in new[] { false, true })
        {
            using var output = new MemoryStream(); Check(document.Save(output, outputFormat), "Literal CLASS save");
            File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"opaque-class-output-{version}-{binary}-{outputFormat}.dxf"), output.ToArray()); output.Position = 0;
            var restored = DxfDocument.Load(output) ?? throw new Exception("Literal CLASS reload");
            Equal(expected, restored.Classes[OpaqueName].ApplicationName, "Literal CLASS escaped text survives output");
        }
    }
    private static void OpaqueXDataLayer(DxfVersion version, bool binary)
    {
        var document = OpaqueLoad(OpaqueFixture(version, "xdata-layer"), binary); var entity = document.Entities.OpaqueEntities.Single();
        var layer = document.Layers["SOURCE_XDATA_LAYER"];
        Check(entity.References.Any(item => ReferenceEquals(item, layer)), "XData1003 binds actual layer");
        Check(!document.Layers.Remove(layer), "XData1003 layer removal protected");
        layer.Name = "RENAMED_SOURCE_XDATA";
        using var output = new MemoryStream(); Check(document.Save(output, !binary), "Source XData layer rename save"); output.Position = 0;
        var restored = DxfDocument.Load(output) ?? throw new Exception("Source XData layer reload");
        var other = restored.Entities.OpaqueEntities.Single();
        Equal(layer.Name, other.XData.Values.Single().XDataRecord.Single(tag => tag.Code == XDataCode.LayerName).Value, "Renamed XData1003 wire value");
        Check(other.References.Any(item => ReferenceEquals(item, restored.Layers[layer.Name])), "Reloaded actual renamed layer");
        var data = entity.XData.Values.Single(); data.XDataRecord.Remove(data.XDataRecord.Single(tag => tag.Code == XDataCode.LayerName));
        Check(document.Layers.Remove(layer), "Removing XData1003 releases known dependency");
    }
    private static void OpaqueTableReference(DxfVersion version, bool binary, string table)
    {
        var raw = OpaqueFixture(version); string template = table == "LAYER" ? "0" : table == "LTYPE" ? "BYLAYER" : "STANDARD";
        var record = raw.Sections.Single(section => section.Name == "TABLES").Records.Single(record => record.Name == table && record.Tags.Any(tag => tag.Code == 2 && string.Equals((string)tag.Value, template, StringComparison.OrdinalIgnoreCase)));
        var copy = record.Tags.ToList(); copy[copy.FindIndex(tag => tag.Code == 5)] = new DxfTag(5, "E001"); copy[copy.FindIndex(tag => tag.Code == 2)] = new DxfTag(2, "OPAQUE_DEPENDENCY");
        var all = raw.Tags.ToList(); all.InsertRange(record.EndTagIndex, copy); raw = DxfRawDocument.Create(all);
        raw = OpaqueModify(raw, packet => packet.Insert(packet.Count - 4, new DxfTag(340, "E001")));
        var document = OpaqueLoad(raw, binary); var entity = document.Entities.OpaqueEntities.Single(); var target = document.GetObjectByHandle("E001");
        Check(entity.References.Any(item => ReferenceEquals(item, target)), "Direct table reference binds actual source object");
        bool remove() => table == "LAYER" ? document.Layers.Remove("OPAQUE_DEPENDENCY") : table == "LTYPE" ? document.Linetypes.Remove("OPAQUE_DEPENDENCY") : document.TextStyles.Remove("OPAQUE_DEPENDENCY");
        var state = new CompatibilityState(document); Check(!remove(), "Direct table target removal protected"); state.CheckUnchanged();
        Check(document.Entities.Remove(entity), "Explicit opaque removal releases direct table reference"); Check(remove(), "Direct table target removable after consumer retirement");
    }
    private static void OpaqueAggregateBudget(bool binary)
    {
        var raw = OpaqueFixture(DxfVersion.AutoCad2018); var record = raw.Sections.Single(section => section.Name == "ENTITIES").Records.Single(record => record.Name == OpaqueName);
        var all = raw.Tags.ToList(); var extra = new List<DxfTag>();
        for (int i = 1; i < 17; i++)
        {
            var copy = record.Tags.ToList(); copy[1] = new DxfTag(5, (0xF001 + i).ToString("X")); extra.AddRange(copy);
        }
        all.InsertRange(record.EndTagIndex, extra);
        var document = OpaqueLoad(DxfRawDocument.Create(all), binary); var entities = document.Entities.OpaqueEntities.ToArray();
        Equal(17, entities.Length, "Aggregate fixture has independent actual records");
        int remaining = 1048576 - entities.Sum(entity => entity.SourceTags.Count - 1);
        DxfOpaqueEntity? spare = null;
        foreach (var entity in entities)
        {
            int count = Math.Min(remaining, 65536 - entity.SourceTags.Count);
            var data = entity.XData.Values.Single();
            for (int i = 0; i < count; i++) data.XDataRecord.Add(new XDataRecord(XDataCode.Int16, (short)1));
            remaining -= count; if (count < 65536 - entity.SourceTags.Count) spare = entity;
        }
        Equal(0, remaining, "Exact aggregate boundary prepared"); Check(spare != null, "Overflow record remains below per-record budget");
        using var output = new MemoryStream(); Check(document.Save(output, binary), "Exact aggregate boundary save"); output.Position = 0;
        var restored = DxfDocument.Load(output) ?? throw new Exception("Exact aggregate boundary reload"); Equal(17, restored.Entities.OpaqueEntities.Count(), "Aggregate boundary reader agreement");
        spare!.XData.Values.Single().XDataRecord.Add(new XDataRecord(XDataCode.Int16, (short)1));
        OpaqueExpectPreflight(document, binary, "aggregate-budget");
        var records = spare.XData.Values.Single().XDataRecord; records.RemoveAt(records.Count - 1);
        using var retry = new MemoryStream(); Check(document.Save(retry, binary), "Aggregate budget repair retry");
    }
    private static void OpaqueBinaryChunk(bool binary)
    {
        var raw = OpaqueModify(OpaqueFixture(DxfVersion.AutoCad2018), packet => packet.Insert(packet.Count - 4, new DxfTag(310, Enumerable.Range(0, 300).Select(i => (byte)i).ToArray())));
        var document = OpaqueLoad(raw, false);
        if (binary) { OpaqueExpectPreflight(document, true, "binary-chunk"); return; }
        using var output = new MemoryStream(); Check(document.Save(output, false), "ASCII long private chunk save"); output.Position = 0;
        OpaqueSameTags(document.Entities.OpaqueEntities.Single().SourceTags, (DxfDocument.Load(output) ?? throw new Exception("ASCII long private chunk reload")).Entities.OpaqueEntities.Single().SourceTags, "ASCII long private chunk retained");
    }
    private static void OpaqueTextPreflight(string value, bool binary)
    {
        var raw = OpaqueFixture(DxfVersion.AutoCad2018);
        if (value.StartsWith("class-", StringComparison.Ordinal))
        {
            raw = OpaqueClassApplication(raw, value == "class-nul" ? @"nul\U+0000text" : @"line\U+000D\U+000Atext");
            using var input = new MemoryStream(); raw.Save(input, binary); input.Position = 0;
            DxfDocument? accepted = null; try { accepted = DxfDocument.Load(input); } catch (Exception) { }
            Check(accepted == null, "Existing CLASS text admission rejects decoded controls"); return;
        }
        var document = OpaqueLoad(raw, binary);
        document.Entities.OpaqueEntities.Single().XData.Values.Single().XDataRecord.Add(new XDataRecord(XDataCode.String, "nul\0text"));
        OpaqueExpectPreflight(document, binary, value);
    }
}
