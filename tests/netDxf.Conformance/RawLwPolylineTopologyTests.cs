// Copyright (c) netDxf contributors. Licensed under the MIT License.
using netDxf;
using netDxf.Header;
using netDxf.IO;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static DxfRawDocument LwTopologySource(int variant = 1)
        => DxfRawDocument.Create(RawLwTags(DxfVersion.AutoCad2018, false, variant));
    private static void LwTopologySameVertex(DxfRawLwPolylineVertex a, DxfRawLwPolylineVertex b)
    {
        SameDoubleBits(a.Position.X, b.Position.X, "Surviving X");
        SameDoubleBits(a.Position.Y, b.Position.Y, "Surviving Y");
        SameDoubleBits(a.StartWidth, b.StartWidth, "Surviving start width");
        SameDoubleBits(a.EndWidth, b.EndWidth, "Surviving end width");
        SameDoubleBits(a.Bulge, b.Bulge, "Surviving bulge");
        Equal(a.Identifier, b.Identifier, "Surviving identifier");
        Equal(a.HasStartWidth, b.HasStartWidth, "Surviving width presence");
        Equal(a.HasEndWidth, b.HasEndWidth, "Surviving width presence");
        Equal(a.HasBulge, b.HasBulge, "Surviving bulge presence");
    }
    private static void RegisterRawLwPolylineTopologyTests()
    {
        foreach (DxfVersion version in HandleProfiles.Where(v => v >= DxfVersion.AutoCad14))
            foreach (bool binary in new[] { false, true }) foreach (bool block in new[] { false, true })
                for (int action = 0; action < 6; action++)
                {
                    int op = action;
                    Run($"raw-lw-topology/matrix/{version}/{binary}/{block}/{op}", () =>
                    {
                        var raw = LoadRaw(RawFixtureBytes(RawLwTags(version, block, 1), binary));
                        var record = RawLwRecord(raw); var original = RawLwRead(raw);
                        byte[] before = SaveRaw(raw);
                        bool insert = op < 3; int index = insert ? new[] { 0, 1, 3 }[op] : op - 3;
                        var edited = insert ? raw.InsertLwPolylineVertex(record, index, new Vector2(-8.5, 16.25), 1.25, .5, .25, -200)
                            : raw.RemoveLwPolylineVertex(record, index);
                        var changed = RawLwRead(edited); var replacement = RawLwRecord(edited);
                        Equal(insert ? 4 : 2, changed.Vertices.Count, "Updated vertex count");
                        Equal(original.Flags, changed.Flags, "Topology changed flags");
                        SameDoubleBits(original.Elevation, changed.Elevation, "Topology changed elevation");
                        SameDoubleBits(original.Thickness, changed.Thickness, "Topology changed thickness");
                        RawLinePointBits(original.ExtrusionDirection, changed.ExtrusionDirection);
                        for (int i = 0; i < original.Vertices.Count; i++)
                        {
                            if (!insert && i == index) continue;
                            int at = insert ? i + (i >= index ? 1 : 0) : i - (i > index ? 1 : 0);
                            LwTopologySameVertex(original.Vertices[i], changed.Vertices[at]);
                        }
                        if (insert)
                        {
                            var vertex = changed.Vertices[index];
                            SameDoubleBits(-8.5, vertex.Position.X, "Inserted X"); SameDoubleBits(16.25, vertex.Position.Y, "Inserted Y");
                            Equal(1.25, vertex.StartWidth, "Inserted start width"); Equal(.5, vertex.EndWidth, "Inserted end width");
                            Equal(.25, vertex.Bulge, "Inserted bulge"); Equal((int?)-200, vertex.Identifier, "Inserted identifier");
                        }
                        var oldIds = original.Vertices.Select(v => v.Identifier).ToArray();
                        Check(oldIds.SequenceEqual(RawLwRead(raw).Vertices.Select(v => v.Identifier)), "Source IDs changed");
                        Check(before.SequenceEqual(SaveRaw(raw)) && !edited.HasOriginalBytes, "Source bytes or output identity changed");
                        AssertOutsideRecordUnchanged(raw, record, edited, replacement.Tags.Count);
                        // Every surviving pre-existing tag is the identical object, except count.
                        foreach (var tag in record.Tags)
                            if (tag.Code != 90 && (insert || !new short[] { 10, 20, 40, 41, 42, 91 }.Contains(tag.Code)))
                                Check(replacement.Tags.Any(t => ReferenceEquals(t, tag)), "Unselected tag object replaced");
                        foreach (bool output in new[] { false, true })
                        {
                            byte[] bytes = SaveRaw(edited, output); var loaded = LoadRaw(bytes);
                            SameRawTags(edited.Tags, loaded.Tags); Equal(version, loaded.Version, "Topology changed dialect");
                            string stem = $"raw-lw-topology-{version}-{binary}-{block}-{op}-{output}";
                            File.WriteAllBytes(Path.Combine(ArtifactDirectory, stem + "-before.dxf"), SaveRaw(raw, output));
                            File.WriteAllBytes(Path.Combine(ArtifactDirectory, stem + "-after.dxf"), bytes);
                        }
                        Throws<ArgumentException>(() => edited.RemoveLwPolylineVertex(record, 0));
                        Throws<ArgumentException>(() => edited.InsertLwPolylineVertex(record, 0, Vector2.Zero));
                    });
                }
        foreach (int index in new[] { -1, 4, int.MaxValue })
            Run($"raw-lw-topology/insert-index/{index}", () =>
            { var r = LwTopologySource(); Throws<ArgumentOutOfRangeException>(() => r.InsertLwPolylineVertex(RawLwRecord(r), index, Vector2.Zero)); });
        foreach (int index in new[] { -1, 3, int.MaxValue })
            Run($"raw-lw-topology/remove-index/{index}", () =>
            { var r = LwTopologySource(); Throws<ArgumentOutOfRangeException>(() => r.RemoveLwPolylineVertex(RawLwRecord(r), index)); });
        for (int field = 0; field < 5; field++) foreach (double bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            int f = field;
            Run($"raw-lw-topology/nonfinite/{f}/{ParameterBits(bad)}", () =>
            {
                var r = LwTopologySource(); var values = new double[5]; values[f] = bad; byte[] before = SaveRaw(r);
                Throws<ArgumentOutOfRangeException>(() => r.InsertLwPolylineVertex(RawLwRecord(r), 1,
                    new Vector2(values[0], values[1]), values[2], values[3], values[4]));
                Check(before.SequenceEqual(SaveRaw(r)), "Failed insertion changed source");
            });
        }
        foreach (bool start in new[] { false, true })
            Run($"raw-lw-topology/negative-width/{start}", () =>
            { var r = LwTopologySource(); Throws<ArgumentOutOfRangeException>(() => r.InsertLwPolylineVertex(RawLwRecord(r), 1, Vector2.Zero, start ? -1 : 0, start ? 0 : -1)); });
        foreach (int identifier in new[] { int.MinValue, -7, 0, 100, int.MaxValue })
            Run($"raw-lw-topology/identifier/{identifier}", () =>
            {
                var r = LwTopologySource(); var record = RawLwRecord(r);
                if (identifier == -7 || identifier == 100)
                    Throws<ArgumentException>(() => r.InsertLwPolylineVertex(record, 0, Vector2.Zero, identifier: identifier));
                else
                {
                    var changed = r.InsertLwPolylineVertex(record, 0, Vector2.Zero, identifier: identifier);
                    Equal((int?)identifier, RawLwRead(changed).Vertices[0].Identifier, "Opaque signed identifier changed");
                }
            });
        foreach (int variant in new[] { 0, 1, 2, 3 })
            Run($"raw-lw-topology/remove-attributes/{variant}", () =>
            {
                var raw = LwTopologySource(variant); var old = RawLwRead(raw);
                var changed = raw.RemoveLwPolylineVertex(RawLwRecord(raw), 1); var current = RawLwRead(changed);
                LwTopologySameVertex(old.Vertices[0], current.Vertices[0]); LwTopologySameVertex(old.Vertices[2], current.Vertices[1]);
                Equal(old.ConstantWidth, current.ConstantWidth, "Removal changed constant width");
            });
        Run("raw-lw-topology/width-conflict", () =>
        {
            var r = LwTopologySource(2); var record = RawLwRecord(r);
            Throws<NotSupportedException>(() => r.InsertLwPolylineVertex(record, 1, Vector2.Zero, 1, 0));
            Throws<NotSupportedException>(() => r.InsertLwPolylineVertex(record, 1, Vector2.Zero, 0, 1));
            var changed = r.InsertLwPolylineVertex(record, 1, Vector2.Zero);
            Equal(2.0, RawLwRead(changed).ConstantWidth, "Insertion changed constant width");
            Check(!RawLwRead(changed).Vertices[1].HasStartWidth, "Default width materialized");
        });
        foreach (double v in new[] { -0.0, double.Epsilon, double.MaxValue })
            Run("raw-lw-topology/extreme/" + ParameterBits(v), () =>
            {
                var r = LwTopologySource(); var changed = r.InsertLwPolylineVertex(RawLwRecord(r), 3, new Vector2(v, -v), v, v, -v);
                foreach (bool binary in new[] { false, true })
                {
                    var loaded = LoadRaw(SaveRaw(changed, binary)); var vertex = RawLwRead(loaded).Vertices[3];
                    SameDoubleBits(v, vertex.Position.X, "Extreme X"); SameDoubleBits(-v, vertex.Position.Y, "Extreme Y");
                    SameDoubleBits(v, vertex.StartWidth, "Extreme width"); SameDoubleBits(-v, vertex.Bulge, "Extreme bulge");
                }
            });
        Run("raw-lw-topology/empty-roundtrip", () =>
        {
            var raw = LwTopologySource();
            for (int i = 0; i < 3; i++) raw = raw.RemoveLwPolylineVertex(RawLwRecord(raw), 0);
            var empty = RawLwRead(raw); Equal(0, empty.Vertices.Count, "Final deletion did not clear vertices");
            Equal((short)129, empty.Flags, "Empty definition lost flags");
            Throws<ArgumentOutOfRangeException>(() => raw.RemoveLwPolylineVertex(RawLwRecord(raw), 0));
            var inserted = raw.InsertLwPolylineVertex(RawLwRecord(raw), 0, new Vector2(5, 6));
            var vertex = RawLwRead(inserted).Vertices.Single(); Equal((int?)null, vertex.Identifier, "Identifier invented");
            Check(!vertex.HasStartWidth && !vertex.HasEndWidth && !vertex.HasBulge, "Defaults materialized");
            foreach (bool binary in new[] { false, true }) SameRawTags(inserted.Tags, LoadRaw(SaveRaw(inserted, binary)).Tags);
        });
        Run("raw-lw-topology/interleaved-header-preservation", () =>
        {
            var tags = RawLwTags(DxfVersion.AutoCad2018, false, 3);
            int at = tags.FindIndex(t => t.Code == 20); var layer = tags.First(t => t.Code == 8);
            tags.Remove(layer); tags.Insert(at, layer); tags.Insert(at + 1, new DxfTag(999, "vertex-adjacent comment"));
            var r = DxfRawDocument.Create(tags); var changed = r.RemoveLwPolylineVertex(RawLwRecord(r), 0);
            Check(changed.Tags.Any(t => ReferenceEquals(t, layer)), "Removing a vertex deleted an interleaved entity field");
            Check(changed.Tags.Any(t => t.Code == 999 && Equals(t.Value, "vertex-adjacent comment")), "Removing a vertex deleted a comment");
            Equal(2, RawLwRead(changed).Vertices.Count, "Interleaved removal count");
        });
        Run("raw-lw-topology/tag-budget", () =>
        {
            var tags = RawLwTags(DxfVersion.AutoCad2018, false, 1);
            var r = DxfRawDocument.Create(tags, false, new DxfRawOptions(maximumTags: tags.Count)); byte[] before = SaveRaw(r);
            Throws<InvalidOperationException>(() => r.InsertLwPolylineVertex(RawLwRecord(r), 0, Vector2.Zero));
            Check(before.SequenceEqual(SaveRaw(r)), "Budget refusal mutated source");
            var smaller = r.RemoveLwPolylineVertex(RawLwRecord(r), 1);
            var restored = smaller.InsertLwPolylineVertex(RawLwRecord(smaller), 1, new Vector2(4, 5), identifier: -7);
            SameRawTags(r.Tags, restored.Tags);
        });
        foreach (short code in new short[] { 92, 310, 1005, 1010, 1041, 1042 })
            Run($"raw-lw-topology/guard/{code}", () =>
            {
                var tags = RawLwTags(DxfVersion.AutoCad2018, false, 1);
                tags.Insert(RawLineAt(tags, 1001) + (code >= 1000 ? 1 : 0), new DxfTag(code,
                    code == 92 ? (object)0 : code == 310 ? new byte[] { 1 } : code == 1005 ? "B" : 2.0));
                var r = DxfRawDocument.Create(tags); var record = RawLwRecord(r); byte[] before = SaveRaw(r);
                Throws<NotSupportedException>(() => r.InsertLwPolylineVertex(record, 0, Vector2.Zero));
                Throws<NotSupportedException>(() => r.RemoveLwPolylineVertex(record, 0));
                Check(before.SequenceEqual(SaveRaw(r)), "Guard failure changed source");
            });
        foreach (short code in new short[] { 320, 330, 340, 350, 360, 1005 })
            Run($"raw-lw-topology/incoming/{code}", () =>
            {
                var tags = RawLwTags(DxfVersion.AutoCad2018, false, 1);
                int at = tags.FindIndex(t => t.Code == 0 && Equals(t.Value, "POINT")) + 2;
                if (code == 1005) tags.Insert(at++, new DxfTag(1001, "REF")); tags.Insert(at, new DxfTag(code, "000a"));
                var r = DxfRawDocument.Create(tags); var record = RawLwRecord(r);
                Throws<NotSupportedException>(() => r.InsertLwPolylineVertex(record, 0, Vector2.Zero));
                Throws<NotSupportedException>(() => r.RemoveLwPolylineVertex(record, 0));
            });
        Run("raw-lw-topology/schema-and-snapshot", () =>
        {
            var r = LwTopologySource(); var foreign = LwTopologySource();
            Throws<ArgumentNullException>(() => r.InsertLwPolylineVertex(null!, 0, Vector2.Zero));
            Throws<ArgumentNullException>(() => r.RemoveLwPolylineVertex(null!, 0));
            Throws<ArgumentException>(() => r.InsertLwPolylineVertex(RawLwRecord(foreign), 0, Vector2.Zero));
            Throws<ArgumentException>(() => r.RemoveLwPolylineVertex(RawLwRecord(foreign), 0));
            var tags = RawLwTags(DxfVersion.AutoCad2018, false, 1);
            tags[RawLineAt(tags, 90)] = new DxfTag(90, 2);
            var malformed = DxfRawDocument.Create(tags);
            Throws<FormatException>(() => malformed.InsertLwPolylineVertex(RawLwRecord(malformed), 0, Vector2.Zero));
            Throws<FormatException>(() => malformed.RemoveLwPolylineVertex(RawLwRecord(malformed), 0));
        });
    }
}
