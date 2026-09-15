using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;
using netDxf.Tables;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static readonly string[] NinthMixedFiles = { "acad_table_simple.dxf", "acad_table_with_blk_ref.dxf" };

    private static void RegisterNinthMixedModuleTests()
    {
        foreach (string file in NinthMixedFiles) foreach (bool binary in new[] { false, true })
        {
            string f = file; bool b = binary;
            Run($"ninth-mixed/reference-release/{f}/{b}", () => NinthMixedReferences(f, b));
            Run($"ninth-mixed/rejected-edits/{f}/{b}", () => NinthMixedRejected(f, b));
        }
    }

    private static DxfStoredCellStyleMap NinthMap(DxfDocument doc) => doc.Objects.Items.OfType<DxfStoredCellStyleMap>().Single();
    private static DxfStoredSectionManager NinthManager(DxfDocument doc) => doc.Objects.Items.OfType<DxfStoredSectionManager>().Single();
    private static PolygonMesh NinthMesh(DxfDocument doc) => doc.Entities.PolygonMeshes.Single(mesh => mesh.Layer.Name == "NINTH_GRAPH");
    private static Section NinthSection(DxfDocument doc, string suffix) => doc.Entities.Sections.Single(section => section.Name == "Ninth " + suffix);
    private static string[] NinthSnapshot(DxfVersionCompatibilityReport report) => report.Diagnostics.Select(diagnostic =>
        string.Join("|", diagnostic.Code, diagnostic.Kind, diagnostic.SourceHandle, diagnostic.SourceCodeName,
            diagnostic.PropertyPath, diagnostic.Message)).ToArray();

    private static DxfSectionTypeSettings NinthSettings(IEnumerable<DxfObject> sources) => new(1, 17, sources,
        null!, "", Array.Empty<DxfSectionGeometrySettings>());

    private static DxfDocument NinthMixedSource(string file, bool binary)
    {
        // Native TABLESTYLE/CELLSTYLEMAP/STYLE/LTYPE packets remain in their original source profile.
        var doc = TableContentLoad(TableContentSourceBytes(file));
        var mesh = new PolygonMesh(2, 2, new[] { Vector3.Zero, new Vector3(2, 0, 0), new Vector3(0, 3, 0), new Vector3(2, 3, 0) });
        mesh.Layer = doc.Layers.Add(new Layer("NINTH_GRAPH")); doc.Entities.Add(mesh);
        doc = TableContentLoad(TableContentSave(doc, binary)); mesh = NinthMesh(doc);
        Check(mesh.VertexRecords.Count == 4 && mesh.EndSequenceRecord != null, "Mixed ordinary mesh records were not retained");
        var metadata = new XData(new ApplicationRegistry("NINTH_MESH_METADATA"));
        metadata.XDataRecord.Add(new XDataRecord(XDataCode.String, "retained vertex metadata"));
        mesh.VertexRecords[0].XData.Add(metadata);
        var map = NinthMap(doc); var style = map.References.OfType<TextStyle>().First(); var linetype = map.References.OfType<Linetype>().First();
        var first = SectionExample("SECTIONOBJECT"); first.Name = "Ninth first";
        var second = SectionExample("SECTIONOBJECT"); second.Name = "Ninth second";
        doc.Entities.Add(first); doc.Entities.Add(second);
        foreach (Section section in new[] { first, second })
        {
            var settings = new DxfSectionSettings { SectionType = 1 };
            // These are explicitly authored generic source-pointer storage links, not rendering semantics.
            settings.SetTypeSettings(new[] { NinthSettings(new DxfObject[] { map, mesh.VertexRecords[0], null!, mesh.VertexRecords[0], style, linetype }) });
            doc.Objects.SetSectionSettings(section, settings);
        }
        var view = doc.Views.Add(new View("NINTH_LIVE_VIEW")); view.LiveSection = first;
        var placeholder = new DxfXRecord(); doc.Objects.Root.Add("ACAD_SECTION_MANAGER", placeholder, false);
        string managerHandle = placeholder.Handle;
        var raw = TableContentRaw(TableContentSave(doc, binary));
        var row = raw.Sections.Single(section => section.Name == "OBJECTS").Records.Single(record => record.Tags.Any(tag => tag.Code == 5 && Equals(tag.Value, managerHandle)));
        var header = row.Tags.TakeWhile(tag => tag.Code != 100).Select(tag => tag.Code == 0 ? new DxfTag(0, "SECTION_MANAGER") : tag);
        var body = new[] { new DxfTag(100, "AcDbSectionManager"), new DxfTag(70, (short)1), new DxfTag(90, 3),
            new DxfTag(330, first.Handle), new DxfTag(330, second.Handle), new DxfTag(330, first.Handle) };
        return TableContentLoad(TableContentRawBytes(raw.WithRecord(row, header.Concat(body)), binary));
    }

    private static void NinthMixedReferences(string file, bool binary)
    {
        var doc = NinthMixedSource(file, binary); var map = NinthMap(doc); var manager = NinthManager(doc);
        var mesh = NinthMesh(doc); var target = mesh.VertexRecords[0]; var records = mesh.VertexRecords.Concat(new[] { mesh.EndSequenceRecord }).ToArray();
        var first = NinthSection(doc, "first"); var second = NinthSection(doc, "second"); var view = doc.Views["NINTH_LIVE_VIEW"];
        var style = map.References.OfType<TextStyle>().First(); var linetype = map.References.OfType<Linetype>().First();
        var mapPacket = OwnershipTagValues(map.Payload).ToArray(); var oldMembers = manager.Sections; var oldTags = manager.Tags;
        TableContentSave(doc, binary, $"ninth-mixed-{file}-{binary}-input.dxf");
        long seed = OwnershipSeed(doc); DxfVersion sourceVersion = doc.DrawingVariables.AcadVer;
        DxfVersionCompatibilityReport? snapshot = null;
        IEnumerable<Section> Replacement()
        {
            snapshot = doc.AnalyzeVersionCompatibility(DxfVersion.AutoCad2004);
            Equal(sourceVersion, doc.DrawingVariables.AcadVer, "Analysis inside enumeration mutated the source version");
            yield return second; yield return second;
        }
        manager.ReplaceSections(Replacement(), false);
        Check(snapshot != null && snapshot.HasKnownRejections, "Mixed target-version analysis omitted known rejections");
        var captured = snapshot!; var snapshotFields = NinthSnapshot(captured);
        var recordDiagnostic = captured.Diagnostics.Single(d => ReferenceEquals(d.SourceObject, target) && d.Code == "STORED_SOURCE_PROFILE");
        Check(captured.Diagnostics.Any(d => ReferenceEquals(d.SourceObject, map) && d.Code == "STORED_SOURCE_PROFILE")
            && captured.Diagnostics.Any(d => ReferenceEquals(d.SourceObject, manager) && d.Code == "STORED_SOURCE_PROFILE"), "Mixed source-profile diagnostics lost exact map/manager identities");
        Check(oldMembers.SequenceEqual(new[] { first, second, first }) && oldTags[1].Value.Equals((short)1), "Mixed membership snapshots changed");
        Equal(seed, OwnershipSeed(doc), "Membership edit or diagnostic inspection allocated handles");
        Throws<InvalidOperationException>(() => doc.Objects.EraseSection(first));
        Check(!doc.Entities.Remove(mesh), "Manager replacement released a still-referenced mesh child");
        view.ClearLiveSectionReference(); doc.Objects.EraseSection(first);
        Check(first.IsErased && !doc.Entities.Remove(mesh), "Second SECTION settings did not retain the child dependency");
        Check(((DxfSectionSettings)second.GeometrySettings).TypeSettings[0].SourceObjects.Where(source => source != null).Any(source => ReferenceEquals(source, map)), "SECTION source map identity changed");
        mesh.Vertexes[1] = new Vector3(4, 0, 2); mesh.IsClosedInU = true;
        Check(ReferenceEquals(target, mesh.VertexRecords[0]), "Coordinate/closure edit changed retained mesh identity");
        TableContentSave(doc, binary, $"ninth-mixed-{file}-{binary}-edited.dxf");
        ((DxfSectionSettings)second.GeometrySettings).SetTypeSettings(new[] { NinthSettings(new DxfObject[] { map, style, linetype }) });
        Check(doc.Entities.Remove(mesh), "Explicitly released mesh remained protected by an unrelated map reference");
        Check(mesh.Owner == null && records.All(record => ReferenceEquals(record.Owner, mesh) && doc.GetObjectByHandle(record.Handle) == null), "Removed mesh records lost structural ownership or remain registered");
        Check(ReferenceEquals(recordDiagnostic.SourceObject, target) && recordDiagnostic.SourceHandle == target.Handle && recordDiagnostic.SourceCodeName == "VERTEX", "Retired source changed captured diagnostic identity");
        Check(snapshotFields.SequenceEqual(NinthSnapshot(captured)), "Diagnostic snapshot changed after source retirement");
        var current = doc.AnalyzeVersionCompatibility(DxfVersion.AutoCad2004);
        Check(!current.Diagnostics.Any(d => records.Any(record => ReferenceEquals(record, d.SourceObject)) || ReferenceEquals(d.SourceObject, first)), "Fresh analysis retained erased source records");
        Check(current.Diagnostics.Any(d => ReferenceEquals(d.SourceObject, map)) && current.Diagnostics.Any(d => ReferenceEquals(d.SourceObject, manager)), "Reference release hid retained source-profile restrictions");
        Check(manager.Sections.SequenceEqual(new[] { second, second }) && !manager.RequiresFullUpdate, "Mesh release changed section-manager membership");
        Check(mapPacket.SequenceEqual(OwnershipTagValues(map.Payload)), "Mixed edits changed native CELLSTYLEMAP payload");
        Equal(0, doc.Objects.Validate().Count, "Mixed graph failed after ordered dependency release");
        var loaded = TableContentLoad(TableContentSave(doc, binary, $"ninth-mixed-{file}-{binary}-released.dxf"));
        var loadedMap = NinthMap(loaded); var loadedManager = NinthManager(loaded); var loadedSecond = NinthSection(loaded, "second");
        Check(loadedManager.Sections.All(section => ReferenceEquals(section, loadedSecond)) && loadedManager.Sections.Count == 2, "Reload rebound repeated manager targets");
        Check(ReferenceEquals(((DxfSectionSettings)loadedSecond.GeometrySettings).TypeSettings[0].SourceObjects[0], loadedMap), "Reload lost the SECTION/settings/native-map chain");
        Check(!loaded.Entities.PolygonMeshes.Any(entity => entity.Layer.Name == "NINTH_GRAPH") && mapPacket.SequenceEqual(OwnershipTagValues(loadedMap.Payload)), "Released output changed native map or retained mesh");
    }

    private static void NinthMixedRejected(string file, bool binary)
    {
        var doc = NinthMixedSource(file, binary); var map = NinthMap(doc); var manager = NinthManager(doc); var mesh = NinthMesh(doc);
        var mapPacket = OwnershipTagValues(map.Payload).ToArray(); var managerPacket = OwnershipTagValues(manager.Tags).ToArray();
        var identities = mesh.VertexRecords.Concat(new[] { mesh.EndSequenceRecord }).ToArray(); var points = mesh.Vertexes.ToArray();
        var sourceVersion = doc.DrawingVariables.AcadVer; long seed = OwnershipSeed(doc); int objectCount = doc.Objects.Items.Count;
        var other = new DxfDocument(sourceVersion); var foreign = SectionExample("SECTIONOBJECT"); other.Entities.Add(foreign);
        Throws<ArgumentException>(() => manager.ReplaceSections(new[] { manager.Sections[1], foreign }, false));
        Throws<NotSupportedException>(() => doc.Objects.EraseOwnedTree(map));
        Throws<NotSupportedException>(() => doc.Objects.CloneObject((DxfDatabaseObject)map.Owner, doc.Objects.Root, "NINTH_INVALID_COPY"));
        var report = doc.AnalyzeVersionCompatibility(DxfVersion.AutoCad2004);
        Check(report.HasKnownRejections && ReferenceEquals(report.Diagnostics.First(d => ReferenceEquals(d.SourceObject, mesh.VertexRecords[0])).SourceObject, mesh.VertexRecords[0]), "Mixed rejection report lost exact child identity");
        doc.DrawingVariables.AcadVer = DxfVersion.AutoCad2004;
        Throws<InvalidOperationException>(() => manager.ReplaceSections(Array.Empty<Section>(), false));
        using (var output = new MemoryStream())
        {
            bool rejected = false;
            try { rejected = !doc.Save(output, binary); }
            catch (Exception error) when (error is NotSupportedException || error is InvalidOperationException) { rejected = true; }
            Check(rejected, "Mixed unsupported target version saved"); Equal(0L, output.Length, "Mixed preflight rejection wrote bytes");
        }
        doc.DrawingVariables.AcadVer = sourceVersion;
        Check(mapPacket.SequenceEqual(OwnershipTagValues(map.Payload)) && managerPacket.SequenceEqual(OwnershipTagValues(manager.Tags)), "Rejected edit/preflight changed source packets");
        Check(identities.SequenceEqual(mesh.VertexRecords.Concat(new[] { mesh.EndSequenceRecord })) && points.SequenceEqual(mesh.Vertexes), "Rejected edit/preflight changed mesh geometry or child identities");
        Equal(seed, OwnershipSeed(doc), "Rejected mixed operation allocated handles"); Equal(objectCount, doc.Objects.Items.Count, "Rejected mixed operation changed object membership");
        Check(!doc.AnalyzeVersionCompatibility(sourceVersion).HasKnownRejections, "Restored source profile still reports a known mixed rejection");
        Equal(0, doc.Objects.Validate().Count, "Rejected mixed operations invalidated the graph");
        TableContentSave(doc, binary, $"ninth-mixed-{file}-{binary}-retry.dxf");
    }
}
