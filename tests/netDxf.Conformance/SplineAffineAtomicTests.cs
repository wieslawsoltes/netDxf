// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System.Reflection;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.Tables;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static readonly string[] SplineAffineModes = { "identity", "translation", "reflection", "scale", "shear", "collapse", "small", "large" };
    private static Matrix3 SplineAffineMatrix(string mode) => mode switch
    {
        "reflection" => new(-1, 0, 0, 0, 1, 0, 0, 0, 1),
        "scale" => new(2, 0, 0, 0, 3, 0, 0, 0, 4),
        "shear" => new(1, 2, 0, 0, 1, 3, 4, 0, 1),
        "collapse" => new(0, 0, 0, 0, 0, 0, 0, 0, 0),
        "small" => Matrix3.Scale(Math.ScaleB(1, -500)),
        "large" => Matrix3.Scale(Math.ScaleB(1, 500)),
        _ => Matrix3.Identity
    };
    private static Vector3 SplineAffineTranslation(string mode) => mode switch
    {
        "translation" => new(5, -7, 11), "reflection" => new(2, 3, 4),
        "scale" => new(-1, 2, -3), "shear" => new(1, -2, 3), "collapse" => new(9, 8, 7), _ => Vector3.Zero
    };
    private static Spline SplineAffineSubject(int kind, int tangents = 3)
    {
        Vector3[] points = { new(1, 2, 3), new(-4, 5, -6), new(7, -8, 9), new(10, 11, -12) };
        var curve = kind == 1 ? new Spline(points) : new Spline(points, new[] { 1.0, 0.5, 2.0, 1.0 }, (short)2, kind == 2);
        curve.Normal = new Vector3(2, -3, 6);
        if ((tangents & 1) != 0) curve.StartTangent = new(2, -3, 5);
        if ((tangents & 2) != 0) curve.EndTangent = new(-7, 11, 13);
        curve.Layer = new Layer("SPLINE_AFFINE"); curve.Color = AciColor.FromCadIndex(3);
        curve.IsVisible = false; curve.LinetypeScale = 2; curve.Lineweight = Lineweight.W35;
        curve.ProxyGraphics = new byte[] { 11, 23, 47, 99 };
        var data = new XData(new ApplicationRegistry("SPLINE_AFFINE"));
        data.XDataRecord.Add(new XDataRecord(XDataCode.String, "spline geometry")); curve.XData.Add(data);
        return curve;
    }
    private static void SplineAffineApply(Spline spline, Matrix3 m, Vector3 t, bool four)
    {
        if (four) spline.TransformBy(new Matrix4(m.M11, m.M12, m.M13, t.X, m.M21, m.M22, m.M23, t.Y,
            m.M31, m.M32, m.M33, t.Z, 0, 0, 0, 1));
        else spline.TransformBy(m, t);
    }
    private static long[] SplineAffineBits(Spline s) => s.ControlPoints.Concat(s.FitPoints).Append(s.Normal)
        .Concat(s.StartTangent.HasValue ? new[] { s.StartTangent.Value } : Array.Empty<Vector3>())
        .Concat(s.EndTangent.HasValue ? new[] { s.EndTangent.Value } : Array.Empty<Vector3>())
        .SelectMany(p => new[] { p.X, p.Y, p.Z }).Concat(s.Knots).Concat(s.Weights)
        .Select(BitConverter.DoubleToInt64Bits).ToArray();
    private static void SplineAffineReject(Spline s, Action operation)
    {
        var bits = SplineAffineBits(s); var controls = s.ControlPoints; var fits = s.FitPoints;
        var knots = s.Knots; var weights = s.Weights; var proxy = s.ProxyGraphics!;
        var layer = s.Layer; var color = s.Color; var data = s.XData.Values.ToArray(); var owner = s.Owner; string handle = s.Handle;
        bool rejected = false;
        try { operation(); } catch (ArgumentException) { rejected = true; }
        catch (InvalidOperationException) { rejected = true; } catch (NotSupportedException) { rejected = true; }
        Check(rejected, "Invalid spline transform accepted");
        Check(bits.SequenceEqual(SplineAffineBits(s)), "Rejected spline geometry changed");
        Check(ReferenceEquals(controls, s.ControlPoints) && ReferenceEquals(fits, s.FitPoints), "Point arrays replaced");
        Check(ReferenceEquals(knots, s.Knots) && ReferenceEquals(weights, s.Weights), "Parameter arrays replaced");
        Check(proxy.SequenceEqual(s.ProxyGraphics!), "Rejected spline proxy changed");
        Check(ReferenceEquals(layer, s.Layer) && ReferenceEquals(color, s.Color) && data.SequenceEqual(s.XData.Values), "Metadata identity changed");
        Check(ReferenceEquals(owner, s.Owner), "Owner changed"); Equal(handle, s.Handle, "Handle changed");
    }
    private static void SplineAffineVector(Vector3 expected, Vector3 actual, string message)
    {
        for (int i = 0; i < 3; i++)
        {
            Check(double.IsFinite(actual[i]), message + " nonfinite");
            Check(Math.Abs(actual[i] - expected[i]) <= 2e-14 * Math.Max(Math.Abs(expected[i]), double.Epsilon), message);
        }
    }
    private static void SplineAffineModel(int kind, int tangents, string mode, bool four, bool owned)
    {
        var s = SplineAffineSubject(kind, tangents); var document = new DxfDocument(); if (owned) document.Entities.Add(s);
        var controls = s.ControlPoints; var fits = s.FitPoints; var source = controls.ToArray(); var sourceFits = fits.ToArray();
        var knots = s.Knots; var weights = s.Weights; var knotBits = knots.Select(BitConverter.DoubleToInt64Bits).ToArray();
        var weightBits = weights.Select(BitConverter.DoubleToInt64Bits).ToArray(); var bits = SplineAffineBits(s);
        var start = s.StartTangent; var end = s.EndTangent; var proxy = s.ProxyGraphics!; var owner = s.Owner; var handle = s.Handle;
        var data = s.XData.Values.ToArray(); var normal = s.Normal; var method = s.CreationMethod;
        var m = SplineAffineMatrix(mode); var t = SplineAffineTranslation(mode); SplineAffineApply(s, m, t, four);
        for (int i = 0; i < controls.Length; i++) SplineAffineVector(m * source[i] + t, controls[i], "Control image");
        for (int i = 0; i < fits.Count; i++) SplineAffineVector(m * sourceFits[i] + t, fits[i], "Fit image");
        Equal(start.HasValue, s.StartTangent.HasValue, "Start presence"); Equal(end.HasValue, s.EndTangent.HasValue, "End presence");
        if (start.HasValue) SplineAffineVector(m * start.Value, s.StartTangent!.Value, "Start magnitude/direction");
        if (end.HasValue) SplineAffineVector(m * end.Value, s.EndTangent!.Value, "End magnitude/direction");
        var image = m * normal; double scale = Math.Max(Math.Abs(image.X), Math.Max(Math.Abs(image.Y), Math.Abs(image.Z)));
        SplineAffineVector(scale == 0 ? normal : Vector3.Normalize(image / scale), s.Normal, "Auxiliary normal image");
        Check(ReferenceEquals(controls, s.ControlPoints) && ReferenceEquals(fits, s.FitPoints), "Point container identity");
        Check(ReferenceEquals(knots, s.Knots) && ReferenceEquals(weights, s.Weights), "Parameter container identity");
        Check(knotBits.SequenceEqual(knots.Select(BitConverter.DoubleToInt64Bits)) && weightBits.SequenceEqual(weights.Select(BitConverter.DoubleToInt64Bits)), "Parameter bits changed");
        Check(data.SequenceEqual(s.XData.Values) && ReferenceEquals(owner, s.Owner), "Metadata/owner changed"); Equal(handle, s.Handle, "Handle changed");
        Equal(method, s.CreationMethod, "Creation method changed"); Equal(kind == 2, s.IsClosedPeriodic, "Periodicity changed");
        if (mode == "identity") { Check(bits.SequenceEqual(SplineAffineBits(s)), "Identity bit drift"); Check(proxy.SequenceEqual(s.ProxyGraphics!), "Identity proxy lost"); }
        else Check(s.ProxyGraphics == null, "Changed geometry retained stale proxy");
    }
    private static void SplineAffineWire(int kind, string mode, bool four, DxfVersion version, bool binary)
    {
        var s = SplineAffineSubject(kind); var document = new DxfDocument(version);
        // Exercise both direct and nested storage without changing the selected SPLINE packet.
        if (four) { var block = new Block("SPLINE_AFFINE_BLOCK"); block.Entities.Add(s); document.Entities.Add(new Insert(block)); }
        else document.Entities.Add(s);
        string stem = $"spline-affine-atomic-{kind}-{mode}-{four}-{version}-{binary}";
        using (var source = new MemoryStream())
        { Check(document.Save(source, binary), "Source save failed"); File.WriteAllBytes(Path.Combine(ArtifactDirectory, stem + "-source.dxf"), source.ToArray()); }
        var controls = s.ControlPoints; var fits = s.FitPoints; string handle = s.Handle;
        SplineAffineApply(s, SplineAffineMatrix(mode), SplineAffineTranslation(mode), four);
        Equal(handle, s.Handle, "Registered handle changed");
        Check(ReferenceEquals(controls, s.ControlPoints) && ReferenceEquals(fits, s.FitPoints), "Registered containers changed");
        using var output = new MemoryStream(); Check(document.Save(output, binary), "Transformed save failed");
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, stem + "-after.dxf"), output.ToArray());
        output.Position = 0; var loaded = DxfDocument.Load(output)!;
        var found = four ? loaded.Blocks["SPLINE_AFFINE_BLOCK"].Entities.OfType<Spline>().Single() : loaded.Entities.Splines.Single();
        Check(controls.SequenceEqual(found.ControlPoints) && fits.SequenceEqual(found.FitPoints), "Point values lost in wire round trip");
        AssertSplineTangent(s.StartTangent, found.StartTangent, "Start wire"); AssertSplineTangent(s.EndTangent, found.EndTangent, "End wire");
        Equal(0, loaded.Objects.Validate().Count, "Loaded graph invalid");
    }
    private sealed class SplineAffineDerived : Spline
    {
        internal bool Armed; internal int Dispatches;
        internal SplineAffineDerived() : base(new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY }, null, (short)2) { }
        public override Vector3 Normal { get { if (Armed) throw new InvalidOperationException("Getter callback"); return base.Normal; }
            set { if (Armed) throw new InvalidOperationException("Setter callback"); base.Normal = value; } }
        internal Vector3 StoredNormal => base.Normal;
        public override void TransformBy(Matrix3 matrix, Vector3 translation) { Dispatches++; base.TransformBy(matrix, translation); }
    }
    private static void RegisterSplineAffineAtomicTests()
    {
        foreach (int kind in new[] { 0, 1, 2 }) foreach (int mask in new[] { 0, 1, 2, 3 })
            foreach (string mode in SplineAffineModes) foreach (bool four in new[] { false, true }) foreach (bool owned in new[] { false, true })
                Run($"spline-affine-atomic/model/{kind}/{mask}/{mode}/{four}/{owned}", () => SplineAffineModel(kind, mask, mode, four, owned));
        foreach (int kind in new[] { 0, 1, 2 }) foreach (string mode in SplineAffineModes.Take(6))
            foreach (bool four in new[] { false, true }) foreach (var version in SupportedVersions) foreach (bool binary in new[] { false, true })
                Run($"spline-affine-atomic/wire/{kind}/{mode}/{four}/{version}/{binary}", () => SplineAffineWire(kind, mode, four, version, binary));
        foreach (double bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            string label = BitConverter.DoubleToInt64Bits(bad).ToString("X16");
            for (int index = 0; index < 12; index++) { int n = index; Run($"spline-affine-atomic/reject/matrix3/{n}/{label}", () =>
                { var s = SplineAffineSubject(1); var m = Matrix3.Identity; var t = Vector3.UnitX; if (n < 9) m[n / 3, n % 3] = bad; else t[n - 9] = bad;
                  SplineAffineReject(s, () => s.TransformBy(m, t)); }); }
            for (int index = 0; index < 16; index++) { int n = index; Run($"spline-affine-atomic/reject/matrix4/{n}/{label}", () =>
                { var s = SplineAffineSubject(1); var m = Matrix4.Identity; m[n / 4, n % 4] = bad; SplineAffineReject(s, () => s.TransformBy(m)); }); }
            foreach (string target in new[] { "control", "fit", "start", "end" }) for (int axis = 0; axis < 3; axis++)
            { int a = axis; foreach (bool four in new[] { false, true }) Run($"spline-affine-atomic/reject/{target}/{a}/{label}/{four}", () =>
                { var s = SplineAffineSubject(1); var value = new Vector3(1, 2, 3); value[a] = bad;
                  if (target == "control") s.ControlPoints[^1] = value; else if (target == "fit") ((Vector3[])s.FitPoints)[^1] = value;
                  else if (target == "start") s.StartTangent = value; else s.EndTangent = value;
                  SplineAffineReject(s, () => SplineAffineApply(s, Matrix3.Scale(2), Vector3.UnitX, four)); }); }
        }
        foreach (bool four in new[] { false, true })
        {
            for (int i = 0; i < 4; i++) { int n = i; Run($"spline-affine-atomic/reject/normal/{n}/{four}", () =>
                { var s = SplineAffineSubject(1); var value = new[] { Vector3.Zero, new Vector3(2, 3, 6), new Vector3(double.NaN, 0, 1), new Vector3(0, double.PositiveInfinity, 1) }[n];
                  typeof(EntityObject).GetField("normal", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(s, value);
                  SplineAffineReject(s, () => SplineAffineApply(s, Matrix3.Identity, Vector3.Zero, four)); }); }
            foreach (string target in new[] { "control", "fit", "start", "end" }) foreach (bool underflow in new[] { false, true })
                Run($"spline-affine-atomic/reject/final/{target}/{underflow}/{four}", () =>
                { var s = SplineAffineSubject(1); var value = new Vector3(underflow ? double.Epsilon : double.MaxValue, 0, 0);
                  if (target == "control") s.ControlPoints[^1] = value; else if (target == "fit") ((Vector3[])s.FitPoints)[^1] = value;
                  else if (target == "start") s.StartTangent = value; else s.EndTangent = value;
                  SplineAffineReject(s, () => SplineAffineApply(s, Matrix3.Scale(underflow ? 0.25 : 2), Vector3.Zero, four)); });
            Run($"spline-affine-atomic/cancellation/{four}", () =>
            { var s = SplineAffineSubject(1); var v = new Vector3(double.MaxValue, double.MaxValue, 3);
              Array.Fill(s.ControlPoints, v); Array.Fill((Vector3[])s.FitPoints, v); s.StartTangent = v; s.EndTangent = v;
              SplineAffineApply(s, new(2, -2, 1, 0, 0, 1, 0, 0, -1), Vector3.Zero, four);
              foreach (var p in s.ControlPoints.Concat(s.FitPoints)) Equal(new Vector3(3, 3, -3), p, "Exact point cancellation");
              Equal(new Vector3(3, 3, -3), s.StartTangent!.Value, "Exact tangent cancellation"); });
            Run($"spline-affine-atomic/tangent-only/{four}", () =>
            { var s = SplineAffineSubject(0); Array.Fill(s.ControlPoints, Vector3.Zero); s.Normal = Vector3.UnitZ;
              SplineAffineApply(s, Matrix3.Scale(2), Vector3.Zero, four); Equal(new Vector3(4, -6, 10), s.StartTangent!.Value, "Tangent-only image"); Check(s.ProxyGraphics == null, "Tangent-only stale proxy"); });
            Run($"spline-affine-atomic/unchanged/{four}", () =>
            { var s = SplineAffineSubject(0); Array.Fill(s.ControlPoints, Vector3.UnitX); NormalFixtureEditAndRestore(s, Vector3.UnitZ);
              s.StartTangent = Vector3.UnitX; s.EndTangent = new Vector3(-0.0, 0, -0.0); var bits = SplineAffineBits(s); var proxy = s.ProxyGraphics!;
              SplineAffineApply(s, new(1, 0, 0, 0, 2, 0, 0, 0, 3), Vector3.Zero, four);
              Check(bits.SequenceEqual(SplineAffineBits(s)), "Unchanged geometry bit drift"); Check(proxy.SequenceEqual(s.ProxyGraphics!), "Unchanged geometry proxy lost"); });
            Run($"spline-affine-atomic/derived/{four}", () =>
            { var s = new SplineAffineDerived { Armed = true }; SplineAffineApply(s, Matrix3.Scale(2), Vector3.UnitZ, four);
              Equal(1, s.Dispatches, "Virtual Matrix3 dispatch"); Equal(new Vector3(2, 0, 1), s.ControlPoints[1], "Derived point image"); Equal(Vector3.UnitZ, s.StoredNormal, "Derived normal"); });
        }
        for (int i = 0; i < 4; i++) { int n = i; Run($"spline-affine-atomic/reject/projective/{n}", () =>
            { var s = SplineAffineSubject(1); var m = Matrix4.Identity; m[3, n] = n == 3 ? 2 : 1e-30; SplineAffineReject(s, () => s.TransformBy(m)); }); }
    }
}
