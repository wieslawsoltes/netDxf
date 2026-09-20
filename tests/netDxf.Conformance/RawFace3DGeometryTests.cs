// Copyright (c) netDxf contributors. Licensed under the MIT License.
using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static readonly short[] RawFaceFields = { 10, 20, 30, 11, 21, 31, 12, 22, 32, 13, 23, 33, 70 };
    private static readonly string[] RawFaceProperties = { "FirstVertex", "SecondVertex", "ThirdVertex", "FourthVertex" };
    private static readonly Vector3[] RawFaceTargets = { new(-8.5, 16.25, -32), new(64, -128.5, 256.25),
        new(7, 9, -11), new(-17, 19, 23) };
    private static DxfRawRecord RawFaceRecord(DxfRawDocument raw)
        => raw.Sections.SelectMany(s => s.Records).First(r => r.Name == "3DFACE");
    private static object RawFaceRead(DxfRawDocument raw, DxfRawRecord record)
        => RawLineCall(raw, "ReadFace3DGeometry", record);
    private static Face3DEdgeFlags RawFaceFlags(object geometry)
        => (Face3DEdgeFlags)geometry.GetType().GetProperty("EdgeFlags")!.GetValue(geometry)!;
    private static DxfRawDocument RawFaceEdit(DxfRawDocument raw, DxfRawRecord record, Vector3[] points, Face3DEdgeFlags flags)
        => (DxfRawDocument)RawLineCall(raw, "WithFace3DGeometry", record, points[0], points[1], points[2], points[3], flags);
    private static Vector3[] RawFacePoints(object geometry)
        => RawFaceProperties.Select(p => RawLinePoint(geometry, p)).ToArray();
    private static Vector3[] RawFaceOriginalPoints(int variant)
    {
        var points = new[] { new Vector3(1, 2, 3), new Vector3(4, 5, 6), new Vector3(7, 8, 9), new Vector3(10, 11, 12) };
        if (variant == 1) points = points.Select(p => new Vector3(p.X, p.Y, 0)).ToArray();
        if (variant == 2) points[3] = points[2];
        return points;
    }
    private static List<DxfTag> RawFaceTags(DxfVersion version, bool block = false, int variant = 0)
    {
        var tags = RawLineTags(version, block);
        int start = tags.FindIndex(t => t.Code == 0 && Equals(t.Value, "LINE"));
        int end = tags.FindIndex(start + 1, t => t.Code == 0);
        var face = new List<DxfTag> { new(0, "3DFACE"), new(5, "A") };
        if (version >= DxfVersion.AutoCad13) face.Add(new(100, "AcDbEntity"));
        face.AddRange(new DxfTag[] { new(8, "0"), new(62, (short)3), new(48, 1.25) });
        if (version >= DxfVersion.AutoCad13) face.Add(new(100, "AcDbFace"));
        if (variant == 3) face.Add(new(70, (short)5));
        var points = RawFaceOriginalPoints(variant);
        for (int i = 0; i < 4; i++)
        {
            face.Add(new((short)(10 + i), points[i].X)); face.Add(new((short)(20 + i), points[i].Y));
            if (variant != 1) face.Add(new((short)(30 + i), points[i].Z));
        }
        if (variant != 1 && variant != 3) face.Add(new(70, (short)(variant == 2 ? 10 : 5)));
        face.AddRange(new DxfTag[] { new(1001, "RAW_FACE"), new(1000, "preserve face"), new(1040, 3.125), new(1070, (short)-7) });
        tags.RemoveRange(start, end - start); tags.InsertRange(start, face);
        return tags;
    }
    private static DxfRawDocument RawFaceSource(DxfVersion version, bool binary, bool block, int variant)
    {
        var tags = RawFaceTags(version, block, variant);
        return LoadRaw(version == DxfVersion.AutoCad12 ? RawR12Bytes(tags, binary) : RawFixtureBytes(tags, binary));
    }
    private static void RawFaceMatrix(DxfVersion version, bool binary, bool block, int variant)
    {
        var raw = RawFaceSource(version, binary, block, variant); var record = RawFaceRecord(raw);
        byte[] original = SaveRaw(raw); var view = RawFaceRead(raw, record); var points = RawFaceOriginalPoints(variant);
        for (int i = 0; i < 4; i++) RawLinePointBits(points[i], RawFacePoints(view)[i]);
        var flags = (Face3DEdgeFlags)(variant == 1 ? 0 : variant == 2 ? 10 : 5);
        Equal(flags, RawFaceFlags(view), "Stored edge flags");
        Check(ReferenceEquals(raw, RawFaceEdit(raw, record, points, flags)), "No-op lost snapshot identity");
        var changed = RawFaceEdit(raw, record, RawFaceTargets, Face3DEdgeFlags.First | Face3DEdgeFlags.Fourth);
        var replacement = RawFaceRecord(changed);
        AssertOutsideRecordUnchanged(raw, record, changed, replacement.Tags.Count);
        var a = record.Tags.Where(t => !RawFaceFields.Contains(t.Code)).ToArray();
        var b = replacement.Tags.Where(t => !RawFaceFields.Contains(t.Code)).ToArray();
        SameRawTags(a, b); Check(a.Zip(b).All(pair => ReferenceEquals(pair.First, pair.Second)), "Untouched face tags lost identity");
        Check(!changed.HasOriginalBytes && original.SequenceEqual(SaveRaw(raw)), "Source or original-byte state changed");
        for (int i = 0; i < 4; i++) RawLinePointBits(points[i], RawFacePoints(view)[i]);
        foreach (bool output in new[] { false, true })
        {
            byte[] bytes = SaveRaw(changed, output); var loaded = LoadRaw(bytes); var read = RawFaceRead(loaded, RawFaceRecord(loaded));
            for (int i = 0; i < 4; i++) RawLinePointBits(RawFaceTargets[i], RawFacePoints(read)[i]);
            Equal((Face3DEdgeFlags)9, RawFaceFlags(read), "Output invisible edges");
            Equal(version, loaded.Version, "Face dialect changed");
            Equal(raw.EncodingCodePage, loaded.EncodingCodePage, "Face encoding changed"); SameRawTags(changed.Tags, loaded.Tags);
            string stem = $"raw-face3d-{version}-{binary}-{block}-{variant}-{output}";
            File.WriteAllBytes(Path.Combine(ArtifactDirectory, stem + "-before.dxf"), SaveRaw(raw, output));
            File.WriteAllBytes(Path.Combine(ArtifactDirectory, stem + "-after.dxf"), bytes);
        }
        Throws<ArgumentException>(() => RawFaceRead(changed, record));
        Throws<ArgumentException>(() => RawFaceEdit(changed, record, RawFaceTargets, flags));
    }
    private static void RegisterRawFace3DGeometryTests()
    {
        foreach (DxfVersion version in HandleProfiles) foreach (bool binary in new[] { false, true })
            foreach (bool block in new[] { false, true }) for (int variant = 0; variant < 4; variant++)
            { int v = variant; Run($"raw-face3d/matrix/{version}/{binary}/{block}/{v}", () => RawFaceMatrix(version, binary, block, v)); }
        DxfRawDocument Modified(Action<List<DxfTag>> edit)
        { var tags = RawFaceTags(DxfVersion.AutoCad2018); edit(tags); return DxfRawDocument.Create(tags); }
        foreach (short code in RawFaceFields)
            Run($"raw-face3d/duplicate/{code}", () =>
            {
                var raw = Modified(t => { int at = RawLineAt(t, code); t.Insert(at, t[at]); });
                Throws<FormatException>(() => RawFaceRead(raw, RawFaceRecord(raw)));
            });
        foreach (short code in new short[] { 10, 20, 11, 21, 12, 22, 13, 23 })
            Run($"raw-face3d/missing/{code}", () =>
            {
                var raw = Modified(t => t.RemoveAt(RawLineAt(t, code)));
                Throws<FormatException>(() => RawFaceRead(raw, RawFaceRecord(raw)));
            });
        for (int mask = 0; mask < 16; mask++)
        {
            int m = mask;
            foreach (bool triangle in new[] { false, true })
                Run($"raw-face3d/flags/{m}/{triangle}", () =>
                {
                    var raw = RawFaceSource(DxfVersion.AutoCad12, false, false, 1); var record = RawFaceRecord(raw);
                    var points = RawFacePoints(RawFaceRead(raw, record)); if (triangle) points[3] = points[2];
                    var edited = RawFaceEdit(raw, record, points, (Face3DEdgeFlags)m);
                    foreach (bool binary in new[] { false, true })
                    {
                        var loaded = LoadRaw(SaveRaw(edited, binary)); var geometry = RawFaceRead(loaded, RawFaceRecord(loaded));
                        for (int i = 0; i < 4; i++) RawLinePointBits(points[i], RawFacePoints(geometry)[i]);
                        Equal((Face3DEdgeFlags)m, RawFaceFlags(geometry), "Visibility bits changed");
                    }
                    if (m == 0 && !triangle) Check(ReferenceEquals(raw, edited), "Missing default flags became materialized");
                });
        }
        foreach (int bad in new[] { -1, 16, 32, int.MaxValue, int.MinValue })
            Run($"raw-face3d/invalid-flags/{bad}", () =>
            {
                var raw = RawFaceSource(DxfVersion.AutoCad2018, false, false, 0); byte[] original = SaveRaw(raw);
                Throws<ArgumentOutOfRangeException>(() => RawFaceEdit(raw, RawFaceRecord(raw), RawFaceTargets, (Face3DEdgeFlags)bad));
                Check(original.SequenceEqual(SaveRaw(raw)), "Flag rejection changed source");
            });
        foreach (short bad in new short[] { -1, 16, short.MaxValue })
            Run($"raw-face3d/invalid-stored-flags/{bad}", () =>
            {
                var raw = Modified(t => t[RawLineAt(t, 70)] = new(70, bad));
                Throws<FormatException>(() => RawFaceRead(raw, RawFaceRecord(raw)));
            });
        foreach (short code in new short[] { 91, 92, 160, 310, 39, 210, 1005, 1010, 1041, 1042 })
            Run($"raw-face3d/edit-guard/{code}", () =>
            {
                var raw = Modified(t => t.Insert(RawLineAt(t, 1001) + (code >= 1000 ? 1 : 0), new(code,
                    code == 91 || code == 92 ? (object)0 : code == 160 ? 0L : code == 310 ? new byte[] { 1 } : code == 1005 ? "B" : 2.0)));
                var record = RawFaceRecord(raw); var view = RawFaceRead(raw, record); byte[] original = SaveRaw(raw);
                Check(ReferenceEquals(raw, RawFaceEdit(raw, record, RawFacePoints(view), RawFaceFlags(view))), "Decorated no-op changed snapshot");
                Throws<NotSupportedException>(() => RawFaceEdit(raw, record, RawFaceTargets, Face3DEdgeFlags.None));
                Check(original.SequenceEqual(SaveRaw(raw)), "Guard rejection changed source");
            });
        foreach (short code in new short[] { 320, 330, 340, 350, 360, 1005 })
            Run($"raw-face3d/incoming/{code}", () =>
            {
                var raw = Modified(t =>
                {
                    int at = t.FindIndex(x => x.Code == 0 && Equals(x.Value, "POINT")) + 2;
                    if (code == 1005) t.Insert(at++, new(1001, "REFERENCE")); t.Insert(at, new(code, "000a"));
                });
                Throws<NotSupportedException>(() => RawFaceEdit(raw, RawFaceRecord(raw), RawFaceTargets, Face3DEdgeFlags.None));
            });
        foreach (string handle in new[] { "0", "B" })
            Run("raw-face3d/identity/" + handle, () =>
            {
                var raw = Modified(t => t[t.FindIndex(x => x.Code == 5 && Equals(x.Value, "A"))] = new(5, handle));
                Throws<NotSupportedException>(() => RawFaceEdit(raw, RawFaceRecord(raw), RawFaceTargets, Face3DEdgeFlags.None));
            });
        Run("raw-face3d/handle-free", () =>
        {
            var raw = Modified(t => t.RemoveAt(t.FindIndex(x => x.Code == 5 && Equals(x.Value, "A"))));
            var edited = RawFaceEdit(raw, RawFaceRecord(raw), RawFaceTargets, Face3DEdgeFlags.Third);
            RawLinePointBits(RawFaceTargets[3], RawFacePoints(RawFaceRead(edited, RawFaceRecord(edited)))[3]);
        });
        foreach (double bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity }) for (int slot = 0; slot < 12; slot++)
        {
            int s = slot;
            Run($"raw-face3d/nonfinite/{ParameterBits(bad)}/{s}", () =>
            {
                var raw = RawFaceSource(DxfVersion.AutoCad2018, false, false, 0); var values = new double[12]; values[s] = bad;
                var points = Enumerable.Range(0, 4).Select(i => new Vector3(values[3*i], values[3*i+1], values[3*i+2])).ToArray();
                Throws<ArgumentOutOfRangeException>(() => RawFaceEdit(raw, RawFaceRecord(raw), points, Face3DEdgeFlags.None));
            });
        }
        foreach (double v in new[] { -0.0, double.Epsilon, -double.Epsilon, double.MaxValue, -double.MaxValue })
            Run("raw-face3d/extreme/" + ParameterBits(v), () =>
            {
                var raw = RawFaceSource(DxfVersion.AutoCad12, false, false, 1); var points = Enumerable.Repeat(new Vector3(v, v, v), 4).ToArray();
                var changed = RawFaceEdit(raw, RawFaceRecord(raw), points, Face3DEdgeFlags.None);
                foreach (bool binary in new[] { false, true })
                {
                    var loaded = LoadRaw(SaveRaw(changed, binary)); var read = RawFaceRead(loaded, RawFaceRecord(loaded));
                    foreach (Vector3 p in RawFacePoints(read)) RawLinePointBits(points[0], p);
                }
            });
        foreach (string token in new[] { "}", "bad", "{OPEN" })
            Run("raw-face3d/control/" + token, () =>
            {
                var raw = Modified(t => t.Insert(RawLineAt(t, 1001), new(102, token)));
                Throws<FormatException>(() => RawFaceRead(raw, RawFaceRecord(raw)));
            });
        foreach (short code in new short[] { 10, 70, 100, 101, 102 })
            Run($"raw-face3d/after-xdata/{code}", () =>
            {
                var raw = Modified(t => t.Insert(RawLineAt(t, 1001)+1,
                    new(code, code == 100 ? (object)"AcDbFace" : code == 101 ? "Embedded Object" : code == 102 ? "{APP" : code == 70 ? (object)(short)0 : 2.0)));
                Throws<FormatException>(() => RawFaceRead(raw, RawFaceRecord(raw)));
            });
        foreach (bool embedded in new[] { false, true })
            Run($"raw-face3d/opaque/{embedded}", () =>
            {
                var raw = Modified(t => t.InsertRange(RawLineAt(t, 1001), embedded
                    ? new DxfTag[] { new(101, "Embedded Object"), new(10, 999.0), new(70, (short)999), new(102, "}") }
                    : new DxfTag[] { new(102, "{APP"), new(10, 999.0), new(102, "{INNER"), new(70, (short)999), new(102, "}"), new(102, "}") }));
                var record = RawFaceRecord(raw); var geometry = RawFaceRead(raw, record);
                for (int i = 0; i < 4; i++) RawLinePointBits(RawFaceOriginalPoints(0)[i], RawFacePoints(geometry)[i]);
                Check(ReferenceEquals(raw, RawFaceEdit(raw, record, RawFacePoints(geometry), RawFaceFlags(geometry))), "Opaque no-op changed source");
                Throws<NotSupportedException>(() => RawFaceEdit(raw, record, RawFaceTargets, Face3DEdgeFlags.None));
            });
        foreach (string marker in new[] { "AcDbPoint", "AcDbFace", "UNKNOWN" })
            Run("raw-face3d/subclass/" + marker, () =>
            {
                var raw = Modified(t => t.Insert(RawLineAt(t, 10), new(100, marker)));
                Throws<NotSupportedException>(() => RawFaceRead(raw, RawFaceRecord(raw)));
            });
        Run("raw-face3d/outside-subclass", () =>
        {
            var raw = Modified(t => { int at = RawLineAt(t, 10); var tag = t[at]; t.RemoveAt(at); t.Insert(at - 1, tag); });
            Throws<FormatException>(() => RawFaceRead(raw, RawFaceRecord(raw)));
        });
        Run("raw-face3d/resource-budget", () =>
        {
            var tags = RawFaceTags(DxfVersion.AutoCad12, false, 1);
            var raw = DxfRawDocument.Create(tags, false, new DxfRawOptions(maximumTags: tags.Count));
            byte[] before = SaveRaw(raw); var record = RawFaceRecord(raw);
            Throws<InvalidOperationException>(() => RawFaceEdit(raw, record, RawFaceTargets, Face3DEdgeFlags.First));
            Throws<InvalidOperationException>(() => RawFaceEdit(raw, record, RawFaceOriginalPoints(1), Face3DEdgeFlags.First));
            var flat = RawFaceTargets.Select(v => new Vector3(v.X, v.Y, 0)).ToArray();
            var changed = RawFaceEdit(raw, record, flat, Face3DEdgeFlags.None);
            Equal(tags.Count, changed.Tags.Count, "Default fields materialized unnecessarily");
            Check(before.SequenceEqual(SaveRaw(raw)), "Budget failure changed source bytes");
        });
        Run("raw-face3d/snapshot-admission", () =>
        {
            var raw = RawFaceSource(DxfVersion.AutoCad2018, false, false, 0);
            var foreign = RawFaceSource(DxfVersion.AutoCad2018, false, false, 0);
            Throws<ArgumentException>(() => RawFaceRead(raw, RawFaceRecord(foreign)));
            Throws<ArgumentNullException>(() => RawFaceRead(raw, null!));
            Throws<ArgumentException>(() => RawFaceRead(raw, raw.Sections.SelectMany(s => s.Records).First(r => r.Name == "POINT")));
        });
        foreach (DxfVersion version in SupportedVersions) foreach (bool binary in new[] { false, true })
            Run($"raw-face3d/typed/{version}/{binary}", () =>
            {
                var doc = new DxfDocument(version); doc.Comments.Clear();
                var p = RawFaceOriginalPoints(2); doc.Entities.Add(new Face3D(p[0],p[1],p[2],p[3]) { EdgeFlags = Face3DEdgeFlags.Second });
                using var stream = new MemoryStream(); Check(doc.Save(stream, binary), "Typed face save");
                var raw = LoadRaw(stream.ToArray()); var changed = RawFaceEdit(raw, RawFaceRecord(raw), RawFaceTargets, (Face3DEdgeFlags)9);
                using var input = new MemoryStream(SaveRaw(changed, binary)); var face = DxfDocument.Load(input)!.Entities.Faces3D.Single();
                var actual = new[] {face.FirstVertex,face.SecondVertex,face.ThirdVertex,face.FourthVertex};
                for (int i=0;i<4;i++) RawLinePointBits(RawFaceTargets[i],actual[i]); Equal((Face3DEdgeFlags)9,face.EdgeFlags,"Typed hidden edges");
            });
    }
}
