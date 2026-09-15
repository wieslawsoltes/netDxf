using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterSectionManagerTests()
    {
        foreach (bool input in new[] { false, true }) foreach (bool output in new[] { false, true })
            Run($"section-manager/native/{input}/{output}", () => SectionManagerNative(input, output));
        foreach (DxfVersion version in SupportedVersions.Where(v => v >= DxfVersion.AutoCad2007))
        foreach (string spelling in new[] { "SECTION_MANAGER", "SECTIONMANAGER" })
        foreach (bool binary in new[] { false, true }) foreach (int count in new[] { 0, 3 })
            Run($"section-manager/schema/{version}/{spelling}/{binary}/{count}", () => SectionManagerSchema(version, spelling, binary, count));
        foreach (bool binary in new[] { false, true })
        {
            foreach (int fault in Enumerable.Range(0, 17)) Run($"section-manager/malformed/{binary}/{fault}", () => SectionManagerMalformed(binary, fault));
            foreach (int variant in Enumerable.Range(0, 8)) Run($"section-manager/opaque/{binary}/{variant}", () => SectionManagerOpaque(binary, variant));
            foreach (int operation in Enumerable.Range(0, 10)) Run($"section-manager/lifecycle/{binary}/{operation}", () => SectionManagerLifecycle(binary, operation));
            Run($"section-manager/numeric-identities/{binary}", () => SectionManagerNumericIdentities(binary));
            Run($"section-manager/xdata/{binary}", () => SectionManagerXData(binary));
        }
    }

    private static byte[] SectionManagerNativeBytes()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText("tests/fixtures/section/manifest.json"));
        using var file = File.OpenRead("tests/fixtures/section/LiveSection1.dxf.gz");
        using var gzip = new GZipStream(file, CompressionMode.Decompress); using var bytes = new MemoryStream(); gzip.CopyTo(bytes);
        Equal(manifest.RootElement.GetProperty("source_sha256").GetString(), Convert.ToHexString(SHA256.HashData(bytes.ToArray())).ToLowerInvariant(), "Pinned native section source hash");
        return bytes.ToArray();
    }
    private static DxfRawRecord SectionManagerRecord(DxfRawDocument raw) => raw.Sections.Single(s => s.Name == "OBJECTS").Records.Single(r => r.Name is "SECTION_MANAGER" or "SECTIONMANAGER");
    private static DxfDocument SectionManagerLoad(DxfRawDocument raw, bool binary)
    {
        using var bytes = new MemoryStream(); raw.WithTags(raw.Tags.Where(t => t.Code != 999)).Save(bytes, binary); bytes.Position = 0;
        return DxfDocument.Load(bytes) ?? throw new FormatException("Section-manager input was rejected");
    }
    private static DxfDocument SectionManagerSave(DxfDocument doc, bool binary, string? name = null)
    {
        using var bytes = new MemoryStream(); Check(doc.Save(bytes, binary), "Section-manager save rejected");
        if (name != null) File.WriteAllBytes(Path.Combine(ArtifactDirectory, name), bytes.ToArray());
        bytes.Position = 0; return DxfDocument.Load(bytes) ?? throw new FormatException("Section-manager reload was rejected");
    }
    private static DxfRawDocument SectionManagerFixture(DxfVersion version, string spelling = "SECTION_MANAGER", int count = 3)
    {
        var doc = new DxfDocument(version); var pointer = new DxfXRecord(); doc.Objects.Root.Add("ACAD_SECTION_MANAGER", pointer, false);
        var targets = new List<string>();
        if (version >= DxfVersion.AutoCad2007)
        {
            var first = SectionExample("SECTIONOBJECT"); first.Name = "Manager first";
            var second = SectionExample("SECTIONOBJECT"); second.Name = "Manager second";
            doc.Entities.Add(first); doc.Entities.Add(second); targets.AddRange(new[] { first.Handle, second.Handle, first.Handle });
        }
        else count = 0;
        var raw = DxfRawDocument.Load(new MemoryStream(SectionBytes(doc, false)));
        var record = raw.Sections.Single(s => s.Name == "OBJECTS").Records.Single(r => r.Tags.Any(t => t.Code == 5 && (string)t.Value == pointer.Handle));
        var header = record.Tags.TakeWhile(t => t.Code != 100).Select(t => t.Code == 0 ? new DxfTag(0, spelling) : t);
        return raw.WithRecord(record, header.Concat(new[] { new DxfTag(100, "AcDbSectionManager"), new DxfTag(70, (short)(count == 0 ? 0 : 1)), new DxfTag(90, count) }).Concat(targets.Take(count).Select(h => new DxfTag(330, h))));
    }
    private static void SectionManagerNative(bool input, bool binary)
    {
        byte[] original = SectionManagerNativeBytes(); var raw = DxfRawDocument.Load(new MemoryStream(original));
        var doc = input ? SectionManagerLoad(raw, true) : DxfDocument.Load(new MemoryStream(original)) ?? throw new FormatException("Unchanged native section-manager input rejected");
        var manager = (DxfStoredSectionManager)doc.GetObjectByHandle("229"); var section = (Section)doc.GetObjectByHandle("228");
        Equal("SECTION_MANAGER", manager.CodeName, "Native manager spelling"); Check(!manager.RequiresFullUpdate, "Native stored update flag");
        Check(manager.Sections.Count == 1 && ReferenceEquals(manager.Sections[0], section), "Native exact section identity");
        Check(ReferenceEquals(manager.Owner, doc.Objects.Root) && ReferenceEquals(doc.Objects.Root["ACAD_SECTION_MANAGER"], manager), "Native reciprocal root entry");
        var expected = OwnershipTagValues(SectionManagerRecord(raw).Tags.SkipWhile(t => t.Code != 100)).ToArray();
        Check(expected.SequenceEqual(OwnershipTagValues(manager.Tags)), "Native manager payload changed on load");
        var loaded = SectionManagerSave(doc, binary, $"section-manager-native-{input}-{binary}.dxf");
        var again = (DxfStoredSectionManager)loaded.GetObjectByHandle("229");
        Check(expected.SequenceEqual(OwnershipTagValues(again.Tags)) && ReferenceEquals(again.Sections.Single(), loaded.GetObjectByHandle("228")), "Native manager packet or reference changed on reload");
        Equal(0, loaded.Objects.Validate().Count, "Native manager graph validation");
    }
    private static void SectionManagerSchema(DxfVersion version, string spelling, bool binary, int count)
    {
        var doc = SectionManagerLoad(SectionManagerFixture(version, spelling, count), binary);
        var manager = doc.Objects.Items.OfType<DxfStoredSectionManager>().Single();
        Equal(count, manager.Sections.Count, "Ordered manager pointer count"); Equal(count != 0, manager.RequiresFullUpdate, "Stored manager update flag");
        Equal(version, manager.SourceVersion, "Stored manager source version"); Equal(spelling, manager.CodeName, "Stored manager spelling");
        if (count != 0) Check(ReferenceEquals(manager.Sections[0], manager.Sections[2]) && manager.Sections[0].Name == "Manager first" && manager.Sections[1].Name == "Manager second", "Repeated manager targets were collapsed or rebound");
        Check(typeof(DxfStoredSectionManager).GetConstructors().Length == 0, "Manager exposes an authored constructor");
        Throws<NotSupportedException>(() => ((IList<Section>)manager.Sections).Clear());
        Throws<NotSupportedException>(() => ((IList<DxfTag>)manager.Tags).Clear());
        var before = OwnershipTagValues(manager.Tags).ToArray();
        var loaded = SectionManagerSave(doc, binary, $"section-manager-schema-{version}-{spelling}-{count}-{binary}.dxf");
        var again = loaded.Objects.Items.OfType<DxfStoredSectionManager>().Single();
        Check(before.SequenceEqual(OwnershipTagValues(again.Tags)), "Stored schema packet changed");
        Equal(count, again.Sections.Count, "Reloaded manager pointer count");
    }
    private static void SectionManagerMalformed(bool binary, int fault)
    {
        var raw = SectionManagerFixture(DxfVersion.AutoCad2018); var record = SectionManagerRecord(raw); var tags = record.Tags.ToList(); int start = tags.FindIndex(t => t.Code == 100);
        if (fault == 0) tags.RemoveAt(start);
        else if (fault == 1) tags.Insert(start, new DxfTag(100, "AcDbSectionManager"));
        else if (fault == 2) tags.RemoveAt(start + 1);
        else if (fault == 3) tags.Insert(start + 1, new DxfTag(70, (short)0));
        else if (fault == 4) tags.RemoveAt(start + 2);
        else if (fault == 5) tags.Insert(start + 2, new DxfTag(90, 3));
        else if (fault == 6) tags[start + 2] = new DxfTag(90, -1);
        else if (fault == 7) tags[start + 2] = new DxfTag(90, 65537);
        else if (fault == 8) tags[start + 2] = new DxfTag(90, 2);
        else if (fault == 9) tags[start + 2] = new DxfTag(90, 4);
        else if (fault == 10) tags[^1] = new DxfTag(330, "0000");
        else if (fault == 11) tags[^1] = new DxfTag(330, (string)tags.Take(start).Last(t => t.Code == 330).Value);
        else if (fault == 12) tags[^1] = new DxfTag(330, "FFFFFFFF");
        else if (fault == 13) tags.Insert(start + 1, new DxfTag(1000, "stray public data"));
        else if (fault == 14) tags[tags.FindIndex(t => t.Code == 330)] = new DxfTag(330, (string)tags.First(t => t.Code == 5).Value);
        else if (fault == 15) tags.Insert(start, new DxfTag(5, "FFFF"));
        else
        {
            var root = raw.Sections.Single(s => s.Name == "OBJECTS").Records.First(r => r.Name == "DICTIONARY");
            raw = raw.WithRecord(root, root.Tags.Select(t => t.Code == 3 && (string)t.Value == "ACAD_SECTION_MANAGER" ? new DxfTag(3, "PRIVATE_MANAGER") : t));
            record = SectionManagerRecord(raw);
        }
        raw = raw.WithRecord(record, tags);
        bool rejected = false; try { SectionManagerLoad(raw, binary); } catch (FormatException) { rejected = true; }
        Check(rejected, "Malformed public manager was accepted or made opaque");
    }
    private static void SectionManagerOpaque(bool binary, int variant)
    {
        var raw = SectionManagerFixture(variant == 5 ? DxfVersion.AutoCad2004 : DxfVersion.AutoCad2018); var record = SectionManagerRecord(raw); var tags = record.Tags.ToList(); int start = tags.FindIndex(t => t.Code == 100);
        if (variant == 0) tags[start] = new DxfTag(100, "PrivateSectionManager");
        else if (variant == 1) tags.Add(new DxfTag(100, "PrivateManagerTail"));
        else if (variant == 2) tags.Insert(start, new DxfTag(300, "private common value"));
        else if (variant == 3) tags.Add(new DxfTag(300, "private payload"));
        else if (variant == 4) tags[start + 1] = new DxfTag(70, (short)2);
        else if (variant == 6) tags.AddRange(new[] { new DxfTag(102, "{PRIVATE"), new DxfTag(1000, "private data"), new DxfTag(102, "}") });
        else if (variant == 7) tags.AddRange(new[] { new DxfTag(100, "PrivateManagerTail"), new DxfTag(340, "EEEEEEEE") });
        var doc = SectionManagerLoad(raw.WithRecord(record, tags), binary); var manager = doc.Objects.Items.OfType<DxfOpaqueObject>().Single(o => o.CodeName == "SECTION_MANAGER");
        var before = OwnershipTagValues(manager.Tags).ToArray();
        var loaded = SectionManagerSave(doc, binary, $"section-manager-opaque-{variant}-{binary}.dxf");
        Check(before.SequenceEqual(OwnershipTagValues(loaded.Objects.Items.OfType<DxfOpaqueObject>().Single(o => o.CodeName == "SECTION_MANAGER").Tags)), "Whole opaque manager packet changed");
    }
    private static void SectionManagerLifecycle(bool binary, int operation)
    {
        var doc = SectionManagerLoad(SectionManagerFixture(DxfVersion.AutoCad2018), binary);
        var manager = doc.Objects.Items.OfType<DxfStoredSectionManager>().Single(); var section = manager.Sections[0];
        long seed = OwnershipSeed(doc); int objects = doc.Objects.Items.Count, entities = doc.Entities.All.Count();
        if (operation == 0) Throws<NotSupportedException>(() => doc.Objects.CloneObject(manager, doc.Objects.Root, "COPY"));
        else if (operation == 1)
        {
            var dest = new DxfDocument(); var root = dest.Objects.Root; long before = OwnershipSeed(dest);
            Throws<NotSupportedException>(() => dest.Objects.CloneObject(manager, root, "COPY"));
            Equal(before, OwnershipSeed(dest), "Foreign manager clone allocated handles");
        }
        else if (operation == 2) Throws<NotSupportedException>(() => doc.Objects.EraseOwnedTree(manager));
        else if (operation == 3) Check(!doc.Entities.Remove(section), "Manager target removed");
        else if (operation == 4) Throws<InvalidOperationException>(() => doc.Objects.EraseSection(section));
        else if (operation == 5)
        {
            doc.DrawingVariables.AcadVer = DxfVersion.AutoCad2013;
            SectionManagerSaveRejected(doc, binary);
        }
        else if (operation == 6)
        {
            doc.Classes.Remove("SECTION_MANAGER"); var loaded = SectionManagerSave(doc, binary);
            Equal(1024, loaded.Classes["SECTION_MANAGER"].ProxyFlags, "Native manager CLASS flags"); Equal(1, loaded.Classes["SECTION_MANAGER"].InstanceCount, "Manager CLASS instance count");
        }
        else
        {
            if (operation == 7) { doc.Classes.Remove("SECTION_MANAGER"); doc.Classes.Add(new DxfClass("SECTION_MANAGER", "PrivateClass", "Private")); }
            if (operation == 8) doc.Objects.Root.Remove("ACAD_SECTION_MANAGER");
            if (operation == 9) manager.PersistentReactors.Add(doc.Objects.Root);
            SectionManagerSaveRejected(doc, binary);
        }
        if (operation != 6) Equal(seed, OwnershipSeed(doc), "Rejected manager operation allocated handles");
        Equal(objects, doc.Objects.Items.Count, "Manager lifecycle changed membership"); Equal(entities, doc.Entities.All.Count(), "Manager lifecycle changed entities");
    }
    private static void SectionManagerSaveRejected(DxfDocument doc, bool binary)
    {
        byte[] sentinel = { 71, 29, 3, 4, 5 }; using var output = new MemoryStream(); output.Write(sentinel); output.Position = 2;
        bool rejected = false;
        try { rejected = !doc.Save(output, binary); }
        catch (InvalidOperationException) { rejected = true; }
        catch (InvalidDataException) { rejected = true; }
        catch (NotSupportedException) { rejected = true; }
        Check(rejected, "Invalid manager preflight accepted");
        Check(sentinel.SequenceEqual(output.ToArray()), "Manager preflight changed existing stream bytes");
        Equal(2L, output.Position, "Manager preflight moved the caller's stream position");
    }
    private static void SectionManagerNumericIdentities(bool binary)
    {
        var raw = SectionManagerFixture(DxfVersion.AutoCad2018); var record = SectionManagerRecord(raw); int start = record.Tags.ToList().FindIndex(t => t.Code == 100);
        raw = raw.WithRecord(record, record.Tags.Select((tag, index) => index > start && tag.Code == 330 ? new DxfTag(330, "000" + ((string)tag.Value).ToLowerInvariant()) : tag));
        var root = raw.Sections.Single(s => s.Name == "OBJECTS").Records.First(r => r.Name == "DICTIONARY");
        raw = raw.WithRecord(root, root.Tags.Select(tag => tag.Code == 3 && (string)tag.Value == "ACAD_SECTION_MANAGER" ? new DxfTag(3, "AcAd_sEcTiOn_MaNaGeR") : tag));
        var doc = SectionManagerLoad(raw, binary); var manager = doc.Objects.Items.OfType<DxfStoredSectionManager>().Single();
        Check(ReferenceEquals(manager.Sections[0], manager.Sections[2]), "Numeric aliases lost source identity");
        var before = OwnershipTagValues(manager.Tags).ToArray(); var again = SectionManagerSave(doc, binary).Objects.Items.OfType<DxfStoredSectionManager>().Single();
        Check(before.SequenceEqual(OwnershipTagValues(again.Tags)), "Numeric source-handle spelling changed");
        Equal("AcAd_sEcTiOn_MaNaGeR", ((DxfDictionary)again.Owner).Entries.Single(entry => ReferenceEquals(entry.Target, again)).Name, "Source manager dictionary-name spelling changed");
    }
    private static void SectionManagerXData(bool binary)
    {
        var doc = SectionManagerLoad(SectionManagerFixture(DxfVersion.AutoCad2018), binary);
        var manager = doc.Objects.Items.OfType<DxfStoredSectionManager>().Single();
        var data = new XData(new netDxf.Tables.ApplicationRegistry("MANAGER_APP"));
        data.XDataRecord.Add(new XDataRecord(XDataCode.String, "Stored manager annotation"));
        data.XDataRecord.Add(new XDataRecord(XDataCode.DatabaseHandle, manager.Sections[0].Handle));
        manager.XData.Add(data);
        var loaded = SectionManagerSave(doc, binary); var again = loaded.Objects.Items.OfType<DxfStoredSectionManager>().Single();
        Equal("Stored manager annotation", (string)again.XData["MANAGER_APP"].XDataRecord[0].Value, "Real manager XData string changed");
        Check(ReferenceEquals(loaded.GetObjectByHandle((string)again.XData["MANAGER_APP"].XDataRecord[1].Value), again.Sections[0]), "Real manager XData target changed");
        Check(!loaded.ApplicationRegistries.Remove("MANAGER_APP"), "Manager XData APPID dependency was removable");
    }
}
