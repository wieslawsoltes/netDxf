using netDxf;
using netDxf.Entities;
using netDxf.Header;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static readonly string[] ReviewedEllipseModes = { "translate", "rotate", "swap", "mirror", "negative", "anisotropic", "shear", "tilted", "flat", "small" };
    private static Matrix3 ReviewedEllipseMatrix(string mode) => mode switch
    {
        "translate" => Matrix3.Identity,
        "rotate" => new(0,-1,0, 1,0,0, 0,0,1),
        "swap" => new(0,1,0, 1,0,0, 0,0,1),
        "mirror" => new(-1,0,0, 0,1,0, 0,0,1),
        "negative" => Matrix3.Scale(-2),
        "anisotropic" => new(2,0,0, 0,3,0, 0,0,4),
        "shear" => new(1,.75,0, .2,1,0, 0,0,1),
        "tilted" => new(1,.75,.2, -.3,2,.1, .4,.25,1),
        "flat" => new(1,0,0, 0,1,0, 0,0,0),
        "small" => Matrix3.Scale(1e-4),
        _ => throw new ArgumentException(mode)
    };

    private static void RegisterEllipseAffineReviewTests()
    {
        foreach (DxfVersion version in SupportedVersions)
        foreach (bool binary in new[] { false, true })
        foreach (string mode in ReviewedEllipseModes)
            Run($"ellipse-review/roundtrip/{version}/{binary}/{mode}", () => ReviewedEllipseRoundTrip(version, binary, mode));
        foreach (string mode in new[] { "identity", "tiny", "large", "large-center", "thickness", "thickness-reflect", "polyline", "affine4", "axis-swap", "polar-large", "polar-small" })
            Run("ellipse-review/boundary/" + mode, () => ReviewedEllipseBoundary(mode));
        foreach (string fault in new[] { "rank-one", "collapsed", "nonfinite-matrix", "translation", "overflow", "underflow", "oblique-thickness", "angle", "projective" })
            Run("ellipse-review/reject/" + fault, () => ReviewedEllipseReject(fault));
        Run("ellipse-review/finite-axes", () =>
        {
            var e = new Ellipse(Vector3.Zero, 4, 2);
            foreach (double v in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, 0, -1 })
            {
                Throws<ArgumentOutOfRangeException>(() => new Ellipse(Vector3.Zero, v, 1));
                Throws<ArgumentOutOfRangeException>(() => new Ellipse(Vector3.Zero, 4, v));
                Throws<ArgumentOutOfRangeException>(() => e.SetAxis(v, 1));
                Throws<ArgumentOutOfRangeException>(() => e.SetAxis(4, v));
                Equal(4.0, e.MajorAxis, "invalid axis changed source"); Equal(2.0, e.MinorAxis, "invalid minor changed source");
            }
        });
    }

    private static Ellipse ReviewedEllipse(bool full, bool tilted = false) => new(new Vector3(3,-4,5), 8, 3)
    {
        Rotation = 37, StartAngle = full ? 0 : 25, EndAngle = full ? 0 : 285,
        Normal = tilted ? Vector3.Normalize(new Vector3(1,2,3)) : Vector3.UnitZ,
        Color = new AciColor(3), IsVisible = false, ProxyGraphics = new byte[] { 1,2,3 }
    };
    private static Vector3 EllipseWorldPoint(Ellipse e, Vector2 point) => e.Center + MathHelper.ArbitraryAxis(e.Normal) * new Vector3(point.X, point.Y, 0);

    private static void AssertEllipseImage(Ellipse before, Ellipse after, Matrix3 matrix, Vector3 translation)
    {
        NearReviewedCircularVector(matrix * before.Center + translation, after.Center, "ellipse center");
        Check(after.MajorAxis >= after.MinorAxis && after.MinorAxis > 0, "ordered positive output axes");
        Near(1, after.Normal.Modulus(), "unit ellipse normal");
        var prior = before.PolygonalVertexes(33); var current = after.PolygonalVertexes(33);
        double rotation = -after.Rotation * MathHelper.DegToRad;
        for (int i = 0; i < prior.Count; i++)
        {
            Vector3 wanted = matrix * EllipseWorldPoint(before, prior[i]) + translation;
            if (!before.IsFullEllipse) NearReviewedCircularVector(wanted, EllipseWorldPoint(after, current[i]), "ordered ellipse arc image");
            Vector3 local = MathHelper.ArbitraryAxis(after.Normal).Transpose() * (wanted - after.Center);
            Vector2 p = Vector2.Rotate(new Vector2(local.X, local.Y), rotation);
            Near(1, Math.Pow(p.X / (after.MajorAxis * .5), 2) + Math.Pow(p.Y / (after.MinorAxis * .5), 2), "affine ellipse locus");
            Near(0, local.Z, "affine ellipse plane");
        }
        Equal(before.IsFullEllipse, after.IsFullEllipse, "arc/full distinction");
        Equal(before.IsVisible, after.IsVisible, "ellipse visibility"); Equal(before.Color.Index, after.Color.Index, "ellipse color");
    }

    private static void ReviewedEllipseRoundTrip(DxfVersion version, bool binary, string mode)
    {
        var originals = new[] { ReviewedEllipse(true, mode == "tilted"), ReviewedEllipse(false, mode == "tilted") };
        var copies = originals.Select(e => (Ellipse)e.Clone()).ToArray();
        Matrix3 matrix = ReviewedEllipseMatrix(mode); Vector3 translation = new(11,-7,13);
        var doc = new DxfDocument(version);
        for (int i = 0; i < copies.Length; i++)
        {
            copies[i].TransformBy(matrix, translation); AssertEllipseImage(originals[i], copies[i], matrix, translation);
            Check(copies[i].ProxyGraphics == null && originals[i].ProxyGraphics!.Length == 3, "ellipse proxy invalidation/clone independence");
            doc.Entities.Add(copies[i]);
        }
        using var stream = new MemoryStream(); Check(doc.Save(stream, binary), "ellipse affine output");
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"ellipse-review-{version}-{binary}-{mode}.dxf"), stream.ToArray());
        stream.Position = 0; var loaded = DxfDocument.Load(stream)!; var entities = loaded.Entities.Ellipses.ToArray();
        for (int i = 0; i < entities.Length; i++) AssertEllipseImage(originals[i], entities[i], matrix, translation);
        using var opposite = new MemoryStream(); Check(loaded.Save(opposite, !binary), "opposite ellipse output"); opposite.Position = 0;
        entities = DxfDocument.Load(opposite)!.Entities.Ellipses.ToArray();
        for (int i = 0; i < entities.Length; i++) AssertEllipseImage(originals[i], entities[i], matrix, translation);
    }

    private static void ReviewedEllipseBoundary(string mode)
    {
        var e = ReviewedEllipse(false);
        if (mode == "identity")
        {
            var before = (Ellipse)e.Clone(); e.TransformBy(Matrix3.Identity, Vector3.Zero);
            SameDoubleBits(before.Rotation, e.Rotation, "identity rotation"); SameDoubleBits(before.StartAngle, e.StartAngle, "identity endpoint");
            Check(e.ProxyGraphics!.SequenceEqual(before.ProxyGraphics!), "identity cleared proxy"); return;
        }
        if (mode.StartsWith("polar-", StringComparison.Ordinal))
        {
            double scale = mode == "polar-large" ? 1e200 : 1e-200; e.SetAxis(8 * scale, 3 * scale);
            foreach (double angle in new[] { 0.0, 25, 45, 90, 175, 285, -25, 450 })
            {
                var point = e.PolarCoordinateRelativeToCenter(angle);
                Near(1, Math.Pow(point.X / (4 * scale), 2) + Math.Pow(point.Y / (1.5 * scale), 2), "stable polar point");
            }
            return;
        }
        if (mode == "polyline") { Check(!e.ToPolyline2D(16).IsVisible, "ellipse polyline lost hidden state"); return; }
        if (mode == "large-center")
        {
            e.Center = new Vector3(1e150, -1e150, 1e150); double a = e.MajorAxis, b = e.MinorAxis;
            e.TransformBy(ReviewedEllipseMatrix("rotate"), Vector3.Zero);
            Near(a, e.MajorAxis, "translated center contaminated major axis"); Near(b, e.MinorAxis, "translated center contaminated minor axis"); return;
        }
        if (mode == "tiny" || mode == "large")
        {
            double scale = mode == "tiny" ? 1e-200 : 1e150;
            e.Center = Vector3.Zero; e.TransformBy(Matrix3.Scale(scale), Vector3.Zero);
            Check(Math.Abs(e.MajorAxis / (8 * scale) - 1) < 1e-12 && Math.Abs(e.MinorAxis / (3 * scale) - 1) < 1e-12, "spurious underflow/overflow or epsilon clamping"); return;
        }
        if (mode == "axis-swap") { e.Rotation = 0; e.TransformBy(new Matrix3(1,0,0, 0,4,0, 0,0,1), Vector3.Zero); Near(12,e.MajorAxis,"principal-axis swap"); Near(8,e.MinorAxis,"principal minor"); return; }
        var original = (Ellipse)e.Clone();
        if (mode == "affine4")
        {
            e.TransformBy(new Matrix4(1,.75,0,11, .2,1,0,-7, 0,0,1,13, 0,0,0,1));
            AssertEllipseImage(original, e, ReviewedEllipseMatrix("shear"), new Vector3(11,-7,13)); return;
        }
        e.Thickness = -2; original.Thickness = -2;
        Matrix3 matrix = mode == "thickness" ? new Matrix3(2,0,0, 0,3,0, 0,0,4) : ReviewedEllipseMatrix("mirror");
        e.TransformBy(matrix, Vector3.Zero); AssertEllipseImage(original, e, matrix, Vector3.Zero);
        NearReviewedCircularVector(matrix * (original.Normal * original.Thickness), e.Normal * e.Thickness, "ellipse extrusion vector");
    }

    private static void ReviewedEllipseReject(string fault)
    {
        var e = ReviewedEllipse(false); Matrix3 matrix = fault switch
        {
            "rank-one" => new(0,0,0, 0,1,0, 0,0,1), "collapsed" => Matrix3.Scale(0),
            "nonfinite-matrix" => new(double.NaN,0,0, 0,1,0, 0,0,1),
            "overflow" => Matrix3.Scale(double.MaxValue), "underflow" => Matrix3.Scale(double.Epsilon),
            "oblique-thickness" => new(1,0,.5, 0,1,0, 0,0,1), _ => Matrix3.Identity
        };
        if (fault == "underflow") e.SetAxis(.5, .25);
        if (fault == "oblique-thickness") e.Thickness = 1;
        if (fault == "angle") e.StartAngle = double.NaN;
        Vector3 translation = fault == "translation" ? new Vector3(double.PositiveInfinity,0,0) : Vector3.Zero;
        var before = (Ellipse)e.Clone(); bool rejected = false;
        try
        {
            if (fault == "projective") e.TransformBy(new Matrix4(1,0,0,0, 0,1,0,0, 0,0,1,0, .1,0,0,1));
            else e.TransformBy(matrix, translation);
        }
        catch (Exception error) when (error is ArgumentException || error is InvalidOperationException || error is NotSupportedException) { rejected = true; }
        Check(rejected, "unrepresentable ellipse transform accepted");
        Equal(before.Center, e.Center, "rejected center"); Equal(before.Normal, e.Normal, "rejected normal");
        SameDoubleBits(before.MajorAxis,e.MajorAxis,"rejected axis"); SameDoubleBits(before.MinorAxis,e.MinorAxis,"rejected minor");
        SameDoubleBits(before.StartAngle,e.StartAngle,"rejected start"); SameDoubleBits(before.EndAngle,e.EndAngle,"rejected end");
        SameDoubleBits(before.Rotation,e.Rotation,"rejected rotation"); SameDoubleBits(before.Thickness,e.Thickness,"rejected thickness");
        Check(before.ProxyGraphics!.SequenceEqual(e.ProxyGraphics!), "rejected proxy");
    }
}
