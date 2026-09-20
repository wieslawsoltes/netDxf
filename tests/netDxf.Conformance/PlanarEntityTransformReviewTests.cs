using netDxf;
using netDxf.Entities;
using netDxf.Header;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterPlanarEntityTransformReviewTests()
    {
        foreach (bool trace in new[] { false, true })
        {
            for (int scenario = 0; scenario < 13; scenario++)
                foreach (bool matrix4 in new[] { false, true })
                {
                    int s = scenario;
                    Run($"planar-review/geometry/{trace}/{s}/{matrix4}", () => PlanarReviewGeometry(trace, s, matrix4));
                }
            for (int fault = 0; fault < 43; fault++)
            {
                int f = fault;
                Run($"planar-review/atomic-rejection/{trace}/{f}", () => PlanarReviewReject(trace, f));
            }
            foreach (double scale in new[] { 1e-310, 1e-200, 1e200 })
                Run($"planar-review/extreme/{trace}/{scale:R}", () => PlanarReviewExtreme(trace, scale));
            Run($"planar-review/triangle/{trace}", () =>
            {
                EntityObject entity = PlanarReviewEntity(trace, 0);
                if (entity is Solid solid) solid.FourthVertex = solid.ThirdVertex;
                else ((Trace)entity).FourthVertex = ((Trace)entity).ThirdVertex;
                entity.TransformBy(Matrix3.Scale(-2, 3, 4), new Vector3(5, 6, 7));
                Vector2[] vertices = PlanarReviewVertices(entity);
                Equal(vertices[2], vertices[3], "Repeated triangle corner changed");
            });
        }
        foreach (DxfVersion version in SupportedVersions)
            foreach (bool binary in new[] { false, true })
                for (int scenario = 0; scenario < 13; scenario++)
                {
                    int s = scenario;
                    Run($"planar-review/wire/{version}/{binary}/{s}", () => PlanarReviewWire(version, binary, s));
                }
    }

    private static EntityObject PlanarReviewEntity(bool trace, int scenario)
    {
        EntityObject entity = trace
            ? new Trace(new Vector2(1, 2), new Vector2(4, 3), new Vector2(2, 7), new Vector2(6, 8)) { Elevation = 7.25, Thickness = scenario == 4 ? 0 : 2.5 }
            : new Solid(new Vector2(1, 2), new Vector2(4, 3), new Vector2(2, 7), new Vector2(6, 8)) { Elevation = 7.25, Thickness = scenario == 4 ? 0 : 2.5 };
        entity.Normal = scenario == 9 ? new Vector3(1, 2, 3) : Vector3.UnitZ;
        entity.IsVisible = false; entity.Color = new AciColor(3);
        entity.ProxyGraphics = new byte[] { 1, 7, 9, 255 };
        return entity;
    }

    private static (Matrix3 Matrix, Vector3 Translation) PlanarReviewOperation(int scenario)
    {
        Matrix3 matrix = scenario switch
        {
            2 => Matrix3.Scale(2, 3, 4),
            3 => new Matrix3(1, .5, 0, 0, 2, 0, 0, 0, 3),
            4 => new Matrix3(1, 0, 0, 0, 1, 0, .5, -.25, 1),
            5 => Matrix3.Scale(-2, 3, 4),
            6 => Matrix3.Scale(2, 3, -4),
            7 => new Matrix3(0, 0, 1, 0, 1, 0, -1, 0, 0),
            8 => Matrix3.Scale(2, 3, 0),
            9 => new Matrix3(0, -3, 0, 3, 0, 0, 0, 0, 3),
            10 => Matrix3.Scale(1e-150),
            11 => Matrix3.Scale(1e150),
            _ => Matrix3.Identity
        };
        Vector3 translation = scenario == 1 ? new Vector3(9, -4, 7)
            : scenario == 12 ? new Vector3(1e15, -1e15, 7) : Vector3.Zero;
        return (matrix, translation);
    }

    private static Vector2[] PlanarReviewVertices(EntityObject entity) => entity is Solid s
        ? new[] { s.FirstVertex, s.SecondVertex, s.ThirdVertex, s.FourthVertex }
        : new[] { ((Trace)entity).FirstVertex, ((Trace)entity).SecondVertex, ((Trace)entity).ThirdVertex, ((Trace)entity).FourthVertex };
    private static double PlanarReviewElevation(EntityObject entity) => entity is Solid s ? s.Elevation : ((Trace)entity).Elevation;
    private static double PlanarReviewThickness(EntityObject entity) => entity is Solid s ? s.Thickness : ((Trace)entity).Thickness;
    private static Vector3[] PlanarReviewWorld(EntityObject entity) => PlanarReviewVertices(entity)
        .Select(v => MathHelper.ArbitraryAxis(entity.Normal) * new Vector3(v.X, v.Y, PlanarReviewElevation(entity))).ToArray();
    private static long[] PlanarReviewState(EntityObject entity) => PlanarReviewVertices(entity).SelectMany(v => new[] { v.X, v.Y })
        .Concat(new[] { PlanarReviewElevation(entity), PlanarReviewThickness(entity), entity.Normal.X, entity.Normal.Y, entity.Normal.Z })
        .Select(BitConverter.DoubleToInt64Bits).ToArray();

    private static void PlanarReviewNear(Vector3 expected, Vector3 actual, string message)
    {
        double scale = Math.Max(Math.Abs(expected.X), Math.Max(Math.Abs(expected.Y), Math.Abs(expected.Z)));
        double tolerance = Math.Max(double.Epsilon * 8, scale * 2e-13);
        Check(double.IsFinite(actual.X) && double.IsFinite(actual.Y) && double.IsFinite(actual.Z), message + " is not finite");
        Check(Math.Abs(expected.X - actual.X) <= tolerance && Math.Abs(expected.Y - actual.Y) <= tolerance && Math.Abs(expected.Z - actual.Z) <= tolerance,
            $"{message}: expected {expected}, actual {actual}");
    }

    private static Matrix4 PlanarReviewMatrix4(Matrix3 m, Vector3 t) => new Matrix4(
        m.M11, m.M12, m.M13, t.X, m.M21, m.M22, m.M23, t.Y, m.M31, m.M32, m.M33, t.Z, 0, 0, 0, 1);

    private static void PlanarReviewGeometry(bool trace, int scenario, bool matrix4)
    {
        EntityObject entity = PlanarReviewEntity(trace, scenario);
        var operation = PlanarReviewOperation(scenario);
        Vector3[] original = PlanarReviewWorld(entity);
        Vector3 extrusion = entity.Normal * PlanarReviewThickness(entity);
        long[] before = PlanarReviewState(entity);
        if (matrix4) entity.TransformBy(PlanarReviewMatrix4(operation.Matrix, operation.Translation));
        else entity.TransformBy(operation.Matrix, operation.Translation);
        Vector3[] actual = PlanarReviewWorld(entity);
        for (int i = 0; i < 4; i++) PlanarReviewNear(operation.Matrix * original[i] + operation.Translation, actual[i], "Transformed planar corner");
        PlanarReviewNear(operation.Matrix * extrusion, entity.Normal * PlanarReviewThickness(entity), "Transformed signed thickness");
        Equal(false, entity.IsVisible, "Transform changed visibility"); Equal((short)3, entity.Color.Index, "Transform changed color");
        if (scenario == 0)
        {
            Check(before.SequenceEqual(PlanarReviewState(entity)), "Identity transform changed exact geometry bits");
            Check(entity.ProxyGraphics!.SequenceEqual(new byte[] { 1, 7, 9, 255 }), "Identity transform dropped proxy");
        }
        else Check(entity.ProxyGraphics == null, "Changed geometry retained stale proxy graphics");
        if (scenario == 12) SameDoubleBits(14.25, PlanarReviewElevation(entity), "Large in-plane translation corrupted elevation");
    }

    private static void PlanarReviewExtreme(bool trace, double scale)
    {
        EntityObject entity = PlanarReviewEntity(trace, 0); Vector3[] points = PlanarReviewWorld(entity);
        entity.TransformBy(Matrix3.Scale(scale), Vector3.Zero);
        for (int i = 0; i < 4; i++) PlanarReviewNear(points[i] * scale, PlanarReviewWorld(entity)[i], "Extreme planar corner");
        PlanarReviewNear(new Vector3(0, 0, 2.5 * scale), entity.Normal * PlanarReviewThickness(entity), "Extreme thickness");
    }

    private static void PlanarReviewReject(bool trace, int fault)
    {
        EntityObject entity = PlanarReviewEntity(trace, 0);
        Matrix3 matrix = Matrix3.Identity; Vector3 translation = Vector3.Zero;
        Matrix4 four = Matrix4.Identity; bool useFour = false;
        if (fault < 9) matrix[fault / 3, fault % 3] = double.NaN;
        else if (fault < 12) translation = fault == 9 ? new Vector3(double.PositiveInfinity, 0, 0) : fault == 10 ? new Vector3(0, double.NaN, 0) : new Vector3(0, 0, double.NegativeInfinity);
        else if (fault < 15) matrix = fault == 12 ? Matrix3.Scale(0, 1, 1) : fault == 13 ? Matrix3.Scale(1, 0, 1) : Matrix3.Scale(0);
        else if (fault == 15) matrix = new Matrix3(1, 0, 0, 0, 1, 0, .5, -.25, 1); // oblique nonzero extrusion
        else if (fault < 20) { useFour = true; four[3, fault - 16] = fault == 19 ? 2 : .25; }
        else if (fault < 36) { useFour = true; four[(fault - 20) / 4, (fault - 20) % 4] = double.NaN; }
        else if (fault == 36) matrix = Matrix3.Scale(double.MaxValue); // overflowing transformed corners
        else if (fault == 37)
        { if (entity is Solid s) s.FourthVertex = new Vector2(double.NaN, 1); else ((Trace)entity).FourthVertex = new Vector2(double.NaN, 1); }
        else if (fault == 38)
        { if (entity is Solid s) s.Elevation = double.PositiveInfinity; else ((Trace)entity).Elevation = double.PositiveInfinity; }
        else if (fault == 39)
        { if (entity is Solid s) s.Thickness = double.NaN; else ((Trace)entity).Thickness = double.NaN; }
        else if (fault == 40)
        { matrix = Matrix3.Scale(1, 1, double.Epsilon); if (entity is Solid s) s.Thickness = .125; else ((Trace)entity).Thickness = .125; }
        else if (fault == 41) matrix = new Matrix3(1, 2, 0, 2, 4, 0, 3, 6, 1); // rank-one plane, live normal
        else matrix = new Matrix3(1, 0, 0, 0, 1, .5, 0, 0, 1); // normal acquires in-plane component
        if (fault >= 37 && fault <= 40)
        {
            // Setup now exercises the corrected direct-setter policy. Reattach
            // a synthetic proxy afterward so the original failed-transform
            // rollback assertion still verifies preservation of real bytes.
            Check(entity.ProxyGraphics == null, "Setup geometry edit retained a stale proxy");
            entity.ProxyGraphics = new byte[] { 1, 7, 9, 255 };
        }
        long[] before = PlanarReviewState(entity); byte[] proxy = entity.ProxyGraphics!;
        bool rejected = false;
        try { if (useFour) entity.TransformBy(four); else entity.TransformBy(matrix, translation); }
        catch (ArgumentOutOfRangeException) { rejected = true; }
        catch (NotSupportedException) { rejected = true; }
        Check(rejected, "Invalid/unrepresentable transform was not rejected");
        Check(before.SequenceEqual(PlanarReviewState(entity)), "Rejected transform partially mutated geometry");
        Check(entity.ProxyGraphics!.SequenceEqual(proxy), "Rejected transform changed proxy");
    }

    private static void PlanarReviewWire(DxfVersion version, bool binary, int scenario)
    {
        var document = new DxfDocument(version);
        foreach (bool trace in new[] { false, true })
        {
            EntityObject entity = PlanarReviewEntity(trace, scenario);
            entity.ProxyGraphics = null; // do not author a synthetic proxy in the wire corpus
            var operation = PlanarReviewOperation(scenario);
            entity.TransformBy(operation.Matrix, operation.Translation);
            document.Entities.Add(entity);
        }
        using var stream = new MemoryStream(); Check(document.Save(stream, binary), "Planar transform save failed");
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"planar-review-{version}-{binary}-{scenario}.dxf"), stream.ToArray());
        stream.Position = 0; DxfDocument loaded = DxfDocument.Load(stream) ?? throw new InvalidOperationException("Planar transform reload failed");
        Equal(1, loaded.Entities.Solids.Count(), "Solid wire count"); Equal(1, loaded.Entities.Traces.Count(), "Trace wire count");
        foreach (EntityObject entity in loaded.Entities.All)
        {
            var operation = PlanarReviewOperation(scenario); EntityObject source = PlanarReviewEntity(entity is Trace, scenario);
            Vector3[] before = PlanarReviewWorld(source), after = PlanarReviewWorld(entity);
            for (int i = 0; i < 4; i++) PlanarReviewNear(operation.Matrix * before[i] + operation.Translation, after[i], "Wire planar corner");
            PlanarReviewNear(operation.Matrix * (source.Normal * PlanarReviewThickness(source)), entity.Normal * PlanarReviewThickness(entity), "Wire planar extrusion");
        }
    }
}
