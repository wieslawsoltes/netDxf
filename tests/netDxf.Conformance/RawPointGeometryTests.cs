// Copyright (c) netDxf contributors. Licensed under the MIT License.
using netDxf;
using netDxf.Header;
using netDxf.IO;
using DxfPoint = netDxf.Entities.Point;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static object PointRead(DxfRawDocument raw, DxfRawRecord record) => RawLineCall(raw, "ReadPointGeometry", record);
    private static DxfRawDocument PointEdit(DxfRawDocument raw, DxfRawRecord record, Vector3 position)
        => (DxfRawDocument)RawLineCall(raw, "WithPointPosition", record, position);
    private static DxfRawRecord PointRecord(DxfRawDocument raw)
        => raw.Sections.SelectMany(s => s.Records).First(r => r.Name == "POINT");
    private static readonly short[] PointFields = { 10, 20, 30 };

    private static List<DxfTag> PointTags(DxfVersion version, bool block = false, int variant = 0)
    {
        var tags = RawLineTags(version, block);
        int first = tags.FindIndex(t => t.Code == 0 && Equals(t.Value, "LINE"));
        int end = tags.FindIndex(first + 1, t => t.Code == 0);
        var record = new List<DxfTag> { new(0, "POINT"), new(5, "A") };
        if (version >= DxfVersion.AutoCad13) record.Add(new(100, "AcDbEntity"));
        record.AddRange(new DxfTag[] { new(8, "0"), new(62, (short)3), new(48, 1.25) });
        if (version >= DxfVersion.AutoCad13) record.Add(new(100, "AcDbPoint"));
        var coordinates = new List<DxfTag> { new(10, 1.25), new(20, -2.0) };
        if (variant != 1) coordinates.Add(new(30, 3.0));
        if (variant != 3) record.AddRange(coordinates);
        if (variant != 1)
            record.AddRange(new DxfTag[] { new(39, -2.5), new(50, -35.5), new(210, 0.0),
                new(220, variant == 2 ? 0.0 : .6), new(230, variant == 2 ? -2.0 : .8) });
        if (variant == 3) record.AddRange(coordinates);
        record.AddRange(new DxfTag[] { new(1001, "RAW_POINT"), new(1000, "preserve point"),
            new(1040, 3.125), new(1070, (short)-7) });
        tags.RemoveRange(first, end - first); tags.InsertRange(first, record);
        return tags;
    }

    private static DxfRawDocument PointSource(DxfVersion version, bool binary, bool block, int variant)
    {
        var tags = PointTags(version, block, variant);
        return LoadRaw(version == DxfVersion.AutoCad12 ? RawR12Bytes(tags, binary) : RawFixtureBytes(tags, binary));
    }

    private static void PointGeometryMatrix(DxfVersion version, bool binary, bool block, int variant)
    {
        var raw = PointSource(version, binary, block, variant); var record = PointRecord(raw);
        byte[] original = SaveRaw(raw); object geometry = PointRead(raw, record);
        var position = new Vector3(1.25, -2, variant == 1 ? 0 : 3);
        RawLinePointBits(position, RawLinePoint(geometry, "Position"));
        RawLinePointBits(variant == 1 ? Vector3.UnitZ : variant == 2 ? new Vector3(0, 0, -2) : new Vector3(0, .6, .8),
            RawLinePoint(geometry, "ExtrusionDirection"));
        SameDoubleBits(variant == 1 ? 0 : -2.5, ConicScalar(geometry, "Thickness"), "POINT thickness");
        SameDoubleBits(variant == 1 ? 0 : -35.5, ConicScalar(geometry, "UcsXAxisAngle"), "POINT UCS angle");
        Check(ReferenceEquals(raw, PointEdit(raw, record, position)), "POINT no-op replaced snapshot");
        var target = new Vector3(-8.5, 16.25, -32);
        var edited = PointEdit(raw, record, target); var replacement = PointRecord(edited);
        AssertOutsideRecordUnchanged(raw, record, edited, replacement.Tags.Count);
        var before = record.Tags.Where(t => !PointFields.Contains(t.Code)).ToArray();
        var after = replacement.Tags.Where(t => !PointFields.Contains(t.Code)).ToArray();
        SameRawTags(before, after);
        Check(before.Zip(after).All(p => ReferenceEquals(p.First, p.Second)), "Untouched POINT tags lost identity");
        Check(!edited.HasOriginalBytes, "Changed POINT retained original byte output");
        Check(original.SequenceEqual(SaveRaw(raw)), "POINT source bytes changed");
        RawLinePointBits(position, RawLinePoint(geometry, "Position"));
        foreach (bool output in new[] { false, true })
        {
            byte[] bytes = SaveRaw(edited, output); var loaded = LoadRaw(bytes);
            RawLinePointBits(target, RawLinePoint(PointRead(loaded, PointRecord(loaded)), "Position"));
            Equal(version, loaded.Version, "POINT edit changed dialect");
            Equal(raw.EncodingCodePage, loaded.EncodingCodePage, "Encoding changed");
            SameRawTags(edited.Tags, loaded.Tags);
            string stem = $"raw-point-{version}-{binary}-{block}-{variant}-{output}";
            File.WriteAllBytes(Path.Combine(ArtifactDirectory, stem + "-before.dxf"), SaveRaw(raw, output));
            File.WriteAllBytes(Path.Combine(ArtifactDirectory, stem + "-after.dxf"), bytes);
        }
        Throws<ArgumentException>(() => PointRead(edited, record));
        Throws<ArgumentException>(() => PointEdit(edited, record, target));
    }

    private static void RegisterRawPointGeometryTests()
    {
        foreach (DxfVersion version in HandleProfiles) foreach (bool binary in new[] { false, true })
            foreach (bool block in new[] { false, true }) for (int variant = 0; variant < 4; variant++)
            { int v = variant; Run($"raw-point/matrix/{version}/{binary}/{block}/{v}", () => PointGeometryMatrix(version, binary, block, v)); }
        DxfRawDocument Modified(Action<List<DxfTag>> mutate)
        { var tags = PointTags(DxfVersion.AutoCad2018); mutate(tags); return DxfRawDocument.Create(tags); }
        foreach (short code in new short[] { 10, 20, 30, 39, 50, 210, 220, 230 })
            Run($"raw-point/duplicate/{code}", () =>
            {
                var raw = Modified(t => { int at = RawLineAt(t, code); t.Insert(at, t[at]); });
                Throws<FormatException>(() => PointRead(raw, PointRecord(raw)));
            });
        foreach (short code in new short[] { 10, 20 })
            Run($"raw-point/missing/{code}", () =>
            {
                var raw = Modified(t => t.RemoveAt(RawLineAt(t, code)));
                Throws<FormatException>(() => PointRead(raw, PointRecord(raw)));
            });
        foreach (short code in new short[] { 91, 92, 310, 1005, 1010, 1011, 1012, 1013, 1041, 1042 })
            Run($"raw-point/edit-guard/{code}", () =>
            {
                var raw = Modified(t => t.Insert(RawLineAt(t, 1001) + (code >= 1000 ? 1 : 0),
                    new(code, code == 91 || code == 92 ? (object)0 : code == 310 ? new byte[] { 1 } : code == 1005 ? "B" : 2.0)));
                var record = PointRecord(raw); var position = RawLinePoint(PointRead(raw, record), "Position");
                byte[] before = SaveRaw(raw);
                Check(ReferenceEquals(raw, PointEdit(raw, record, position)), "Decorated POINT no-op replaced snapshot");
                Throws<NotSupportedException>(() => PointEdit(raw, record, Vector3.Zero));
                Check(before.SequenceEqual(SaveRaw(raw)), "Guarded POINT edit changed source");
            });
        foreach (short code in new short[] { 320, 330, 340, 350, 360, 1005 })
            Run($"raw-point/incoming/{code}", () =>
            {
                var raw = Modified(t =>
                {
                    int at = t.FindLastIndex(x => x.Code == 0 && Equals(x.Value, "POINT")) + 2;
                    if (code == 1005) t.Insert(at++, new(1001, "REFERENCES"));
                    t.Insert(at, new(code, "000a"));
                });
                Throws<NotSupportedException>(() => PointEdit(raw, PointRecord(raw), Vector3.Zero));
            });
        foreach (string identity in new[] { "0", "B" })
            Run("raw-point/ambiguous-identity/" + identity, () =>
            {
                var raw = Modified(t => { int at = t.FindIndex(x => x.Code == 5 && Equals(x.Value, "A")); t[at] = new(5, identity); });
                Throws<NotSupportedException>(() => PointEdit(raw, PointRecord(raw), Vector3.Zero));
            });
        Run("raw-point/handle-free", () =>
        {
            var raw = Modified(t => t.RemoveAt(t.FindIndex(x => x.Code == 5 && Equals(x.Value, "A"))));
            var edited = PointEdit(raw, PointRecord(raw), Vector3.Zero);
            RawLinePointBits(Vector3.Zero, RawLinePoint(PointRead(edited, PointRecord(edited)), "Position"));
        });
        foreach (string name in new[] { "AcDbLine", "AcDbPoint", "UNKNOWN" })
            Run("raw-point/subclass/" + name, () =>
            {
                var raw = Modified(t => t.Insert(RawLineAt(t, 10), new(100, name)));
                Throws<NotSupportedException>(() => PointRead(raw, PointRecord(raw)));
            });
        Run("raw-point/geometry-before-subclass", () =>
        {
            var raw = Modified(t => { int at = RawLineAt(t, 10); var value = t[at]; t.RemoveAt(at); t.Insert(at - 1, value); });
            Throws<FormatException>(() => PointRead(raw, PointRecord(raw)));
        });
        Run("raw-point/zero-extrusion", () =>
        {
            var raw = Modified(t => { foreach (short c in new short[] { 210, 220, 230 }) t[RawLineAt(t, c)] = new(c, 0.0); });
            Throws<FormatException>(() => PointRead(raw, PointRecord(raw)));
        });
        Run("raw-point/nested-control", () =>
        {
            var raw = Modified(t => t.InsertRange(RawLineAt(t, 10), new DxfTag[] {
                new(102, "{VENDOR"), new(10, 99.0), new(102, "{INNER"), new(20, 88.0), new(102, "}"), new(102, "}") }));
            var record = PointRecord(raw);
            RawLinePointBits(new(1.25, -2, 3), RawLinePoint(PointRead(raw, record), "Position"));
            Throws<NotSupportedException>(() => PointEdit(raw, record, Vector3.Zero));
        });
        foreach (bool afterXData in new[] { false, true })
            Run($"raw-point/embedded/{afterXData}", () =>
            {
                var raw = Modified(t => t.InsertRange(RawLineAt(t, 1001) + (afterXData ? 1 : 0),
                    new DxfTag[] { new(101, "Embedded Object"), new(10, 99.0), new(20, 88.0) }));
                var record = PointRecord(raw);
                if (afterXData) Throws<FormatException>(() => PointRead(raw, record));
                else
                {
                    RawLinePointBits(new(1.25, -2, 3), RawLinePoint(PointRead(raw, record), "Position"));
                    Check(ReferenceEquals(raw, PointEdit(raw, record, new(1.25, -2, 3))), "Embedded no-op changed snapshot");
                    Throws<NotSupportedException>(() => PointEdit(raw, record, Vector3.Zero));
                }
            });
        foreach (string token in new[] { "}", "bad", "{OPEN" })
            Run("raw-point/control-framing/" + token, () =>
            {
                var raw = Modified(t => t.Insert(RawLineAt(t, 1001), new(102, token)));
                Throws<FormatException>(() => PointRead(raw, PointRecord(raw)));
            });
        foreach (short code in new short[] { 10, 50, 100, 102 })
            Run($"raw-point/ordinary-after-xdata/{code}", () =>
            {
                var raw = Modified(t => t.Insert(RawLineAt(t, 1001) + 1, new(code, code == 100 ? (object)"AcDbPoint" : code == 102 ? "{APP" : 2.0)));
                Throws<FormatException>(() => PointRead(raw, PointRecord(raw)));
            });
        foreach (double bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            for (int axis = 0; axis < 3; axis++)
            {
                int a = axis;
                Run($"raw-point/nonfinite/{ParameterBits(bad)}/{a}", () =>
                {
                    var raw = PointSource(DxfVersion.AutoCad2018, false, false, 0);
                    var values = new double[3]; values[a] = bad;
                    Throws<ArgumentOutOfRangeException>(() => PointEdit(raw, PointRecord(raw), new(values[0], values[1], values[2])));
                });
            }
        foreach (double value in new[] { -0.0, double.Epsilon, -double.Epsilon, double.MaxValue, -double.MaxValue })
            Run("raw-point/extreme/" + ParameterBits(value), () =>
            {
                var raw = PointSource(DxfVersion.AutoCad12, false, false, 1);
                var position = new Vector3(value, value, value);
                var edited = PointEdit(raw, PointRecord(raw), position);
                foreach (bool binary in new[] { false, true })
                {
                    var loaded = LoadRaw(SaveRaw(edited, binary));
                    RawLinePointBits(position, RawLinePoint(PointRead(loaded, PointRecord(loaded)), "Position"));
                }
            });
        Run("raw-point/budget", () =>
        {
            var tags = PointTags(DxfVersion.AutoCad12, false, 1);
            var raw = DxfRawDocument.Create(tags, false, new DxfRawOptions(maximumTags: tags.Count));
            var record = PointRecord(raw); byte[] original = SaveRaw(raw);
            Throws<InvalidOperationException>(() => PointEdit(raw, record, Vector3.UnitZ));
            Check(original.SequenceEqual(SaveRaw(raw)), "POINT budget failure changed bytes");
            var edited = PointEdit(raw, record, Vector3.Zero);
            Equal(tags.Count, edited.Tags.Count, "Zero inserted an unnecessary Z field");
        });
        Run("raw-point/snapshot-admission", () =>
        {
            var raw = PointSource(DxfVersion.AutoCad2018, false, false, 0);
            var foreign = PointSource(DxfVersion.AutoCad2018, false, false, 0);
            Throws<ArgumentException>(() => PointRead(raw, PointRecord(foreign)));
            Throws<ArgumentNullException>(() => PointRead(raw, null!));
            Throws<ArgumentException>(() => PointRead(raw, raw.Sections.SelectMany(s => s.Records).First(r => r.Name == "OPAQUE")));
        });
        foreach (DxfVersion version in SupportedVersions) foreach (bool binary in new[] { false, true })
            Run($"raw-point/typed/{version}/{binary}", () =>
            {
                var doc = new DxfDocument(version); doc.Comments.Clear();
                doc.Entities.Add(new DxfPoint(new Vector3(1, 2, 3)) { Thickness = -2, Normal = new(0, .6, .8), Rotation = 25 });
                using var stream = new MemoryStream(); Check(doc.Save(stream, binary), "Typed POINT save failed");
                var raw = LoadRaw(stream.ToArray()); var record = PointRecord(raw); var initial = PointRead(raw, record);
                var edited = PointEdit(raw, record, new(7, 8, 9));
                using var input = new MemoryStream(SaveRaw(edited, binary));
                var loaded = DxfDocument.Load(input)!.Entities.Points.Single();
                RawLinePointBits(new(7, 8, 9), loaded.Position); SameDoubleBits(-2, loaded.Thickness, "Typed thickness changed");
                SameDoubleBits(ConicScalar(initial, "UcsXAxisAngle"), ConicScalar(PointRead(edited, PointRecord(edited)), "UcsXAxisAngle"), "Stored angle changed");
                Near(25, loaded.Rotation, "Typed rotation changed");
            });
    }
}
