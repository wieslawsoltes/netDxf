using netDxf;
using netDxf.Entities;
using netDxf.IO;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RunSourceIdentityMetadataTests()
    {
        foreach (bool binary in new[] { false, true })
        {
            Run($"source-identity-metadata/retained-reactor/{binary}", () => SourceIdentityMetadata(false, false, true, binary));
            foreach (bool before in new[] { false, true }) foreach (bool metadata in new[] { false, true })
                Run($"source-identity-metadata/reject-duplicate/{binary}/{before}/{metadata}", () => SourceIdentityMetadata(true, before, metadata, binary));
        }
    }

    private static void SourceIdentityMetadata(bool duplicate, bool before, bool metadata, bool binary)
    {
        var document = new DxfDocument();
        var source = new Line(Vector3.Zero, Vector3.UnitX);
        var reactor = new Circle(Vector3.Zero, 2);
        document.Entities.Add(source); document.Entities.Add(reactor);
        if (metadata) source.PersistentReactors.Add(reactor);
        using var original = new MemoryStream();
        Check(document.Save(original, binary), "Source identity metadata fixture export"); original.Position = 0;
        var raw = DxfRawDocument.Load(original);
        var record = raw.Sections.Single(section => section.Name == "ENTITIES").Records.Single(entity => entity.Name == "LINE");
        var tags = raw.Tags.ToList();
        if (duplicate)
            tags.InsertRange(before ? record.StartTagIndex : record.StartTagIndex + record.Tags.Count, new[]
            {
                new DxfTag(0, "UNSUPPORTED_CURVE"), new DxfTag(5, source.Handle), new DxfTag(330, source.Owner.Record.Handle),
                new DxfTag(100, "AcDbEntity"), new DxfTag(8, "0"), new DxfTag(100, "AcDbFutureCurve")
            });
        using var input = new MemoryStream(); DxfRawDocument.Create(tags).Save(input, binary); input.Position = 0;
        if (duplicate)
        {
#if DEBUG
            bool rejected = false;
            try { DxfDocument.Load(input); }
            catch (FormatException error)
            {
                rejected = error.Message.Contains("ambiguous physical source identity", StringComparison.Ordinal)
                    && error.Message.Contains(source.Handle, StringComparison.Ordinal);
            }
            Check(rejected, "Unreferenced retained entity with a duplicate physical declaration must reject before metadata can be skipped");
#else
            Check(DxfDocument.Load(input) == null, "Release must reject ambiguous retained source identity without silently discarding metadata");
#endif
            return;
        }
        var loaded = DxfDocument.Load(input) ?? throw new Exception("Unique retained entity was rejected");
        for (int cycle = 0; cycle < 2; cycle++)
        {
            var retained = (Line)loaded.GetObjectByHandle(source.Handle);
            Equal(1, retained.PersistentReactors.Count, "Unique source retains its common reactor metadata");
            Check(ReferenceEquals(retained.PersistentReactors.Single(), loaded.GetObjectByHandle(reactor.Handle)), "Reactor points to its exact registered source object");
            Equal(Vector3.Zero, retained.StartPoint, "Retained source start geometry");
            Equal(Vector3.UnitX, retained.EndPoint, "Retained source end geometry");
            using var output = new MemoryStream(); Check(loaded.Save(output, cycle == 0 ? !binary : binary), "Unique source metadata export");
            output.Position = 0; loaded = DxfDocument.Load(output) ?? throw new Exception("Unique source metadata reload failed");
        }
    }
}
