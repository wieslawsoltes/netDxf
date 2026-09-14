using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterSplinePeriodicInputTests()
    {
        foreach (DxfVersion version in SupportedVersions)
            foreach (bool binary in new[] { false, true })
                foreach (short degree in new short[] { 1, 2, 3 })
                    foreach (short flags in new short[] { 2, 3, 7, 2048, 2055 })
                        foreach (bool weighted in new[] { false, true })
                        foreach (int phase in flags == 2 ? new[] { 0, (int)degree } : new[] { 0 })
                        {
                            DxfVersion v = version; bool b = binary, w = weighted; short d = degree, f = flags; int p = phase;
                            Run($"spline/periodic-input/{v}/{b}/{d}/{f}/{w}/{p}", () => SplinePeriodicInput(v, b, d, f, w, p));
                        }
    }

    private static void RegisterSplinePeriodicOverlapTests()
    {
        foreach (DxfVersion version in SupportedVersions)
            foreach (bool binary in new[] { false, true })
                foreach (short flags in new short[] { 2, 2048 })
                    foreach (short component in new short[] { 10, 20, 30, 41 })
                    {
                        DxfVersion v = version; bool b = binary; short f = flags, c = component;
                        Run($"spline/periodic-input/nonrepresentable/{v}/{b}/{f}/{c}", () =>
                        {
                            var tags = PeriodicSplineInputTags(v, 2, f, true);
                            int at = tags.FindIndex(t => t.Code == c);
                            tags[at] = new(c, (double)tags[at].Value + 0.125);
                            using var input = new MemoryStream(RawFixtureBytes(tags, b));
#if DEBUG
                            try { DxfDocument.Load(input); throw new InvalidOperationException("Nonoverlapping cyclic data was silently compacted."); }
                            catch (NotSupportedException error)
                            { Check(error.Message.Contains("Periodic SPLINE", StringComparison.Ordinal), "Missing contextual periodic layout diagnostic."); }
#else
                            Check(DxfDocument.Load(input) == null, "Nonoverlapping cyclic data was silently compacted.");
#endif
                            Check(input.CanRead, "Unsupported import closed caller stream.");
                        });
                    }
    }

    private static List<DxfTag> PeriodicSplineInputTags(DxfVersion version, short degree, short flags, bool weighted, int phase = 0)
    {
        var tags = new List<DxfTag>
        {
            new(0, "SECTION"), new(2, "HEADER"), new(9, "$ACADVER"), new(1, HeaderVersion(version)), new(9, "$DWGCODEPAGE"), new(3, "ANSI_1252"),
            new(0, "ENDSEC"), new(0, "SECTION"), new(2, "ENTITIES"),
            new(0, "SPLINE"), new(5, "200"), new(100, "AcDbEntity"), new(8, "0"),
            new(100, "AcDbSpline"), new(70, flags), new(71, degree),
            new(72, (short)(5 + 2 * degree + 1)), new(73, (short)(5 + degree)), new(74, (short)0)
        };
        // Conventional periodic wire layout: degree-fold cyclic overlap. The
        // prefix and suffix are repeated verbatim, independently of the writer.
        for (int i = 0; i < 5 + 2 * degree + 1; i++) tags.Add(new(40, -3.25 + i * 0.125));
        for (int i = 0; i < 5 + degree; i++)
        {
            int j = (i + 5 - degree + phase) % 5;
            tags.Add(new(10, (double)(j * j))); tags.Add(new(20, (double)(j % 2 * 4 - j))); tags.Add(new(30, (double)j));
            if (weighted) tags.Add(new(41, 1.0 + j * 0.25));
        }
        tags.AddRange(new DxfTag[]
        {
            new(1001, "PERIODIC_INPUT"), new(1000, "standard periodic bit"),
            new(0, "LINE"), new(5, "201"), new(100, "AcDbEntity"), new(8, "0"), new(100, "AcDbLine"),
            new(10, 20.0), new(20, 30.0), new(30, 40.0), new(11, 50.0), new(21, 60.0), new(31, 70.0),
            new(0, "ENDSEC"), new(0, "EOF")
        });
        return tags;
    }

    private static void SplinePeriodicInput(DxfVersion version, bool binary, short degree, short flags, bool weighted, int phase = 0)
    {
        var tags = PeriodicSplineInputTags(version, degree, flags, weighted, phase);
        using var input = new MemoryStream(RawFixtureBytes(tags, binary));
        var doc = DxfDocument.Load(input) ?? throw new InvalidOperationException("Periodic input rejected.");
        var spline = doc.Entities.Splines.Single();
        Check(spline.IsClosedPeriodic, "Documented periodic flag did not set periodic state.");
        Equal(5, spline.ControlPoints.Length, "Wire overlap not normalized into compact controls");
        for (int j = 0; j < 5; ++j)
        {
            int k = (j + phase) % 5;
            Equal(new Vector3(k*k, k%2*4-k, k), spline.ControlPoints[j], "Periodic control order");
            Equal(weighted ? 1.0+k*0.25 : 1.0, spline.Weights[j], "Periodic control weight");
        }
        var expectedKnots = tags.Where(t => t.Code == 40).Select(t => (double)t.Value).ToArray();
        Check(expectedKnots.SequenceEqual(spline.Knots), "Import regenerated periodic knots.");
        var samples = spline.PolygonalVertexes(31).ToArray();
        var block = new Block("PeriodicBlock"); block.Entities.Add((Spline)spline.Clone());
        var clone = (Insert)new Insert(block).Clone();
        var nested = clone.Block.Entities.OfType<Spline>().Single();
        Check(nested.IsClosedPeriodic && nested.ControlPoints.SequenceEqual(spline.ControlPoints), "Nested clone changed periodic data.");
        doc.Entities.Add((Spline)spline.Clone());
        for (int cycle = 0; cycle < 3; ++cycle)
        {
            using var output = new MemoryStream();
            Check(doc.Save(output, cycle % 2 == 0 ? binary : !binary), "Periodic save failed.");
            output.Position = 0;
            foreach (var record in DxfRawDocument.Load(output).Sections.SelectMany(s => s.Records).Where(r => r.Name == "SPLINE"))
            {
                Check(((short)record.Tags.Single(t => t.Code == 70).Value & 3) == 3, "Export omitted standard closed/periodic bits.");
                foreach (short code in new short[] { 10, 20, 30, 40 })
                {
                    var expected = tags.TakeWhile(t => t.Code != 1001).Where(t => t.Code == code).Select(t => (double)t.Value).ToArray();
                    var actual = record.Tags.Where(t => t.Code == code).Select(t => (double)t.Value).ToArray();
                    Equal(expected.Length, actual.Length, "Periodic serialized count");
                    for (int i = 0; i < actual.Length; ++i) SameDoubleBits(expected[i], actual[i], "Periodic wire coordinate/knot");
                }
            }
            if (cycle == 0 && flags == 7 && weighted)
                File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"spline-periodic-input-{version}-{binary}-{degree}.dxf"), output.ToArray());
            output.Position = 0; doc = DxfDocument.Load(output) ?? throw new InvalidOperationException("Periodic reload rejected.");
            foreach (var current in doc.Entities.Splines)
            {
                Check(current.IsClosedPeriodic, "Reload lost periodic state.");
                Check(current.Knots.SequenceEqual(expectedKnots) && current.ControlPoints.SequenceEqual(spline.ControlPoints), "Periodic geometry changed across transports.");
                Check(samples.SequenceEqual(current.PolygonalVertexes(31)), "Periodic evaluator changed across transports.");
                Equal("standard periodic bit", (string)current.XData["PERIODIC_INPUT"].XDataRecord.Single().Value, "Following periodic XData");
            }
            Equal(new Vector3(20, 30, 40), doc.Entities.Lines.Single().StartPoint, "Following LINE");
        }
        Check(input.CanRead, "Periodic parser closed caller stream.");
    }
}
