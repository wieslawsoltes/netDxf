using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;
using netDxf.Tables;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterEighthMixedModuleTests()
    {
        foreach (DxfVersion version in new[] { DxfVersion.AutoCad2013, DxfVersion.AutoCad2018 })
        foreach (bool binary in new[] { false, true })
        {
            var profile = version; bool transport = binary;
            Run($"eighth-mixed/references-and-topology/{profile}/{transport}", () => EighthMixedReferences(profile, transport));
            Run($"eighth-mixed/rejected-transform/{profile}/{transport}", () => EighthMixedRejectedTransform(profile, transport));
        }
    }

    private static DxfDocument EighthMixedSource(DxfVersion version, bool binary)
    {
        string year = version.ToString().Replace("AutoCad", "");
        string source = $"tests/fixtures/sunstudy-producer/typed-carriers/ixmilia-sunstudy-R{year}-ascii-no-dates-hours.dxf";
        var doc = LoadSunStudy(SunStudySource(source), binary) ?? throw new InvalidDataException("Mixed SUNSTUDY source");
        var study = GetSunStudy(doc);
        var section = SectionExample("SECTIONOBJECT"); doc.Entities.Add(section);
        ((View)study.View).LiveSection = section;
        var polyline = new Polyline3D(new[] { Vector3.Zero, new Vector3(3, 0, 1), new Vector3(5, 4, 2) });
        doc.Entities.Add(polyline);
        var spline = new HatchBoundaryPath.Spline {
            Degree = 2, IsPeriodic = true, IsRational = false,
            Knots = new double[] { -2, -2, -2, 3, 3, 3 },
            ControlPoints = new[] { new Vector3(0, 0, 1), new Vector3(2, 5, -2.5), new Vector3(7, 0, 1) }
        };
        var hatch = new Hatch(HatchPattern.Solid, new[] { new HatchBoundaryPath(new HatchBoundaryPath.Edge[] {
            spline, new HatchBoundaryPath.Line { Start = new Vector2(7, 0), End = Vector2.Zero }
        }) }, false);
        doc.Entities.Add(hatch);
        var placeholder = new DxfXRecord(); doc.Objects.Root.Add("MIXED_GEOMETRY", placeholder);
        string placeholderHandle = placeholder.Handle;
        doc = TableContentLoad(TableContentSave(doc, binary));
        polyline = doc.Entities.Polylines3D.Single();
        polyline.InsertVertex(1, new Vector3(1, 2, 3));
        string target = polyline.VertexRecords[1].Handle;
        var raw = TableContentRaw(TableContentSave(doc, binary));
        var record = raw.Sections.Single(s => s.Name == "OBJECTS").Records.Single(r => r.Tags.Any(t => t.Code == 5 && Equals(t.Value, placeholderHandle)));
        int marker = record.Tags.ToList().FindIndex(t => t.Code == 100);
        var payload = TableGeometryPayload(); payload[7] = new DxfTag(330, target);
        raw = raw.WithRecord(record, record.Tags.Take(marker).Select(t => t.Code == 0 ? new DxfTag(0, "TABLEGEOMETRY") : t).Concat(payload));
        return TableGeometryLoad(raw, binary);
    }

    private static void EighthMixedReferences(DxfVersion version, bool binary)
    {
        var doc = EighthMixedSource(version, binary);
        TableContentSave(doc, binary, $"eighth-mixed-{version}-{binary}-input.dxf");
        var study = GetSunStudy(doc); var view = (View)study.View;
        var section = view.LiveSection; var geometry = TableGeometryObject(doc);
        var polyline = doc.Entities.Polylines3D.Single();
        var target = (Polyline3DRecord)geometry.Cells[0].GeometryReference;
        var end = polyline.EndSequenceRecord;
        var studyTags = OwnershipTagValues(study.Payload).ToArray();
        var geometryTags = OwnershipTagValues(geometry.Payload).ToArray();
        long seed = OwnershipSeed(doc);
        Throws<NotSupportedException>(() => polyline.RemoveVertexAt(1));
        Equal(seed, OwnershipSeed(doc), "Mixed reference rejection allocated a handle");
        Check(ReferenceEquals(target, polyline.VertexRecords[1]), "Rejected deletion changed referenced slot");
        polyline.MoveVertex(1, polyline.Vertexes.Count - 1);
        Check(ReferenceEquals(target, polyline.VertexRecords[^1]), "Stored geometry pointer failed to follow moved identity");
        Equal(new Vector3(1, 2, 3), polyline.Vertexes[^1], "Moved identity lost its coordinates");
        polyline.InsertVertex(0, new Vector3(-1, -2, -3));
        var transient = polyline.VertexRecords[0]; polyline.RemoveVertexAt(0);
        Check(transient.IsRemoved && transient.Owner == null && doc.GetObjectByHandle(transient.Handle) == null, "Unreferenced inserted record was not retired");
        Check(ReferenceEquals(end, polyline.EndSequenceRecord), "Sequence terminator changed during explicit edits");
        Check(!doc.Views.Remove(view), "SUNSTUDY lost its actual VIEW target");
        Check(!doc.Entities.Remove(section), "VIEW lost its actual SECTION target");
        var secondView = (View)view.Clone("MIXED_SECOND_VIEW"); doc.Views.Add(secondView);
        view.ClearLiveSectionReference();
        Check(!doc.Entities.Remove(section), "Second VIEW did not retain its SECTION reference");
        view.LiveSection = section; secondView.ClearLiveSectionReference();
        Check(doc.Views.Remove(secondView), "Unreferenced VIEW clone could not be removed");
        var hatch = doc.Entities.Hatches.Single();
        hatch.TransformBy(new Matrix3(2, 1, 0, 0, 3, 0, 0, 0, 1), new Vector3(4, -2, 0));
        var transformed = hatch.BoundaryPaths.Single().Edges.OfType<HatchBoundaryPath.Spline>().Single();
        Check(!transformed.IsRational && transformed.IsPeriodic, "Mixed HATCH transform changed stored flags");
        Equal(-2.5, transformed.ControlPoints[1].Z, "Mixed HATCH transform changed a stored weight");
        Equal(new Vector3(13, 13, -2.5), transformed.ControlPoints[1], "Mixed HATCH affine control location");
        Check(studyTags.SequenceEqual(OwnershipTagValues(study.Payload)), "Mutable consumers changed immutable SUNSTUDY payload");
        Check(geometryTags.SequenceEqual(OwnershipTagValues(geometry.Payload)), "Topology editing changed immutable TABLEGEOMETRY payload");
        Equal(0, doc.Objects.Validate().Count, "Mixed object graph failed validation");
        string output = $"eighth-mixed-{version}-{binary}.dxf";
        var loaded = TableContentLoad(TableContentSave(doc, binary, output));
        var loadedStudy = GetSunStudy(loaded); var loadedGeometry = TableGeometryObject(loaded);
        var loadedPolyline = loaded.Entities.Polylines3D.Single();
        Check(ReferenceEquals(((View)loadedStudy.View).LiveSection, loaded.Entities.Sections.Single()), "Reload lost the SUNSTUDY/VIEW/SECTION chain");
        Check(ReferenceEquals(loadedGeometry.Cells[0].GeometryReference, loadedPolyline.VertexRecords[^1]), "Reload lost TABLEGEOMETRY/VERTEX identity");
        Check(studyTags.SequenceEqual(OwnershipTagValues(loadedStudy.Payload)) && geometryTags.SequenceEqual(OwnershipTagValues(loadedGeometry.Payload)), "Immutable mixed packets changed on output");
    }

    private static void EighthMixedRejectedTransform(DxfVersion version, bool binary)
    {
        var doc = EighthMixedSource(version, binary); var hatch = doc.Entities.Hatches.Single();
        var path = hatch.BoundaryPaths.Single(); var edge = path.Edges.OfType<HatchBoundaryPath.Spline>().Single();
        var points = edge.ControlPoints.ToArray(); var study = GetSunStudy(doc); var geometry = TableGeometryObject(doc);
        var studyTags = OwnershipTagValues(study.Payload).ToArray(); var geometryTags = OwnershipTagValues(geometry.Payload).ToArray();
        var target = geometry.Cells[0].GeometryReference; var section = ((View)study.View).LiveSection;
        long seed = OwnershipSeed(doc); int objects = doc.Objects.Items.Count;
        bool rejected = false;
        try { hatch.TransformBy(new Matrix3(1, 0, 0, 0, 0, 0, 0, 0, 1), Vector3.Zero); }
        catch (Exception error) when (error is ArgumentException || error is InvalidOperationException || error is NotSupportedException) { rejected = true; }
        Check(rejected, "Collapsed HATCH plane was accepted");
        Check(ReferenceEquals(path, hatch.BoundaryPaths.Single()) && points.SequenceEqual(edge.ControlPoints), "Rejected transform mutated boundary state");
        Check(ReferenceEquals(target, geometry.Cells[0].GeometryReference) && ReferenceEquals(section, ((View)study.View).LiveSection), "Rejected transform changed another module's references");
        Check(studyTags.SequenceEqual(OwnershipTagValues(study.Payload)) && geometryTags.SequenceEqual(OwnershipTagValues(geometry.Payload)), "Rejected transform changed immutable packets");
        Equal(seed, OwnershipSeed(doc), "Rejected transform allocated a handle"); Equal(objects, doc.Objects.Items.Count, "Rejected transform changed object membership");
        Check(TableContentSave(doc, binary).Length > 0, "Mixed document could not save after rejected transform");
    }
}
