using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterHelixWireTests()
    {
        foreach (DxfVersion version in SupportedVersions)
            foreach (bool binary in new[] { false, true })
                foreach (short constraint in new short[] { 0, 1, 2 })
                    foreach (bool right in new[] { false, true })
                        foreach (bool reordered in new[] { false, true })
                        {
                            DxfVersion v = version; bool b = binary, r = right, order = reordered; short c = constraint;
                            Run($"helix/wire/{v}/{b}/{c}/{r}/{order}", () => HelixWire(v, b, c, r, order));
                        }
    }

    private static readonly Vector3[] HelixWireControls = { new(5, 0, 0), new(5, 3, 1), new(2, 5, 2), new(0, 5, 3) };

    private static List<DxfTag> HelixWireTags(DxfVersion version, short constraint, bool right, bool reordered)
    {
        var tags = new List<DxfTag>
        {
            new(0, "SECTION"), new(2, "HEADER"), new(9, "$ACADVER"), new(1, HeaderVersion(version)),
            new(9, "$DWGCODEPAGE"), new(3, "ANSI_1252"), new(0, "ENDSEC"), new(0, "SECTION"), new(2, "ENTITIES"),
            new(0, "HELIX"), new(5, "200"), new(100, "AcDbEntity"), new(8, "0"), new(100, "AcDbSpline"),
            new(70, (short)4), new(71, (short)3), new(72, (short)8), new(73, (short)4), new(74, (short)0),
            new(12, 3.0), new(22, 4.0), new(32, 5.0), new(13, 6.0), new(23, 7.0), new(33, 8.0)
        };
        for (int i = 0; i < 8; ++i) tags.Add(new(40, i < 4 ? 0.0 : 1.0));
        for (int i = 0; i < 4; ++i)
        {
            Vector3 p = HelixWireControls[i];
            tags.AddRange(new DxfTag[] { new(10, p.X), new(20, p.Y), new(30, p.Z), new(41, 1.0 + i * 0.125) });
        }
        tags.Add(new(100, "AcDbHelix"));
        var definition = new List<DxfTag>
        {
            new(90, 29), new(91, 63), new(10, 17.25), new(20, -11.5), new(30, 23.0),
            new(11, 22.25), new(21, -11.5), new(31, 23.0), new(12, 0.0), new(22, 0.0), new(32, 2.0),
            new(40, 2.75), new(41, 3.125), new(42, -1.5), new(290, right), new(280, constraint)
        };
        // The spline and definition intentionally differ: import must retain
        // both authored representations, not silently fit replacement geometry.
        if (reordered) definition.Reverse();
        tags.AddRange(definition);
        tags.AddRange(new DxfTag[]
        {
            new(1001, "HELIX_TEST"), new(1000, "after helix"), new(0, "LINE"), new(5, "201"),
            new(100, "AcDbEntity"), new(8, "0"), new(100, "AcDbLine"),
            new(10, 20.0), new(20, 30.0), new(30, 40.0), new(11, 50.0), new(21, 60.0), new(31, 70.0),
            new(0, "ENDSEC"), new(0, "EOF")
        });
        return tags;
    }

    private static void HelixWire(DxfVersion version, bool binary, short constraint, bool right, bool reordered)
    {
        var tags = HelixWireTags(version, constraint, right, reordered);
        using var input = new MemoryStream(RawFixtureBytes(tags, binary));
        var doc = DxfDocument.Load(input) ?? throw new InvalidOperationException("HELIX input rejected.");
        EntityObject entity = doc.Entities.All.Single(e => e.CodeName == "HELIX");
        Check(entity is Spline, "HELIX must retain its inherited spline representation.");
        var spline = (Spline)entity;
        Check(spline.ControlPoints.SequenceEqual(HelixWireControls), "HELIX controls were refitted.");
        Equal<Vector3?>(new(3, 4, 5), spline.StartTangent, "Spline tangent collided with HELIX axis codes");
        Equal<Vector3?>(new(6, 7, 8), spline.EndTangent, "Spline end tangent");
        var clone = (EntityObject)entity.Clone();
        Equal("HELIX", clone.CodeName, "Clone sliced the derived entity");
        doc.Entities.Add(clone);
        for (int cycle = 0; cycle < 3; ++cycle)
        {
            using var output = new MemoryStream();
            if (version < DxfVersion.AutoCad2007)
            {
#if DEBUG
                Throws<NotSupportedException>(() => doc.Save(output, binary));
#else
                Check(!doc.Save(output, binary), "Earlier writer profile admitted HELIX.");
#endif
                Equal(0L, output.Length, "HELIX preflight wrote to stream");
                return;
            }
            Check(doc.Save(output, cycle % 2 == 0 ? binary : !binary), "HELIX save failed.");
            output.Position = 0;
            var raw = DxfRawDocument.Load(output);
            var records = raw.Sections.SelectMany(s => s.Records).Where(r => r.Name == "HELIX").ToArray();
            Equal(2, records.Length, "Original/clone HELIX count");
            foreach (var record in records)
            {
                var packet = record.Tags.SkipWhile(t => !(t.Code == 100 && Equals(t.Value, "AcDbHelix")))
                    .Skip(1).TakeWhile(t => t.Code != 1001).ToArray();
                var expected = tags.SkipWhile(t => !(t.Code == 100 && Equals(t.Value, "AcDbHelix")))
                    .Skip(1).TakeWhile(t => t.Code != 1001).ToArray();
                foreach (DxfTag value in expected)
                    Equal(value.Value, packet.Single(t => t.Code == value.Code).Value, "HELIX parameter changed");
            }
            if (cycle == 0 && !reordered)
                File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"helix-wire-{version}-{binary}-{constraint}-{right}.dxf"), output.ToArray());
            output.Position = 0; doc = DxfDocument.Load(output) ?? throw new InvalidOperationException("HELIX reload failed.");
            foreach (var current in doc.Entities.All.Where(e => e.CodeName == "HELIX").Cast<Spline>())
            {
                Check(current.ControlPoints.SequenceEqual(HelixWireControls), "Repeated HELIX save changed control polygon.");
                Check(spline.Knots.SequenceEqual(current.Knots) && spline.Weights.SequenceEqual(current.Weights), "Repeated HELIX knots/weights changed.");
                Equal("after helix", (string)current.XData["HELIX_TEST"].XDataRecord.Single().Value, "HELIX XData");
            }
            Equal(new Vector3(20, 30, 40), doc.Entities.Lines.Single().StartPoint, "Following LINE");
        }
        Check(input.CanRead, "HELIX reader closed caller stream.");
    }
}
