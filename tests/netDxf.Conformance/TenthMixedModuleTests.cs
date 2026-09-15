using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;
using netDxf.Tables;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static readonly string[] TenthMixedFiles = { "acad_table_simple.dxf", "acad_table_with_blk_ref.dxf" };
    private const string TenthOpaqueCode = "QUALIFIED_FUTURE_CURVE";
    private static void RegisterTenthMixedModuleTests()
    {
        foreach (string file in TenthMixedFiles) foreach (bool input in new[] { false, true }) foreach (bool output in new[] { false, true })
        {
            string f = file; bool i = input, o = output;
            Run($"tenth-mixed/reference-release/{f}/{i}/{o}", () => TenthMixedReferences(f, i, o));
            Run($"tenth-mixed/atomic-retry/{f}/{i}/{o}", () => TenthMixedRejected(f, i, o));
        }
    }
    private static PolyfaceMesh TenthMesh(DxfDocument doc) => doc.Entities.PolyfaceMeshes.Single(mesh => mesh.Layer.Name == "TENTH_GRAPH");
    private static Hatch TenthHatch(DxfDocument doc) => doc.Entities.Hatches.Single(hatch => hatch.Layer.Name == "TENTH_PATTERN");
    private static Section TenthSection(DxfDocument doc, string name) => doc.Entities.Sections.Single(section => section.Name == "Tenth " + name);
    private static DxfStoredTableContent TenthContent(DxfDocument doc) => doc.Objects.Items.OfType<DxfStoredTableContent>().Single();
    private static object TenthScalar(object value) => value switch { int i => i + 7, double d => d + .125, string text => "Tenth " + text, Vector3 point => point + new Vector3(1, 2, 3), _ => throw new Exception("Unexpected native scalar kind") };
    private static void TenthContentEdit(DxfStoredTableContent content)
    {
        var old = content.StoredValues; var payload = content.Payload; var oldPacket = OwnershipTagValues(payload).ToArray();
        var refs = content.References; var targets = refs.ToArray(); var style = content.TableStyle;
        content.ReplaceContent("Tenth edited name", "Tenth edited description", style, new[] { old[0].WithValue(TenthScalar(old[0].Value), "Tenth explicit display") });
        Check(oldPacket.SequenceEqual(OwnershipTagValues(payload)) && old[0].Value.Equals(content.StoredValues[0].Value) == false, "Content edit changed its oldest packet/value snapshots");
        Check(targets.SequenceEqual(refs) && targets.SequenceEqual(content.References) && ReferenceEquals(content.TableStyle, style), "Content edit changed actual native dependencies or TABLE-owned style");
        Equal(TenthScalar(old[0].Value), content.StoredValues[0].Value, "Qualified content scalar edit");
        Equal("Tenth explicit display", content.StoredValues[0].FormattedText, "Explicit content display edit");
        Check(old.Skip(1).Select(value => value.Value).SequenceEqual(content.StoredValues.Skip(1).Select(value => value.Value)), "Content edit changed unselected native scalars");
        for (int i = 0; i < old.Count; i++)
        {
            Equal(old[i].StoredFormatFlags, content.StoredValues[i].StoredFormatFlags, "Content format flags changed");
            Equal(old[i].StoredUnitType, content.StoredValues[i].StoredUnitType, "Content units changed");
            Equal(old[i].FormatString, content.StoredValues[i].FormatString, "Content format string changed");
        }
    }
    private static DxfStoredTableGeometry TenthGeometry(DxfDocument doc) => doc.Objects.Items.OfType<DxfStoredTableGeometry>().Single();
    private static DxfStoredSectionManager TenthManager(DxfDocument doc) => doc.Objects.Items.OfType<DxfStoredSectionManager>().Single();
    private static DxfOpaqueEntity TenthOpaque(DxfDocument doc) => doc.Entities.All.OfType<DxfOpaqueEntity>().Single(entity => entity.CodeName == TenthOpaqueCode);
    private static string TenthHandle(DxfRawRecord row) => (string)row.Tags.Single(tag => tag.Code == 5).Value;
    private static DxfStoredTableGeometryCell TenthCell(DxfStoredTableGeometryCell old, DxfObject? target, bool edit) => new(
        edit ? old.GeometryDataFlags ^ 128 : old.GeometryDataFlags, old.WidthWithGap + (edit ? .5 : 0), old.HeightWithGap - (edit ? .25 : 0), target,
        old.Geometry.Select(value => new DxfStoredTableCellGeometry(value.TopLeftDistance, value.CenterDistance,
            value.ContentWidth + (edit ? .125 : 0), value.ContentHeight, value.Width, value.Height, value.StoredValue95)));
    private static void TenthGeometryReference(DxfStoredTableGeometry geometry, DxfObject? target, bool edit)
        => geometry.ReplaceGeometry(geometry.RowCount, geometry.ColumnCount, new[] { TenthCell(geometry.Cells[0], target, edit) }.Concat(geometry.Cells.Skip(1)));
    private static DxfDocument TenthMixedSource(string file, bool input)
    {
        byte[] bytes = TableContentSourceBytes(file);
        if (input) bytes = TableContentRawBytes(TableContentRaw(bytes), true);
        var doc = TableContentLoad(bytes);
        var mesh = new PolyfaceMesh(new[] { Vector3.Zero, new Vector3(2, 0, 0), new Vector3(0, 3, 0), new Vector3(2, 3, 0) }, new[] { new short[] { 1, 2, 3, 4 } });
        mesh.Layer = doc.Layers.Add(new Layer("TENTH_GRAPH")); doc.Entities.Add(mesh);
        var pattern = HatchPatternAffineExample();
        var hatch = new Hatch(pattern, new[] { new HatchBoundaryPath(new HatchBoundaryPath.Edge[] {
            new HatchBoundaryPath.Line { Start = Vector2.Zero, End = new Vector2(2, 0) },
            new HatchBoundaryPath.Line { Start = new Vector2(2, 0), End = new Vector2(2, 3) },
            new HatchBoundaryPath.Line { Start = new Vector2(2, 3), End = new Vector2(0, 3) },
            new HatchBoundaryPath.Line { Start = new Vector2(0, 3), End = Vector2.Zero } }) }, false);
        hatch.Layer = doc.Layers.Add(new Layer("TENTH_PATTERN")); hatch.Elevation = 4;
        hatch.Normal = doc.DrawingVariables.AcadVer == DxfVersion.AutoCad2018 ? new Vector3(1, 2, 3) : Vector3.UnitZ; doc.Entities.Add(hatch);
        var first = SectionExample("SECTIONOBJECT"); first.Name = "Tenth first";
        var second = SectionExample("SECTIONOBJECT"); second.Name = "Tenth second"; doc.Entities.Add(first); doc.Entities.Add(second);
        var placeholder = new Line(Vector3.Zero, Vector3.UnitX); placeholder.Layer = mesh.Layer; doc.Entities.Add(placeholder);
        string opaqueHandle = placeholder.Handle, block = placeholder.Owner.Record.Handle;
        var raw = TableContentRaw(TableContentSave(doc, input));
        var entities = raw.Sections.Single(section => section.Name == "ENTITIES");
        var face = entities.Records.Single(row => row.Name == "VERTEX" && row.Tags.Any(tag => tag.Code == 70 && (short)tag.Value == 128));
        string faceHandle = TenthHandle(face);
        // Explicitly synthetic public storage: inactive extrema, omitted-by-zero active slots,
        // out-of-order physical slots, face-first sequence and independent advisory counts.
        raw = raw.WithRecord(face, face.Tags.Where(tag => tag.Code < 71 || tag.Code > 74).Concat(new[] {
            new DxfTag(74, short.MinValue), new DxfTag(72, (short)0), new DxfTag(71, (short)-1), new DxfTag(73, short.MaxValue) }));
        entities = raw.Sections.Single(section => section.Name == "ENTITIES");
        var header = entities.Records.Single(row => row.Name == "POLYLINE" && row.Tags.Any(tag => tag.Code == 8 && (string)tag.Value == "TENTH_GRAPH"));
        raw = raw.WithRecord(header, header.Tags.Where(tag => tag.Code != 71 && tag.Code != 72).Concat(new[] { new DxfTag(71, (short)-17), new DxfTag(72, (short)123) }));
        entities = raw.Sections.Single(section => section.Name == "ENTITIES"); face = entities.Records.Single(row => row.Name == "VERTEX" && TenthHandle(row) == faceHandle);
        var firstCoordinate = entities.Records.First(row => row.Name == "VERTEX" && row.Tags.Any(tag => tag.Code == 70 && (short)tag.Value == 192));
        var all = raw.Tags.ToList(); all.RemoveRange(face.StartTagIndex, face.Tags.Count); all.InsertRange(firstCoordinate.StartTagIndex, face.Tags); raw = raw.WithTags(all);
        var opaqueRow = raw.Sections.Single(section => section.Name == "ENTITIES").Records.Single(row => row.Tags.Any(tag => tag.Code == 5 && (string)tag.Value == opaqueHandle));
        raw = raw.WithRecord(opaqueRow, new[] { new DxfTag(0, TenthOpaqueCode), new DxfTag(5, opaqueHandle), new DxfTag(330, block),
            new DxfTag(100, "AcDbEntity"), new DxfTag(8, "TENTH_GRAPH"), new DxfTag(100, "AcDbQualifiedFutureCurve"), new DxfTag(340, faceHandle) });
        doc = TableContentLoad(TableContentRawBytes(raw, input)); mesh = TenthMesh(doc); hatch = TenthHatch(doc);
        Check(mesh.RecordSequence[0] == mesh.FaceRecords.Single() && mesh.Faces[0].VertexIndexes.SequenceEqual(new short[] { -1 }), "Synthetic retained face-first public grammar changed");
        Check(TenthOpaque(doc).Handle == opaqueHandle, "Declared-schema opaque source identity was not retained");
        var metadata = new XData(new ApplicationRegistry("TENTH_FACE_LINK")); metadata.XDataRecord.Add(new XDataRecord(XDataCode.String, "authored retained face reference"));
        metadata.XDataRecord.Add(new XDataRecord(XDataCode.DatabaseHandle, hatch.Handle)); mesh.FaceRecords[0].XData.Add(metadata);
        var map = doc.Objects.Items.OfType<DxfStoredCellStyleMap>().Single(); first = TenthSection(doc, "first"); second = TenthSection(doc, "second");
        foreach (var section in new[] { first, second })
        {
            var settings = new DxfSectionSettings { SectionType = 1 };
            settings.SetTypeSettings(new[] { NinthSettings(new DxfObject[] { map, mesh.FaceRecords[0], null!, mesh.FaceRecords[0] }) }); doc.Objects.SetSectionSettings(section, settings);
        }
        doc.Classes.Add(new DxfClass("SECTION_MANAGER", "AcDbSectionManager", "ObjectDBX Classes") { ProxyFlags = 1024, InstanceCount = 37 });
        var manager = doc.Objects.CreateSectionManager(new[] { first, second, first }, true);
        Check(manager.SourceVersion == doc.DrawingVariables.AcadVer && doc.Classes["SECTION_MANAGER"].InstanceCount == 1, "Canonical manager source profile/class count changed");
        return doc;
    }
    private static string TenthOutput(string file, bool input, bool output, string phase) => $"tenth-mixed-{file}-{input}-{output}-{phase}.dxf";
    private static void TenthAffine(Hatch hatch)
    {
        var matrix = HatchPatternAffineMatrix(3); var translation = new Vector3(7, -11, 0);
        var original = hatch.Pattern; var snapshot = (HatchPattern)original.Clone();
        var expectedSeeds = hatch.SeedPoints.Select(point => matrix * HatchAffineWorld(hatch, point) + translation).ToArray();
        var expected = original.LineDefinitions.Select(line => HatchPatternSamples(original, line).Select(point => matrix * HatchAffineWorld(hatch, point) + translation).ToArray()).ToArray();
        hatch.TransformBy(matrix, translation); HatchPatternAffineEqual(snapshot, original);
        Check(!ReferenceEquals(original, hatch.Pattern), "Mixed affine transform mutated the oldest pattern object");
        for (int line = 0; line < hatch.Pattern.LineDefinitions.Count; line++)
        {
            var points = HatchPatternSamples(hatch.Pattern, hatch.Pattern.LineDefinitions[line]);
            for (int i = 0; i < points.Length; i++) HatchAffineNear(expected[line][i], HatchAffineWorld(hatch, points[i]), "Mixed affine world family/dash point");
        }
        Equal(expectedSeeds.Length, hatch.SeedPoints.Count, "Mixed affine seed inventory");
        for (int i = 0; i < expectedSeeds.Length; i++) HatchAffineNear(expectedSeeds[i], HatchAffineWorld(hatch, hatch.SeedPoints[i]), "Mixed affine world seed point");
        HatchAffineNear(new Vector3(13, -7, 0), new Vector3(hatch.Pattern.Origin.X, hatch.Pattern.Origin.Y, 0), "Separate pattern Origin follows WCS affine map");
    }
    private static void TenthMixedReferences(string file, bool input, bool output)
    {
        var doc = TenthMixedSource(file, input); var geometry = TenthGeometry(doc); var manager = TenthManager(doc); var mesh = TenthMesh(doc); var hatch = TenthHatch(doc); var opaque = TenthOpaque(doc);
        var first = TenthSection(doc, "first"); var second = TenthSection(doc, "second"); var map = doc.Objects.Items.OfType<DxfStoredCellStyleMap>().Single();
        var oldContentPacket = TenthContent(doc).Payload; var oldContentValues = OwnershipTagValues(oldContentPacket).ToArray(); var oldContentScalars = TenthContent(doc).StoredValues; var oldFirstScalar = oldContentScalars[0].Value;
        var oldCells = geometry.Cells; var oldGeometryPacket = geometry.Payload; var oldRefs = geometry.References; var oldMembers = manager.Sections; var oldManagerPacket = manager.Tags;
        var oldOpaqueTags = opaque.SourceTags; var oldOpaqueValues = OwnershipTagValues(oldOpaqueTags).ToArray();
        var oldPattern = hatch.Pattern; var oldPatternValues = (HatchPattern)oldPattern.Clone(); var records = mesh.RecordSequence.ToArray(); var target = mesh.FaceRecords.Single();
        Check(opaque.References.Any(value => ReferenceEquals(value, target)), "Opaque link did not bind the actual source Polyface child");
        string managerHandle = manager.Handle, opaqueHandle = opaque.Handle, hatchHandle = hatch.Handle; string[] settingsHandles = { first.GeometrySettings.Handle, second.GeometrySettings.Handle };
        TableContentSave(doc, output, TenthOutput(file, input, output, "input"));
        var report = doc.AnalyzeVersionCompatibility(DxfVersion.AutoCad2004); var diagnostics = NinthSnapshot(report);
        var recordDiagnostics = records.Select(record => report.Diagnostics.Single(value => ReferenceEquals(value.SourceObject, record) && value.Code == "STORED_SOURCE_PROFILE")).ToArray();
        var opaqueDiagnostic = report.Diagnostics.Single(value => ReferenceEquals(value.SourceObject, opaque) && value.Code == "STORED_SOURCE_PROFILE");
        var managerDiagnostic = report.Diagnostics.Single(value => ReferenceEquals(value.SourceObject, manager) && value.Code == "STORED_SOURCE_PROFILE");
        long seed = OwnershipSeed(doc); TenthContentEdit(TenthContent(doc)); TenthGeometryReference(geometry, manager, true);
        Equal(seed, OwnershipSeed(doc), "Geometry replacement allocated handles"); Check(oldRefs.Count == 0 && oldCells[0].GeometryReference == null && !ReferenceEquals(oldGeometryPacket, geometry.Payload), "Geometry edit changed its oldest snapshots");
        manager.ReplaceSections(new[] { second, first, second }, false);
        Check(oldMembers.SequenceEqual(new[] { first, second, first }) && oldManagerPacket[1].Value.Equals((short)1), "Mixed manager edit changed original snapshots");
        Throws<InvalidOperationException>(() => doc.Objects.EraseSectionManager(manager));
        Throws<InvalidOperationException>(() => doc.Objects.EraseSection(first));
        Check(!doc.Entities.Remove(mesh), "Live SECTION/opaque face dependency was ignored");
        Check(!doc.Entities.Remove(hatch), "Live retained face XData1005 dependency was ignored");
        TenthAffine(hatch); HatchPatternAffineEqual(oldPatternValues, oldPattern);
        TableContentSave(doc, output, TenthOutput(file, input, output, "linked"));
        var linkedCells = geometry.Cells; var linkedPacket = geometry.Payload;
        TenthGeometryReference(geometry, null, false); Check(ReferenceEquals(linkedCells[0].GeometryReference, manager) && !ReferenceEquals(linkedPacket, geometry.Payload), "Geometry reference release mutated linked snapshots");
        doc.Objects.EraseSectionManager(manager);
        Check(manager.IsErased && manager.Handle == managerHandle && managerDiagnostic.SourceHandle == managerHandle && ReferenceEquals(managerDiagnostic.SourceObject, manager), "Manager erasure lost terminal/snapshot identity");
        Check(ReferenceEquals(doc.GetObjectByHandle(first.Handle), first) && ReferenceEquals(doc.GetObjectByHandle(second.Handle), second), "Manager erasure cascaded into Sections");
        Check(doc.Classes["SECTION_MANAGER"].InstanceCount == 0 && !doc.Entities.Remove(mesh) && !doc.Entities.Remove(hatch), "Manager erasure released independent live dependencies");
        TableContentSave(doc, output, TenthOutput(file, input, output, "manager-erased"));
        doc.Objects.EraseSection(first); Check(!doc.Entities.Remove(mesh), "First SECTION erasure released the second SECTION/opaque guard");
        ((DxfSectionSettings)second.GeometrySettings).SetTypeSettings(new[] { NinthSettings(new DxfObject[] { map }) });
        Check(!doc.Entities.Remove(mesh), "Releasing SECTION pointers also released the independent opaque source pointer");
        TableContentSave(doc, output, TenthOutput(file, input, output, "opaque-guard"));
        Check(doc.Entities.Remove(opaque) && doc.GetObjectByHandle(opaqueHandle) == null, "Explicit opaque removal did not retire its actual source identity");
        Check(opaque.SourceHandle == opaqueHandle && ReferenceEquals(opaque.SourceTags, oldOpaqueTags) && oldOpaqueValues.SequenceEqual(OwnershipTagValues(opaque.SourceTags)), "Opaque retirement changed the oldest complete source packet snapshot");
        Check(!doc.Entities.Remove(hatch), "Opaque removal released retained face XData before mesh detach");
        Check(doc.Entities.Remove(mesh), "Released mesh stayed protected after both SECTION and opaque sources were removed");
        Check(records.All(record => ReferenceEquals(record.Owner, mesh) && doc.GetObjectByHandle(record.Handle) == null), "Mesh detach left a live child or lost structural snapshot ownership");
        Check(doc.Entities.Remove(hatch) && doc.GetObjectByHandle(hatchHandle) == null, "Retired face XData still protected the HATCH");
        Check(oldContentValues.SequenceEqual(OwnershipTagValues(oldContentPacket)) && oldContentScalars[0].Value.Equals(oldFirstScalar), "Old native content packet/scalar snapshot changed during graph retirement");
        Check(NinthSnapshot(report).SequenceEqual(diagnostics) && ReferenceEquals(managerDiagnostic.SourceObject, manager), "Old diagnostic snapshots changed after source retirement");
        for (int i = 0; i < records.Length; i++) Check(ReferenceEquals(recordDiagnostics[i].SourceObject, records[i]) && recordDiagnostics[i].SourceHandle == records[i].Handle && recordDiagnostics[i].SourceCodeName == records[i].CodeName, "Retired child diagnostic lost captured identity/code or live object");
        Check(ReferenceEquals(opaqueDiagnostic.SourceObject, opaque) && opaqueDiagnostic.SourceHandle == opaqueHandle && opaqueDiagnostic.SourceCodeName == TenthOpaqueCode, "Retired opaque diagnostic lost captured metadata/live object");
        var current = doc.AnalyzeVersionCompatibility(DxfVersion.AutoCad2004);
        Check(!current.Diagnostics.Any(value => ReferenceEquals(value.SourceObject, manager) || records.Any(record => ReferenceEquals(value.SourceObject, record)) || ReferenceEquals(value.SourceObject, opaque)), "New diagnostics include retired graph sources");
        Check(geometry.Cells[0].GeometryReference == null && geometry.References.Count == 0 && oldRefs.Count == 0, "Retired manager dependency remains in geometry snapshots");
        Check(doc.GetObjectByHandle(settingsHandles[0]) == null && doc.GetObjectByHandle(settingsHandles[1]) == second.GeometrySettings, "SECTION owned settings release order changed");
        Check(oldMembers.Count == 3 && oldManagerPacket[1].Value.Equals((short)1), "Terminal manager changed its oldest membership/packet snapshots");
        Equal(0, doc.Objects.Validate().Count, "Mixed released graph failed object validation");
        var loaded = TableContentLoad(TableContentSave(doc, output, TenthOutput(file, input, output, "released")));
        Check(loaded.GetObjectByHandle(managerHandle) == null && loaded.GetObjectByHandle(opaqueHandle) == null && !loaded.Entities.PolyfaceMeshes.Any(entity => entity.Layer.Name == "TENTH_GRAPH"), "Reload resurrected a retired graph identity");
        Check(TenthGeometry(loaded).Cells[0].GeometryReference == null && TenthSection(loaded, "second").GeometrySettings != null, "Released geometry/Section state did not survive output");
    }
    private static void TenthMixedRejected(string file, bool input, bool output)
    {
        var doc = TenthMixedSource(file, input); var geometry = TenthGeometry(doc); var manager = TenthManager(doc); var hatch = TenthHatch(doc); var mesh = TenthMesh(doc);
        TenthGeometryReference(geometry, manager, true); manager.ReplaceSections(new[] { TenthSection(doc, "second"), TenthSection(doc, "first"), TenthSection(doc, "second") }, false);
        var content = TenthContent(doc); var contentPacket = content.Payload; var contentValues = content.StoredValues;
        var packet = geometry.Payload; var cells = geometry.Cells; var managerPacket = manager.Tags; var records = mesh.RecordSequence.ToArray();
        var pattern = hatch.Pattern; var patternCopy = (HatchPattern)pattern.Clone(); long seed = OwnershipSeed(doc); var members = doc.Objects.Items.ToArray();
        var other = new DxfDocument(doc.DrawingVariables.AcadVer); var foreign = other.Objects.CreateSectionManager(Array.Empty<Section>(), false);
        Throws<ArgumentException>(() => TenthGeometryReference(geometry, foreign, false));
        Throws<InvalidOperationException>(() => doc.Objects.CreateSectionManager(manager.Sections, true));
        IEnumerable<DxfStoredTableGeometryCell> ReentrantErase()
        {
            yield return TenthCell(geometry.Cells[0], null, false);
            doc.Objects.EraseSectionManager(manager);
            foreach (var cell in geometry.Cells.Skip(1)) yield return cell;
        }
        Throws<InvalidOperationException>(() => geometry.ReplaceGeometry(geometry.RowCount, geometry.ColumnCount, ReentrantErase()));
        IEnumerable<DxfStoredTableContentValueEdit> ContentReentrantErase()
        {
            yield return content.StoredValues[0].WithValue(TenthScalar(content.StoredValues[0].Value), "never committed");
            doc.Objects.EraseSectionManager(manager);
        }
        Throws<InvalidOperationException>(() => content.ReplaceContent("never committed", "never committed", content.TableStyle, ContentReentrantErase()));
        Check(ReferenceEquals(contentPacket, content.Payload) && ReferenceEquals(contentValues, content.StoredValues), "Content iterator failure partially changed a stored native packet");
        ManagerLifecycleReject(() => hatch.TransformBy(Matrix3.Scale(0, 0, 0), Vector3.Zero));
        Check(ReferenceEquals(packet, geometry.Payload) && ReferenceEquals(cells, geometry.Cells) && ReferenceEquals(managerPacket, manager.Tags), "Cross-module failure partially changed stored packet snapshots");
        Check(ReferenceEquals(pattern, hatch.Pattern) && records.SequenceEqual(mesh.RecordSequence), "Cross-module failure changed HATCH pattern or retained record identities"); HatchPatternAffineEqual(patternCopy, pattern);
        Equal(seed, OwnershipSeed(doc), "Cross-module failure allocated handles"); Check(members.SequenceEqual(doc.Objects.Items), "Cross-module failure changed object registration");
        var version = doc.DrawingVariables.AcadVer; doc.DrawingVariables.AcadVer = DxfVersion.AutoCad2004; using var bytes = new MemoryStream(); bool rejected = false;
        try { rejected = !doc.Save(bytes, output); } catch (Exception error) when (error is InvalidOperationException or NotSupportedException or InvalidDataException) { rejected = true; }
        Check(rejected && bytes.Length == 0, "Mixed source-profile rejection emitted bytes"); Equal(seed, OwnershipSeed(doc), "Mixed profile rejection allocated handles"); doc.DrawingVariables.AcadVer = version;
        TenthContentEdit(content); TenthAffine(hatch); TableContentSave(doc, output, TenthOutput(file, input, output, "retry"));
    }
}
