using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterHatchBoundaryFlagsTests()
    {
        foreach (DxfVersion version in SupportedVersions)
            foreach (bool binary in new[] { false, true })
            {
                DxfVersion v = version; bool b = binary;
                for (int flags = 0; flags < 32; ++flags)
                {
                    int f = flags;
                    Run($"hatch/path-flags/read-clone-transform/{v}/{b}/{f}", () => HatchFlagsRoundTrip(v, b, f));
                }
                Run($"hatch/path-flags/open-polyline/{v}/{b}", () => HatchFlagsOpenPolyline(v, b));
                Run($"hatch/path-flags/output-corpus/{v}/{b}", () => HatchFlagsCorpus(v, b));
                Run($"hatch/path-flags/unknown-bits/{v}/{b}", () => HatchFlagsRoundTrip(v, b, 0x40000002));
            }
        Run("hatch/path-flags/associative-update", HatchFlagsUpdate);
        Run("hatch/path-flags/constructor-defaults", HatchFlagsDefaults);
        Run("hatch/path-flags/independent-clone", () => HatchFlagsIndependent(false));
        Run("hatch/path-flags/independent-transform", () => HatchFlagsIndependent(true));
    }

    private static List<DxfTag> HatchFlagsTags(DxfVersion version, int flags, bool closed = true)
    {
        var tags = HatchDoubleTags(version, HatchType.UserDefined, 0);
        int start = tags.FindIndex(t => t.Code == 92), end = tags.FindIndex(t => t.Code == 97);
        if ((flags & 2) != 0)
        {
            tags[start] = new(92, flags);
            tags[tags.FindIndex(t => t.Code == 73)] = new(73, closed ? (short)1 : (short)0);
        }
        else
        {
            tags.RemoveRange(start, end - start);
            var edges = new List<DxfTag> { new(92, flags), new(93, 4) };
            var points = new[] { new Vector2(0, 0), new Vector2(10, 0), new Vector2(10, 10), new Vector2(0, 10) };
            for (int i = 0; i < points.Length; ++i)
            {
                Vector2 a = points[i], b = points[(i + 1) % points.Length];
                edges.AddRange(new DxfTag[] { new(72, (short)1), new(10, a.X), new(20, a.Y), new(11, b.X), new(21, b.Y) });
            }
            tags.InsertRange(start, edges);
        }
        return tags;
    }

    private static Hatch ReadFlagsHatch(DxfVersion version, bool binary, int flags, bool closed = true)
    {
        using var input = new MemoryStream(RawFixtureBytes(HatchFlagsTags(version, flags, closed), binary));
        var doc = DxfDocument.Load(input) ?? throw new InvalidOperationException("Boundary flag fixture failed to load.");
        Check(input.CanRead, "Path flags closed caller stream.");
        return doc.Entities.Hatches.Single();
    }

    private static void CheckPathFlags(Hatch hatch, int flags)
    {
        var path = hatch.BoundaryPaths.Single();
        Equal((HatchBoundaryPathTypeFlags)flags, path.PathType, "Boundary classification bits were rewritten");
        bool polyline = path.Edges.Count == 1 && path.Edges[0] is HatchBoundaryPath.Polyline;
        Equal((flags & 2) != 0, polyline, "Polyline marker does not describe the actual edge representation");
        Equal((flags & 2) != 0 ? 1 : 4, path.Edges.Count, "Flag preservation changed closed-boundary geometry");
    }

    private static void HatchFlagsRoundTrip(DxfVersion version, bool binary, int flags)
    {
        var original = ReadFlagsHatch(version, binary, flags);
        CheckPathFlags(original, flags);
        var clone = (Hatch)original.Clone(); CheckPathFlags(clone, flags);
        var block = new Block("FlagBlock"); block.Entities.Add(clone);
        var insert = new Insert(block, new Vector3(4, 5, 0));
        var copied = (Insert)insert.Clone(); CheckPathFlags(copied.Block.Entities.OfType<Hatch>().Single(), flags);
        var exploded = insert.Explode().OfType<Hatch>().Single(); CheckPathFlags(exploded, flags);
        exploded.TransformBy(Matrix3.Scale(2), new Vector3(1, 2, 0)); CheckPathFlags(exploded, flags);
        var document = new DxfDocument(version); document.Entities.Add(exploded);
        for (int cycle = 0; cycle < 2; ++cycle)
        {
            using var output = new MemoryStream(); Check(document.Save(output, cycle == 0 ? !binary : binary), "Flagged HATCH save failed.");
            output.Position = 0; var raw = DxfRawDocument.Load(output);
            var record = raw.Sections.Single(s => s.Name == "ENTITIES").Records.Single(r => r.Name == "HATCH");
            Equal(flags, (int)record.Tags.Single(t => t.Code == 92).Value, "Writer changed group 92");
            output.Position = 0; document = DxfDocument.Load(output) ?? throw new InvalidOperationException("Flagged HATCH reload failed.");
            CheckPathFlags(document.Entities.Hatches.Single(), flags);
        }
        CheckPathFlags(original, flags);
    }

    private static void HatchFlagsOpenPolyline(DxfVersion version, bool binary)
    {
        // TransformBy converts an open polyline to individual edges: only bit 2 must change.
        var original = ReadFlagsHatch(version, binary, 26, false);
        Equal((HatchBoundaryPathTypeFlags)26, original.BoundaryPaths.Single().PathType, "Open input flags");
        var copy = (Hatch)original.Clone();
        copy.TransformBy(Matrix3.Identity, new Vector3(1, 2, 0));
        var path = copy.BoundaryPaths.Single();
        Equal((HatchBoundaryPathTypeFlags)24, path.PathType, "Representation change altered classification bits");
        Equal(3, path.Edges.Count, "Open transformed boundary acquired a closing edge");
        Check(path.Edges.All(e => e is HatchBoundaryPath.Line), "Open boundary was not converted to line edges.");
        var doc = new DxfDocument(version); doc.Entities.Add(copy);
        using var output = new MemoryStream(); Check(doc.Save(output, !binary), "Transformed edge-path save failed.");
        output.Position = 0;
        var loaded = DxfDocument.Load(output) ?? throw new InvalidOperationException("Transformed edge-path reload failed.");
        Equal((HatchBoundaryPathTypeFlags)24, loaded.Entities.Hatches.Single().BoundaryPaths.Single().PathType, "Transformed flags lost on reload");
        Equal((HatchBoundaryPathTypeFlags)26, original.BoundaryPaths.Single().PathType, "Transform changed source flags");
    }

    private static void HatchFlagsUpdate()
    {
        var contour = new Polyline2D(new[] { Vector2.Zero, Vector2.UnitX, new Vector2(1, 1), Vector2.UnitY }, true);
        var path = new HatchBoundaryPath(new EntityObject[] { contour });
        // Exercise the existing internal setter without adding a new public authoring contract.
        typeof(HatchBoundaryPath).GetProperty(nameof(HatchBoundaryPath.PathType))!.SetValue(path, (HatchBoundaryPathTypeFlags)19);
        contour.IsClosed = false; path.Update();
        Equal((HatchBoundaryPathTypeFlags)17, path.PathType, "Update retained a stale Polyline flag");
        Equal(3, path.Edges.Count, "Updated open boundary edge count");
        contour.IsClosed = true; path.Update();
        Equal((HatchBoundaryPathTypeFlags)19, path.PathType, "Update failed to restore the Polyline flag");
        Equal(1, path.Edges.Count, "Updated closed boundary representation");
        var clone = (HatchBoundaryPath)path.Clone();
        Equal(path.PathType, clone.PathType, "Direct path clone classification");
        Equal(0, clone.Entities.Count, "Path cloning changed existing contour-reference policy");
    }

    private static void HatchFlagsIndependent(bool transform)
    {
        for (int flags = 0; flags < 32; ++flags)
        {
            var hatch = ReadFlagsHatch(DxfVersion.AutoCad2018, false, flags);
            typeof(HatchBoundaryPath).GetProperty(nameof(HatchBoundaryPath.PathType))!
                .SetValue(hatch.BoundaryPaths.Single(), (HatchBoundaryPathTypeFlags)flags);
            if (transform)
            {
                hatch.TransformBy(Matrix3.Identity, Vector3.Zero);
                CheckPathFlags(hatch, flags);
            }
            else
            {
                CheckPathFlags((Hatch)hatch.Clone(), flags);
                Equal((HatchBoundaryPathTypeFlags)flags, ((HatchBoundaryPath)hatch.BoundaryPaths.Single().Clone()).PathType,
                    "Direct path clone classification");
            }
        }
    }

    private static void HatchFlagsDefaults()
    {
        var poly = new HatchBoundaryPath(new HatchBoundaryPath.Edge[] { ClosurePolyline(true) });
        Equal((HatchBoundaryPathTypeFlags)7, poly.PathType, "New polyline constructor default changed");
        var line = new HatchBoundaryPath(new HatchBoundaryPath.Edge[] { new HatchBoundaryPath.Line { Start = Vector2.Zero, End = Vector2.UnitX } });
        Equal((HatchBoundaryPathTypeFlags)5, line.PathType, "New edge constructor default changed");
    }

    private static void HatchFlagsCorpus(DxfVersion version, bool binary)
    {
        var document = new DxfDocument(version);
        for (int flags = 0; flags < 32; ++flags)
        {
            var hatch = (Hatch)ReadFlagsHatch(version, binary, flags).Clone();
            document.Entities.Add(hatch);
        }
        using var output = new MemoryStream(); Check(document.Save(output, binary), "Flag corpus save failed.");
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"hatch-path-flags-{version}-{binary}.dxf"), output.ToArray());
    }
}
