using System.Text.Json;
using netDxf;
using netDxf.Entities;
using netDxf.Header;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void NearReviewedCircularVector(Vector3 expected, Vector3 actual, string message)
    {
        Near(expected.X, actual.X, message + " X");
        Near(expected.Y, actual.Y, message + " Y");
        Near(expected.Z, actual.Z, message + " Z");
    }

    private static Matrix3 ReviewedCircleMatrix(string name) => name switch
    {
        "translate" => Matrix3.Identity,
        "rotate" => new(0,-1,0, 1,0,0, 0,0,1),
        "swap" => new(0,1,0, 1,0,0, 0,0,1),
        "mirror" => new(-1,0,0, 0,1,0, 0,0,1),
        "negative" => Matrix3.Scale(-2),
        "axial" => new(2,0,0, 0,2,0, 0,0,-3),
        "tilted" => new(0,-2,0, 0,0,2, 2,0,0),
        "normal-shear" => new(1,0,.5, 0,1,0, 0,0,1),
        _ => throw new ArgumentException(name)
    };

    private static void RegisterCircularGeometryReviewTests()
    {
        foreach (string kind in new[] { "circle", "arc" })
        {
            foreach (string mode in new[] { "identity", "small-radius", "small-scale", "large-scale", "polyline-visibility", "affine4", "zero-normal-scale" })
                Run($"circular-review/boundary/{kind}/{mode}", () => CircularReviewBoundary(kind, mode));
            foreach (string fault in new[] { "ellipse", "plane-shear", "collapsed", "oblique-thickness", "nonfinite-matrix", "nonfinite-translation", "overflow", "underflow", "radius-nan", "center-nan", "thickness-nan", "projective" })
                Run($"circular-review/reject/{kind}/{fault}", () => CircularReviewRejected(kind, fault));
        }
        foreach (DxfVersion version in SupportedVersions)
        foreach (bool binary in new[] { false, true })
        foreach (string mode in new[] { "translate", "rotate", "swap", "mirror", "negative", "axial", "tilted", "normal-shear" })
            Run($"circular-review/roundtrip/{version}/{binary}/{mode}", () => CircularReviewRoundTrip(version, binary, mode));
    }

    private static EntityObject NewReviewedCircular(string kind, Vector3? normal = null)
    {
        EntityObject entity = kind == "circle" ? new Circle(new Vector3(3,-4,5), 2.5) { Thickness = -1.75 }
            : new Arc(new Vector3(3,-4,5), 2.5, 25, 285) { Thickness = -1.75 };
        entity.Normal = normal ?? Vector3.UnitZ;
        entity.IsVisible = false;
        entity.Color = new AciColor(3);
        entity.ProxyGraphics = new byte[] { 1,2,3,4 };
        return entity;
    }

    private static (Vector3 center, double radius, double thickness) ReviewedCircularValues(EntityObject entity)
    {
        return entity is Circle circle ? (circle.Center, circle.Radius, circle.Thickness)
            : (((Arc)entity).Center, ((Arc)entity).Radius, ((Arc)entity).Thickness);
    }

    private static Vector3 ReviewedCircularPoint(EntityObject entity, double t)
    {
        var values = ReviewedCircularValues(entity);
        double angle;
        if (entity is Arc arc)
        {
            double sweep = MathHelper.NormalizeAngle(arc.EndAngle - arc.StartAngle);
            angle = (arc.StartAngle + t * sweep) * MathHelper.DegToRad;
        }
        else angle = t * MathHelper.TwoPI;
        return values.center + MathHelper.ArbitraryAxis(entity.Normal) *
            new Vector3(values.radius * Math.Cos(angle), values.radius * Math.Sin(angle), 0);
    }

    private static void AssertReviewedTransform(EntityObject before, EntityObject after, Matrix3 matrix, Vector3 translation)
    {
        var prior = ReviewedCircularValues(before); var next = ReviewedCircularValues(after);
        NearReviewedCircularVector(matrix * prior.center + translation, next.center, "circular center");
        Near(1, after.Normal.Modulus(), "unit plane normal");
        NearReviewedCircularVector(matrix * (before.Normal * prior.thickness), after.Normal * next.thickness, "extrusion vector");
        for (int i = 0; i <= 16; i++)
        {
            var wanted = matrix * ReviewedCircularPoint(before, i / 16.0) + translation;
            if (before is Arc) NearReviewedCircularVector(wanted, ReviewedCircularPoint(after, i / 16.0), "parameterized arc point");
            else
            {
                Vector3 delta = wanted - next.center;
                Near(next.radius, delta.Modulus(), "circle surface radius");
                Near(0, Vector3.DotProduct(delta, after.Normal), "circle plane");
            }
        }
        Equal(before.IsVisible, after.IsVisible, "transform appearance");
        Equal(before.Color.Index, after.Color.Index, "transform color");
    }

    private static void CircularReviewRoundTrip(DxfVersion version, bool binary, string mode)
    {
        Matrix3 matrix = ReviewedCircleMatrix(mode); Vector3 translation = new(11,-7,13);
        Vector3 normal = mode == "tilted" ? Vector3.Normalize(new Vector3(1,2,3)) : Vector3.UnitZ;
        var originals = new[] { NewReviewedCircular("circle", normal), NewReviewedCircular("arc", normal) };
        if (mode == "normal-shear")
        {
            ((Circle)originals[0]).Thickness = 0; ((Arc)originals[1]).Thickness = 0;
        }
        var transformed = originals.Select(e => (EntityObject)e.Clone()).ToArray();
        var doc = new DxfDocument(version);
        for (int i = 0; i < transformed.Length; i++)
        {
            transformed[i].TransformBy(matrix, translation);
            Check(transformed[i].ProxyGraphics == null, "stale circular proxy survived geometry change");
            Check(originals[i].ProxyGraphics!.Length == 4, "source clone's proxy mutated");
            AssertReviewedTransform(originals[i], transformed[i], matrix, translation);
            doc.Entities.Add(transformed[i]);
        }
        using var stream = new MemoryStream(); Check(doc.Save(stream, binary), "circular save");
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"circular-review-{version}-{binary}-{mode}.dxf"), stream.ToArray());
        stream.Position = 0; var loaded = DxfDocument.Load(stream)!;
        AssertReviewedTransform(originals[0], loaded.Entities.Circles.Single(), matrix, translation);
        AssertReviewedTransform(originals[1], loaded.Entities.Arcs.Single(), matrix, translation);
        using var opposite = new MemoryStream(); Check(loaded.Save(opposite, !binary), "opposite circular save"); opposite.Position = 0;
        loaded = DxfDocument.Load(opposite)!;
        AssertReviewedTransform(originals[0], loaded.Entities.Circles.Single(), matrix, translation);
        AssertReviewedTransform(originals[1], loaded.Entities.Arcs.Single(), matrix, translation);
    }

    private static void CircularReviewBoundary(string kind, string mode)
    {
        var entity = NewReviewedCircular(kind);
        var before = (EntityObject)entity.Clone();
        if (mode == "identity")
        {
            entity.TransformBy(Matrix3.Identity, Vector3.Zero);
            Check(entity.ProxyGraphics!.SequenceEqual(before.ProxyGraphics!), "identity cleared proxy");
            Equal(ReviewedCircularValues(before), ReviewedCircularValues(entity), "identity geometry changed");
        }
        else if (mode == "polyline-visibility")
        {
            Polyline2D poly = entity is Circle circle ? circle.ToPolyline2D(16) : ((Arc)entity).ToPolyline2D(16);
            Check(!poly.IsVisible, "circular conversion lost hidden state");
        }
        else if (mode == "affine4")
        {
            Matrix4 matrix = new(0,1,0,11, 1,0,0,-7, 0,0,1,13, 0,0,0,1);
            entity.TransformBy(matrix);
            AssertReviewedTransform(before, entity, ReviewedCircleMatrix("swap"), new Vector3(11,-7,13));
        }
        else if (mode == "zero-normal-scale")
        {
            entity.TransformBy(new Matrix3(1,0,0, 0,1,0, 0,0,0), Vector3.Zero);
            Equal(0.0, ReviewedCircularValues(entity).thickness, "collapsed extrusion must become zero thickness");
        }
        else
        {
            double radius = mode == "small-radius" ? 1e-200 : 2.5;
            double scale = mode == "small-scale" ? 1e-200 : mode == "large-scale" ? 1e150 : 2;
            if (entity is Circle circle) { circle.Center = Vector3.Zero; circle.Thickness = 0; circle.Radius = radius; }
            else { var arc = (Arc)entity; arc.Center = Vector3.Zero; arc.Thickness = 0; arc.Radius = radius; }
            entity.TransformBy(Matrix3.Scale(scale), Vector3.Zero);
            double actual = ReviewedCircularValues(entity).radius;
            Check(actual > 0 && Math.Abs(actual / (radius * scale) - 1) < 1e-12, "tiny/large radius was clamped or overflowed spuriously");
        }
    }

    private static void CircularReviewRejected(string kind, string fault)
    {
        var entity = NewReviewedCircular(kind);
        Matrix3 matrix = fault switch
        {
            "ellipse" => new(2,0,0, 0,3,0, 0,0,1),
            "plane-shear" => new(1,.5,0, 0,1,0, 0,0,1),
            "collapsed" => new(0,0,0, 0,0,0, 0,0,1),
            "oblique-thickness" => ReviewedCircleMatrix("normal-shear"),
            "nonfinite-matrix" => new(double.NaN,0,0, 0,1,0, 0,0,1),
            "overflow" => Matrix3.Scale(double.MaxValue),
            "underflow" => Matrix3.Scale(double.Epsilon),
            _ => Matrix3.Identity
        };
        if (fault == "underflow") { if (entity is Circle c) c.Radius = .125; else ((Arc)entity).Radius = .125; }
        if (fault == "radius-nan") { if (entity is Circle c) c.Radius = double.NaN; else ((Arc)entity).Radius = double.NaN; }
        if (fault == "thickness-nan") { if (entity is Circle c) c.Thickness = double.NaN; else ((Arc)entity).Thickness = double.NaN; }
        if (fault == "center-nan") { if (entity is Circle c) c.Center = new Vector3(double.NaN,0,0); else ((Arc)entity).Center = new Vector3(double.NaN,0,0); }
        Vector3 translation = fault == "nonfinite-translation" ? new Vector3(double.PositiveInfinity,0,0) : Vector3.Zero;
        var values = ReviewedCircularValues(entity); var normal = entity.Normal; var proxy = entity.ProxyGraphics!;
        double start = entity is Arc arc ? arc.StartAngle : 0, end = entity is Arc a ? a.EndAngle : 0;
        Exception? caught = null;
        try
        {
            if (fault == "projective") entity.TransformBy(new Matrix4(1,0,0,0, 0,1,0,0, 0,0,1,0, .1,0,0,1));
            else entity.TransformBy(matrix, translation);
        }
        catch (Exception error) { caught = error; }
        Check(caught is ArgumentException or InvalidOperationException or NotSupportedException, "invalid circular transform accepted");
        var actual = ReviewedCircularValues(entity);
        SameDoubleBits(values.center.X, actual.center.X, "failed transform center X"); SameDoubleBits(values.center.Y, actual.center.Y, "failed transform center Y"); SameDoubleBits(values.center.Z, actual.center.Z, "failed transform center Z");
        SameDoubleBits(values.radius, actual.radius, "failed transform radius"); SameDoubleBits(values.thickness, actual.thickness, "failed transform thickness");
        Equal(normal, entity.Normal, "failed transform normal"); Check(proxy.SequenceEqual(entity.ProxyGraphics!), "failed transform proxy");
        if (entity is Arc checkArc) { SameDoubleBits(start, checkArc.StartAngle, "failed transform start"); SameDoubleBits(end, checkArc.EndAngle, "failed transform end"); }
    }
}
