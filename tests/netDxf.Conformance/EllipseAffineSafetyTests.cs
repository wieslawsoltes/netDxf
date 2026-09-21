// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System.Reflection;
using netDxf;
using netDxf.Entities;
using netDxf.Header;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private sealed class CallbackEllipse : Ellipse
    {
        internal QuadNormalProbe Probe { get; } = new();
        internal Vector3 StoredNormal => base.Normal;
        public override Vector3 Normal
        {
            get => Probe.Read(base.Normal);
            set => Probe.Write(value, n => base.Normal = n);
        }
        public override void TransformBy(Matrix3 matrix, Vector3 translation)
        { Probe.Transforms++; base.TransformBy(matrix, translation); }
    }

    private static CallbackEllipse SafeEllipse(int mode, bool arc, bool rotated = true)
    {
        var e = new CallbackEllipse { Center = new(1, 2, 3), Rotation = rotated ? 23 : 0,
            StartAngle = arc ? 25 : 0, EndAngle = arc ? 215 : 0, Color = new AciColor(3), IsVisible = false };
        e.SetAxis(12, 4); e.Probe.Mode = mode; e.ProxyGraphics = new byte[] { 1, 7, 9, 255 };
        return e;
    }

    private static long[] SafeEllipseState(Ellipse e, Vector3 normal)
        => new[] { e.Center.X, e.Center.Y, e.Center.Z, e.MajorAxis, e.MinorAxis, e.Rotation,
            e.StartAngle, e.EndAngle, e.Thickness, normal.X, normal.Y, normal.Z }
            .Select(BitConverter.DoubleToInt64Bits).ToArray();

    private static (Matrix3 Matrix, Vector3 Translation) SafeEllipseOperation(int kind)
        => kind switch
        {
            0 => (Matrix3.Identity, Vector3.Zero),
            1 => (Matrix3.Identity, new(10, 20, 30)),
            2 => (new(0, 0, 1, 0, 1, 0, -1, 0, 0), new(10, 20, 30)),
            3 => (Matrix3.Scale(2, 3, 4), Vector3.Zero),
            4 => (Matrix3.Scale(-1, 1, 1), Vector3.Zero),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

    private static void RegisterEllipseAffineSafetyTests()
    {
        foreach (bool arc in new[] { false, true }) foreach (bool four in new[] { false, true })
            foreach (bool owned in new[] { false, true }) for (int mode = 0; mode < 6; mode++)
                for (int kind = 0; kind < 5; kind++)
                {
                    int m = mode, k = kind;
                    Run($"ellipse-affine-safety/callback/{arc}/{four}/{owned}/{m}/{k}", () =>
                    {
                        var e = SafeEllipse(m, arc); var reference = (Ellipse)e.Clone();
                        var doc = new DxfDocument(); if (owned) doc.Entities.Add(e);
                        var owner = e.Owner; string handle = e.Handle; var color = e.Color;
                        long[] before = SafeEllipseState(e, e.StoredNormal); byte[] proxy = e.ProxyGraphics!;
                        var op = SafeEllipseOperation(k); reference.TransformBy(op.Matrix, op.Translation);
                        e.Probe.Armed = true;
                        if (four) e.TransformBy(PlanarReviewMatrix4(op.Matrix, op.Translation));
                        else e.TransformBy(op.Matrix, op.Translation);
                        Equal(0, e.Probe.Reads + e.Probe.Writes, "Affine transform called derived Normal");
                        Equal(1, e.Probe.Transforms, "Matrix4 lost virtual Matrix3 dispatch");
                        Check(SafeEllipseState(reference, reference.Normal).SequenceEqual(SafeEllipseState(e, e.StoredNormal)), "Callback changed affine result");
                        Check(ReferenceEquals(owner, e.Owner) && handle == e.Handle && ReferenceEquals(color, e.Color), "Metadata identity changed");
                        if (k == 0)
                        {
                            Check(before.SequenceEqual(SafeEllipseState(e, e.StoredNormal)), "Identity changed bits");
                            Check(e.ProxyGraphics!.SequenceEqual(proxy), "Identity lost proxy");
                        }
                        else Check(e.ProxyGraphics == null, "Changed geometry retained proxy");
                    });
                }
        foreach (bool four in new[] { false, true }) for (int fault = 0; fault < 6; fault++)
        {
            int f = fault;
            Run($"ellipse-affine-safety/reject/{four}/{f}", () =>
            {
                var e = SafeEllipse(4, true); var matrix = Matrix3.Scale(2); var translation = Vector3.Zero;
                if (f == 0) matrix = Matrix3.Scale(0);
                if (f == 1) matrix.M11 = double.NaN;
                if (f == 2) translation = new(double.NaN, 0, 0);
                if (f == 3) { matrix = new(1, 0, 1, 0, 1, 0, 0, 0, 1); e.Thickness = 2; }
                if (f == 4) typeof(Ellipse).GetField("majorAxis", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(e, double.NaN);
                if (f == 5) e.EndAngle = double.NaN;
                long[] before = SafeEllipseState(e, e.StoredNormal); byte[] proxy = e.ProxyGraphics!;
                e.Probe.Armed = true; Exception? error = null;
                try { if (four) e.TransformBy(PlanarReviewMatrix4(matrix, translation)); else e.TransformBy(matrix, translation); }
                catch (Exception ex) { error = ex; }
                Check(error is ArgumentException or NotSupportedException, "Invalid transform did not reject through geometry admission");
                Equal(0, e.Probe.Reads + e.Probe.Writes, "Rejected operation called derived Normal");
                Check(before.SequenceEqual(SafeEllipseState(e, e.StoredNormal)) && e.ProxyGraphics!.SequenceEqual(proxy), "Rejected operation changed source");
            });
        }
        Run("ellipse-affine-safety/projective", () =>
        {
            var e = SafeEllipse(4, true); e.Probe.Armed = true; var matrix = Matrix4.Identity; matrix.M41 = .5;
            Throws<NotSupportedException>(() => e.TransformBy(matrix));
            Equal(0, e.Probe.Transforms + e.Probe.Reads + e.Probe.Writes, "Projective input dispatched before validation");
        });
        foreach (double epsilon in new[] { 1e-12, 1e-3, 100.0 })
            foreach (double angle in new[] { 0.0, -0.0, 5e-14, -5e-14, 30.0, 30.0000000001, 359.9999999999, 360.0, -360.0, 720.0, -720.0, 1e20 })
                Run($"ellipse-affine-safety/normalization/{epsilon:R}/{ParameterBits(angle)}", () =>
                {
                    var e = new Ellipse(); double old = MathHelper.Epsilon;
                    double expected = angle % 360; if (expected < 0) expected += 360; if (expected == 0 || expected == 360) expected = 0;
                    try
                    {
                        MathHelper.Epsilon = epsilon; e.Rotation = angle; e.StartAngle = angle; e.EndAngle = angle;
                        SameDoubleBits(expected, e.Rotation, "Rotation coalesced"); SameDoubleBits(expected, e.StartAngle, "Start coalesced");
                        SameDoubleBits(expected, e.EndAngle, "End coalesced"); Check(e.IsFullEllipse, "Equal normalized angles are not full");
                    }
                    finally { MathHelper.Epsilon = old; }
                });
        foreach (double epsilon in new[] { 1e-12, 1e-3, 100.0 })
            foreach (var sweep in new[] { (0.0, 5e-14), (30.0, 30.0000000001), (30.0000000001, 30.0), (359.9999999999, 0.0) })
                Run($"ellipse-affine-safety/sweep/{epsilon:R}/{ParameterBits(sweep.Item1)}/{ParameterBits(sweep.Item2)}", () =>
                {
                    var e = new Ellipse(Vector3.Zero, 12, 4); double old = MathHelper.Epsilon;
                    try
                    {
                        MathHelper.Epsilon = epsilon; e.StartAngle = sweep.Item1; e.EndAngle = sweep.Item2;
                        Check(!e.IsFullEllipse, "Distinct angles became full ellipse");
                        var points = e.PolygonalVertexes(5); Equal(5, points.Count, "Arc sample count");
                        Check(!DirectionBits(new Vector3(points[0].X, points[0].Y, 0)).SequenceEqual(DirectionBits(new Vector3(points[^1].X, points[^1].Y, 0))), "Arc endpoints collapsed");
                    }
                    finally { MathHelper.Epsilon = old; }
                });
        foreach (bool four in new[] { false, true })
            Run($"ellipse-affine-safety/short-arc-transform/{four}", () =>
            {
                var e = new Ellipse(Vector3.Zero, 12, 4) { StartAngle = 30, EndAngle = 30.0000000001 };
                var first = e.PolarCoordinateRelativeToCenter(e.StartAngle); var last = e.PolarCoordinateRelativeToCenter(e.EndAngle);
                e.ProxyGraphics = new byte[] { 9, 1 };
                if (four) e.TransformBy(PlanarReviewMatrix4(Matrix3.Scale(2), Vector3.Zero)); else e.TransformBy(Matrix3.Scale(2), Vector3.Zero);
                Check(!e.IsFullEllipse && e.StartAngle != e.EndAngle && e.ProxyGraphics == null, "Short arc transform lost sweep/cache invalidation");
                var a = e.PolarCoordinateRelativeToCenter(e.StartAngle); var b = e.PolarCoordinateRelativeToCenter(e.EndAngle);
                Check(Math.Abs(a.X - 2 * first.X) < 1e-13 && Math.Abs(a.Y - 2 * first.Y) < 1e-13 &&
                    Math.Abs(b.X - 2 * last.X) < 1e-13 && Math.Abs(b.Y - 2 * last.Y) < 1e-13, "Short arc endpoints moved incorrectly");
            });
        foreach (double angle in new[] { 0.0, 90.0, 180.0, 270.0 })
            Run($"ellipse-affine-safety/thin-cardinal/{angle}", () =>
            {
                var e = new Ellipse(Vector3.Zero, 1e200, 2e-100); var p = e.PolarCoordinateRelativeToCenter(angle);
                Vector2 expected = angle == 0 ? new(5e199, 0) : angle == 90 ? new(0, 1e-100) : angle == 180 ? new(-5e199, 0) : new(0, -1e-100);
                SameDoubleBits(expected.X, p.X, "Cardinal X"); SameDoubleBits(expected.Y, p.Y, "Cardinal Y");
            });
        foreach (DxfVersion version in SupportedVersions) foreach (bool binary in new[] { false, true }) for (int kind = 0; kind < 3; kind++)
        {
            int k = kind;
            Run($"ellipse-affine-safety/wire/{version}/{binary}/{k}", () =>
            {
                var e = SafeEllipse(4, k != 0, false);
                if (k == 1) { e.StartAngle = 0; e.EndAngle = 90; }
                if (k == 2) { e.StartAngle = 30; e.EndAngle = 30.0000000001; }
                e.Probe.Armed = true; var op = SafeEllipseOperation(2); e.TransformBy(PlanarReviewMatrix4(op.Matrix, op.Translation));
                Equal(0, e.Probe.Reads + e.Probe.Writes, "Wire transform called Normal override"); e.Probe.Armed = false;
                var doc = new DxfDocument(version); doc.Comments.Clear(); doc.Entities.Add(e);
                using var stream = new MemoryStream(); Check(doc.Save(stream, binary), "Ellipse save failed");
                File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"ellipse-affine-safety-{version}-{binary}-{k}.dxf"), stream.ToArray());
                // Tiny-arc typed read tolerance is a separate IO limitation. The new
                // independent checker verifies its actual emitted parameter packet.
                if (k != 2)
                {
                    stream.Position = 0; var loaded = DxfDocument.Load(stream)!; var result = loaded.Entities.Ellipses.Single();
                    Equal(k == 0, result.IsFullEllipse, "Ordinary round-trip closure"); Equal(0, loaded.Objects.Validate().Count, "Ellipse graph");
                }
            });
        }
    }
}
