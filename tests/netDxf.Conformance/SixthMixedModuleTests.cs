using System.IO.Compression;
using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;
using netDxf.Tables;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterSixthMixedModuleTests()
    {
        foreach (DxfVersion version in SupportedVersions.Where(v => v >= DxfVersion.AutoCad2007))
        foreach (bool binary in new[] { false, true })
        {
            Run($"sixth-mixed/graph/{version}/{binary}", () => SixthMixedGraph(version, binary));
            Run($"sixth-mixed/source-context/{version}/{binary}", () => SixthMixedContext(version, binary));
        }
        foreach (bool binary in new[] { false, true })
            Run($"sixth-mixed/native-complete/AutoCad2018/{binary}", () => SixthMixedNative(binary));
    }

    private static DxfRawRecord SixthRecord(DxfRawDocument raw, string handle) => raw.Sections.SelectMany(s => s.Records)
        .Single(r => r.Tags.TakeWhile(t => t.Code != 100).Any(t => t.Code == 5 && (string)t.Value == handle));
    private static byte[] SixthBytes(DxfDocument doc, bool binary)
    { using var bytes = new MemoryStream(); Check(doc.Save(bytes, binary), "Sixth mixed save"); return bytes.ToArray(); }
    private static DxfDocument SixthLoad(DxfRawDocument raw, bool binary)
    { using var bytes = new MemoryStream(); raw.Save(bytes, binary); bytes.Position = 0; return DxfDocument.Load(bytes) ?? throw new FormatException("Sixth mixed input rejected"); }
    private static DxfDocument SixthReload(DxfDocument doc, bool binary)
    { return DxfDocument.Load(new MemoryStream(SixthBytes(doc, binary))) ?? throw new FormatException("Sixth mixed reload rejected"); }
    private static void SixthSave(DxfDocument doc, bool binary, string file)
    { File.WriteAllBytes(Path.Combine(ArtifactDirectory, file), SixthBytes(doc, binary)); }
    private static DxfObject SixthHost(DxfDocument doc, string name)
    { return doc.DrawingVariables.AcadVer == DxfVersion.AutoCad2007 ? doc.VPorts.AddRecord(new VPort(name)) : doc.Views.Add(new View(name)); }
    private static DxfDatabaseObject? SixthSun(DxfObject host) => host is VPort vport ? vport.Sun : ((View)host).Sun;
    private static DxfSun SixthNewSun() => new() { Enabled = true, ColorIndex = 5, TrueColor = 0x345678, Intensity = 1.375,
        ShadowsEnabled = true, JulianDay = 2455826, StoredTime = 54000000, DaylightSavingTime = false,
        ShadowType = DxfSunShadowType.AreaSampled, ShadowMapSize = 512, ShadowSoftness = 19 };

    private static List<DxfTag> SixthStylePayload(string line)
    {
        var tags = new List<DxfTag> { new(100, "AcDbTableStyle"), new(3, "Sixth style \\U+03A9"), new(70, (short)0), new(71, (short)0),
            new(40, 0.125), new(41, 0.25), new(280, (short)0), new(281, (short)1) };
        for (int i = 0; i < 3; i++) tags.AddRange(new DxfTag[] { new(7, "sixth_\\U+0073tyle"), new(140, 2.5 + i), new(170, (short)5),
            new(62, (short)3), new(63, (short)257), new(283, (short)0), new(90, 512), new(91, 0), new(1, "Literal\\U+005CU+0041"), new(284, (short)1) });
        tags.AddRange(new DxfTag[] { new(340, line), new(102, "{SIXTH_PRIVATE"), new(7, "SIXTH_PRIVATE_STYLE"), new(310, new byte[] { 7, 0, 255 }), new(102, "}") });
        return tags;
    }
    private static List<DxfTag> SixthFieldPayload(string child, params string[] targets)
    {
        var tags = new List<DxfTag> { new(100, "AcDbField"), new(1, "SixthSynthetic"), new(2, "prefix \\U+03"), new(3, "A9 Literal\\U+005CU+0041"),
            new(90, 1), new(360, child), new(97, targets.Length) };
        tags.AddRange(targets.Select(handle => new DxfTag(331, handle)));
        tags.AddRange(new DxfTag[] { new(91, 63), new(92, 0), new(94, 43), new(95, 32), new(96, 335), new(300, "inert evaluator cache"),
            new(93, 0), new(7, "ACFD_FIELD_VALUE"), new(90, 0), new(91, 0), new(301, "####"), new(98, 4), new(310, new byte[] { 0, 17, 255 }), new(320, "F0F0F0") });
        return tags;
    }

    // FIELD/TABLESTYLE packets are explicitly authored scaffolds. Their placeholders
    // are replaced before typed load; no evaluator or native producer is implied.
    private static DxfDocument SixthSeed(DxfVersion version, bool binary, DxfDocument? native = null)
    {
        var doc = native ?? new DxfDocument(version);
        var section = native == null ? new Section { Name = "Sixth section Ω Literal\\U+0041", State = 4, Flags = 17,
            VerticalDirection = new Vector3(0.25, -0.5, 2), TopHeight = 7.125, BottomHeight = -3.5, IndicatorTransparency = 37, StoredNativeIndicatorColor = 9 } : doc.Entities.Sections.Single();
        if (native == null) { section.Vertices.Add(new Vector3(1.25, 2.5, 3.75)); section.Vertices.Add(new Vector3(-4.25, 5.5, -6.75)); doc.Entities.Add(section); }
        var line = new Line(new Vector3(11, 12, 13), new Vector3(14, 15, 16)); doc.Entities.Add(line);
        if (native == null)
        {
            var settings = new DxfSectionSettings { SectionType = 4 };
            settings.SetTypeSettings(new[] { new DxfSectionTypeSettings(4, 17, new DxfObject[] { section, line, settings }, section.Owner.Record,
                "inert\\U+0041.dwg", new[] { new DxfSectionGeometrySettings { SectionType = 4, GeometryValue = 8, Flags = 33, ColorCode = 62,
                    ColorIndex = 9, LayerName = "*_BackgroundLines", LinetypeScale = 1.125, FaceTransparency = 23, EdgeTransparency = 41 } }) });
            doc.Objects.SetSectionSettings(section, settings);
        }
        var host = SixthHost(doc, "SIXTH_SUN_OWNER"); var sun = SixthNewSun(); doc.Objects.SetSun(host, sun);
        var graph = new DxfDictionary(); doc.NamedObjects.Add("SIXTH_MIXED", graph);
        var style = new DxfXRecord(); var parent = new DxfXRecord(); var child = new DxfXRecord();
        graph.Add("STYLE", style); graph.Add("FIELD", parent); graph.Add("TEMP_CHILD", child);
        doc.TextStyles.Add(new TextStyle("SIXTH_STYLE", "txt.shx")); doc.TextStyles.Add(new TextStyle("SIXTH_PRIVATE_STYLE", "txt.shx"));
        var raw = DxfRawDocument.Load(new MemoryStream(SixthBytes(doc, binary)));
        var dictionary = SixthRecord(raw, graph.Handle); var dictionaryTags = dictionary.Tags.ToList();
        int temporary = dictionaryTags.FindIndex(t => t.Code == 3 && (string)t.Value == "TEMP_CHILD"); dictionaryTags.RemoveRange(temporary, 2); raw = raw.WithRecord(dictionary, dictionaryTags);
        var old = SixthRecord(raw, style.Handle); raw = raw.WithRecord(old, old.Tags.TakeWhile(t => t.Code != 100)
            .Select(t => t.Code == 0 ? new DxfTag(0, "TABLESTYLE") : t).Concat(SixthStylePayload(line.Handle)));
        old = SixthRecord(raw, parent.Handle); raw = raw.WithRecord(old, old.Tags.TakeWhile(t => t.Code != 100)
            .Select(t => t.Code == 0 ? new DxfTag(0, "FIELD") : t).Concat(SixthFieldPayload(child.Handle,
                section.Handle, section.GeometrySettings!.Handle, sun.Handle, style.Handle, line.Handle, line.Handle, "0000")));
        old = SixthRecord(raw, child.Handle); raw = raw.WithRecord(old, old.Tags.TakeWhile(t => t.Code != 100)
            .Select(t => t.Code == 0 ? new DxfTag(0, "FIELD") : t.Code == 330 ? new DxfTag(330, parent.Handle) : t)
            .Concat(new DxfTag[] { new(100, "AcDbField"), new(1, "SixthChild"), new(2, "1+1"), new(90, 0), new(97, 0), new(93, 0), new(7, "ACFD_FIELD_VALUE"), new(90, 0) }));
        return SixthLoad(raw, binary);
    }

    private static (DxfDictionary Graph, DxfTableStyle Style, DxfStoredField Field, Section Section, DxfSectionSettings Settings, DxfSun Sun, Line Line) SixthParts(DxfDocument doc)
    {
        var graph = (DxfDictionary)doc.NamedObjects["SIXTH_MIXED"]; var field = (DxfStoredField)graph["FIELD"];
        return (graph, (DxfTableStyle)graph["STYLE"], field, (Section)field.ReferencedObjects[0], (DxfSectionSettings)field.ReferencedObjects[1],
            (DxfSun)field.ReferencedObjects[2], (Line)field.ReferencedObjects[4]);
    }
    private static void SixthAssert(DxfDocument doc)
    {
        var p = SixthParts(doc); Equal(0, doc.Objects.Validate().Count, "Sixth mixed valid database");
        Equal(1, p.Field.Children.Count, "FIELD child inventory"); Check(ReferenceEquals(p.Field.Children.Single().Owner, p.Field), "FIELD reciprocal child owner");
        Equal("prefix Ω Literal\\U+0041", p.Field.FieldCode, "FIELD complete-chunk decode once");
        Check(ReferenceEquals(p.Field.ReferencedObjects[3], p.Style) && ReferenceEquals(p.Field.ReferencedObjects[4], p.Field.ReferencedObjects[5]) && p.Field.ReferencedObjects[6] == null, "FIELD exact cross-family/repeated/null targets");
        Check(ReferenceEquals(p.Settings.Owner, p.Section) && ReferenceEquals(p.Section.GeometrySettings, p.Settings), "SECTION reciprocal settings");
        Check(ReferenceEquals(SixthSun(p.Sun.Owner), p.Sun), "SUN reciprocal registered host");
        Check(p.Style.References.Contains(p.Line) && p.Style.Rows.All(row => row.TextStyle == doc.TextStyles[p.Style.Rows[0].TextStyle!.Name]), "TABLESTYLE line and STYLE resources");
        Check(!p.Style.References.Contains(doc.TextStyles["SIXTH_PRIVATE_STYLE"]), "TABLESTYLE private style is not bound");
    }
    private static void SixthRejectAtomic(DxfDocument doc, Action action)
    {
        long seed = OwnershipSeed(doc); int objects = doc.Objects.Items.Count, entities = doc.Entities.All.Count();
        bool rejected = false; try { action(); } catch (Exception error) when (error is InvalidOperationException || error is NotSupportedException || error is ArgumentException) { rejected = true; }
        Check(rejected, "Sixth mixed operation must reject"); Equal(seed, OwnershipSeed(doc), "Rejected operation allocated handles");
        Equal(objects, doc.Objects.Items.Count, "Rejected operation changed object membership"); Equal(entities, doc.Entities.All.Count(), "Rejected operation changed entities");
        SixthAssert(doc);
    }
    private static void SixthMixedGraph(DxfVersion version, bool binary)
    {
        var doc = SixthSeed(version, binary); var p = SixthParts(doc);
        var old = p.Settings.TypeSettings.Single(); p.Settings.SetTypeSettings(new[] { new DxfSectionTypeSettings(old.SectionType, old.GenerationOptions,
            new DxfObject[] { p.Section, p.Line, p.Line, null!, p.Settings, p.Style, p.Sun, p.Field }, old.DestinationBlock, old.DestinationFileName, old.GeometrySettings) });
        var note = new DxfXRecord(); note.Data.Add(new DxfTag(1, "links Ω Literal\\U+0041")); note.Data.Add(new DxfTag(330, p.Sun.Owner.Handle)); note.Data.Add(new DxfTag(331, p.Sun.Handle));
        note.Data.Add(new DxfTag(340, p.Settings.Handle)); note.Data.Add(new DxfTag(350, p.Style.Handle)); note.Data.Add(new DxfTag(340, p.Field.Handle)); note.Data.Add(new DxfTag(310, new byte[] { 3, 0, 255 }));
        var extension = new DxfDictionary(); extension.Add("LINKS", note); extension.Add("ALIAS", note); doc.Objects.SetExtensionDictionary(p.Sun, extension);
        p.Settings.PersistentReactors.Add(p.Section);
        doc = SixthReload(doc, binary); p = SixthParts(doc); SixthAssert(doc);
        SixthSave(doc, binary, $"sixth-mixed-original-{version}-{binary}.dxf");
        Check(!doc.Entities.Remove(p.Line), "Cross-family line dependency removal rejected"); Check(!doc.TextStyles.Remove(p.Style.Rows[0].TextStyle!), "TABLESTYLE text resource removal rejected");
        SixthRejectAtomic(doc, () => doc.Objects.EraseSection(p.Section)); SixthRejectAtomic(doc, () => doc.Objects.EraseOwnedTree(p.Sun));
        SixthRejectAtomic(doc, () => doc.Objects.EraseOwnedTree(p.Style)); SixthRejectAtomic(doc, () => doc.Objects.EraseOwnedTree(p.Graph));
        SixthRejectAtomic(doc, () => doc.Objects.Clone(p.Graph, doc.Objects.Root, "MIXED_COPY"));
        SixthRejectAtomic(doc, () => p.Section.Clone()); SixthRejectAtomic(doc, () => ((ICloneable)p.Sun.Owner).Clone());
        var fieldPacket = OwnershipTagValues(p.Field.Payload).ToArray(); doc.TextStyles["SIXTH_STYLE"].Name = "SIXTH_STYLE_RENAMED_Ω";
        Check(OwnershipTagValues(p.Field.Payload).SequenceEqual(fieldPacket), "Resource rename cannot rewrite FIELD code/cache");
        SixthSave(doc, binary, $"sixth-mixed-renamed-{version}-{binary}.dxf");
        var sectionCopy = doc.Objects.CloneSection(p.Section, p.Section.Owner); var copiedSettings = (DxfSectionSettings)sectionCopy.GeometrySettings!;
        Check(ReferenceEquals(copiedSettings.TypeSettings.Single().SourceObjects[0], sectionCopy) && ReferenceEquals(copiedSettings.TypeSettings.Single().SourceObjects[4], copiedSettings), "SECTION owned references remapped");
        Check(ReferenceEquals(copiedSettings.TypeSettings.Single().SourceObjects[5], p.Style) && ReferenceEquals(copiedSettings.TypeSettings.Single().SourceObjects[7], p.Field), "SECTION immutable external dependencies retained");
        var destination = SixthHost(doc, "SIXTH_SUN_COPY"); var sunCopy = doc.Objects.CloneSun(p.Sun, destination); var copiedNote = (DxfXRecord)sunCopy.ExtensionDictionary!["LINKS"];
        Check(ReferenceEquals(sunCopy.ExtensionDictionary["ALIAS"], copiedNote), "SUN cloned alias identity");
        Equal(destination.Handle, (string)copiedNote.Data.Single(tag => tag.Code == 330).Value, "SUN owner handle mapped"); Equal(sunCopy.Handle, (string)copiedNote.Data.Single(tag => tag.Code == 331).Value, "SUN own handle mapped");
        Check(copiedNote.Data.Where(t => t.Code == 340).Select(t => (string)t.Value).SequenceEqual(new[] { p.Settings.Handle, p.Field.Handle }), "SUN external settings/FIELD identities retained");
        SixthSave(doc, binary, $"sixth-mixed-cloned-{version}-{binary}.dxf");
        string[] deleted = new[] { sectionCopy.Handle, copiedSettings.Handle, sunCopy.Handle, sunCopy.ExtensionDictionary.Handle, copiedNote.Handle };
        doc.Objects.EraseSection(sectionCopy); doc.Objects.EraseOwnedTree(sunCopy); Check(deleted.All(handle => doc.GetObjectByHandle(handle) == null), "Cloned owned identities removed");
        Check(sectionCopy.IsErased && copiedSettings.IsErased && sunCopy.IsErased && copiedNote.IsErased && SixthSun(destination) == null, "Clone teardown terminal and SUN slot cleared");
        SixthAssert(doc); SixthSave(doc, binary, $"sixth-mixed-erased-{version}-{binary}.dxf"); SixthAssert(SixthReload(doc, binary));
        var profile = doc.DrawingVariables.AcadVer; doc.DrawingVariables.AcadVer = profile == DxfVersion.AutoCad2018 ? DxfVersion.AutoCad2013 : DxfVersion.AutoCad2018;
        using var rejectedOutput = new MemoryStream(); bool refused = false; try { refused = !doc.Save(rejectedOutput, binary); } catch (InvalidOperationException) { refused = true; }
        Check(refused && rejectedOutput.Length == 0, "Immutable mixed graph profile conversion rejected before output"); doc.DrawingVariables.AcadVer = profile;
    }
    private static void SixthMixedContext(DxfVersion version, bool binary)
    {
        var doc = SixthSeed(version, binary); var p = SixthParts(doc); var raw = DxfRawDocument.Load(new MemoryStream(SixthBytes(doc, binary)));
        var host = SixthRecord(raw, p.Sun.Owner.Handle); raw = raw.WithRecord(host, host.Tags.Concat(new DxfTag[] { new(102, "{SIXTH_PRIVATE"), new(361, "F00"), new(102, "}") }));
        var field = SixthRecord(raw, p.Field.Handle); raw = raw.WithRecord(field, field.Tags.Select(t => t.Code == 331 && (string)t.Value != "0" ? new DxfTag(331, ((string)t.Value).ToLowerInvariant().PadLeft(12, '0')) : t));
        var loaded = SixthLoad(raw, binary); SixthAssert(loaded); SixthSave(loaded, binary, $"sixth-mixed-context-{version}-{binary}.dxf");
    }
    private static void SixthMixedNative(bool binary)
    {
        using var input = File.OpenRead(Path.Combine("tests", "fixtures", "section", "LiveSection1.dxf.gz")); using var unzip = new GZipStream(input, CompressionMode.Decompress); using var bytes = new MemoryStream(); unzip.CopyTo(bytes);
        var original = DxfRawDocument.Load(new MemoryStream(bytes.ToArray())); var source = DxfDocument.Load(new MemoryStream(bytes.ToArray())) ?? throw new FormatException("Native mixed seed failed");
        var doc = SixthSeed(DxfVersion.AutoCad2018, binary, source); SixthAssert(doc); var p = SixthParts(doc);
        Check(p.Section.Handle == "228" && p.Settings.Handle == "22A", "Complete native graph source identities unchanged");
        var saved = DxfRawDocument.Load(new MemoryStream(SixthBytes(doc, binary)));
        foreach (string handle in new[] { "228", "22A", "87" })
        {
            var before = SixthRecord(original, handle); var after = SixthRecord(saved, handle); string marker = handle == "228" ? "AcDbSection" : handle == "22A" ? "AcDbSectionSettings" : "AcDbTableStyle";
            Check(OwnershipTagValues(before.Tags.SkipWhile(t => t.Code != 100 || (string)t.Value != marker)).SequenceEqual(OwnershipTagValues(after.Tags.SkipWhile(t => t.Code != 100 || (string)t.Value != marker))), "Native body unchanged in mixed graph: " + handle);
        }
        SixthRejectAtomic(doc, () => doc.Objects.EraseSection(p.Section)); SixthRejectAtomic(doc, () => doc.Objects.EraseOwnedTree(p.Sun));
        SixthSave(doc, binary, $"sixth-mixed-native-AutoCad2018-{binary}.dxf"); SixthAssert(SixthReload(doc, binary));
    }
}
