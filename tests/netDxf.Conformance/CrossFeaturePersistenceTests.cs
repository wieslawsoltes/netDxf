using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterCrossFeaturePersistenceTests()
    {
        foreach (DxfVersion version in SupportedVersions)
            foreach (bool binary in new[] { false, true })
            {
                DxfVersion v = version; bool b = binary;
                Run($"integration/atomic-empty-hatch/{v}/{b}", () => AtomicEmptyHatchRejection(v, b));
                Run($"integration/atomic-remapped-raw/{v}/{b}", () => AtomicRemappedRaw(v, b));
            }
    }

    private static void AtomicEmptyHatchRejection(DxfVersion version, bool binary) => WithAtomicDirectory(path =>
    {
        AtomicPrepare(path, true);
        var doc = new DxfDocument(version) { Name = "before failure" };
        string folder = doc.SupportFolders.WorkingFolder;
        var hatch = new Hatch(HatchPattern.Solid, false);
        doc.Entities.Add(hatch);
        string? handle = hatch.Handle;
        string seed = doc.DrawingVariables.HandleSeed;
        Throws<InvalidDataException>(() => doc.SaveAtomic(path, binary));
        AtomicUnchanged(path, true);
        Equal("before failure", doc.Name, "Atomic empty-HATCH failure changed the name");
        Equal(folder, doc.SupportFolders.WorkingFolder, "Atomic empty-HATCH failure changed the folder");
        Equal(handle, hatch.Handle, "Atomic empty-HATCH preflight changed identity");
        Equal(seed, doc.DrawingVariables.HandleSeed, "Atomic empty-HATCH preflight allocated handles");
        Equal(1, doc.Entities.Hatches.Count(), "Atomic rejection dropped the empty entity");
    });

    private static void AtomicRemappedRaw(DxfVersion version, bool binary) => WithAtomicDirectory(path =>
    {
        var typed = new DxfDocument(version); typed.Comments.Clear();
        typed.Entities.Add(new Line(new Vector3(1, 2, 3), new Vector3(4, 5, 6)));
        using var source = new MemoryStream();
        Check(typed.Save(source, binary), "Atomic remap source export failed");
        source.Position = 0;
        var raw = DxfRawDocument.Load(source);
        var index = DxfRawHandleIndex.Create(raw);
        var line = index.Occurrences.Single(x => x.Role == DxfRawHandleRole.Identity && x.Record.Name == "LINE");
        var edited = index.RemapHandles(new Dictionary<string, string> { [line.Handle] = "FFFFFF" });
        AtomicPrepare(path, true);
        edited.SaveAtomic(path, !binary);
        using var output = File.OpenRead(path);
        var saved = DxfRawDocument.Load(output);
        Equal("LINE", DxfRawHandleIndex.Create(saved).FindDefinitions("FFFFFF").Single().Record.Name,
            "Atomic save lost the remapped identity");
        output.Position = 0;
        var loaded = DxfDocument.Load(output) ?? throw new InvalidOperationException("Atomic remapped drawing rejected");
        Equal(new Vector3(1, 2, 3), loaded.Entities.Lines.Single().StartPoint, "Atomic remap changed geometry");
        output.Dispose();
        using var unchanged = new MemoryStream(); raw.Save(unchanged);
        Check(source.ToArray().SequenceEqual(unchanged.ToArray()), "Atomic remap mutated original snapshot bytes");
        CheckLifetimeFileReleased(path);
    });
}
