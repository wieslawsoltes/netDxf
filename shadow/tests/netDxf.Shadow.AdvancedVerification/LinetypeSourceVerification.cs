using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.Tables;
using netDxf.Shadow;
using netDxf.Shadow.NetDxf;

internal static class LinetypeSourceVerification
{
    public static void Register(List<(string Name, Action Run)> tests)
    {
        foreach (var version in new[] { DxfVersion.AutoCad2000, DxfVersion.AutoCad2007, DxfVersion.AutoCad2013, DxfVersion.AutoCad2018 })
        foreach (var binary in new[] { false, true })
            tests.Add(($"linetype-source/{version}/{(binary ? "binary" : "ascii")}/unresolved shapes survive two round trips", () => RoundTrip(version, binary)));
        tests.Add(("linetype-source/clone retains numeric identity and isolates style", () =>
        {
            var original = Shape(17, -0.5);
            var clone = (LinetypeShapeSegment)original.Clone();
            Check(clone.ShapeNumber == 17 && clone.Name == original.Name && clone.Offset == original.Offset);
            Check(!ReferenceEquals(clone.Style, original.Style));
            clone.ShapeNumber = 23;
            Check(original.ShapeNumber == 17);
        }));
        tests.Add(("linetype-source/numeric identity is explicit and nullable", () =>
        {
            var shape = Shape(17, 0);
            shape.Name = "OTHER";
            shape.Style = new ShapeStyle("shadow-other-not-installed.shx");
            Check(shape.ShapeNumber == 17);
            shape.ShapeNumber = null;
            Check(shape.ShapeNumber is null && ((LinetypeShapeSegment)shape.Clone()).ShapeNumber is null);
        }));
        tests.Add(("linetype-source/detached capture retains missing-resource identifiers", () =>
        {
            var doc = new DxfDocument();
            var shape = Shape(17, -0.5);
            var type = new Linetype("SHADOW_CAPTURE", new LinetypeSegment[] { new LinetypeSimpleSegment(2), shape });
            doc.Entities.Add(new Line(Vector3.Zero, new Vector3(20, 0, 0)) { Linetype = type });
            var session = new ShadowSession(doc);
            var id = new ObjectId("H:" + type.Handle.ToUpperInvariant());
            var retained = session.Current;
            Check(retained.Read(id)["Segments"].Items.Last()["ShapeNumber"].Number() == 17);
            session.MutateSource(_ => shape.ShapeNumber = 23);
            Check(session.Current.Read(id)["Segments"].Items.Last()["ShapeNumber"].Number() == 23);
            Check(retained.Read(id)["Segments"].Items.Last()["ShapeNumber"].Number() == 17);
        }));
    }

    private static LinetypeShapeSegment Shape(short number, double length) => new("UNRESOLVED", new ShapeStyle("shadow-never-installed-resource.shx"),
        length, new Vector2(0.125, -0.25), LinetypeSegmentRotationType.Absolute, 37, 0.75) { ShapeNumber = number };

    private static void RoundTrip(DxfVersion version, bool binary)
    {
        var document = new DxfDocument(version);
        var numbers = new short[] { 0, 17, short.MaxValue, short.MinValue, -1 };
        var segments = new List<LinetypeSegment> { new LinetypeSimpleSegment(2) };
        segments.AddRange(numbers.Select(number => Shape(number, -0.5)));
        var type = new Linetype("SHADOW_MISSING_RESOURCE", segments);
        document.Entities.Add(new Line(Vector3.Zero, new Vector3(20, 0, 0)) { Linetype = type });
        for (var pass = 0; pass < 2; pass++)
        {
            using var stream = new MemoryStream();
            Check(document.Save(stream, binary), "DXF save failed.");
            stream.Position = 0;
            document = DxfDocument.Load(stream) ?? throw new Exception("DXF load failed.");
            var loaded = document.Linetypes["SHADOW_MISSING_RESOURCE"];
            Check(loaded.Segments.Count == numbers.Length + 1, "Loading must not delete unresolved shape segments.");
            Check(Math.Abs(loaded.Length() - 4.5) < 1e-12, "Resource availability changed the pattern period.");
            var shapes = loaded.Segments.OfType<LinetypeShapeSegment>().ToArray();
            Check(shapes.Select(shape => shape.ShapeNumber).SequenceEqual(numbers.Select(number => (short?)number)));
            foreach (var shape in shapes)
            {
                Check(shape.Length == -0.5 && shape.Scale == 0.75 && shape.Rotation == 37);
                Check(shape.Offset == new Vector2(0.125, -0.25));
                Check(shape.RotationType == LinetypeSegmentRotationType.Absolute);
            }
            Check(document.Entities.Lines.Single().Linetype == loaded, "The entity must retain its canonical linetype.");
        }
    }

    private static void Check(bool condition, string message = "Linetype source assertion failed.")
    { if (!condition) throw new Exception(message); }
}
