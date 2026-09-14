using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static readonly Matrix3[] SplineTangentMatrices =
    {
        Matrix3.Identity,
        new(0, -1, 0, 1, 0, 0, 0, 0, 1),
        new(2, 0, 0, 0, 3, 0, 0, 0, -4),
        new(1, 2, 0, 0, 1, 3, 4, 0, 1),
        new(0, 0, 0, 0, 0, 0, 0, 0, 0)
    };

    private static void RegisterSplineTangentTransformTests()
    {
        foreach (DxfVersion version in SupportedVersions)
            foreach (bool binary in new[] { false, true })
                for (int mask = 0; mask < 4; mask++)
                    for (int transform = 0; transform < SplineTangentMatrices.Length; transform++)
                    {
                        DxfVersion v = version; bool b = binary; int m = mask, t = transform;
                        Run($"spline/tangent-transform/{v}/{b}/{m}/{t}", () => SplineTangentTransform(v, b, m, t));
                    }
        for (int mask = 0; mask < 4; mask++)
        {
            int m = mask;
            Run($"spline/tangent-transform/fit/{m}", () => SplineFitTangentTransform(m));
            Run($"spline/tangent-transform/insert/{m}", () => SplineInsertTangentTransform(m));
        }
    }

    private static Spline NewTangentSpline(int mask, bool fitted = false)
    {
        Vector3[] points = { new(1, 2, 3), new(4, 6, 9), new(8, 5, 2) };
        Spline spline = fitted ? new Spline(points) : new Spline(points, new[] { 1.0, 0.75, 1.0 }, (short)2);
        if ((mask & 1) != 0) spline.StartTangent = new Vector3(2, -3, 5);
        if ((mask & 2) != 0) spline.EndTangent = new Vector3(-7, 11, 13);
        return spline;
    }

    private static void AssertSplineTangent(Vector3? expected, Vector3? actual, string name)
    {
        Equal(expected.HasValue, actual.HasValue, name + " presence");
        if (expected.HasValue)
        {
            Near(expected.Value.X, actual!.Value.X, name + " X");
            Near(expected.Value.Y, actual.Value.Y, name + " Y");
            Near(expected.Value.Z, actual.Value.Z, name + " Z");
        }
    }

    private static void SplineTangentTransform(DxfVersion version, bool binary, int mask, int index)
    {
        Spline source = NewTangentSpline(mask), spline = (Spline)source.Clone();
        Matrix3 matrix = SplineTangentMatrices[index]; Vector3 translation = new(101, -203, 307);
        Vector3? start = source.StartTangent.HasValue ? matrix * source.StartTangent.Value : null;
        Vector3? end = source.EndTangent.HasValue ? matrix * source.EndTangent.Value : null;
        spline.TransformBy(matrix, translation);
        AssertSplineTangent(start, spline.StartTangent, "Transformed start tangent");
        AssertSplineTangent(end, spline.EndTangent, "Transformed end tangent");
        for (int i = 0; i < source.ControlPoints.Length; i++)
            Equal(matrix * source.ControlPoints[i] + translation, spline.ControlPoints[i], "Control transform changed");
        Check(source.Knots.SequenceEqual(spline.Knots) && source.Weights.SequenceEqual(spline.Weights), "Transform changed parameterization.");
        var copy = (Spline)spline.Clone();
        AssertSplineTangent(start, copy.StartTangent, "Cloned start tangent");
        AssertSplineTangent(end, copy.EndTangent, "Cloned end tangent");
        var doc = new DxfDocument(version); doc.Entities.Add(spline); doc.Entities.Add(copy);
        doc.Entities.Add(new Line(new Vector3(20, 30, 40), new Vector3(50, 60, 70)));
        for (int cycle = 0; cycle < 2; cycle++)
        {
            using var output = new MemoryStream();
            Check(doc.Save(output, cycle == 0 ? binary : !binary), "Transformed spline save failed.");
            if (index == 2 && mask == 3 && cycle == 0)
                File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"spline-tangent-transform-{version}-{binary}.dxf"), output.ToArray());
            output.Position = 0; doc = DxfDocument.Load(output) ?? throw new InvalidOperationException("Transformed spline reload failed.");
            foreach (Spline loaded in doc.Entities.Splines)
            {
                AssertSplineTangent(start, loaded.StartTangent, "Wire start tangent");
                AssertSplineTangent(end, loaded.EndTangent, "Wire end tangent");
            }
            Equal(2, doc.Entities.Splines.Count(), "Spline original/clone count");
            Equal(new Vector3(20, 30, 40), doc.Entities.Lines.Single().StartPoint, "Following LINE");
            Check(output.CanRead, "Save/load closed caller stream.");
        }
        AssertSplineTangent(NewTangentSpline(mask).StartTangent, source.StartTangent, "Source start isolation");
        AssertSplineTangent(NewTangentSpline(mask).EndTangent, source.EndTangent, "Source end isolation");
    }

    private static void SplineFitTangentTransform(int mask)
    {
        var spline = NewTangentSpline(mask, true); var points = spline.FitPoints.ToArray();
        Vector3? start = spline.StartTangent, end = spline.EndTangent;
        Vector3 translation = new(101, -203, 307);
        foreach (Matrix3 matrix in SplineTangentMatrices.Take(4))
        {
            spline.TransformBy(matrix, translation);
            start = start.HasValue ? matrix * start.Value : null;
            end = end.HasValue ? matrix * end.Value : null;
            points = points.Select(p => matrix * p + translation).ToArray();
            AssertSplineTangent(start, spline.StartTangent, "Fit start");
            AssertSplineTangent(end, spline.EndTangent, "Fit end");
            Check(points.SequenceEqual(spline.FitPoints), "Fit-point transform changed.");
        }
    }

    private static void SplineInsertTangentTransform(int mask)
    {
        var source = NewTangentSpline(mask);
        var block = new Block("TangentBlock"); block.Entities.Add(source);
        var insert = new Insert(block, new Vector3(101, -203, 307)) { Rotation = 37, Scale = new Vector3(2, 3, -4) };
        var cloned = (Insert)insert.Clone();
        Matrix3 matrix = cloned.GetTransformation();
        Spline exploded = cloned.Explode().OfType<Spline>().Single();
        AssertSplineTangent(source.StartTangent.HasValue ? matrix * source.StartTangent.Value : null, exploded.StartTangent, "INSERT start");
        AssertSplineTangent(source.EndTangent.HasValue ? matrix * source.EndTangent.Value : null, exploded.EndTangent, "INSERT end");
        AssertSplineTangent(NewTangentSpline(mask).StartTangent, source.StartTangent, "INSERT source start");
        AssertSplineTangent(NewTangentSpline(mask).EndTangent, source.EndTangent, "INSERT source end");
    }
}
