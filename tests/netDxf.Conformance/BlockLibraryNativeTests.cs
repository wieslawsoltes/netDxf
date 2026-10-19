using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Tables;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterBlockLibraryNativeTests()
    {
        foreach (string name in new[] { "*U1", "*D23", "*X456", "*T12" })
            Run("block-library-native/anonymous/" + name, () =>
            {
                var block = Block.CreateAnonymous(name);
                Equal(name, block.Name, "Stored anonymous name");
                Check(block.Flags.HasFlag(BlockTypeFlags.AnonymousBlock) && block.Owner.Owner == null, "Anonymous flag or detached ownership");
            });
        foreach (string name in new[] { "", "*", "*U", "*u1", "*U-1", "*U1x", "Ordinary", "*Model_Space", "*Paper_Space" })
            Run("block-library-native/anonymous-refuses/" + name, () => Throws<ArgumentException>(() => Block.CreateAnonymous(name)));
        Run("block-library-native/anonymous-null", () => Throws<ArgumentNullException>(() => Block.CreateAnonymous(null!)));
        foreach (bool binary in new[] { false, true })
        {
            Run("block-library-native/anonymous-end-record-roundtrip/" + binary, () =>
            {
                var doc = new DxfDocument(); var block = Block.CreateAnonymous("*U7");
                block.Entities.Add(new Circle(Vector3.Zero, 2));
                block.EndRecord.XData.Add(new XData(new ApplicationRegistry("LIBRARY_META")));
                block.EndRecord.XData["LIBRARY_META"].XDataRecord.Add(new XDataRecord(XDataCode.String, "retained"));
                var end = block.EndRecord; doc.Entities.Add(new Insert(block));
                Check(ReferenceEquals(end, block.EndRecord) && end.Owner == block && !string.IsNullOrEmpty(end.Handle), "ENDBLK identity/registration");
                using var bytes = new MemoryStream(); Check(doc.Save(bytes, binary), "Anonymous save"); bytes.Position = 0;
                var loaded = DxfDocument.Load(bytes)!.Entities.Inserts.Single().Block;
                Check(loaded.Flags.HasFlag(BlockTypeFlags.AnonymousBlock), "Anonymous flag lost");
                Equal("retained", (string)loaded.EndRecord.XData["LIBRARY_META"].XDataRecord.Single().Value, "ENDBLK metadata");
            });
            Run("block-library-native/stored-hatch-links-and-roundtrip/" + binary, () =>
            {
                var circle = new Circle(Vector3.Zero, 2);
                var edge = new HatchBoundaryPath.Arc(circle);
                var path = HatchBoundaryPath.FromStoredEdges(new[] { edge }, HatchBoundaryPathTypeFlags.Outermost, new[] { circle });
                Check(circle.Reactors.Count == 0 && ReferenceEquals(path.Edges.Single(), edge), "Stored constructor mutated relationships or cloned geometry");
                var hatch = new Hatch(HatchPattern.Solid, new[] { path }, true); var doc = new DxfDocument(); doc.Entities.Add(hatch);
                var paths = hatch.BoundaryPaths.ToArray(); var sources = paths.Select(p => p.Entities.ToArray()).ToArray();
                doc.Entities.Remove(hatch); Check(!hatch.Associative && path.Entities.Count == 0 && circle.Reactors.Count == 0, "Native unlink contract");
                hatch.SetBoundarySources(true, sources); doc.Entities.Add(hatch);
                Check(hatch.BoundaryPaths.SequenceEqual(paths) && path.Entities.Single() == circle && circle.Reactors.Single() == hatch, "Restored relationships or identity");
                hatch.SetBoundarySources(true, sources); Check(circle.Reactors.Count == 1, "Idempotence duplicated reactors");
                using var bytes = new MemoryStream(); Check(doc.Save(bytes, binary), "Hatch save"); bytes.Position = 0;
                var loaded = DxfDocument.Load(bytes)!; var actual = loaded.Entities.Hatches.Single();
                Check(actual.Associative && actual.BoundaryPaths.Single().PathType == HatchBoundaryPathTypeFlags.Outermost &&
                    actual.BoundaryPaths.Single().Entities.Single() == loaded.Entities.Circles.Single(), "Stored links or path classification lost");
            });
        }
        Run("block-library-native/boundary-validation-is-atomic", () =>
        {
            var circle = new Circle(Vector3.Zero, 2); var path = new HatchBoundaryPath(new[] { circle });
            var hatch = new Hatch(HatchPattern.Solid, new[] { path }, true); var doc = new DxfDocument(); doc.Entities.Add(hatch);
            var other = new DxfDocument(); var foreign = new Circle(Vector3.Zero, 3); other.Entities.Add(foreign);
            Throws<ArgumentNullException>(() => hatch.SetBoundarySources(true, null!));
            Throws<ArgumentException>(() => hatch.SetBoundarySources(true, Array.Empty<EntityObject[]>()));
            Throws<ArgumentException>(() => hatch.SetBoundarySources(true, new[] { new EntityObject[] { null! } }));
            Throws<ArgumentException>(() => hatch.SetBoundarySources(true, new[] { new EntityObject[] { hatch } }));
            Throws<ArgumentException>(() => hatch.SetBoundarySources(true, new[] { new EntityObject[] { foreign } }));
            Throws<ArgumentException>(() => hatch.SetBoundarySources(false, new[] { new EntityObject[] { circle } }));
            Check(hatch.Associative && path.Entities.Single() == circle && circle.Reactors.Single() == hatch && foreign.Reactors.Count == 0, "Refusal changed source state");
            hatch.SetBoundarySources(false, new[] { Array.Empty<EntityObject>() });
            Check(!hatch.Associative && circle.Owner != null && circle.Reactors.Count == 0 && path.Edges.Count == 1, "Unlink removed geometry");
        });
        Run("block-library-native/stored-flags-must-match-edges", () =>
        {
            var polyline = new HatchBoundaryPath.Polyline { Vertexes = new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY }, IsClosed = true };
            Throws<ArgumentException>(() => HatchBoundaryPath.FromStoredEdges(new[] { polyline }, HatchBoundaryPathTypeFlags.External, Array.Empty<EntityObject>()));
            Throws<ArgumentException>(() => HatchBoundaryPath.FromStoredEdges(new[] { new HatchBoundaryPath.Arc(new Circle(Vector3.Zero, 1)) }, HatchBoundaryPathTypeFlags.Polyline, Array.Empty<EntityObject>()));
            var flags = HatchBoundaryPathTypeFlags.Polyline | HatchBoundaryPathTypeFlags.Outermost;
            Check(HatchBoundaryPath.FromStoredEdges(new[] { polyline }, flags, Array.Empty<EntityObject>()).PathType == flags, "Valid flags changed");
        });
    }
}
