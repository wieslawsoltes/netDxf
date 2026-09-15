using netDxf;
using netDxf.Entities;
using netDxf.Header;

namespace NetDxf.Conformance;
internal static partial class Program
{
    private static void RegisterPolyfaceVersionCompatibilityTests()
    {
        foreach (DxfVersion target in SupportedVersions)
            foreach (bool binary in new[] { false, true })
                foreach (bool retained in new[] { false, true })
                    Run($"version-compatibility/polyface/{target}/{binary}/{retained}",
                        () => CompatibilityPolyface(target, binary, retained));
    }

    private static void CompatibilityPolyface(DxfVersion target, bool binary, bool retained)
    {
        var document = new DxfDocument(DxfVersion.AutoCad2010);
        document.Entities.Add(new PolyfaceMesh(
            new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY },
            new[] { new short[] { 1, -2, 3 } }));
        if (retained)
        {
            using var input = new MemoryStream();
            Check(document.Save(input, binary), "Create source-profile Polyface fixture");
            input.Position = 0;
            document = DxfDocument.Load(input) ?? throw new Exception("Reload Polyface source");
        }
        // This fixture isolates mesh profile rules from the environment-derived
        // LastSavedBy value, whose R2000 omission has separate coverage.
        document.DrawingVariables.LastSavedBy = string.Empty;
        PolyfaceMesh mesh = document.Entities.PolyfaceMeshes.Single();
        var actualRecords = mesh.VertexRecords.Concat(mesh.FaceRecords).ToList();
        if (mesh.EndSequenceRecord != null) actualRecords.Add(mesh.EndSequenceRecord);
        Equal(retained ? 5 : 0, actualRecords.Count, "Actual retained child inventory");

        var report = CompatibilityAnalyze(document, target);
        bool rejected = retained && target != DxfVersion.AutoCad2010;
        Equal(rejected, report.HasKnownRejections, "Retained source-profile prediction");
        int omittedClassCounts = target == DxfVersion.AutoCad2000
            ? document.Classes.Count(definition => definition.InstanceCount.HasValue) : 0;
        Equal(omittedClassCounts > 0, report.HasKnownLosses, "Loaded CLASS count omission remains visible beside the mesh rule");
        var diagnostics = report.Diagnostics.Where(item => item.SourceObject is PolyfaceMeshRecord).ToList();
        Equal(rejected ? 5 : 0, diagnostics.Count, "One diagnostic per actual retained record");
        Equal(omittedClassCounts, report.Diagnostics.Count(item => item.Code == "CLASS_INSTANCE_COUNT_OMITTED"
            && item.SourceObject is DxfClass definition && definition.InstanceCount.HasValue
            && item.PropertyPath == "InstanceCount"), "Only actual declared CLASS counts are omitted");
        Equal(diagnostics.Count + omittedClassCounts, report.Diagnostics.Count, "Exact mesh and CLASS diagnostic inventory");
        foreach (var diagnostic in diagnostics)
        {
            Equal("STORED_SOURCE_PROFILE", diagnostic.Code, "Stable source-profile code");
            Equal("SourceVersion", diagnostic.PropertyPath, "Property relative to actual record");
            Check(actualRecords.Any(record => ReferenceEquals(record, diagnostic.SourceObject)), "Diagnostic uses actual registered source identity");
            Equal(((DxfObject)diagnostic.SourceObject).Handle, diagnostic.SourceHandle, "Captured physical source handle");
        }
        bool saved = CompatibilitySave(document, target, binary, "polyface-" + retained, out byte[] output);
        Equal(!rejected, saved, "Actual writer agrees with profile diagnostic");
        if (rejected)
        {
            Equal(0, output.Length, "Profile rejection leaves caller stream empty");
            Check(actualRecords.All(record => ReferenceEquals(document.GetObjectByHandle(record.Handle), record)), "Rejected Save retains exact registrations");
        }
        else
        {
            using var input = new MemoryStream(output);
            var reloaded = DxfDocument.Load(input) ?? throw new Exception("Reload accepted target profile");
            Equal(target, reloaded.DrawingVariables.AcadVer, "Actual written target profile");
            var result = reloaded.Entities.PolyfaceMeshes.Single();
            Check(result.Faces.Single().VertexIndexes.SequenceEqual(new short[] { 1, -2, 3 }), "Signed face topology survives target save");
            Equal(3, result.VertexRecords.Count, "Accepted output creates retained coordinate records");
            Equal(1, result.FaceRecords.Count, "Accepted output creates retained face record");
            Check(result.EndSequenceRecord != null, "Accepted output retains SEQEND");
        }
    }
}
