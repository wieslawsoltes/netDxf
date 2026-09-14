using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Tables;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterSplineFlagTests()
    {
        foreach (DxfVersion version in SupportedVersions)
            foreach (bool binary in new[] { false, true })
                foreach (int kind in Enumerable.Range(0, 5))
                    foreach (SplineKnotParameterization parameter in Enum.GetValues<SplineKnotParameterization>())
                    {
                        DxfVersion v = version; bool b = binary; int k = kind; SplineKnotParameterization p = parameter;
                        Run($"spline/flags/{v}/{b}/{k}/{p}", () => SplineFlags(v, b, k, p));
                    }
    }

    private static Spline FlagSpline(int kind)
    {
        var points = new[] { new Vector3(0, 0, 0), new Vector3(3, 7, 2), new Vector3(9, 4, -1), new Vector3(12, 2, 6) };
        if (kind == 1 || kind == 4) points[3] = points[0];
        Spline spline = kind >= 3 ? new Spline(points) : new Spline(points, new[] { 1.0, 0.75, 1.25, 1.0 }, (short)2, kind == 2);
        spline.StartTangent = new Vector3(2, 3, 4); spline.EndTangent = new Vector3(5, -6, 7);
        var data = new XData(new ApplicationRegistry("SPLINE_FLAGS"));
        data.XDataRecord.Add(new XDataRecord(XDataCode.Int16, (short)kind)); spline.XData.Add(data);
        return spline;
    }

    private static void SplineFlags(DxfVersion version, bool binary, int kind, SplineKnotParameterization parameter)
    {
        var original = FlagSpline(kind); original.KnotParameterization = parameter;
        int expected = 4 | (int)parameter;
        if (kind == 1 || kind == 4) expected |= 1;
        if (kind == 2) expected |= 1 | 2 | 2048;
        if (kind >= 3) expected |= 1024;
        var doc = new DxfDocument(version); doc.Entities.Add(original); doc.Entities.Add((Spline)original.Clone());
        doc.Entities.Add(new Line(new Vector3(20, 30, 40), new Vector3(50, 60, 70)));
        for (int cycle = 0; cycle < 3; ++cycle)
        {
            using var stream = new MemoryStream(); bool transport = cycle % 2 == 0 ? binary : !binary;
            Check(doc.Save(stream, transport), "Spline flags save failed.");
            stream.Position = 0;
            var raw = DxfRawDocument.Load(stream);
            foreach (var record in raw.Sections.SelectMany(s => s.Records).Where(r => r.Name == "SPLINE"))
                Equal(expected, (int)(short)record.Tags.Single(t => t.Code == 70).Value, "Independent SPLINE flag mask");
            if (cycle == 0 && parameter == SplineKnotParameterization.FitChord)
                File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"spline-flags-{version}-{binary}-{kind}.dxf"), stream.ToArray());
            stream.Position = 0; doc = DxfDocument.Load(stream) ?? throw new InvalidOperationException("Spline flag reload failed.");
            foreach (Spline spline in doc.Entities.Splines)
            {
                Equal(original.CreationMethod, spline.CreationMethod, "Creation method lost");
                Equal(original.IsClosed, spline.IsClosed, "Control-polygon closure changed");
                Equal(original.IsClosedPeriodic, spline.IsClosedPeriodic, "Periodic state changed");
                Equal(parameter, spline.KnotParameterization, "Parameterization flag changed");
                Check(original.Knots.SequenceEqual(spline.Knots), "Flag update regenerated knots.");
                Check(original.ControlPoints.SequenceEqual(spline.ControlPoints), "Flag update regenerated controls.");
                Check(original.Weights.SequenceEqual(spline.Weights), "Flag update changed weights.");
                Check(original.FitPoints.SequenceEqual(spline.FitPoints), "Flag update changed fit data.");
                Equal(original.StartTangent, spline.StartTangent, "Start tangent changed");
                Equal(original.EndTangent, spline.EndTangent, "End tangent changed");
                Equal((short)kind, (short)spline.XData["SPLINE_FLAGS"].XDataRecord.Single().Value, "Following XData lost");
            }
            Equal(new Vector3(20, 30, 40), doc.Entities.Lines.Single().StartPoint, "Following LINE changed");
            Check(stream.CanRead, "Flag serialization closed caller stream.");
        }
    }
}
