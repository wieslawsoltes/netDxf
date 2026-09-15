using netDxf;
using netDxf.Entities;
using netDxf.Header;

namespace NetDxf.Conformance;
internal static partial class Program
{
    private static void RegisterPolyline2DVersionCompatibilityTests()
    {
        foreach (DxfVersion source in SupportedVersions)
        foreach (DxfVersion target in SupportedVersions)
        foreach (bool binary in new[] { false, true })
            Run($"version-compatibility/legacy2d/{source}/{target}/{binary}",
                () => CompatibilityLegacy2D(source, target, binary));
    }

    private static void CompatibilityLegacy2D(DxfVersion source, DxfVersion target, bool binary)
    {
        var producer = StoredDimAssocLoad(Legacy2DInput(source, binary));
        var document = new DxfDocument(source);
        // Isolate the retained-chain rule from producer VPORT SUN slots and CLASS counts.
        document.Entities.Add((Polyline2D)Legacy2DPolyline(producer, source, binary, true).Clone());
        var parents = document.Entities.Polylines2D.ToArray();
        var actualRecords = parents.SelectMany(parent => parent.VertexRecords.Append(parent.EndSequenceRecord)).ToArray();
        Equal(5, actualRecords.Length, "The actual legacy chain includes its SEQEND identity");
        // Legacy group 91 belongs to the retained source profile, including R2000.
        foreach (var parent in parents) parent.Vertexes[0].VertexIdentifier = 901;
        document.DrawingVariables.LastSavedBy = string.Empty;
        var report = CompatibilityAnalyze(document, target);
        bool rejected = source != target;
        Equal(rejected, report.HasKnownRejections, "Legacy source-profile prediction");
        var diagnostics = report.Diagnostics.Where(item => item.SourceObject is Polyline2DRecord).ToArray();
        Equal(rejected ? actualRecords.Length : 0, diagnostics.Length, "Every actual retained record reports its source profile");
        Check(!report.Diagnostics.Any(item => item.Code == "LWPOLYLINE_VERTEX_ID_PROFILE"), "Legacy group 91 was misclassified as a lightweight identifier");
        int omittedCounts = target == DxfVersion.AutoCad2000
            ? document.Classes.Count(definition => definition.InstanceCount.HasValue) : 0;
        Equal(diagnostics.Length + omittedCounts, report.Diagnostics.Count, "Exact source-profile and CLASS omission inventory");
        foreach (var diagnostic in diagnostics)
        {
            Equal("STORED_SOURCE_PROFILE", diagnostic.Code, "Stable profile diagnostic code");
            Equal("SourceVersion", diagnostic.PropertyPath, "Profile property belongs to the actual record");
            Check(actualRecords.Any(record => ReferenceEquals(record, diagnostic.SourceObject)), "Diagnostic lost actual source identity");
            Equal(((DxfObject)diagnostic.SourceObject).Handle, diagnostic.SourceHandle, "Captured physical source handle");
        }
        bool saved = CompatibilitySave(document, target, binary, "legacy2d-" + source, out byte[] output);
        Equal(!rejected, saved, "Actual writer agrees with the source-profile report");
        if (rejected)
        {
            Equal(0, output.Length, "Rejected source-profile save leaves the stream empty");
            Check(actualRecords.All(record => ReferenceEquals(document.GetObjectByHandle(record.Handle), record)), "Rejected save changed child registration");
        }
        else
        {
            var reloaded = StoredDimAssocLoad(output);
            Equal(target, reloaded.DrawingVariables.AcadVer, "Written target profile");
            foreach (var parent in parents)
            {
                var copy = (Polyline2D)reloaded.GetObjectByHandle(parent.Handle);
                Equal((int?)901, copy.Vertexes[0].VertexIdentifier, "Retained group 91 survives its actual legacy profile");
                Check(copy.VertexRecords.Select(record => record.Handle).SequenceEqual(parent.VertexRecords.Select(record => record.Handle)), "Actual VERTEX identities changed");
                Equal(parent.EndSequenceRecord.Handle, copy.EndSequenceRecord.Handle, "Actual SEQEND identity changed");
            }
        }
    }
}
