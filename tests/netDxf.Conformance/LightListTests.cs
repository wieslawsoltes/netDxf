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
    private static readonly DxfVersion[] LightListVersions = { DxfVersion.AutoCad2007, DxfVersion.AutoCad2010, DxfVersion.AutoCad2013, DxfVersion.AutoCad2018 };
    private static void RunLightListTests()
    {
        foreach (var version in LightListVersions)
            foreach (bool binary in new[] { false, true })
            {
                Run($"lightlist/authored/{version}/{binary}", () => LightListAuthored(version, binary));
                Run($"lightlist/mapped-clone/{version}/{binary}", () => LightListClone(version, binary));
                foreach (bool sourceBinary in new[] { false, true })
                    Run($"lightlist/independent/{version}/{sourceBinary}/{binary}", () => LightListIndependent(version, sourceBinary, binary));
                foreach (string defect in new[] { "missing-version", "missing-count", "negative-count", "huge-count", "wrong-count", "extra-version", "missing-handle", "missing-name", "pair-order", "duplicate-marker", "wrong-target", "missing-target", "null-target", "late-pair", "invalid-name" })
                    Run($"lightlist/malformed/{version}/{binary}/{defect}", () => LightListMalformed(version, binary, defect));
                foreach (string extension in new[] { "private-field", "private-subclass", "private-marker" })
                    Run($"lightlist/opaque/{version}/{binary}/{extension}", () => LightListOpaque(version, binary, extension));
                Run($"lightlist/class-contract/{version}/{binary}", () => LightListClassContract(version, binary));
            }
        Run("lightlist/entry-validation-and-atomicity", LightListEntryValidation);
        foreach (var version in new[] { DxfVersion.AutoCad2000, DxfVersion.AutoCad2004 })
            foreach (bool binary in new[] { false, true })
                Run($"lightlist/older-profile/{version}/{binary}", () => LightListOlderProfile(version, binary));
    }
    private static DxfDocument LightListDocument(DxfVersion version)
    {
        var doc = new DxfDocument(version);
        var a = new Light { Name = "Actual A", Position = new Vector3(1.25, -2.5, 4), Target = new Vector3(5, 7, -1), LightType = LightType.Point };
        var b = new Light { Name = "Actual B", Position = new Vector3(-8, 9, 2), Target = new Vector3(2, 4, 6), LightType = LightType.Spot };
        doc.Entities.Add(a); doc.Entities.Add(b);
        doc.Entities.Add(new Line(new Vector3(21, 22, 23), new Vector3(31, 32, 33)));
        var dictionary = new DxfDictionary();
        var list = new DxfLightList(42);
        list.Entries.Add(new DxfLightListEntry(a, "Stored alias 青"));
        list.Entries.Add(new DxfLightListEntry(b, ""));
        list.Entries.Add(new DxfLightListEntry(a, "Literal\\U+0041 🧪"));
        dictionary.Add("MAIN", list, true); dictionary.Add("ALIAS", list, false);
        dictionary.Add("EMPTY", new DxfLightList(int.MinValue), true);
        doc.NamedObjects.Add("QA_LIGHTLISTS", dictionary, true);
        list.PersistentReactors.Add(dictionary);
        var ext = new DxfDictionary(); var payload = new DxfXRecord();
        payload.Data.Add(new DxfTag(1, "extension payload")); payload.Data.Add(new DxfTag(90, 1701)); payload.Data.Add(new DxfTag(310, new byte[] { 0, 1, 255, 0 }));
        ext.Add("PAYLOAD", payload, true); doc.Objects.SetExtensionDictionary(list, ext);
        var data = new XData(new ApplicationRegistry("QA_LIGHTLIST")); data.XDataRecord.Add(new XDataRecord(XDataCode.String, "list metadata")); data.XDataRecord.Add(new XDataRecord(XDataCode.DatabaseHandle, a.Handle)); list.XData.Add(data);
        return doc;
    }
    private static void LightListAssert(DxfDocument doc, string? expectedHandle = null)
    {
        var dictionary = (DxfDictionary)doc.NamedObjects["QA_LIGHTLISTS"];
        var list = (DxfLightList)dictionary["MAIN"];
        Equal(42, list.StoredVersion, "explicit raw version"); Equal(3, list.Entries.Count, "entry count");
        if (expectedHandle != null) Equal(expectedHandle, list.Handle, "object identity differs from repeated payload handles");
        Check(ReferenceEquals(list, dictionary["ALIAS"]), "dictionary alias identity");
        Equal(int.MinValue, ((DxfLightList)dictionary["EMPTY"]).StoredVersion, "signed version retained"); Equal(0, ((DxfLightList)dictionary["EMPTY"]).Entries.Count, "empty list count");
        Equal("Stored alias 青", list.Entries[0].Name, "stored name independent of Light.Name"); Equal("", list.Entries[1].Name, "empty name"); Equal("Literal\\U+0041 🧪", list.Entries[2].Name, "literal escape and supplementary Unicode");
        Check(ReferenceEquals(list.Entries[0].Light, list.Entries[2].Light), "duplicate target identity");
        Check(list.Entries.All(e => ReferenceEquals(doc.GetObjectByHandle(e.Light.Handle), e.Light)), "canonical registered LIGHT identities");
        Equal("Actual A", list.Entries[0].Light.Name, "referenced entity independent name"); Equal("Actual B", list.Entries[1].Light.Name, "second referenced entity name");
        Check(ReferenceEquals(list.Owner, dictionary) && ReferenceEquals(list.PersistentReactors.Single(), dictionary), "owner/reactor graph");
        Check(list.ExtensionDictionary != null && ReferenceEquals(list.ExtensionDictionary.Owner, list), "extension owner graph");
        var payload = (DxfXRecord)list.ExtensionDictionary!["PAYLOAD"]; Equal(1701, (int)payload.Data[1].Value, "extension payload");
        Equal(list.Entries[0].Light.Handle, (string)list.XData["QA_LIGHTLIST"].XDataRecord[1].Value, "XData target identity");
        Equal(0, doc.Objects.Validate().Count, "valid database graph");
    }
    private static void LightListAuthored(DxfVersion version, bool binary)
    {
        var doc = LightListDocument(version); var list = (DxfLightList)((DxfDictionary)doc.NamedObjects["QA_LIGHTLISTS"])["MAIN"]; string handle = list.Handle;
        for (int cycle = 0; cycle < 3; cycle++)
        {
            using var stream = new MemoryStream(); Check(doc.Save(stream, cycle == 1 ? !binary : binary), "LIGHTLIST save");
            if (cycle == 2) File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"lightlist-{version}-{binary}.dxf"), stream.ToArray());
            stream.Position = 0; doc = DxfDocument.Load(stream) ?? throw new Exception("LIGHTLIST reload failed."); LightListAssert(doc, handle);
        }
    }
    private static void LightListIndependent(DxfVersion version, bool sourceBinary, bool binary)
    {
        string year = version.ToString().Substring(7), transport = sourceBinary ? "binary" : "ascii";
        string directory = Path.Combine("tests", "fixtures", "lightlist"), stem = $"independent-lightlist-R{year}-{transport}";
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "manifest.json")));
        var fixtures = manifest.RootElement.GetProperty("fixtures"); Equal(8, fixtures.GetArrayLength(), "mandatory independent fixture inventory");
        var expected = fixtures.EnumerateArray().Single(f => f.GetProperty("file").GetString() == stem + ".dxf");
        var doc = DxfDocument.Load(Path.Combine(directory, stem + ".dxf")) ?? throw new Exception("Independent LIGHTLIST load failed.");
        for (int cycle = 0; cycle < 2; cycle++)
        {
            var dictionary = (DxfDictionary)doc.NamedObjects["QA_LIGHTLISTS"]; Equal(5, dictionary.Entries.Count, "independent list inventory");
            foreach (var record in expected.GetProperty("lists").EnumerateArray())
            {
                var list = doc.GetObjectByHandle(record.GetProperty("handle").GetString()!) as DxfLightList ?? throw new Exception("Independent LIGHTLIST was not typed.");
                Equal(record.GetProperty("stored_version").GetInt32(), list.StoredVersion, "independent signed version");
                var entries = record.GetProperty("entries"); Equal(entries.GetArrayLength(), list.Entries.Count, "independent count");
                for (int i = 0; i < entries.GetArrayLength(); i++)
                { Equal(entries[i].GetProperty("light").GetString()!, list.Entries[i].Light.Handle, "independent actual LIGHT reference"); Equal(entries[i].GetProperty("name").GetString()!, list.Entries[i].Name, "independent exact stored name"); }
                Check(ReferenceEquals(list.Owner, dictionary) && ReferenceEquals(list.PersistentReactors.Single(), dictionary), "independent owner/reactor");
                Equal("1D", (string)list.XData["QA_LIGHTLIST"].XDataRecord[1].Value, "independent XData");
                if (record.GetProperty("extension").ValueKind != JsonValueKind.Null) Equal("500", list.ExtensionDictionary.Handle, "independent extension");
            }
            Equal(0, doc.Objects.Validate().Count, "independent graph validity");
            using var stream = new MemoryStream(); Check(doc.Save(stream, binary), "independent LIGHTLIST save");
            if (cycle == 0) File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"{stem}-roundtrip-{(binary ? "binary" : "ascii")}.dxf"), stream.ToArray());
            stream.Position = 0; doc = DxfDocument.Load(stream) ?? throw new Exception("Independent LIGHTLIST reload failed.");
        }
    }
    private static void LightListClone(DxfVersion version, bool binary)
    {
        var source = LightListDocument(version); var dictionary = (DxfDictionary)source.NamedObjects["QA_LIGHTLISTS"];
        var target = new DxfDocument(version); for (int i = 0; i < 40; i++) target.Layers.Add(new Layer("Offset" + i));
        var map = new Dictionary<DxfObject, DxfObject>();
        foreach (Light light in source.Entities.Lights) { var copy = (Light)light.Clone(); target.Entities.Add(copy); map.Add(light, copy); Check(light.Handle != copy.Handle, "mapped LIGHT handles actually differ"); }
        target.Entities.Add((Line)source.Entities.Lines.Single().Clone());
        int before = target.Objects.Items.Count; string seed = target.DrawingVariables.HandleSeed;
        Throws<InvalidOperationException>(() => target.Objects.Clone(dictionary, target.NamedObjects, "QA_LIGHTLISTS")); Equal(before, target.Objects.Items.Count, "missing mapping inventory atomicity"); Equal(seed, target.DrawingVariables.HandleSeed, "missing mapping seed atomicity");
        var wrong = new Dictionary<DxfObject, DxfObject>(map) { [source.Entities.Lights.First()] = target.Entities.Lines.Single() };
        Throws<ArgumentException>(() => target.Objects.Clone(dictionary, target.NamedObjects, "QA_LIGHTLISTS", wrong)); Equal(before, target.Objects.Items.Count, "wrong type mapping inventory atomicity"); Equal(seed, target.DrawingVariables.HandleSeed, "wrong type mapping seed atomicity");
        target.Objects.Clone(dictionary, target.NamedObjects, "QA_LIGHTLISTS", map); LightListAssert(target);
        var cloned = (DxfLightList)((DxfDictionary)target.NamedObjects["QA_LIGHTLISTS"])["MAIN"];
        Check(cloned.Handle != dictionary["MAIN"].Handle, "mapped list identity differs");
        using var stream = new MemoryStream(); Check(target.Save(stream, binary), "cloned LIGHTLIST save"); File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"lightlist-clone-{version}-{binary}.dxf"), stream.ToArray());
        stream.Position = 0; LightListAssert(DxfDocument.Load(stream) ?? throw new Exception("Cloned reload failed."));
        var local = source.Objects.Clone(dictionary, source.NamedObjects, "LOCAL");
        Check(ReferenceEquals(((DxfLightList)local["MAIN"]).Entries[0].Light, source.Entities.Lights.First()), "same-document clone keeps exact existing LIGHT");
        ((DxfLightList)local["MAIN"]).Entries.Clear(); Equal(3, ((DxfLightList)dictionary["MAIN"]).Entries.Count, "clone collection independence");
    }
    private static DxfRawDocument LightListRaw(DxfVersion version, out string handle)
    {
        var doc = LightListDocument(version); handle = ((DxfDictionary)doc.NamedObjects["QA_LIGHTLISTS"])["MAIN"].Handle;
        using var stream = new MemoryStream(); Check(doc.Save(stream, true), "raw LIGHTLIST source"); stream.Position = 0; return DxfRawDocument.Load(stream);
    }
    private static void LightListMalformed(DxfVersion version, bool binary, string defect)
    {
        var raw = LightListRaw(version, out string handle);
        raw = ObjectStoreReplaceRecord(raw, handle, tags =>
        {
            int marker = tags.FindIndex(t => t.Code == 100 && Equals(t.Value, "AcDbLightList")), pair = marker + 3;
            switch (defect)
            {
                case "missing-version": tags.RemoveAt(marker + 1); break;
                case "missing-count": tags.RemoveAt(marker + 2); break;
                case "negative-count": tags[marker + 2] = new DxfTag(90, -1); break;
                case "huge-count": tags[marker + 2] = new DxfTag(90, int.MaxValue); break;
                case "wrong-count": tags[marker + 2] = new DxfTag(90, 2); break;
                case "extra-version": tags.Insert(marker + 2, new DxfTag(90, 7)); break;
                case "missing-handle": tags.RemoveAt(pair); break;
                case "missing-name": tags.RemoveAt(pair + 1); break;
                case "pair-order": (tags[pair], tags[pair + 1]) = (tags[pair + 1], tags[pair]); break;
                case "duplicate-marker": tags.Insert(marker + 1, new DxfTag(100, "AcDbLightList")); break;
                case "wrong-target": tags[pair] = new DxfTag(5, handle); break;
                case "missing-target": tags[pair] = new DxfTag(5, "FFABC"); break;
                case "null-target": tags[pair] = new DxfTag(5, "0"); break;
                case "late-pair": tags.Add(new DxfTag(5, "FFABC")); tags.Add(new DxfTag(1, "late")); break;
                case "invalid-name": tags[pair + 1] = new DxfTag(1, "encoded\\U+000Aname"); break;
            }
            return tags;
        });
        using var stream = new MemoryStream(); raw.Save(stream, binary); stream.Position = 0; bool rejected;
        try { rejected = DxfDocument.Load(stream) == null; } catch (FormatException) { rejected = true; }
        Check(rejected, "Recognized malformed LIGHTLIST accepted: " + defect);
    }
    private static void LightListOpaque(DxfVersion version, bool binary, string extension)
    {
        var raw = LightListRaw(version, out string handle);
        raw = ObjectStoreReplaceRecord(raw, handle, tags =>
        {
            int marker = tags.FindIndex(t => t.Code == 100 && Equals(t.Value, "AcDbLightList"));
            if (extension == "private-marker") tags[marker] = new DxfTag(100, "PrivateLightList");
            else tags.Insert(tags.FindIndex(t => t.Code == 1001), extension == "private-field" ? new DxfTag(300, "private data") : new DxfTag(100, "PrivateExtension"));
            return tags;
        });
        using var stream = new MemoryStream(); raw.Save(stream, binary); stream.Position = 0;
        var doc = DxfDocument.Load(stream) ?? throw new Exception("Opaque LIGHTLIST load failed.");
        var opaque = doc.GetObjectByHandle(handle) as DxfOpaqueObject ?? throw new Exception("Private LIGHTLIST was promoted."); var tagsBefore = opaque.Tags.ToArray();
        using var saved = new MemoryStream(); Check(doc.Save(saved, !binary), "opaque LIGHTLIST save"); saved.Position = 0;
        var after = (DxfOpaqueObject)(DxfDocument.Load(saved) ?? throw new Exception("Opaque reload failed.")).GetObjectByHandle(handle);
        Equal(tagsBefore.Length, after.Tags.Count, "opaque full payload length including terminal XData");
        for (int i = 0; i < tagsBefore.Length; i++) { Equal(tagsBefore[i].Code, after.Tags[i].Code, "opaque code"); Equal(tagsBefore[i].Value, after.Tags[i].Value, "opaque value"); }
    }
    private static void LightListEntryValidation()
    {
        var light = new Light(); var list = new DxfLightList(int.MinValue); list.StoredVersion = int.MaxValue;
        Throws<ArgumentNullException>(() => new DxfLightListEntry(null!, "name")); Throws<ArgumentNullException>(() => new DxfLightListEntry(light, null!));
        foreach (string name in new[] { "a\0b", "a\rb", "a\nb", "\uD800", "\uDC00" }) Throws<ArgumentException>(() => new DxfLightListEntry(light, name));
        Throws<ArgumentNullException>(() => list.Entries.Add(null!));
        list.Entries.Add(new DxfLightListEntry(light, "")); var doc = new DxfDocument(DxfVersion.AutoCad2018); int count = doc.Objects.Items.Count; string seed = doc.DrawingVariables.HandleSeed;
        Throws<ArgumentException>(() => doc.NamedObjects.Add("INVALID", list)); Equal(count, doc.Objects.Items.Count, "unregistered target did not allocate"); Equal(seed, doc.DrawingVariables.HandleSeed, "unregistered target seed");
        doc.Entities.Add(light); doc.NamedObjects.Add("VALID", list);
        var foreign = new DxfDocument(DxfVersion.AutoCad2018); var other = new Light(); foreign.Entities.Add(other);
        Throws<ArgumentException>(() => list.Entries.Add(new DxfLightListEntry(other, "foreign"))); Throws<ArgumentException>(() => list.Entries[0] = new DxfLightListEntry(other, "foreign"));
        Equal(1, list.Entries.Count, "failed insert count"); Check(ReferenceEquals(light, list.Entries[0].Light), "failed replace target");
        light.Name = "renamed actual light"; Equal("", list.Entries[0].Name, "rename must not overwrite independently stored name");
    }
    private static void LightListClassContract(DxfVersion version, bool binary)
    {
        var doc = LightListDocument(version); doc.Classes.Add(new DxfClass("LIGHTLIST", "AcDbLightList", "Independent Application") { ProxyFlags = 17, InstanceCount = 987, IsEntity = false });
        using var stream = new MemoryStream(); Check(doc.Save(stream, binary), "compatible class save"); stream.Position = 0; var loaded = DxfDocument.Load(stream) ?? throw new Exception("class reload");
        Equal("Independent Application", loaded.Classes["LIGHTLIST"].ApplicationName, "existing class app retained"); Equal(17, loaded.Classes["LIGHTLIST"].ProxyFlags, "existing class flags retained"); Equal(2, loaded.Classes["LIGHTLIST"].InstanceCount, "actual instance count");
        var bad = LightListDocument(version); bad.Classes.Add(new DxfClass("LIGHTLIST", "PrivateClass", "PrivateApp"));
        using var rejectedStream = new MemoryStream(); bool rejected; try { rejected = !bad.Save(rejectedStream, binary); } catch (InvalidDataException) { rejected = true; }
        Check(rejected && rejectedStream.Length == 0, "conflicting typed class wrote bytes");
        var emptied = new DxfDocument(version); emptied.Classes.Add(new DxfClass("LIGHTLIST", "AcDbLightList", "SCENEOE") { InstanceCount = 1, ProxyFlags = 1025 });
        using var emptyStream = new MemoryStream(); Check(emptied.Save(emptyStream, binary), "class after last instance removed"); emptyStream.Position = 0;
        Equal(0, DxfDocument.Load(emptyStream)!.Classes["LIGHTLIST"].InstanceCount, "compatible class count must reach zero");
        var plain = new DxfDocument(version); plain.Classes.Add(new DxfClass("LIGHTLIST", "PrivateClass", "PrivateApp") { InstanceCount = 17, ProxyFlags = 7 });
        using var preserved = new MemoryStream(); Check(plain.Save(preserved, binary), "no typed instances preserve unrelated CLASS"); preserved.Position = 0; var restored = DxfDocument.Load(preserved)!;
        Equal("PrivateClass", restored.Classes["LIGHTLIST"].CppClassName, "no typed class enforcement"); Equal(17, restored.Classes["LIGHTLIST"].InstanceCount, "no typed count rewrite");
    }
    private static void LightListOlderProfile(DxfVersion version, bool binary)
    {
        var doc = new DxfDocument(version); doc.NamedObjects.Add("EMPTY_LIGHTLIST", new DxfLightList(-7));
        using var stream = new MemoryStream(); bool rejected; try { rejected = !doc.Save(stream, binary); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected && stream.Length == 0, "unsupported typed profile emitted output");
        var modern = new DxfDocument(DxfVersion.AutoCad2007); modern.NamedObjects.Add("EMPTY_LIGHTLIST", new DxfLightList(-7));
        using var original = new MemoryStream(); Check(modern.Save(original, false), "modern empty source"); original.Position = 0;
        var raw = DxfRawDocument.Load(original); var tags = raw.Tags.ToList(); int index = tags.FindIndex(t => t.Code == 9 && Equals(t.Value, "$ACADVER")); tags[index + 1] = new DxfTag(1, version == DxfVersion.AutoCad2000 ? "AC1015" : "AC1018");
        // Raw WithTags intentionally forbids version changes; re-author this low-level transport
        // fixture through the existing plain-tag helper instead of mutating a raw snapshot.
        using var oldStream = new MemoryStream();
        using (var writer = new StreamWriter(oldStream, new System.Text.UTF8Encoding(false), 1024, true))
            foreach (var tag in tags) { writer.WriteLine(tag.Code); writer.WriteLine(tag.Value is bool flag ? (flag ? "1" : "0") : Convert.ToString(tag.Value, System.Globalization.CultureInfo.InvariantCulture)); }
        oldStream.Position = 0; var older = DxfDocument.Load(oldStream) ?? throw new Exception("older opaque load");
        Check(older.NamedObjects["EMPTY_LIGHTLIST"] is DxfOpaqueObject, "older LIGHTLIST must remain opaque");
        using var saved = new MemoryStream(); Check(older.Save(saved, binary), "older opaque save");
    }
}
