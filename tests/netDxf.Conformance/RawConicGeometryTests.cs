// Copyright (c) netDxf contributors. Licensed under the MIT License.
using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static readonly short[] RawConicFields = { 10, 20, 30, 40, 50, 51 };
    private static object ConicRead(DxfRawDocument raw, DxfRawRecord r, bool arc)
        => RawLineCall(raw, arc ? "ReadArcGeometry" : "ReadCircleGeometry", r);
    private static double ConicScalar(object geometry, string name)
        => (double)geometry.GetType().GetProperty(name)!.GetValue(geometry)!;
    private static DxfRawDocument ConicEdit(DxfRawDocument raw, DxfRawRecord r, bool arc,
        Vector3 center, double radius, double start = 350, double end = 35)
        => (DxfRawDocument)(arc ? RawLineCall(raw, "WithArcGeometry", r, center, radius, start, end)
            : RawLineCall(raw, "WithCircleGeometry", r, center, radius));
    private static DxfRawRecord ConicRecord(DxfRawDocument raw, bool arc)
        => raw.Sections.SelectMany(s => s.Records).First(r => r.Name == (arc ? "ARC" : "CIRCLE"));

    private static List<DxfTag> ConicTags(DxfVersion version, bool arc, bool block = false, bool omit = false)
    {
        var tags = RawLineTags(version, block);
        int first = tags.FindIndex(t => t.Code == 0 && Equals(t.Value, "LINE"));
        int last = tags.FindIndex(first + 1, t => t.Code == 0);
        var record = new List<DxfTag> { new(0, arc ? "ARC" : "CIRCLE"), new(5, "A") };
        if (version >= DxfVersion.AutoCad13) record.Add(new(100, "AcDbEntity"));
        record.AddRange(new DxfTag[] { new(8, "0"), new(62, (short)3), new(48, 1.25) });
        if (version >= DxfVersion.AutoCad13) record.Add(new(100, "AcDbCircle"));
        record.AddRange(new DxfTag[] { new(10, 1.25), new(20, -2.0) });
        if (!omit) record.Add(new(30, 3.0));
        record.AddRange(new DxfTag[] { new(40, 7.5), new(39, -2.5),
            new(210, 0.0), new(220, omit ? .6 : 0.0), new(230, omit ? .8 : -1.0) });
        if (arc)
        {
            if (version >= DxfVersion.AutoCad13) record.Add(new(100, "AcDbArc"));
            record.Add(new(50, 15.0)); record.Add(new(51, 270.0));
        }
        record.AddRange(new DxfTag[] { new(1001, "RAW_CONIC"), new(1000, "retained"),
            new(1040, 3.125), new(1070, (short)-7) });
        tags.RemoveRange(first, last - first); tags.InsertRange(first, record);
        return tags;
    }

    private static void ConicMatrix(DxfVersion version, bool arc, bool binary, bool block, bool omit)
    {
        var tags = ConicTags(version, arc, block, omit);
        byte[] original = version == DxfVersion.AutoCad12 ? RawR12Bytes(tags, binary) : RawFixtureBytes(tags, binary);
        var raw = LoadRaw(original); var record = ConicRecord(raw, arc);
        object before = ConicRead(raw, record, arc);
        var oldCenter = new Vector3(1.25, -2, omit ? 0 : 3);
        RawLinePointBits(oldCenter, RawLinePoint(before, "CenterInObjectCoordinates"));
        RawLinePointBits(new(0, omit ? .6 : 0, omit ? .8 : -1), RawLinePoint(before, "ExtrusionDirection"));
        SameDoubleBits(7.5, ConicScalar(before, "Radius"), "Stored radius");
        SameDoubleBits(-2.5, ConicScalar(before, "Thickness"), "Stored thickness");
        Check(ReferenceEquals(raw, ConicEdit(raw, record, arc, oldCenter, 7.5, 15, 270)), "No-op lost identity");
        Check(original.SequenceEqual(SaveRaw(raw)), "No-op lost original bytes");
        var center = new Vector3(-8.5, 16.25, -32);
        var edited = ConicEdit(raw, record, arc, center, 3.75);
        var replacement = ConicRecord(edited, arc);
        Check(!edited.HasOriginalBytes, "Changed document retained original bytes");
        AssertOutsideRecordUnchanged(raw, record, edited, replacement.Tags.Count);
        var oldExtra = record.Tags.Where(t => !RawConicFields.Contains(t.Code)).ToArray();
        var newExtra = replacement.Tags.Where(t => !RawConicFields.Contains(t.Code)).ToArray();
        SameRawTags(oldExtra, newExtra);
        Check(oldExtra.Zip(newExtra).All(p => ReferenceEquals(p.First, p.Second)), "Unselected tags lost identity");
        Check(original.SequenceEqual(SaveRaw(raw)), "Editing mutated source bytes");
        RawLinePointBits(oldCenter, RawLinePoint(before, "CenterInObjectCoordinates"));
        foreach (bool output in new[] { false, true })
        {
            byte[] bytes = SaveRaw(edited, output); var loaded = LoadRaw(bytes);
            Equal(version, loaded.Version, "Dialect changed"); Equal(raw.EncodingCodePage, loaded.EncodingCodePage, "Encoding changed");
            SameRawTags(edited.Tags, loaded.Tags);
            object value = ConicRead(loaded, ConicRecord(loaded, arc), arc);
            RawLinePointBits(center, RawLinePoint(value, "CenterInObjectCoordinates"));
            SameDoubleBits(3.75, ConicScalar(value, "Radius"), "Radius changed");
            if (arc) { SameDoubleBits(350, ConicScalar(value, "StartAngle"), "Start angle"); SameDoubleBits(35, ConicScalar(value, "EndAngle"), "End angle"); }
            string stem = $"raw-conic-{version}-{arc}-{binary}-{block}-{omit}-{output}";
            File.WriteAllBytes(Path.Combine(ArtifactDirectory, stem + "-before.dxf"), SaveRaw(raw, output));
            File.WriteAllBytes(Path.Combine(ArtifactDirectory, stem + "-after.dxf"), bytes);
        }
        Throws<ArgumentException>(() => ConicRead(edited, record, arc));
        Throws<ArgumentException>(() => ConicEdit(edited, record, arc, center, 3.75));
    }

    private static void RegisterRawConicGeometryTests()
    {
        foreach (DxfVersion version in HandleProfiles) foreach (bool arc in new[] { false, true })
            foreach (bool binary in new[] { false, true }) foreach (bool block in new[] { false, true })
                foreach (bool omit in new[] { false, true })
                    Run($"raw-conic/matrix/{version}/{arc}/{binary}/{block}/{omit}", () => ConicMatrix(version, arc, binary, block, omit));
        foreach (bool arc in new[] { false, true })
        {
            DxfRawDocument Modified(Action<List<DxfTag>> mutate)
            { var tags = ConicTags(DxfVersion.AutoCad2018, arc); mutate(tags); return DxfRawDocument.Create(tags); }
            foreach (short code in (arc ? RawConicFields : RawConicFields.Take(4)).Concat(new short[] { 39, 210, 220, 230 }))
                Run($"raw-conic/duplicate/{arc}/{code}", () =>
                {
                    var raw = Modified(t => { int i = RawLineAt(t, code); t.Insert(i, t[i]); });
                    Throws<FormatException>(() => ConicRead(raw, ConicRecord(raw, arc), arc));
                });
            foreach (short code in arc ? new short[] { 10, 20, 40, 50, 51 } : new short[] { 10, 20, 40 })
                Run($"raw-conic/missing/{arc}/{code}", () =>
                {
                    var raw = Modified(t => t.RemoveAt(RawLineAt(t, code)));
                    Throws<FormatException>(() => ConicRead(raw, ConicRecord(raw, arc), arc));
                });
            foreach (double radius in new[] { 0.0, -1.0 })
                Run($"raw-conic/invalid-stored-radius/{arc}/{radius}", () =>
                {
                    var raw = Modified(t => t[RawLineAt(t, 40)] = new(40, radius));
                    Throws<FormatException>(() => ConicRead(raw, ConicRecord(raw, arc), arc));
                });
            foreach (short code in new short[] { 91, 92, 310, 1005, 1010, 1020, 1030, 1041, 1042 })
                Run($"raw-conic/guard/{arc}/{code}", () =>
                {
                    var raw = Modified(t => t.Insert(RawLineAt(t, 1001) + (code >= 1000 ? 1 : 0),
                        new(code, code == 91 || code == 92 ? (object)0 : code == 310 ? new byte[] { 1 } : code == 1005 ? "B" : 2.0)));
                    var r = ConicRecord(raw, arc); object g = ConicRead(raw, r, arc); byte[] before = SaveRaw(raw);
                    Check(ReferenceEquals(raw, ConicEdit(raw, r, arc, RawLinePoint(g, "CenterInObjectCoordinates"), 7.5, 15, 270)), "Decorated no-op changed snapshot");
                    Throws<NotSupportedException>(() => ConicEdit(raw, r, arc, Vector3.Zero, 2));
                    Check(before.SequenceEqual(SaveRaw(raw)), "Rejected decorated edit changed bytes");
                });
            foreach (short code in new short[] { 320, 330, 340, 350, 360, 1005 })
                Run($"raw-conic/incoming/{arc}/{code}", () =>
                {
                    var raw = Modified(t =>
                    {
                        int i = t.FindIndex(x => x.Code == 0 && Equals(x.Value, "POINT")) + 2;
                        if (code == 1005) t.Insert(i++, new(1001, "REF"));
                        t.Insert(i, new(code, "000a"));
                    });
                    Throws<NotSupportedException>(() => ConicEdit(raw, ConicRecord(raw, arc), arc, Vector3.Zero, 2));
                });
            for (int fault = 0; fault < 7; fault++)
            {
                int f = fault;
                Run($"raw-conic/framing/{arc}/{f}", () =>
                {
                    var raw = Modified(t =>
                    {
                        int i = RawLineAt(t, 10);
                        if (f == 0) t.Insert(i, new(102, "}"));
                        else if (f == 1) t.Insert(i, new(102, "{OPEN"));
                        else if (f == 2) t.Insert(RawLineAt(t, 1001) + 1, new(101, "Embedded Object"));
                        else if (f == 3) t.Insert(RawLineAt(t, 1001) + 1, new(20, 19.0));
                        else if (f == 4) t.RemoveAt(RawLineAt(t, 1001));
                        else if (f == 5) t.RemoveAt(t.FindIndex(x => x.Code == 100 && Equals(x.Value, "AcDbCircle")));
                        else { t[RawLineAt(t, 220)] = new(220, 0.0); t[RawLineAt(t, 230)] = new(230, 0.0); }
                    });
                    // A removed circle marker produces either an unrecognized arc marker or out-of-subclass geometry.
                    Throws<FormatException>(() => ConicRead(raw, ConicRecord(raw, arc), arc));
                });
            }
            Run($"raw-conic/opaque-scopes/{arc}", () =>
            {
                var raw = Modified(t => t.InsertRange(RawLineAt(t, 10), new DxfTag[] { new(102, "{VENDOR"), new(40, -99.0), new(102, "}") }));
                SameDoubleBits(7.5, ConicScalar(ConicRead(raw, ConicRecord(raw, arc), arc), "Radius"), "Vendor radius leaked");
                Throws<NotSupportedException>(() => ConicEdit(raw, ConicRecord(raw, arc), arc, Vector3.Zero, 2));
                raw = Modified(t => t.InsertRange(RawLineAt(t, 1001), new DxfTag[] { new(101, "Embedded Object"), new(40, -99.0) }));
                SameDoubleBits(7.5, ConicScalar(ConicRead(raw, ConicRecord(raw, arc), arc), "Radius"), "Embedded radius leaked");
                Throws<NotSupportedException>(() => ConicEdit(raw, ConicRecord(raw, arc), arc, Vector3.Zero, 2));
            });
            foreach (double bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, 0.0, -1.0 })
                Run($"raw-conic/reject-radius/{arc}/{ParameterBits(bad)}", () =>
                {
                    var raw = Modified(_ => { });
                    Throws<ArgumentOutOfRangeException>(() => ConicEdit(raw, ConicRecord(raw, arc), arc, Vector3.Zero, bad));
                });
            foreach (double bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
                for (int axis = 0; axis < (arc ? 5 : 3); axis++)
                {
                    int a = axis;
                    Run($"raw-conic/reject-scalar/{arc}/{a}/{ParameterBits(bad)}", () =>
                    {
                        var raw = Modified(_ => { }); var p = new double[5]; p[a] = bad;
                        Throws<ArgumentOutOfRangeException>(() => ConicEdit(raw, ConicRecord(raw, arc), arc, new(p[0], p[1], p[2]), 2, p[3], p[4]));
                    });
                }
            Run($"raw-conic/defaults-extremes/{arc}", () =>
            {
                var tags = ConicTags(DxfVersion.AutoCad12, arc, omit: true);
                tags.RemoveAll(t => t.Code == 39 || t.Code == 210 || t.Code == 220 || t.Code == 230);
                var raw = DxfRawDocument.Create(tags); var r = ConicRecord(raw, arc);
                object g = ConicRead(raw, r, arc); RawLinePointBits(Vector3.UnitZ, RawLinePoint(g, "ExtrusionDirection"));
                Equal(0.0, ConicScalar(g, "Thickness"), "Default thickness");
                var changed = ConicEdit(raw, r, arc, new(double.MaxValue, double.Epsilon, -0.0), double.Epsilon, -90, 720);
                g = ConicRead(changed, ConicRecord(changed, arc), arc);
                RawLinePointBits(new(double.MaxValue, double.Epsilon, -0.0), RawLinePoint(g, "CenterInObjectCoordinates"));
                SameDoubleBits(double.Epsilon, ConicScalar(g, "Radius"), "Subnormal radius");
                if (arc) { Equal(-90.0, ConicScalar(g, "StartAngle"), "Angle normalized"); Equal(720.0, ConicScalar(g, "EndAngle"), "Angle normalized"); }
                raw = DxfRawDocument.Create(tags, false, new DxfRawOptions(maximumTags: tags.Count));
                Throws<InvalidOperationException>(() => ConicEdit(raw, ConicRecord(raw, arc), arc, Vector3.UnitZ, 2));
                Throws<ArgumentException>(() => ConicRead(raw, r, arc));
                Throws<ArgumentNullException>(() => ConicRead(raw, null!, arc));
                Throws<ArgumentException>(() => ConicRead(raw, raw.Sections.SelectMany(s => s.Records).First(x => x.Name == "POINT"), arc));
            });
            Run($"raw-conic/reordered-fields/{arc}", () =>
            {
                var raw = Modified(t => { int i = RawLineAt(t, 10); var tag = t[i]; t.RemoveAt(i); t.Insert(RawLineAt(t, 40) + 1, tag); });
                var changed = ConicEdit(raw, ConicRecord(raw, arc), arc, Vector3.Zero, 2);
                RawLinePointBits(Vector3.Zero, RawLinePoint(ConicRead(changed, ConicRecord(changed, arc), arc), "CenterInObjectCoordinates"));
            });
            foreach (DxfVersion version in SupportedVersions) foreach (bool binary in new[] { false, true })
                Run($"raw-conic/typed/{arc}/{version}/{binary}", () =>
                {
                    var doc = new DxfDocument(version); doc.Comments.Clear();
                    EntityObject entity = arc ? new Arc(new Vector3(1, 2, 3), 7.5, 15, 270) : new Circle(new Vector3(1, 2, 3), 7.5);
                    entity.Normal = new(0, .6, .8); doc.Entities.Add(entity);
                    using var output = new MemoryStream(); Check(doc.Save(output, binary), "Typed save");
                    var raw = LoadRaw(output.ToArray()); var center = new Vector3(-8.5, 16.25, -32);
                    var changed = ConicEdit(raw, ConicRecord(raw, arc), arc, center, 3.75);
                    using var input = new MemoryStream(SaveRaw(changed, binary)); var loaded = DxfDocument.Load(input)!;
                    Vector3 actual = arc ? loaded.Entities.Arcs.Single().Center : loaded.Entities.Circles.Single().Center;
                    Vector3 expected = MathHelper.Transform(center, entity.Normal, CoordinateSystem.Object, CoordinateSystem.World);
                    Near(expected.X, actual.X, "OCS->WCS X"); Near(expected.Y, actual.Y, "OCS->WCS Y"); Near(expected.Z, actual.Z, "OCS->WCS Z");
                });
        }
    }
}
