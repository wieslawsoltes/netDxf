using System.IO.Compression;
using System.Security.Cryptography;
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
    private static void RunSectionSettingsTests()
    {
        foreach (DxfVersion version in SupportedVersions.Where(value => value >= DxfVersion.AutoCad2007))
        foreach (bool binary in new[] { false, true })
        {
            Run($"section-settings/authored/{version}/{binary}", () => SectionSettingsAuthored(version, binary));
            Run($"section-settings/clone-erase/{version}/{binary}", () => SectionSettingsCloneErase(version, binary));
            foreach (bool sourceBinary in new[] { false, true }) Run($"section-settings/producer/{version}/{sourceBinary}/{binary}", () => SectionSettingsProducer(version, sourceBinary, binary));
        }
        foreach (DxfVersion version in new[] { DxfVersion.AutoCad2007, DxfVersion.AutoCad2018 })
        foreach (bool binary in new[] { false, true }) foreach (int fault in Enumerable.Range(0, 24))
            Run($"section-settings/malformed/{version}/{binary}/{fault}", () => SectionSettingsMalformed(version, binary, fault));
        foreach (bool binary in new[] { false, true }) foreach (int variant in Enumerable.Range(0, 7))
            Run($"section-settings/opaque/{binary}/{variant}", () => SectionSettingsOpaque(binary, variant));
        foreach (DxfVersion version in new[] { DxfVersion.AutoCad2000, DxfVersion.AutoCad2004 })
        foreach (bool binary in new[] { false, true }) Run($"section-settings/profile/{version}/{binary}", () => SectionSettingsProfile(version, binary));
        foreach (int scenario in Enumerable.Range(0, 13)) Run($"section-settings/api/{scenario}", () => SectionSettingsApi(scenario));
        foreach (bool binary in new[] { false, true }) Run($"section-settings/native/{binary}", () => SectionSettingsNative(binary));
    }

    private static DxfSectionGeometrySettings SectionAppearance(int ordinal, short colorCode)
    {
        return new DxfSectionGeometrySettings
        {
            SectionType = -17 + ordinal, GeometryValue = 1 << ordinal, Flags = unchecked((int)0x80000005),
            ColorCode = colorCode, ColorIndex = (short)(ordinal == 2 ? 256 : ordinal + 1), LayerName = ordinal == 0 ? "*_BackgroundLines" : "東京",
            LinetypeName = ordinal == 1 ? @"Literal\U+0041" : "ByLayer", LinetypeScale = 1.125 + ordinal,
            PlotStyleName = @"plot\U+0041", Lineweight = 40, FaceTransparency = (short)(ordinal * 30), EdgeTransparency = 100,
            HatchPatternType = (short)ordinal, HatchPatternName = ordinal == 0 ? "" : ordinal == 1 ? "SectionGeometrySettings" : "ANSI31",
            HatchAngle = -7.125 + ordinal, HatchScale = 21.5 + ordinal, HatchSpacing = -0.125 - ordinal
        };
    }
    private static DxfDocument SectionSettingsDocument(DxfVersion version)
    {
        var doc = new DxfDocument(version); doc.Layers.Add(new Layer("東京"));
        var a = new Line(new Vector3(1, 2, 3), new Vector3(4, 5, 6)); var b = new Line(new Vector3(-1, -2, -3), new Vector3(-4, -5, -6));
        doc.Entities.Add(a); doc.Entities.Add(b); var destination = doc.Blocks.Add(new Block("SECTION_OUTPUT"));
        var main = new DxfSectionSettings { SectionType = 4 }; var empty = new DxfSectionSettings { SectionType = -1 };
        var geometry = Enumerable.Range(0, 3).Select(i => SectionAppearance(i, 63)).ToArray();
        main.SetTypeSettings(new[]
        {
            new DxfSectionTypeSettings(4, 17, new DxfObject[] { a, b, a, null!, main, empty }, destination.Record, @"inert\U+0041.dwg", geometry, false),
            new DxfSectionTypeSettings(2, 33, new DxfObject[] { b }, null!, "", Enumerable.Range(0, 3).Select(i => SectionAppearance(i, 62))),
            new DxfSectionTypeSettings(0, 0, Array.Empty<DxfObject>(), null!, "", Array.Empty<DxfSectionGeometrySettings>(), false),
            new DxfSectionTypeSettings(1, 1, Array.Empty<DxfObject>(), null!, "", Array.Empty<DxfSectionGeometrySettings>(), true)
        });
        geometry[0].HatchScale = 999; Check(main.TypeSettings[0].GeometrySettings[0].HatchScale == 21.5, "settings retained caller geometry aliases");
        var parent = new DxfDictionary(); parent.Add("MAIN", main); parent.Add("MAIN_ALIAS", main, false); parent.Add("EMPTY", empty); doc.NamedObjects.Add("QA_SECTION_SETTINGS", parent);
        main.PersistentReactors.Add(a); main.PersistentReactors.Add(parent);
        var data = new XData(new ApplicationRegistry("SECTION_SETTINGS_APP")); data.XDataRecord.Add(new XDataRecord(XDataCode.BinaryData, new byte[] { 7, 0, 255 }));
        data.XDataRecord.Add(new XDataRecord(XDataCode.DatabaseHandle, empty.Handle)); main.XData.Add(data);
        var extension = new DxfDictionary(); var note = new DxfXRecord(); note.Data.Add(new DxfTag(1, "section settings metadata")); note.Data.Add(new DxfTag(330, empty.Handle)); extension.Add("NOTE", note); doc.Objects.SetExtensionDictionary(main, extension);
        return doc;
    }
    private static byte[] SectionSettingsSave(DxfDocument doc, bool binary, string? filename = null)
    {
        using var output = new MemoryStream(); Check(doc.Save(output, binary), "section-settings save failed"); var data = output.ToArray();
        if (filename != null) File.WriteAllBytes(Path.Combine(ArtifactDirectory, filename), data); return data;
    }
    private static DxfDocument SectionSettingsLoad(byte[] bytes)
    { using var input = new MemoryStream(bytes); return DxfDocument.Load(input) ?? throw new Exception("section-settings input rejected"); }
    private static void SectionSettingsCheck(DxfDocument doc, string parentName = "QA_SECTION_SETTINGS")
    {
        var parent = (DxfDictionary)doc.NamedObjects[parentName]; var main = (DxfSectionSettings)parent["MAIN"]; var empty = (DxfSectionSettings)parent["EMPTY"];
        Check(ReferenceEquals(parent["MAIN_ALIAS"], main), "settings alias identity changed"); Equal(4, main.SectionType, "settings section type"); Equal(-1, empty.SectionType, "empty stored type"); Equal(0, empty.TypeSettings.Count, "empty settings invented records");
        Equal(4, main.TypeSettings.Count, "type count"); var first = main.TypeSettings[0]; var second = main.TypeSettings[1]; var lines = doc.Entities.Lines.ToArray();
        Check(first.SourceObjects.SequenceEqual(new DxfObject[] { lines[0], lines[1], lines[0], null!, main, empty }), "source order, duplicates, null or internal identities changed");
        Check(ReferenceEquals(first.DestinationBlock, doc.Blocks["SECTION_OUTPUT"].Record), "destination BLOCK_RECORD identity changed");
        Equal(17, first.GenerationOptions, "integer generation flags were reduced to Boolean"); Equal(33, second.GenerationOptions, "second generation flags");
        Equal(@"inert\U+0041.dwg", first.DestinationFileName, "destination file string changed");
        Check(!first.RepeatGeometryMarkers && second.RepeatGeometryMarkers && !main.TypeSettings[2].RepeatGeometryMarkers && main.TypeSettings[3].RepeatGeometryMarkers, "physical geometry marker layouts changed");
        for (int bundle = 0; bundle < 2; bundle++) for (int ordinal = 0; ordinal < 3; ordinal++)
        {
            var actual = main.TypeSettings[bundle].GeometrySettings[ordinal]; var expected = SectionAppearance(ordinal, (short)(bundle == 0 ? 63 : 62));
            foreach (var property in typeof(DxfSectionGeometrySettings).GetProperties()) Equal(property.GetValue(expected), property.GetValue(actual), "stored geometry " + property.Name);
        }
        Check(main.PersistentReactors.Contains(lines[0]) && main.PersistentReactors.Contains(parent), "settings reactors changed");
        Equal(empty.Handle, (string)((DxfXRecord)main.ExtensionDictionary["NOTE"]).Data[1].Value, "extension reference mapping changed");
        Check(((byte[])main.XData["SECTION_SETTINGS_APP"].XDataRecord[0].Value).SequenceEqual(new byte[] { 7, 0, 255 }), "settings binary metadata changed");
        Equal(empty.Handle, (string)main.XData["SECTION_SETTINGS_APP"].XDataRecord[1].Value, "settings XData handle changed");
        Equal(0, doc.Objects.Validate().Count, "settings database validation");
    }
    private static void SectionSettingsAuthored(DxfVersion version, bool binary)
    {
        var doc = SectionSettingsDocument(version); SectionSettingsCheck(doc); doc.Layers["東京"].Name = "RenamedLayer";
        var loaded = SectionSettingsLoad(SectionSettingsSave(doc, binary, $"section-settings-authored-{version}-{binary}.dxf")); SectionSettingsCheck(loaded);
        var again = SectionSettingsLoad(SectionSettingsSave(loaded, !binary)); SectionSettingsCheck(again);
        var cls = loaded.Classes["SECTIONSETTINGS"]; Check(!cls.IsEntity && cls.CppClassName == "AcDbSectionSettings", "settings CLASS identity"); Equal(2, cls.InstanceCount, "settings physical CLASS count");
    }
    private static void SectionSettingsCloneErase(DxfVersion version, bool binary)
    {
        var source = SectionSettingsDocument(version); var parent = (DxfDictionary)source.NamedObjects["QA_SECTION_SETTINGS"]; var main = (DxfSectionSettings)parent["MAIN"];
        var target = new DxfDocument(version); for (int i = 0; i < 8; i++) target.Layers.Add(new Layer("PAD" + i));
        var originalLines = source.Entities.Lines.ToArray(); var lines = originalLines.Select(line => (Line)line.Clone()).ToArray(); target.Entities.Add(lines);
        var block = target.Blocks.Add(new Block("SECTION_OUTPUT")); _ = target.Objects; string seed = target.DrawingVariables.HandleSeed;
        Throws<InvalidOperationException>(() => target.Objects.Clone(parent, target.NamedObjects, "COPY")); Equal(seed, target.DrawingVariables.HandleSeed, "missing map allocated handles");
        var map = new Dictionary<DxfObject, DxfObject> { [originalLines[0]] = lines[0], [originalLines[1]] = lines[1], [source.Blocks["SECTION_OUTPUT"].Record] = block.Record };
        var copyParent = target.Objects.Clone(parent, target.NamedObjects, "COPY", map); var copy = (DxfSectionSettings)copyParent["MAIN"];
        SectionSettingsCheck(target, "COPY"); Check(main.Handle != copy.Handle, "settings clone reused source handle");
        copy.TypeSettings[0].GeometrySettings[0].HatchScale = 101; Equal(21.5, main.TypeSettings[0].GeometrySettings[0].HatchScale, "clone shared mutable geometry"); copy.TypeSettings[0].GeometrySettings[0].HatchScale = 21.5;
        ((byte[])copy.XData["SECTION_SETTINGS_APP"].XDataRecord[0].Value)[0] = 99; Equal((byte)7, ((byte[])main.XData["SECTION_SETTINGS_APP"].XDataRecord[0].Value)[0], "clone shared binary metadata"); ((byte[])copy.XData["SECTION_SETTINGS_APP"].XDataRecord[0].Value)[0] = 7;
        target = SectionSettingsLoad(SectionSettingsSave(target, binary, $"section-settings-copy-{version}-{binary}.dxf")); copyParent = (DxfDictionary)target.NamedObjects["COPY"]; copy = (DxfSectionSettings)copyParent["MAIN"];
        var blocker = new DxfXRecord(); blocker.Data.Add(new DxfTag(340, copy.Handle)); target.NamedObjects.Add("BLOCKER", blocker);
        Throws<InvalidOperationException>(() => target.Objects.EraseOwnedTree(copyParent)); blocker.Data.Clear(); target.Objects.EraseOwnedTree(blocker);
        string handle = copy.Handle; target.Objects.EraseOwnedTree(copyParent); Check(copy.IsErased && copy.Handle == handle && copy.Database == null, "settings erasure did not preserve tombstone identity");
        SectionSettingsSave(target, binary, $"section-settings-erased-{version}-{binary}.dxf"); Equal(2, target.Entities.Lines.Count(), "erasing settings erased source entities"); Check(target.Blocks.Contains("SECTION_OUTPUT"), "erasing settings erased destination block"); SectionSettingsCheck(source);
    }
    private static List<int> SectionPositions(List<DxfTag> tags, short code, string? value = null)
    { return Enumerable.Range(0, tags.Count).Where(i => tags[i].Code == code && (value == null || Equals(tags[i].Value, value))).ToList(); }
    private static void SectionSettingsMalformed(DxfVersion version, bool binary, int fault)
    {
        var doc = SectionSettingsDocument(version); var main = (DxfSectionSettings)((DxfDictionary)doc.NamedObjects["QA_SECTION_SETTINGS"])["MAIN"];
        using var bytes = new MemoryStream(SectionSettingsSave(doc, binary)); var raw = DxfRawDocument.Load(bytes);
        raw = ObjectStoreReplaceRecord(raw, main.Handle, tags =>
        {
            int subclass = tags.FindIndex(tag => tag.Code == 100); int type = SectionPositions(tags, 1, "SectionTypeSettings")[0]; int geometry = SectionPositions(tags, 2, "SectionGeometrySettings")[0];
            int sources = tags.FindIndex(type, tag => tag.Code == 92); int count = tags.FindIndex(type, tag => tag.Code == 93); int source = tags.FindIndex(type, tag => tag.Code == 330); int destination = tags.FindIndex(type, tag => tag.Code == 331);
            int end = SectionPositions(tags, 3, "SectionGeometrySettingsEnd")[0];
            switch (fault)
            {
                case 0: tags[subclass + 2] = new DxfTag(91, -1); break;
                case 1: tags[subclass + 2] = new DxfTag(91, 0); break;
                case 2: tags[sources] = new DxfTag(92, 5); break;
                case 3: tags[source] = new DxfTag(330, "EEEFFFF"); break;
                case 4: tags[destination] = new DxfTag(331, doc.Layers["0"].Handle); break;
                case 5: tags[destination] = new DxfTag(331, "EEEFFFF"); break;
                case 6: tags[count] = new DxfTag(93, 2); break;
                case 7: tags[count] = new DxfTag(93, 4); break;
                case 8: tags.RemoveAt(geometry); break;
                case 9: tags.Insert(geometry, tags[geometry]); break;
                case 10: tags.RemoveAt(end); break;
                case 11: tags[end] = new DxfTag(3, "SectionTypeSettingsEnd"); break;
                case 12: tags.Insert(geometry + 1, tags[geometry + 1]); break;
                case 13: tags.RemoveAt(tags.FindIndex(geometry, tag => tag.Code == 40)); break;
                case 14: tags[tags.FindIndex(geometry, tag => tag.Code == 63)] = new DxfTag(63, (short)-1); break;
                case 15: tags[tags.FindIndex(geometry, tag => tag.Code == 70)] = new DxfTag(70, (short)101); break;
                case 16: tags[tags.FindIndex(geometry, tag => tag.Code == 8)] = new DxfTag(8, @"bad\U+D800"); break;
                case 17: tags.Insert(tags.FindIndex(tag => tag.Code == 5), new DxfTag(5, "123456")); break;
                case 18: tags.RemoveAt(subclass); break;
                case 19: tags.Insert(type, tags[type]); break;
                case 20: { var markers = SectionPositions(tags, 2, "SectionGeometrySettings").Where(i => i + 1 < tags.Count && tags[i + 1].Code == 90).ToList(); tags.RemoveAt(markers[2]); break; }
                case 21: tags[count] = new DxfTag(93, int.MaxValue); break;
                case 22: tags[sources] = new DxfTag(92, DxfSectionSettings.MaximumSourceReferences + 1); break;
                default: tags.Insert(tags.FindLastIndex(subclass - 1, tag => tag.Code == 330), new DxfTag(330, main.Owner.Handle)); break;
            }
            return tags;
        });
        using var changed = new MemoryStream(); raw.Save(changed, binary); changed.Position = 0;
        bool rejected; try { rejected = DxfDocument.Load(changed) == null; } catch (FormatException) { rejected = true; }
        Check(rejected, "malformed section-settings packet was admitted: " + fault);
    }
    private static void SectionSettingsOpaque(bool binary, int variant)
    {
        var doc = SectionSettingsDocument(DxfVersion.AutoCad2018); var main = (DxfSectionSettings)((DxfDictionary)doc.NamedObjects["QA_SECTION_SETTINGS"])["MAIN"];
        using var bytes = new MemoryStream(SectionSettingsSave(doc, binary)); var raw = DxfRawDocument.Load(bytes);
        raw = ObjectStoreReplaceRecord(raw, main.Handle, tags =>
        {
            int first = tags.FindIndex(tag => tag.Code == 100); int xdata = tags.FindIndex(tag => tag.Code == 1001);
            if (variant == 0) tags.Insert(first, new DxfTag(1, "private header"));
            else if (variant == 1) tags.InsertRange(first, new[] { new DxfTag(102, "{PRIVATE"), new DxfTag(70, (short)7), new DxfTag(102, "}") });
            else if (variant == 2) tags[first] = new DxfTag(100, "PrivateSectionSettings");
            else if (variant == 3) tags.InsertRange(xdata, new[] { new DxfTag(100, "PrivateSectionExtension"), new DxfTag(91, 99) });
            else if (variant == 4) tags.Insert(tags.FindIndex(tag => tag.Code == 63) + 1, new DxfTag(420, 0x123456));
            else if (variant == 5) tags.Insert(tags.FindIndex(tag => tag.Code == 63), new DxfTag(62, (short)7));
            else tags.Insert(xdata, new DxfTag(300, "private payload"));
            return tags;
        });
        using var changed = new MemoryStream(); raw.Save(changed, binary); var loaded = SectionSettingsLoad(changed.ToArray());
        var opaque = (DxfOpaqueObject)((DxfDictionary)loaded.NamedObjects["QA_SECTION_SETTINGS"])["MAIN"]; var before = opaque.Tags.Select(tag => tag.Code + ":" + tag.Value).ToArray();
        var target = new DxfDocument(); Throws<NotSupportedException>(() => target.Objects.CloneObject(opaque, target.NamedObjects, "COPY"));
        var again = SectionSettingsLoad(SectionSettingsSave(loaded, binary, $"section-settings-opaque-{variant}-{binary}.dxf"));
        var after = (DxfOpaqueObject)((DxfDictionary)again.NamedObjects["QA_SECTION_SETTINGS"])["MAIN"];
        Check(before.SequenceEqual(after.Tags.Select(tag => tag.Code + ":" + tag.Value)), "whole private settings packet changed");
    }
    private static void SectionSettingsProfile(DxfVersion version, bool binary)
    {
        var doc = SectionSettingsDocument(version); using var output = new MemoryStream();
        bool rejected; try { rejected = !doc.Save(output, binary); } catch (NotSupportedException) { rejected = true; }
        Check(rejected, "unsupported typed settings profile was written"); Equal(0L, output.Length, "unsupported settings profile wrote partial output");
        var supported = new DxfDocument(DxfVersion.AutoCad2018); var parent = new DxfDictionary(); parent.Add("MAIN", new DxfSectionSettings()); supported.NamedObjects.Add("QA_SECTION_SETTINGS", parent);
        using var bytes = new MemoryStream(SectionSettingsSave(supported, binary)); var raw = DxfRawDocument.Load(bytes);
        var tags = raw.Tags.ToList(); int at = tags.FindIndex(tag => tag.Code == 9 && Equals(tag.Value, "$ACADVER")); tags[at + 1] = new DxfTag(1, version == DxfVersion.AutoCad2000 ? "AC1015" : "AC1018"); raw = DxfRawDocument.Create(tags, binary);
        using var altered = new MemoryStream(); raw.Save(altered, binary); var loaded = SectionSettingsLoad(altered.ToArray());
        Check(((DxfDictionary)loaded.NamedObjects["QA_SECTION_SETTINGS"])["MAIN"] is DxfOpaqueObject, "old-profile settings received invented typed support");
    }
    private static void SectionSettingsApi(int scenario)
    {
        var value = SectionAppearance(0, 63);
        if (scenario == 0) { foreach (double bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity }) Throws<ArgumentOutOfRangeException>(() => value.HatchScale = bad); Equal(21.5, value.HatchScale, "rejected scalar changed previous value"); return; }
        if (scenario == 1) { foreach (string bad in new[] { "x\n", "x\0", "\uD800", "\uDC00" }) Throws<ArgumentException>(() => value.LayerName = bad); return; }
        if (scenario == 2) { Throws<ArgumentOutOfRangeException>(() => value.ColorCode = 64); Throws<ArgumentOutOfRangeException>(() => value.ColorIndex = 257); Throws<ArgumentOutOfRangeException>(() => value.EdgeTransparency = -1); return; }
        var doc = SectionSettingsDocument(DxfVersion.AutoCad2018); var main = (DxfSectionSettings)((DxfDictionary)doc.NamedObjects["QA_SECTION_SETTINGS"])["MAIN"]; var previous = main.TypeSettings.ToArray();
        if (scenario == 3) { Throws<ArgumentException>(() => main.SetTypeSettings(new DxfSectionTypeSettings[] { null! })); Equal(4, main.TypeSettings.Count, "null bundle mutated settings"); return; }
        if (scenario == 4) { var foreign = new DxfDocument(); var source = new Line(Vector3.Zero, Vector3.UnitX); foreign.Entities.Add(source); Throws<ArgumentException>(() => main.SetTypeSettings(new[] { new DxfSectionTypeSettings(1, 1, new[] { source }, null!, "", Array.Empty<DxfSectionGeometrySettings>()) })); Equal(4, main.TypeSettings.Count, "foreign source rejection changed settings"); return; }
        if (scenario == 5) { var foreign = new DxfDocument(); var block = foreign.Blocks.Add(new Block("FOREIGN")); Throws<ArgumentException>(() => main.SetTypeSettings(new[] { new DxfSectionTypeSettings(1, 1, Array.Empty<DxfObject>(), block.Record, "", Array.Empty<DxfSectionGeometrySettings>()) })); Equal(4, main.TypeSettings.Count, "foreign destination rejection changed settings"); return; }
        if (scenario == 6) { main.SetTypeSettings(previous.Reverse()); main.SetTypeSettings(main.TypeSettings.Reverse()); SectionSettingsCheck(doc); return; }
        if (scenario == 7) { var copy = value.Clone(); copy.LayerName = "edited"; Check(value.LayerName != copy.LayerName, "geometry clone shared mutable state"); return; }
        if (scenario == 8) { Throws<ArgumentException>(() => new DxfSectionTypeSettings(0, 0, new DxfObject[] { doc }, null!, "", Array.Empty<DxfSectionGeometrySettings>())); return; }
        if (scenario == 10)
        {
            var empty = new DxfSectionTypeSettings(0, 0, Array.Empty<DxfObject>(), null!, "", Array.Empty<DxfSectionGeometrySettings>());
            IEnumerable<DxfSectionTypeSettings> Endless() { while (true) yield return empty; }
            Throws<ArgumentException>(() => main.SetTypeSettings(Endless())); Equal(4, main.TypeSettings.Count, "bounded type rejection changed prior settings"); return;
        }
        if (scenario == 11)
        {
            IEnumerable<DxfSectionGeometrySettings> Endless() { while (true) yield return value; }
            Throws<ArgumentException>(() => new DxfSectionTypeSettings(0, 0, Array.Empty<DxfObject>(), null!, "", Endless())); return;
        }
        if (scenario == 12)
        {
            IEnumerable<DxfObject> Endless() { while (true) yield return null!; }
            Throws<ArgumentException>(() => new DxfSectionTypeSettings(0, 0, Endless(), null!, "", Array.Empty<DxfSectionGeometrySettings>())); return;
        }
        var parent = (DxfDictionary)doc.NamedObjects["QA_SECTION_SETTINGS"]; doc.Objects.EraseOwnedTree(parent); Throws<InvalidOperationException>(() => main.SetTypeSettings(Array.Empty<DxfSectionTypeSettings>()));
    }
    private static void SectionSettingsNative(bool binary)
    {
        const string folder = "tests/fixtures/section"; using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "manifest.json")));
        byte[] packed = File.ReadAllBytes(Path.Combine(folder, "LiveSection1.dxf.gz"));
        Equal(manifest.RootElement.GetProperty("gzip_sha256").GetString(), Convert.ToHexString(SHA256.HashData(packed)).ToLowerInvariant(), "native fixture gzip hash");
        using var source = new MemoryStream(packed); using var gzip = new GZipStream(source, CompressionMode.Decompress); using var original = new MemoryStream(); gzip.CopyTo(original);
        byte[] bytes = original.ToArray(); Equal(manifest.RootElement.GetProperty("source_sha256").GetString(), Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), "native source bytes hash");
        var doc = SectionSettingsLoad(bytes); var settings = doc.Objects.Items.OfType<DxfSectionSettings>().Single();
        Equal("SECTION_SETTINGS", settings.CodeName, "native settings wire name"); Equal(4, settings.SectionType, "native outer type"); Equal(1, settings.TypeSettings.Count, "native type count");
        var type = settings.TypeSettings[0]; Equal(17, type.GenerationOptions, "native flags17"); Equal(0, type.SourceObjects.Count, "native zero source count"); Check(type.DestinationBlock == null && type.DestinationFileName == "", "native empty destinations");
        Check(type.RepeatGeometryMarkers, "native repeated geometry markers"); Equal(4, type.GeometrySettings.Count, "native geometry count");
        Check(type.GeometrySettings.Select(value => value.GeometryValue).SequenceEqual(new[] { 1, 2, 4, 8 }), "native geometry91 sequence changed");
        Check(type.GeometrySettings.All(value => value.ColorCode == 62), "native color spelling changed");
        Check(type.GeometrySettings.Select(value => value.ColorIndex).SequenceEqual(new short[] { 1, 2, 256, 3 }), "native ACI values changed");
        Equal("*_BackgroundLines", type.GeometrySettings[2].LayerName, "native unresolved reserved layer");
        Check(settings.Owner is Section && ReferenceEquals(((Section)settings.Owner).GeometrySettings, settings), "native owner relationship changed");
        byte[] saved = SectionSettingsSave(doc, binary, $"section-settings-native-AutoCad2018-{binary}.dxf");
        DxfRawRecord Packet(byte[] data) { using var stream = new MemoryStream(data); return DxfRawDocument.Load(stream).Sections.SelectMany(section => section.Records).Single(record => record.Name == "SECTION_SETTINGS"); }
        var before = Packet(bytes).Tags; var after = Packet(saved).Tags;
        Equal(before.Count, after.Count, "native complete settings packet length");
        for (int i = 0; i < before.Count; i++) { Equal(before[i].Code, after[i].Code, "native complete packet code"); Equal(before[i].Value, after[i].Value, "native complete packet value"); }
        Equal(17, SectionSettingsLoad(saved).Objects.Items.OfType<DxfSectionSettings>().Single().TypeSettings[0].GenerationOptions, "native reloaded generation flags");
    }
    private static void SectionSettingsProducer(DxfVersion version, bool sourceBinary, bool binary)
    {
        const string folder = "tests/fixtures/section-settings";
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "manifest.json")));
        string name = $"ixmilia-section-settings-R{version.ToString().Replace("AutoCad", "")}-{(sourceBinary ? "binary" : "ascii")}.dxf.gz";
        var fixture = manifest.RootElement.GetProperty("fixtures").EnumerateArray().Single(item => item.GetProperty("file").GetString() == name);
        byte[] original = File.ReadAllBytes(Path.Combine(folder, name));
        Equal(fixture.GetProperty("gzip_sha256").GetString(), Convert.ToHexString(SHA256.HashData(original)).ToLowerInvariant(), "producer archive hash");
        byte[] input = File.ReadAllBytes(Path.Combine(folder, fixture.GetProperty("extracted_file").GetString()!));
        Equal(fixture.GetProperty("extracted_sha256").GetString(), Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant(), "exact producer extraction hash");
        var doc = SectionSettingsLoad(input); var settings = (DxfSectionSettings)doc.NamedObjects["QA_SECTION_SETTINGS_PRODUCER"];
        Equal(4, settings.SectionType, "producer outer type"); Equal(2, settings.TypeSettings.Count, "producer type count");
        var first = settings.TypeSettings[0]; Check(!first.RepeatGeometryMarkers && !settings.TypeSettings[1].RepeatGeometryMarkers, "producer sequence markers were rewritten");
        Equal(1, first.GenerationOptions, "producer Boolean flag value"); Equal(3, first.GeometrySettings.Count, "producer geometry count");
        Check(first.GeometrySettings.All(value => value.ColorCode == 63), "producer documented color code");
        Equal("*_BackgroundLines", first.GeometrySettings[0].LayerName, "producer unresolved reserved layer");
        byte[] output = SectionSettingsSave(doc, binary, $"section-settings-producer-{version}-{sourceBinary}-{binary}.dxf");
        DxfRawRecord Packet(byte[] data) { using var stream = new MemoryStream(data); return DxfRawDocument.Load(stream).Sections.SelectMany(section => section.Records).Single(record => record.Name == "SECTIONSETTINGS"); }
        var before = Packet(input).Tags; var after = Packet(output).Tags; Equal(before.Count, after.Count, "producer exact packet length");
        for (int i = 0; i < before.Count; i++) { Equal(before[i].Code, after[i].Code, "producer exact packet code"); Equal(before[i].Value, after[i].Value, "producer exact packet value"); }
        Equal(3, ((DxfSectionSettings)SectionSettingsLoad(output).NamedObjects["QA_SECTION_SETTINGS_PRODUCER"]).TypeSettings[0].GeometrySettings.Count, "producer reloaded geometry count");
    }
}
