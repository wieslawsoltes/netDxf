using System.IO.Compression;
using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterSectionTests()
    {
        Run("section/api/stored-values", SectionValues);
        foreach (var version in SupportedVersions.Where(v => v >= DxfVersion.AutoCad2007))
        foreach (bool binary in new[] { false, true })
        {
            var v = version; bool b = binary;
            Run($"section/authored/{v}/{b}", () => SectionAuthored(v, b));
            Run($"section/native-name/{v}/{b}", () => SectionNativeName(v, b));
            foreach (string defect in new[] { "count-negative", "count-excessive", "count-short", "incomplete-vector", "duplicate-state", "duplicate-color", "private-field", "missing-subclass", "unknown-subclass", "missing-state", "wrong-settings", "private-after-xdata" })
            { string d = defect; Run($"section/malformed/{v}/{b}/{d}", () => SectionMalformed(v, b, d)); }
        }
        foreach (bool binary in new[] { false, true })
        { bool b = binary; Run($"section/native/R2018/{b}", () => SectionNative(b)); }
        foreach (var version in SupportedVersions.Where(v => v < DxfVersion.AutoCad2007))
        { var v = version; Run($"section/unsupported-profile/{v}", () => { var doc = new DxfDocument(v); SectionReject(() => doc.Entities.Add(new Section())); }); }
    }
    private static void SectionReject(Action action)
    { bool rejected = false; try { action(); } catch (Exception) { rejected = true; } Check(rejected, "SECTION input or mutation was not rejected"); }
    private static Section SectionExample(string codeName = "SECTION")
    {
        var section = new Section(codeName) { Name = "東京 Literal\\U+0041", State = 4, Flags = 17, VerticalDirection = new Vector3(1, 2, 3),
            TopHeight = 5.25, BottomHeight = -15.5, IndicatorTransparency = 70, StoredIndicatorColor = 256,
            StoredNativeIndicatorColor = 9, IndicatorColorName = "Book$Color" };
        section.Vertices.Add(new Vector3(1, 2, 3)); section.Vertices.Add(new Vector3(-4, 5, 6));
        section.BackLineVertices.Add(new Vector3(9, 8, 7));
        return section;
    }
    private static void SectionValues()
    {
        var section = SectionExample(); var clone = (Section)section.Clone();
        Equal(section.Name, clone.Name, "Section clone text"); Equal(section.VerticalDirection, clone.VerticalDirection, "Section clone independent vector");
        clone.Vertices[0] = Vector3.Zero; Check(section.Vertices[0] != clone.Vertices[0], "Section clone shared vertices");
        foreach (double bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        { double value = bad; SectionReject(() => section.TopHeight = value); SectionReject(() => section.Vertices.Add(new Vector3(value, 0, 0))); }
        Equal(2, section.Vertices.Count, "Rejected vertex mutated collection");
        SectionReject(() => section.Name = "bad\ud800"); SectionReject(() => section.IndicatorColorName = "bad\nline");
        SectionReject(() => section.TransformBy(Matrix3.Identity, Vector3.UnitX));
        section.TransformBy(Matrix3.Identity, Vector3.Zero); section.TransformBy(Matrix4.Identity);
        var absent = new Section(); Equal("SECTIONOBJECT", absent.CodeName, "Native default spelling"); SectionReject(() => new Section("section")); Check(absent.StoredIndicatorColor == null && absent.StoredNativeIndicatorColor == null && absent.IndicatorColorName == null, "New section invented indicator fields");
    }
    private static byte[] SectionBytes(DxfDocument doc, bool binary)
    { using var output = new MemoryStream(); Check(doc.Save(output, binary), "SECTION save"); return output.ToArray(); }
    private static DxfRawDocument SectionRaw(DxfVersion version)
    { var doc = new DxfDocument(version); doc.Entities.Add(SectionExample()); return DxfRawDocument.Load(new MemoryStream(SectionBytes(doc, false))); }
    private static DxfDocument SectionLoad(DxfRawDocument raw, bool binary)
    { using var output = new MemoryStream(); raw.WithTags(raw.Tags.Where(t => t.Code != 999)).Save(output, binary); output.Position = 0; return DxfDocument.Load(output) ?? throw new Exception("SECTION load failed"); }
    private static void SectionAuthored(DxfVersion version, bool binary)
    {
        var doc = SectionLoad(SectionRaw(version), binary); var section = doc.Entities.All.OfType<Section>().Single();
        Equal("SECTION", section.CodeName, "Section documented spelling"); Equal("東京 Literal\\U+0041", section.Name, "Section Unicode and literal escape");
        Equal((short)256, section.StoredIndicatorColor!.Value, "Section indicator63"); Equal((short)9, section.StoredNativeIndicatorColor!.Value, "Section indicator62");
        Equal("Book$Color", section.IndicatorColorName, "Section string411"); Equal(2, section.Vertices.Count, "Section vertices"); Equal(1, section.BackLineVertices.Count, "Section back-line vertices");
        Equal(new Vector3(1, 2, 3), section.VerticalDirection, "Section vector was normalized"); Equal(-15.5, section.BottomHeight, "Section signed bottom height");
        var bytes = SectionBytes(doc, binary); File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"section-authored-{version}-{binary}.dxf"), bytes);
        var raw = DxfRawDocument.Load(new MemoryStream(bytes)); var record = raw.Sections.SelectMany(s => s.Records).Single(r => r.Name == "SECTION");
        Equal(1, record.Tags.Count(t => t.Code == 360 && Equals(t.Value, "0")), "Explicit null settings presence");
        section.StoredIndicatorColor = null; section.StoredNativeIndicatorColor = null; section.IndicatorColorName = null;
        var cleared = DxfDocument.Load(new MemoryStream(SectionBytes(doc, binary)))!.Entities.All.OfType<Section>().Single();
        Check(cleared.StoredIndicatorColor == null && cleared.StoredNativeIndicatorColor == null && cleared.IndicatorColorName == null, "Cleared color presence returned");
    }
    private static void SectionNativeName(DxfVersion version, bool binary)
    {
        var doc = new DxfDocument(version); var section = SectionExample("SECTIONOBJECT"); doc.Entities.Add(section);
        var bytes = SectionBytes(doc, binary); File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"section-native-name-{version}-{binary}.dxf"), bytes);
        var loaded = DxfDocument.Load(new MemoryStream(bytes))!.Entities.Sections.Single(); Equal("SECTIONOBJECT", loaded.CodeName, "Authored native spelling");
        Equal(section.Name, loaded.Name, "Native-name text"); Equal(section.StoredNativeIndicatorColor, loaded.StoredNativeIndicatorColor, "Native-name indicator field"); Equal("SECTIONOBJECT", ((Section)loaded.Clone()).CodeName, "Cloned native spelling");
    }
    private static void SectionMalformed(DxfVersion version, bool binary, string defect)
    {
        var raw = MLeaderNativeReplace(SectionRaw(version), record =>
        {
            if (record.Name != "SECTION") return null; var tags = record.Tags.ToList(); int body = tags.FindIndex(t => t.Code == 100 && Equals(t.Value, "AcDbSection"));
            int Index(short code) => tags.FindIndex(body + 1, t => t.Code == code);
            switch (defect)
            {
                case "count-negative": tags[Index(92)] = new DxfTag(92, -1); break;
                case "count-excessive": tags[Index(92)] = new DxfTag(92, Section.MaximumVertices + 1); break;
                case "count-short": tags[Index(92)] = new DxfTag(92, 1); break;
                case "incomplete-vector": tags.RemoveAt(Index(21)); break;
                case "duplicate-state": tags.Insert(Index(90), new DxfTag(90, 1)); break;
                case "duplicate-color": tags.Insert(Index(63), new DxfTag(63, (short)3)); break;
                case "private-field": tags.Insert(Index(92), new DxfTag(299, true)); break;
                case "missing-subclass": tags.RemoveAt(body); break;
                case "unknown-subclass": tags[body] = new DxfTag(100, "AcDbPrivateSection"); break;
                case "missing-state": tags.RemoveAt(Index(90)); break;
                case "wrong-settings": tags[Index(360)] = new DxfTag(360, (string)tags.First(t => t.Code == 330).Value); break;
                case "private-after-xdata": tags.Add(new DxfTag(1001, "ACAD")); tags.Add(new DxfTag(1000, "marker")); tags.Add(new DxfTag(90, 8)); break;
            }
            return tags;
        });
        SectionReject(() => SectionLoad(raw, binary));
    }
    private static void SectionNative(bool binary)
    {
        string fixture = Path.Combine("tests", "fixtures", "section", "LiveSection1.dxf.gz");
        using var file = File.OpenRead(fixture); using var unzip = new GZipStream(file, CompressionMode.Decompress); using var input = new MemoryStream(); unzip.CopyTo(input); input.Position = 0;
        var doc = DxfDocument.Load(input) ?? throw new Exception("Native section original load"); var section = doc.Entities.All.OfType<Section>().Single();
        Equal("SECTIONOBJECT", section.CodeName, "Native section spelling"); Equal("Section Plane (1)", section.Name, "Native section name");
        Equal((short)9, section.StoredNativeIndicatorColor!.Value, "Native subclass62 color"); Check(section.StoredIndicatorColor == null && section.IndicatorColorName == null, "Native section invented color slots");
        Equal(188, section.ProxyGraphics!.Length, "Native proxy bytes"); Check(section.GeometrySettings != null && ReferenceEquals(section.GeometrySettings.Owner, section), "Native reciprocal owned settings");
        Equal("SECTION_SETTINGS", section.GeometrySettings!.CodeName, "Native settings spelling");
        var output = SectionBytes(doc, binary); File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"section-native-AutoCad2018-{binary}.dxf"), output);
        var loaded = DxfDocument.Load(new MemoryStream(output))!.Entities.All.OfType<Section>().Single(); Equal(2, loaded.Vertices.Count, "Native reloaded vertices");
        Check(ReferenceEquals(loaded.GeometrySettings!.Owner, loaded), "Native reloaded settings owner");
        if (section.GeometrySettings is DxfSectionSettings)
        {
            var clone = doc.Objects.CloneSection(section, section.Owner); Equal("SECTIONOBJECT", clone.CodeName, "Native graph clone spelling");
            Check(!ReferenceEquals(clone.GeometrySettings, section.GeometrySettings), "Native graph clone shared settings");
            File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"section-native-copy-AutoCad2018-{binary}.dxf"), SectionBytes(doc, binary));
            doc.Objects.EraseSection(clone);
            File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"section-native-erased-AutoCad2018-{binary}.dxf"), SectionBytes(doc, binary));
        }

    }
}
