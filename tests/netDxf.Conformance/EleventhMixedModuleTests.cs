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
    private static void RegisterEleventhMixedModuleTests()
    {
        foreach (string file in TenthMixedFiles)
        foreach (bool input in new[] { false, true })
        foreach (bool output in new[] { false, true })
        {
            string f = file; bool i = input, o = output;
            Run($"eleventh-mixed/edit-graph/{f}/{i}/{o}", () => EleventhMixedGraph(f, i, o));
            Run($"eleventh-mixed/atomic-retry/{f}/{i}/{o}", () => EleventhMixedAtomic(f, i, o));
        }
    }

    private static Polyline2D EleventhLegacy(DxfDocument doc) => doc.Entities.Polylines2D.Single(p => p.Layer.Name == "ELEVENTH_LEGACY");
    private static Hatch EleventhHatch(DxfDocument doc) => doc.Entities.Hatches.Single(h => h.Layer.Name == "ELEVENTH_PERIODIC");
    private static DxfDocument EleventhMixedSource(string file, bool input)
    {
        var doc = TenthMixedSource(file, input); var version = doc.DrawingVariables.AcadVer;
        // Native rows name reserved STANDARD. Bind an explicitly authored clone
        // before loading this mixed fixture so resource-rename behavior is tested
        // without attempting to rename a reserved native resource.
        var nativeStyle = TableStyleObject(doc);
        var textStyle = (TextStyle)nativeStyle.Rows.First(row => row.TextStyle != null).TextStyle.Clone("ELEVENTH_SOURCE");
        doc.TextStyles.Add(textStyle);
        var raw = TableContentRaw(TableContentSave(doc, input));
        var styleRecord = raw.Sections.Single(section => section.Name == "OBJECTS").Records.Single(record => record.Name == "TABLESTYLE");
        raw = raw.WithRecord(styleRecord, styleRecord.Tags.Select(tag => tag.Code == 7 ? new DxfTag(7, textStyle.Name) : tag));
        doc = TableContentLoad(TableContentRawBytes(raw, input));
        var source = StoredDimAssocLoad(Legacy2DInput(version, input));
        var legacy = (Polyline2D)Legacy2DPolyline(source, version, input, true).Clone();
        legacy.Layer = doc.Layers.Add(new Layer("ELEVENTH_LEGACY")); doc.Entities.Add(legacy);
        Equal(4, legacy.VertexRecords.Count, "Mixed cloned legacy vertex inventory");
        Check(legacy.VertexRecords.Select(r => r.Vertex).SequenceEqual(legacy.Vertexes), "Mixed legacy model/record correspondence");
        var periodic = HatchPeriodicPacket(); periodic.FitPoints.Add(new Vector2(1, 2)); periodic.FitPoints.Add(new Vector2(3, 4));
        periodic.StartTangent = new Vector2(1, 2); periodic.EndTangent = new Vector2(-3, 4);
        var hatch = new Hatch(HatchPattern.Solid, new[] { new HatchBoundaryPath(new[] { periodic }) }, false)
        { Normal = new Vector3(1, 2, 3), Elevation = 4, Layer = doc.Layers.Add(new Layer("ELEVENTH_PERIODIC")) };
        doc.Entities.Add(hatch);
        var link = new XData(new ApplicationRegistry("ELEVENTH_LINK"));
        link.XDataRecord.Add(new XDataRecord(XDataCode.DatabaseHandle, hatch.Handle));
        link.XDataRecord.Add(new XDataRecord(XDataCode.DatabaseHandle, TenthManager(doc).Handle));
        legacy.VertexRecords[0].XData.Add(link);
        TenthGeometryReference(TenthGeometry(doc), legacy.VertexRecords[0], false);
        return doc;
    }

    private static DxfTableStyleRowValues EleventhRowValues(DxfTableStyleRow row) => new(
        row.Values.TextHeight + .375, (short)(row.Values.CellAlignment == 1 ? 2 : 1),
        (short)3, (short)5, !row.Values.BackgroundColorEnabled);

    private static void EleventhEditStyle(DxfTableStyle style)
    {
        var row = style.Rows.First(r => r.Values != null); var header = style.Header;
        style.ReplaceStyle(header == null ? null : new DxfTableStyleHeader("Eleventh style — 日本語", header.FlowDirection,
            header.StoredFlags, header.HorizontalCellMargin + .125, header.VerticalCellMargin + .25,
            !header.SuppressTitle, !header.SuppressColumnHeading), new[] { row.WithValues(EleventhRowValues(row)) });
        if (header == null) Check(style.Header == null, "Unrecognized native style header changed during row editing");
        else
        {
            Equal("Eleventh style — 日本語", style.Header.Description, "Requested style description");
            Equal(header.FlowDirection, style.Header.FlowDirection, "Retained style flow direction");
            Equal(header.StoredFlags, style.Header.StoredFlags, "Retained style flags");
            Equal(header.HorizontalCellMargin + .125, style.Header.HorizontalCellMargin, "Requested horizontal margin");
            Equal(header.VerticalCellMargin + .25, style.Header.VerticalCellMargin, "Requested vertical margin");
            Equal(!header.SuppressTitle, style.Header.SuppressTitle, "Requested title suppression");
            Equal(!header.SuppressColumnHeading, style.Header.SuppressColumnHeading, "Requested heading suppression");
        }
    }

    private static string EleventhName(string file, bool input, bool output, string phase)
        => $"eleventh-mixed-{file}-{input}-{output}-{phase}.dxf";

    private static DxfTag[] EleventhStyleHeaderTags(DxfTableStyle style) => style.Tags
        .SkipWhile(tag => !(tag.Code == 100 && Equals(tag.Value, "AcDbTableStyle"))).Skip(1)
        .TakeWhile(tag => tag.Code != 7).ToArray();

    private static object EleventhPeriodicPacket(HatchBoundaryPath.Spline edge) => new {
        degree = edge.Degree, rational = edge.IsRational, periodic = edge.IsPeriodic,
        knots = edge.Knots, controls = edge.ControlPoints.Select(p => new[] { p.X, p.Y, p.Z }).ToArray(),
        fits = edge.FitPoints.Select(p => new[] { p.X, p.Y }).ToArray(),
        start = edge.StartTangent.HasValue ? new[] { edge.StartTangent.Value.X, edge.StartTangent.Value.Y } : null,
        end = edge.EndTangent.HasValue ? new[] { edge.EndTangent.Value.X, edge.EndTangent.Value.Y } : null
    };

    private static void EleventhMixedGraph(string file, bool input, bool output)
    {
        var doc = EleventhMixedSource(file, input); var legacy = EleventhLegacy(doc); var hatch = EleventhHatch(doc);
        var geometry = TenthGeometry(doc); var manager = TenthManager(doc); var style = TableStyleObject(doc);
        var map = doc.Objects.Items.OfType<DxfStoredCellStyleMap>().Single();
        var records = legacy.VertexRecords.ToArray(); var end = legacy.EndSequenceRecord; var target = records[0];
        var oldRows = style.Rows; var oldStyleTags = style.Tags; var oldStylePacket = OwnershipTagValues(oldStyleTags).ToArray();
        var oldNames = map.Entries; var oldMapPacket = map.Payload; var oldMapValues = OwnershipTagValues(oldMapPacket).ToArray();
        var oldFormats = oldNames.Select(e => OwnershipTagValues(e.FormatPayload).ToArray()).ToArray();
        string[] names = oldNames.Select((_, index) => $"Eleventh {index} — Zażółć 日本語 \\U+0041").ToArray();
        int rowIndex = Array.FindIndex(oldRows.ToArray(), row => row.Values != null);
        Check(rowIndex >= 0 && oldRows[rowIndex].TextStyle != null, "Native classic row and exact STYLE dependency required");
        var resource = oldRows[rowIndex].TextStyle; string resourceHandle = resource.Handle;
        var expectedRow = EleventhRowValues(oldRows[rowIndex]);
        long seed = OwnershipSeed(doc); map.ReplaceEntryNames(names); EleventhEditStyle(style);
        resource.Name = "Eleventh text — Ω";
        Equal(seed, OwnershipSeed(doc), "Style/name edits allocated handles");
        Check(oldStylePacket.SequenceEqual(OwnershipTagValues(oldStyleTags)) && oldMapValues.SequenceEqual(OwnershipTagValues(oldMapPacket)), "Style/map edits mutated older complete packet snapshots");
        Check(oldRows[rowIndex].Values.TextHeight != style.Rows[rowIndex].Values.TextHeight && ReferenceEquals(style.Rows[rowIndex].TextStyle, resource), "TABLESTYLE edit lost row snapshot or bound STYLE identity");
        Check(oldNames.Select(e => e.Name).SequenceEqual(names) == false, "Old map names changed with new names");
        for (int index = 0; index < oldNames.Count; index++)
            Check(oldFormats[index].SequenceEqual(OwnershipTagValues(map.Entries[index].FormatPayload)), "Map name edit changed nested formatting");

        var edge = hatch.BoundaryPaths.Single().Edges.OfType<HatchBoundaryPath.Spline>().Single();
        var local = ((Spline)edge.ConvertTo()).PolygonalVertexes(32);
        var matrix = HatchPatternAffineMatrix(3); var translation = new Vector3(7, -11, 2);
        var expected = local.Select(p => matrix * HatchAffineWorld(hatch, new Vector2(p.X, p.Y)) + translation).ToArray();
        hatch.TransformBy(matrix, translation);
        var curve = hatch.CreateBoundary(true).OfType<Spline>().Single(); curve.Layer = doc.Layers.Add(new Layer("ELEVENTH_CURVE"));
        var samples = curve.PolygonalVertexes(32);
        for (int index = 0; index < expected.Length; index++) HatchAffineNear(expected[index], samples[index], "Mixed transformed periodic boundary world curve");
        target.XData["ELEVENTH_LINK"].XDataRecord.Add(new XDataRecord(XDataCode.DatabaseHandle, curve.Handle));
        legacy.Vertexes[0].StartWidthOverride = 0; legacy.Vertexes[1].EndWidthOverride = null; legacy.Vertexes[2].VertexIdentifier = 1107;
        legacy.TransformBy(Matrix3.Scale(2), new Vector3(8, -2, 0)); legacy.Reverse();
        Check(legacy.VertexRecords.SequenceEqual(records.Reverse()) && ReferenceEquals(end, legacy.EndSequenceRecord), "Mixed transform/reverse replaced actual legacy records");
        Check(ReferenceEquals(geometry.Cells[0].GeometryReference, target) && ReferenceEquals(target.Vertex, legacy.Vertexes.Last()), "Table geometry lost exact legacy child after reorder");
        Check(!doc.Entities.Remove(legacy) && !doc.Entities.Remove(hatch) && !doc.Entities.Remove(curve), "Mixed live dependencies allowed target removal");
        Throws<InvalidOperationException>(() => doc.Objects.EraseSectionManager(manager));
        Equal(0, doc.Objects.Validate().Count, "Edited mixed database graph validation");
        var expectedEdge = (HatchBoundaryPath.Spline)hatch.BoundaryPaths.Single().Edges.OfType<HatchBoundaryPath.Spline>().Single().Clone();
        var expectedHeader = EleventhStyleHeaderTags(style);
        string name = EleventhName(file, input, output, "edited");
        byte[] bytes = TableContentSave(doc, output, name);
        var reload = TableContentLoad(bytes); var copy = EleventhLegacy(reload); var reloadedStyle = TableStyleObject(reload);
        Check(copy.VertexRecords.Select(r => r.Handle).SequenceEqual(legacy.VertexRecords.Select(r => r.Handle)) && copy.EndSequenceRecord.Handle == end.Handle, "Reload changed retained record identities or order");
        Check(ReferenceEquals(TenthGeometry(reload).Cells[0].GeometryReference, reload.GetObjectByHandle(target.Handle)), "Reload rebound TABLEGEOMETRY to the wrong legacy child");
        Equal(expectedRow.TextHeight, reloadedStyle.Rows[rowIndex].Values.TextHeight, "Edited native row text height");
        Equal(expectedRow.CellAlignment, reloadedStyle.Rows[rowIndex].Values.CellAlignment, "Edited native row alignment");
        Equal(expectedRow.StoredTextColor, reloadedStyle.Rows[rowIndex].Values.StoredTextColor, "Edited native row color");
        Equal(expectedRow.StoredFillColor, reloadedStyle.Rows[rowIndex].Values.StoredFillColor, "Edited native row fill");
        Equal(expectedRow.BackgroundColorEnabled, reloadedStyle.Rows[rowIndex].Values.BackgroundColorEnabled, "Edited native row background");
        Equal(resourceHandle, reloadedStyle.Rows[rowIndex].TextStyle.Handle, "Renamed STYLE identity");
        Equal(resource.Name, reloadedStyle.Rows[rowIndex].TextStyle.Name, "Renamed STYLE emitted name");
        Check(OwnershipTagValues(expectedHeader).SequenceEqual(OwnershipTagValues(EleventhStyleHeaderTags(reloadedStyle))), "Complete edited TABLESTYLE header changed on reload");
        Check(reload.Objects.Items.OfType<DxfStoredCellStyleMap>().Single().Entries.Select(e => e.Name).SequenceEqual(names), "Mixed map names did not survive output");
        var loadedCurve = (Spline)reload.GetObjectByHandle(curve.Handle); var loadedSamples = loadedCurve.PolygonalVertexes(32);
        for (int index = 0; index < expected.Length; index++) HatchAffineNear(expected[index], loadedSamples[index], "Reloaded periodic curve");
        Check(EleventhHatch(reload).Associative && EleventhHatch(reload).BoundaryPaths.Single().Entities.Single().Handle == curve.Handle, "Reload lost actual HATCH source association");
        HatchRelationsEqual(expectedEdge, EleventhHatch(reload).BoundaryPaths.Single().Edges.OfType<HatchBoundaryPath.Spline>().Single());
        var loadedTarget = (Polyline2DRecord)reload.GetObjectByHandle(target.Handle);
        Check(loadedTarget.XData["ELEVENTH_LINK"].XDataRecord.Select(item => item.Value).SequenceEqual(new object[] { hatch.Handle, manager.Handle, curve.Handle }), "Reload changed ordered retained-child graph references");
        Throws<InvalidOperationException>(() => reload.Objects.EraseSectionManager(TenthManager(reload)));

        File.WriteAllText(Path.Combine(ArtifactDirectory, name + ".json"), JsonSerializer.Serialize(new {
            source = file, binary = output, version = doc.DrawingVariables.AcadVer.ToString(),
            legacy = legacy.Handle, records = legacy.VertexRecords.Select(r => r.Handle).ToArray(), end = end.Handle,
            geometry = geometry.Handle, target = target.Handle, hatch = hatch.Handle, curve = curve.Handle, manager = manager.Handle,
            map = map.Handle, names, style = style.Handle, row = rowIndex, textStyle = resourceHandle, textStyleName = resource.Name,
            height = expectedRow.TextHeight, alignment = expectedRow.CellAlignment, textColor = expectedRow.StoredTextColor,
            fillColor = expectedRow.StoredFillColor, background = expectedRow.BackgroundColorEnabled,
            styleHeader = expectedHeader.Select(tag => new { code = tag.Code, value = tag.Value }).ToArray(),
            hatchPacket = EleventhPeriodicPacket(expectedEdge),
            samples = samples.Select(p => new[] { p.X, p.Y, p.Z }).ToArray()
        }));

        TenthGeometryReference(geometry, null, false); Check(doc.Entities.Remove(legacy), "Released legacy chain stayed protected");
        Check(records.All(r => ReferenceEquals(r.Owner, legacy) && doc.GetObjectByHandle(r.Handle) == null) && doc.GetObjectByHandle(end.Handle) == null, "Detached legacy child identities stayed registered");
        hatch.UnLinkBoundary(); Check(doc.Entities.Remove(curve) && doc.Entities.Remove(hatch), "Detached legacy references still protected HATCH boundary");
        doc.Objects.EraseSectionManager(manager);
        var released = TableContentLoad(TableContentSave(doc, output, EleventhName(file, input, output, "released")));
        Check(released.GetObjectByHandle(target.Handle) == null && released.GetObjectByHandle(end.Handle) == null && released.GetObjectByHandle(curve.Handle) == null, "Output resurrected retired mixed dependencies");
        Equal(0, released.Objects.Validate().Count, "Released mixed graph validation");
    }

    private static void EleventhMixedAtomic(string file, bool input, bool output)
    {
        var doc = EleventhMixedSource(file, input); var legacy = EleventhLegacy(doc); var hatch = EleventhHatch(doc);
        var style = TableStyleObject(doc); var map = doc.Objects.Items.OfType<DxfStoredCellStyleMap>().Single(); var manager = TenthManager(doc);
        var curve = hatch.CreateBoundary(true).Single(); var edge = hatch.BoundaryPaths.Single().Edges.OfType<HatchBoundaryPath.Spline>().Single();
        var oldStyle = style.Tags; var oldRows = style.Rows; var oldMap = map.Payload; var oldNames = map.Entries;
        var oldSources = hatch.BoundaryPaths.Single().Entities.ToArray(); var oldReactors = curve.Reactors.ToArray(); var members = doc.Entities.All.ToArray();
        var records = legacy.VertexRecords.ToArray(); long seed = OwnershipSeed(doc);
        IEnumerable<string> FailedNames()
        { yield return "uncommitted"; doc.Objects.EraseSectionManager(manager); foreach (var item in oldNames.Skip(1)) yield return item.Name; }
        Throws<InvalidOperationException>(() => map.ReplaceEntryNames(FailedNames()));
        IEnumerable<DxfTableStyleRowEdit> FailedRows()
        { var row = style.Rows.First(r => r.Values != null); yield return row.WithValues(EleventhRowValues(row)); doc.Objects.EraseSectionManager(manager); }
        Throws<InvalidOperationException>(() => style.ReplaceStyle(null, FailedRows()));
        double original = edge.ControlPoints[0].X; edge.ControlPoints[0].X += .125;
        Throws<NotSupportedException>(() => hatch.CreateBoundary(true)); edge.ControlPoints[0].X = original;
        Check(ReferenceEquals(oldStyle, style.Tags) && ReferenceEquals(oldRows, style.Rows) && ReferenceEquals(oldMap, map.Payload) && ReferenceEquals(oldNames, map.Entries), "Cross-module callback failure changed style/map snapshots");
        Check(hatch.Associative && oldSources.SequenceEqual(hatch.BoundaryPaths.Single().Entities) && oldReactors.SequenceEqual(curve.Reactors), "Failed boundary conversion changed old association/reactors");
        Check(members.SequenceEqual(doc.Entities.All) && records.SequenceEqual(legacy.VertexRecords), "Cross-module failure changed registration or actual child identities");
        Equal(seed, OwnershipSeed(doc), "Cross-module failure allocated handles");
        DxfVersion version = doc.DrawingVariables.AcadVer; doc.DrawingVariables.AcadVer = DxfVersion.AutoCad2004;
        using var stream = new MemoryStream(); bool rejected = false;
        try { rejected = !doc.Save(stream, output); } catch (Exception error) when (error is InvalidOperationException or NotSupportedException or InvalidDataException) { rejected = true; }
        Check(rejected && stream.Length == 0, "Mixed incompatible source-profile save emitted bytes");
        Equal(seed, OwnershipSeed(doc), "Mixed incompatible save allocated handles"); doc.DrawingVariables.AcadVer = version;
        map.ReplaceEntryNames(oldNames.Select((_, index) => "Eleventh retry " + index)); EleventhEditStyle(style);
        TableContentSave(doc, output, EleventhName(file, input, output, "retry"));
    }
}
