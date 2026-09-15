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
    private static void RegisterSectionLifecycleTests()
    {
        foreach (var version in SupportedVersions.Where(v => v >= DxfVersion.AutoCad2007))
        foreach (bool binary in new[] { false, true })
        {
            var v = version; bool b = binary;
            Run($"section/lifecycle/owned/{v}/{b}", () => SectionOwned(v, b));
            Run($"section/lifecycle/cross-document/{v}/{b}", () => SectionCrossDocument(v, b));
            Run($"section/lifecycle/adoption-rollback/{v}/{b}", () => SectionAdoptionRollback(v, b));
            Run($"section/lifecycle/numeric-handles/{v}/{b}", () => SectionNumericHandles(v, b));
        }
        foreach (var version in SupportedVersions.Where(v => v >= DxfVersion.AutoCad2007)) foreach (bool binary in new[] { false, true })
        {
            foreach (string action in new[] { "layer-source", "layer-destination", "linetype-destination", "appid-destination", "appid-attachment" })
            { var v = version; bool b = binary; string a = action; Run($"section/lifecycle/callback/{a}/{v}/{b}", () => SectionCallback(v, b, a)); }
            var followingVersion = version; bool followingBinary = binary;
            Run($"section/lifecycle/following-line/{version}/{binary}", () => SectionFollowingLine(followingVersion, followingBinary));
        }
        Run("section/lifecycle/native-private-clone-rejection", SectionOpaqueLifecycle);
        Run("section/lifecycle/common-metadata-admission", SectionCommonMetadata);
    }
    private static (DxfDocument Doc, Section Section, DxfSectionSettings Settings, Line Line) SectionGraph(DxfVersion version)
    {
        var doc = new DxfDocument(version); var section = SectionExample(); var line = new Line(Vector3.Zero, Vector3.UnitX);
        doc.Entities.Add(line); doc.Entities.Add(section);
        var settings = new DxfSectionSettings { SectionType = 4 };
        settings.SetTypeSettings(new[] { new DxfSectionTypeSettings(4, 17, new DxfObject[] { section, line, line, null!, settings }, section.Owner.Record,
            "never-open-this.dwg", new[] { new DxfSectionGeometrySettings { SectionType = 4, GeometryValue = 8, ColorCode = 62, ColorIndex = 9, LayerName = "*_BackgroundLines" } }) });
        doc.Objects.SetSectionSettings(section, settings);
        section.PersistentReactors.Add(doc.Objects.Root); section.PersistentReactors.Add(line); settings.PersistentReactors.Add(section);
        var sectionExtension = new DxfDictionary(); var note = new DxfXRecord();
        sectionExtension.Add("NOTE", note); sectionExtension.Add("ALIAS", note);
        note.Data.Add(new DxfTag(1, "東京 Literal\\U+0041")); note.Data.Add(new DxfTag(330, settings.Handle)); note.Data.Add(new DxfTag(310, new byte[] { 3, 0, 255 }));
        doc.Objects.SetExtensionDictionary(section, sectionExtension);
        var settingsExtension = new DxfDictionary(); settingsExtension.Add("EMPTY", new DxfXRecord()); doc.Objects.SetExtensionDictionary(settings, settingsExtension);
        var data = new XData(new ApplicationRegistry("SECTION_APP")); data.XDataRecord.Add(new XDataRecord(XDataCode.DatabaseHandle, settings.Handle)); section.XData.Add(data);
        var objectData = new XData(new ApplicationRegistry("SECTION_APP")); objectData.XDataRecord.Add(new XDataRecord(XDataCode.DatabaseHandle, section.Handle)); settings.XData.Add(objectData);
        Check(doc.Objects.Validate().Count == 0, "Initial section graph validation");
        return (doc, section, settings, line);
    }
    private static void CheckSectionGraph(Section section, Line line)
    {
        var settings = (DxfSectionSettings)section.GeometrySettings!; var type = settings.TypeSettings.Single();
        Check(ReferenceEquals(settings.Owner, section), "Settings reciprocal owner");
        Equal(17, type.GenerationOptions, "Integer generation flags"); Equal(5, type.SourceObjects.Count, "Source count");
        Check(ReferenceEquals(type.SourceObjects[0], section) && ReferenceEquals(type.SourceObjects[1], line) && ReferenceEquals(type.SourceObjects[2], line) && type.SourceObjects[3] == null && ReferenceEquals(type.SourceObjects[4], settings), "Cloned source identities, duplicates and null");
        Check(ReferenceEquals(type.DestinationBlock, section.Owner.Record), "Destination block mapping");
        Check(ReferenceEquals(settings.PersistentReactors.Single(), section), "Settings owner reactor mapping");
        Equal(settings.Handle, (string)section.XData["SECTION_APP"].XDataRecord.Single().Value, "Entity XData reference mapping");
        Equal(section.Handle, (string)settings.XData["SECTION_APP"].XDataRecord.Single().Value, "Settings XData reference mapping");
        var extension = section.ExtensionDictionary!; Check(ReferenceEquals(extension.Owner, section), "Section extension owner");
        Check(ReferenceEquals(extension["NOTE"], extension["ALIAS"]), "Extension alias mapping");
        var note = (DxfXRecord)extension["NOTE"]; Equal(settings.Handle, (string)note.Data.Single(t => t.Code == 330).Value, "XRECORD reference mapping");
        Check(((byte[])note.Data.Single(t => t.Code == 310).Value).SequenceEqual(new byte[] { 3, 0, 255 }), "XRECORD binary bytes");
        Check(ReferenceEquals(settings.ExtensionDictionary!.Owner, settings), "Settings extension owner");
    }
    private static void SectionOwned(DxfVersion version, bool binary)
    {
        var (doc, section, settings, line) = SectionGraph(version);
        CheckSectionGraph(section, line);
        Check(!doc.Entities.Remove(section), "Ordinary removal orphaned owned section settings"); Check(!doc.Entities.Remove(line), "Removal invalidated settings source reference");
        SectionReject(() => doc.Objects.EraseOwnedTree(settings)); SectionReject(() => section.Clone());
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"section-owned-{version}-{binary}.dxf"), SectionBytes(doc, binary));
        var clone = doc.Objects.CloneSection(section, section.Owner); CheckSectionGraph(clone, line);
        Check(!ReferenceEquals(clone.GeometrySettings, settings) && clone.GeometrySettings!.Handle != settings.Handle, "Cloned owned settings identity");
        Check(doc.Objects.Validate().Count == 0, "Cloned graph database validation");
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"section-copy-{version}-{binary}.dxf"), SectionBytes(doc, binary));
        string[] deleted = doc.Objects.Items.Where(o => IsSectionOwned(clone, o)).Select(o => o.Handle).Append(clone.Handle).ToArray();
        doc.Objects.EraseSection(clone); Check(clone.IsErased && clone.GeometrySettings!.IsErased, "Section subtree erasure was not terminal");
        Check(deleted.All(h => doc.GetObjectByHandle(h) == null), "Erased graph remained registered"); Check(ReferenceEquals(doc.GetObjectByHandle(line.Handle), line), "Erasure cascaded into external line");
        SectionReject(() => doc.Entities.Add(clone)); SectionReject(() => clone.Clone()); SectionReject(() => doc.Objects.EraseSection(clone));
        Equal(1, doc.Entities.Sections.Count(), "Erasure removed the wrong section"); CheckSectionGraph(section, line);
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"section-erased-{version}-{binary}.dxf"), SectionBytes(doc, binary));
        var reload = DxfDocument.Load(new MemoryStream(SectionBytes(doc, binary)))!; CheckSectionGraph(reload.Entities.Sections.Single(), reload.Entities.Lines.Single());
    }
    private static bool IsSectionOwned(Section section, DxfObject item)
    { for (DxfObject? parent = item.Owner; parent != null; parent = parent.Owner) if (ReferenceEquals(parent, section)) return true; return false; }
    private static void SectionCrossDocument(DxfVersion version, bool binary)
    {
        var (source, section, _, sourceLine) = SectionGraph(version); var target = new DxfDocument(version); var line = new Line(Vector3.Zero, Vector3.UnitY); target.Entities.Add(line);
        var destination = target.Blocks[Block.DefaultModelSpaceName]; int before = target.Objects.Items.Count; int entities = target.Entities.All.Count();
        SectionReject(() => target.Objects.CloneSection(section, destination)); Equal(before, target.Objects.Items.Count, "Rejected cross clone registered objects"); Equal(entities, target.Entities.All.Count(), "Rejected cross clone registered entity");
        var map = new Dictionary<DxfObject, DxfObject> { { sourceLine, line }, { source.Objects.Root, target.Objects.Root }, { section.Layer, target.Layers[section.Layer.Name] }, { section.Linetype, target.Linetypes[section.Linetype.Name] } };
        var clone = target.Objects.CloneSection(section, destination, map); CheckSectionGraph(clone, line); CheckSectionGraph(section, sourceLine);
        Check(target.Objects.Validate().Count == 0 && source.Objects.Validate().Count == 0, "Cross-cloned database validation");
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"section-cross-{version}-{binary}.dxf"), SectionBytes(target, binary));
        var loaded = DxfDocument.Load(new MemoryStream(SectionBytes(target, binary)))!; CheckSectionGraph(loaded.Entities.Sections.Single(), loaded.Entities.Lines.Single());
        // An incoming external handle must reject before the first object is erased.
        var blocker = new DxfXRecord(); blocker.Data.Add(new DxfTag(330, clone.Handle)); target.Objects.Root.Add("BLOCK_ERASE", blocker);
        int objectCount = target.Objects.Items.Count; SectionReject(() => target.Objects.EraseSection(clone)); Equal(objectCount, target.Objects.Items.Count, "Rejected erase mutated objects"); Check(!clone.IsErased, "Rejected erase marked entity terminal");
    }
    private static void SectionAdoptionRollback(DxfVersion version, bool binary)
    {
        var target = new DxfDocument(version); var section = SectionExample(); target.Entities.Add(section); var foreign = new DxfDocument(version); var line = new Line(Vector3.Zero, Vector3.UnitX); foreign.Entities.Add(line);
        var settings = new DxfSectionSettings(); settings.SetTypeSettings(new[] { new DxfSectionTypeSettings(1, 0, new[] { line }, null!, "", Array.Empty<DxfSectionGeometrySettings>()) });
        int before = target.Objects.Items.Count; SectionReject(() => target.Objects.SetSectionSettings(section, settings));
        Equal(before, target.Objects.Items.Count, "Rejected attachment registered settings"); Check(settings.Owner == null && settings.Database == null && section.GeometrySettings == null, "Rejected attachment changed ownership");
        settings.SetTypeSettings(Array.Empty<DxfSectionTypeSettings>()); target.Objects.SetSectionSettings(section, settings);
        SectionReject(() => target.Objects.SetSectionSettings(section, new DxfSectionSettings())); Check(ReferenceEquals(section.GeometrySettings, settings), "Rejected replacement changed settings slot");
        var doc = DxfDocument.Load(new MemoryStream(SectionBytes(target, binary)))!; Check(doc.Entities.Sections.Single().GeometrySettings is DxfSectionSettings, "Empty owned settings round trip");
    }
    private static void SectionNumericHandles(DxfVersion version, bool binary)
    {
        var (doc, section, settings, line) = SectionGraph(version);
        section.XData["SECTION_APP"].XDataRecord.Add(new XDataRecord(XDataCode.DatabaseHandle, "0000"));
        section.XData["SECTION_APP"].XDataRecord.Add(new XDataRecord(XDataCode.DatabaseHandle, line.Handle.ToLowerInvariant().PadLeft(10, '0')));
        var note = (DxfXRecord)section.ExtensionDictionary!["NOTE"]; note.Data.Add(new DxfTag(340, "0000")); note.Data.Add(new DxfTag(350, line.Handle.ToLowerInvariant().PadLeft(10, '0')));
        var clone = doc.Objects.CloneSection(section, section.Owner);
        var data = clone.XData["SECTION_APP"].XDataRecord;
        Equal(clone.GeometrySettings!.Handle, (string)data[0].Value, "Owned settings XData mapping"); Equal("0000", (string)data[1].Value, "Numeric null spelling retention"); Equal(line.Handle, (string)data[2].Value, "Padded external XData mapping");
        var copyNote = (DxfXRecord)clone.ExtensionDictionary!["NOTE"];
        Equal("0000", (string)copyNote.Data.Single(t => t.Code == 340).Value, "XRECORD numeric null retention"); Equal(line.Handle, (string)copyNote.Data.Single(t => t.Code == 350).Value, "Padded external XRECORD mapping");
        var raw = MLeaderNativeReplace(SectionRaw(version), record => { if (record.Name != "SECTION") return null; return record.Tags.Select(t => t.Code == 360 ? new DxfTag(360, "0000") : t); });
        var loaded = SectionLoad(raw, binary).Entities.Sections.Single(); Check(loaded.GeometrySettings == null && loaded.HasStoredGeometrySettings, "Padded null section settings handle");
    }
    private static void SectionCommonMetadata()
    {
        var doc = new DxfDocument(DxfVersion.AutoCad2018); var line = new Line(Vector3.Zero, Vector3.UnitX); doc.Entities.Add(line);
        var invalid = new Section(); var missing = new XData(new ApplicationRegistry("SECTION_APP")); missing.XDataRecord.Add(new XDataRecord(XDataCode.DatabaseHandle, "FFFFFF")); invalid.XData.Add(missing);
        int count = doc.Entities.All.Count(); SectionReject(() => doc.Entities.Add(invalid)); Equal(count, doc.Entities.All.Count(), "Invalid metadata changed entity membership"); Check(invalid.Handle == null && invalid.Owner == null, "Invalid metadata consumed entity identity");
        var section = new Section(); var data = new XData(new ApplicationRegistry("SECTION_APP")); data.XDataRecord.Add(new XDataRecord(XDataCode.DatabaseHandle, line.Handle)); section.XData.Add(data); doc.Entities.Add(section);
        SectionReject(() => section.Clone()); var clone = doc.Objects.CloneSection(section, section.Owner); Equal(line.Handle, (string)clone.XData["SECTION_APP"].XDataRecord.Single().Value, "Bare section graph clone mapped XData");
        var blocker = new DxfXRecord(); blocker.Data.Add(new DxfTag(330, clone.Handle)); doc.Objects.Root.Add("SECTION_BLOCKER", blocker);
        Check(!doc.Entities.Remove(clone), "Ordinary section removal invalidated incoming metadata"); SectionReject(() => doc.Objects.EraseSection(clone));
        blocker.Data.Clear(); Check(doc.Entities.Remove(clone), "An unowned unreferenced section could not be removed");
    }
    private static void SectionCallback(DxfVersion version, bool binary, string action)
    {
        var doc = new DxfDocument(version); var section = new Section("SECTION"); var layer = new SectionCallbackLayer("CALLBACK_LAYER"); var linetype = new SectionCallbackLinetype("CALLBACK_LINE");
        section.Layer = layer; section.Linetype = linetype; doc.Entities.Add(section);
        var destination = doc.Blocks.Add(new Block("CALLBACK_DESTINATION")); var app = new MetadataCallbackRegistry("SECTION_CALLBACK"); int calls = 0;
        var settings = new DxfSectionSettings(); settings.SetTypeSettings(new[] { new DxfSectionTypeSettings(4, 17, new DxfObject[] { section, settings }, section.Owner.Record, "", Array.Empty<DxfSectionGeometrySettings>()) });
        if (action == "appid-attachment")
        {
            settings.XData.Add(new XData(app)); app.Callback = () => { calls++; doc.Objects.EraseSection(section); };
            doc.Objects.SetSectionSettings(section, settings); Check(calls == 0 && !section.IsErased && ReferenceEquals(section.GeometrySettings, settings) && ReferenceEquals(settings.Owner, section), "Settings attachment invoked registry clone callback");
        }
        else
        {
            doc.Objects.SetSectionSettings(section, settings); doc.ApplicationRegistries.Add(app); section.XData.Add(new XData(app));
            if (action == "layer-source") layer.Callback = () => { calls++; doc.Objects.EraseSection(section); };
            if (action == "layer-destination") layer.Callback = () => { calls++; Check(doc.Blocks.Remove(destination), "Destination callback precondition"); };
            if (action == "linetype-destination") linetype.Callback = () => { calls++; Check(doc.Blocks.Remove(destination), "Linetype callback precondition"); };
            if (action == "appid-destination") app.Callback = () => { calls++; Check(doc.Blocks.Remove(destination), "APPID callback precondition"); };
            var copy = doc.Objects.CloneSection(section, destination);
            Check(calls == 0 && !section.IsErased && doc.Blocks.Contains(destination) && ReferenceEquals(copy.Owner, destination) && destination.Entities.Contains(copy), "Graph clone invoked resource callback or lost registered ownership");
            var copied = (DxfSectionSettings)copy.GeometrySettings!; Check(ReferenceEquals(copied.Owner, copy) && ReferenceEquals(copied.TypeSettings[0].SourceObjects[0], copy) && ReferenceEquals(copied.TypeSettings[0].SourceObjects[1], copied), "Callback-free clone reference mapping");
        }
        Check(doc.Objects.Validate().Count == 0, "Callback-free section database validation");
        var loaded = DxfDocument.Load(new MemoryStream(SectionBytes(doc, binary)))!; Check(loaded != null && loaded.Objects.Validate().Count == 0, "Callback-free section reload"); Equal(0, calls, "A later save invoked a resource callback");
    }
    private static void SectionFollowingLine(DxfVersion version, bool binary)
    {
        var doc = new DxfDocument(version); var section = new Section("SECTION"); doc.Entities.Add(section); var line = new Line(Vector3.Zero, Vector3.UnitX); doc.Entities.Add(line);
        var settings = new DxfSectionSettings(); settings.SetTypeSettings(new[] { new DxfSectionTypeSettings(4, 17, new[] { line }, null!, "", Array.Empty<DxfSectionGeometrySettings>()) }); doc.Objects.SetSectionSettings(section, settings);
        var loaded = DxfDocument.Load(new MemoryStream(SectionBytes(doc, binary)))!;
        Check(loaded != null && ReferenceEquals(((DxfSectionSettings)loaded.Entities.Sections.Single().GeometrySettings!).TypeSettings[0].SourceObjects.Single(), loaded.Entities.Lines.Single()), "Documented SECTION lost following entity source identity context");
    }
    private sealed class SectionCallbackLayer : Layer
    {
        internal Action? Callback;
        internal SectionCallbackLayer(string name) : base(name) { }
        public override object Clone() { this.Callback?.Invoke(); return base.Clone(); }
    }
    private sealed class SectionCallbackLinetype : Linetype
    {
        internal Action? Callback;
        internal SectionCallbackLinetype(string name) : base(name) { }
        public override object Clone() { this.Callback?.Invoke(); return base.Clone(); }
    }
    private static void SectionOpaqueLifecycle()
    {
        using var file = File.OpenRead(Path.Combine("tests", "fixtures", "section", "LiveSection1.dxf.gz")); using var gzip = new System.IO.Compression.GZipStream(file, System.IO.Compression.CompressionMode.Decompress); using var bytes = new MemoryStream(); gzip.CopyTo(bytes);
        var raw = DxfRawDocument.Load(new MemoryStream(bytes.ToArray())); raw = MLeaderNativeReplace(raw, r => { if (r.Name != "SECTION_SETTINGS") return null; var tags = r.Tags.ToList(); tags.Add(new DxfTag(299, true)); return tags; });
        var doc = SectionLoad(raw, false); var section = doc.Entities.Sections.Single(); Check(section.GeometrySettings is DxfOpaqueObject, "Private settings should remain opaque"); int count = doc.Objects.Items.Count;
        SectionReject(() => doc.Objects.CloneSection(section, section.Owner)); SectionReject(() => doc.Objects.EraseSection(section)); Equal(count, doc.Objects.Items.Count, "Private lifecycle rejection mutated database");
    }
}
