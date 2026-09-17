// Copyright (c) netDxf contributors. Licensed under the MIT License.
using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.Tables;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private sealed record InfiniteCase(string Name, Matrix3 Matrix, Vector3 Translation, Vector3 Origin, Vector3 Direction, Vector3 Normal);
    private static InfiniteCase[] InfiniteCases()
    {
        var o = new Vector3(1, 2, 3); var d = new Vector3(2, -3, 6); var n = Vector3.UnitZ;
        Matrix3 Scale(double x, double y, double z) => new(x, 0, 0, 0, y, 0, 0, 0, z);
        double tiny = Math.ScaleB(1, -1000), big = Math.ScaleB(1, 1000), max = double.MaxValue, min = double.Epsilon;
        return new[]
        {
            new InfiniteCase("identity", Matrix3.Identity, Vector3.Zero, o, d, n),
            new InfiniteCase("translation", Matrix3.Identity, new(4, -5, 6), o, d, n),
            new InfiniteCase("uniform", Scale(3, 3, 3), Vector3.Zero, o, d, n),
            new InfiniteCase("nonuniform", Scale(2, 3, 4), Vector3.Zero, o, d, n),
            new InfiniteCase("reflection-x", Scale(-1, 1, 1), Vector3.Zero, o, d, n),
            new InfiniteCase("reflection-all", Scale(-2, -2, -2), Vector3.Zero, o, d, n),
            new InfiniteCase("rotation", new(0, -1, 0, 1, 0, 0, 0, 0, 1), Vector3.Zero, o, d, n),
            new InfiniteCase("shear", new(1, 2, 0, 0, 1, .5, 0, 0, 1), Vector3.Zero, o, d, n),
            new InfiniteCase("unchanged", Scale(1, 2, 3), Vector3.Zero, Vector3.Zero, Vector3.UnitX, Vector3.UnitX),
            new InfiniteCase("normal-collapse", Scale(1, 1, 0), Vector3.Zero, o, new(2, -3, 0), n),
            new InfiniteCase("singular", Scale(0, 1, 1), Vector3.Zero, o, new(0, 3, 4), n),
            new InfiniteCase("tiny", Scale(tiny, tiny, tiny), Vector3.Zero, o, d, n),
            new InfiniteCase("subnormal", Scale(min, min, min), Vector3.Zero, o, Vector3.UnitX, n),
            new InfiniteCase("large", Scale(big, big, big), Vector3.Zero, o, d, n),
            new InfiniteCase("max", Scale(max, max, max), Vector3.Zero, Vector3.Zero, new(1, 1, 1), n),
            new InfiniteCase("overflow-cancellation", new(max, -max, 0, 0, 1, 0, 0, 0, 1), new(3, 4, 5), new(2, 2, 2), new(1, 1, 1), n),
            new InfiniteCase("direction-overflow", new(max, max, max, max, -max, 0, 0, 0, max), new(5, 7, 9), Vector3.Zero, new(1, 1, 1), n),
            new InfiniteCase("direction-subnormal", new(min, -min, min, 0, min, 0, 0, 0, min), Vector3.Zero, Vector3.Zero, new(1, 1, 0), n),
            new InfiniteCase("large-translation-cancellation", Matrix3.Identity, new(-1e300, 1e300, -1e300), new(1e300, -1e300, 1e300), d, n),
            new InfiniteCase("permutation", new(0, 0, 1, 1, 0, 0, 0, 1, 0), Vector3.Zero, o, d, n),
            new InfiniteCase("anisotropic", Scale(1e-100, 1e100, 1), Vector3.Zero, o, new(1, 1, 1), n),
        };
    }

    private static EntityObject InfiniteEntity(bool ray, InfiniteCase c)
    {
        EntityObject entity = ray ? new Ray(c.Origin, c.Direction) : new XLine(c.Origin, c.Direction);
        entity.Normal = c.Normal; entity.Layer = new Layer("INFINITE_AUDIT"); entity.Color = AciColor.Green;
        entity.LinetypeScale = 1.25; entity.IsVisible = false; entity.ProxyGraphics = new byte[] { 4, 3, 2, 1 };
        return entity;
    }
    private static Vector3 InfiniteOrigin(EntityObject e) => e is Ray r ? r.Origin : ((XLine)e).Origin;
    private static Vector3 InfiniteDirection(EntityObject e) => e is Ray r ? r.Direction : ((XLine)e).Direction;
    private static Matrix4 InfiniteMatrix4(Matrix3 m, Vector3 t) => new(m.M11, m.M12, m.M13, t.X,
        m.M21, m.M22, m.M23, t.Y, m.M31, m.M32, m.M33, t.Z, 0, 0, 0, 1);
    private static void InfiniteApply(EntityObject e, Matrix3 m, Vector3 t, bool four)
    { if (four) e.TransformBy(InfiniteMatrix4(m, t)); else e.TransformBy(m, t); }
    private static string[] InfiniteSnapshot(EntityObject e) => DirectionBits(InfiniteOrigin(e))
        .Concat(DirectionBits(InfiniteDirection(e))).Concat(DirectionBits(e.Normal)).ToArray();
    private static void InfiniteUnit(Vector3 v)
    {
        Check(double.IsFinite(v.X) && double.IsFinite(v.Y) && double.IsFinite(v.Z), "Nonfinite result direction");
        Check(Math.Abs(Vector3.DotProduct(v, v) - 1) <= 2e-15, "Result direction is not unit length");
    }
    private static void InfiniteWire(bool ray, bool four, DxfVersion version, bool binary, int index)
    {
        var c = InfiniteCases()[index]; var e = InfiniteEntity(ray, c); var doc = new DxfDocument(version);
        doc.Entities.Add(e); string handle = e.Handle; var owner = e.Owner; var layer = e.Layer; var color = e.Color;
        var before = InfiniteSnapshot(e);
        InfiniteApply(e, c.Matrix, c.Translation, four);
        var after = InfiniteSnapshot(e); InfiniteUnit(InfiniteDirection(e)); InfiniteUnit(e.Normal);
        Vector3 normalImage = c.Matrix * c.Normal;
        if (normalImage.X == 0 && normalImage.Y == 0 && normalImage.Z == 0)
            Check(before.Skip(6).SequenceEqual(after.Skip(6)), "Collapsed auxiliary normal was not retained");
        else CheckUnitDirection(normalImage, e.Normal);
        Check(double.IsFinite(InfiniteOrigin(e).X) && double.IsFinite(InfiniteOrigin(e).Y) && double.IsFinite(InfiniteOrigin(e).Z), "Nonfinite origin");
        Check(ReferenceEquals(owner, e.Owner) && handle == e.Handle && ReferenceEquals(layer, e.Layer) && ReferenceEquals(color, e.Color), "Transform changed source identities");
        bool unchanged = c.Name is "identity" or "unchanged";
        Check(unchanged ? e.ProxyGraphics!.SequenceEqual(new byte[] { 4, 3, 2, 1 }) : e.ProxyGraphics == null, "Proxy invalidation differs");
        if (unchanged) Check(before.SequenceEqual(after), "Unchanged geometry lost exact components");
        if (c.Name.Contains("translation", StringComparison.Ordinal))
            Check(before.Skip(3).SequenceEqual(after.Skip(3)), "Translation altered direction or auxiliary normal");
        if (index < 11 || c.Name == "permutation")
        {
            Vector3 expected = c.Matrix * c.Origin + c.Translation;
            Check(Vector3.Distance(expected, InfiniteOrigin(e)) < 1e-12, "Ordinary affine origin differs");
            Vector3 image = c.Matrix * Vector3.Normalize(c.Direction);
            Check(Vector3.DotProduct(image, InfiniteDirection(e)) > 0, "Ray traversal reversed");
            Check(Vector3.CrossProduct(Vector3.Normalize(image), InfiniteDirection(e)).Modulus() < 1e-12, "Ordinary direction differs");
        }
        using var stream = new MemoryStream(); Check(doc.Save(stream, binary), "Save failed");
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"infinite-affine-{version}-{binary}-{ray}-{four}-{index}.dxf"), stream.ToArray());
        stream.Position = 0; var loaded = DxfDocument.Load(stream) ?? throw new InvalidOperationException("Load failed");
        EntityObject copy = ray ? loaded.Entities.Rays.Single() : loaded.Entities.XLines.Single();
        InfiniteUnit(InfiniteDirection(copy));
        Check(DirectionBits(InfiniteOrigin(e)).SequenceEqual(DirectionBits(InfiniteOrigin(copy))), "Saved origin differs");
        Check(!copy.IsVisible && copy.Layer.Name == "INFINITE_AUDIT" && copy.LinetypeScale == 1.25, "Common metadata differs");
    }

    private static void InfiniteReject(EntityObject e, Action action)
    {
        var before = InfiniteSnapshot(e); byte[] proxy = e.ProxyGraphics!; var layer = e.Layer; var color = e.Color;
        Exception? failure = null; try { action(); } catch (Exception ex) { failure = ex; }
        Check(failure is ArgumentException or NotSupportedException or InvalidOperationException, "Invalid transform was accepted or failed unexpectedly");
        Check(before.SequenceEqual(InfiniteSnapshot(e)), "Rejected transform partially changed geometry");
        Check(proxy.SequenceEqual(e.ProxyGraphics ?? Array.Empty<byte>()) && ReferenceEquals(layer, e.Layer) && ReferenceEquals(color, e.Color), "Rejected transform changed metadata");
    }
    private static void InfiniteBadMatrix(bool ray, bool four, int slot, double bad)
    {
        var e = InfiniteEntity(ray, InfiniteCases()[0]);
        if (four)
        { var m = InfiniteMatrix4(Matrix3.Identity, new(1, 2, 3)); m[slot / 4, slot % 4] = bad; InfiniteReject(e, () => e.TransformBy(m)); }
        else if (slot < 9)
        { var m = Matrix3.Identity; m[slot / 3, slot % 3] = bad; InfiniteReject(e, () => e.TransformBy(m, new(1, 2, 3))); }
        else
        { var t = new Vector3(1, 2, 3); t[slot - 9] = bad; InfiniteReject(e, () => e.TransformBy(Matrix3.Identity, t)); }
    }
    private static void InfiniteInvalidSource(bool ray, bool four, int field, int component, double bad)
    {
        var e = InfiniteEntity(ray, InfiniteCases()[0]); var value = field == 0 ? InfiniteOrigin(e) : field == 1 ? InfiniteDirection(e) : e.Normal;
        value[component] = bad;
        var type = field == 2 ? typeof(EntityObject) : e.GetType();
        type.GetField(new[] { "origin", "direction", "normal" }[field], System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(e, value);
        InfiniteReject(e, () => InfiniteApply(e, Matrix3.Identity, new(1, 2, 3), four));
    }
    private static void InfiniteUnrepresentable(bool ray, bool four, int variant)
    {
        var c = InfiniteCases()[0]; Matrix3 m = Matrix3.Identity; Vector3 t = new(1, 2, 3);
        if (variant == 0) m = new(0, 0, 0, 0, 0, 0, 0, 0, 0);
        else if (variant == 1) { c = c with { Direction = Vector3.UnitX }; m = new(0, 0, 0, 0, 1, 0, 0, 0, 1); }
        else if (variant == 2) { c = c with { Origin = new(2, 0, 0) }; m = new(double.MaxValue, 0, 0, 0, 1, 0, 0, 0, 1); }
        else if (variant == 3) { c = c with { Origin = new(.25, 0, 0) }; m = new(double.Epsilon, 0, 0, 0, 1, 0, 0, 0, 1); t = Vector3.Zero; }
        else { c = c with { Origin = Vector3.Zero, Direction = new(1, 1, 0) }; m = new(double.Epsilon, 0, 0, 0, double.MaxValue, 0, 0, 0, 1); }
        var e = InfiniteEntity(ray, c); InfiniteReject(e, () => InfiniteApply(e, m, t, four));
    }
    private sealed class InfiniteHostileRay : Ray
    { public override Vector3 Normal { get => throw new InvalidOperationException("Virtual getter invoked"); set => throw new InvalidOperationException("Virtual setter invoked"); } }
    private sealed class InfiniteHostileXLine : XLine
    { public override Vector3 Normal { get => throw new InvalidOperationException("Virtual getter invoked"); set => throw new InvalidOperationException("Virtual setter invoked"); } }
    private static void RegisterInfiniteLineTransformTests()
    {
        Run("infinite-affine/numerical-oracle", InfiniteNumericalOracle);
        foreach (bool ray in new[] { false, true }) foreach (bool four in new[] { false, true })
        {
            foreach (DxfVersion version in SupportedVersions) foreach (bool binary in new[] { false, true })
                for (int i = 0; i < InfiniteCases().Length; i++)
                { int n = i; Run($"infinite-affine/wire/{ray}/{four}/{version}/{binary}/{n}", () => InfiniteWire(ray, four, version, binary, n)); }
            for (int i = 0; i < (four ? 16 : 12); i++) foreach (double bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            { int n = i; Run($"infinite-affine/matrix/{ray}/{four}/{n}/{bad:R}", () => InfiniteBadMatrix(ray, four, n, bad)); }
            for (int field = 0; field < 3; field++) for (int component = 0; component < 3; component++)
                foreach (double bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
                { int f = field, c = component; Run($"infinite-affine/source/{ray}/{four}/{f}/{c}/{bad:R}", () => InfiniteInvalidSource(ray, four, f, c, bad)); }
            foreach (int field in new[] { 1, 2 }) foreach (int invalid in new[] { 0, 1 })
                Run($"infinite-affine/invalid-unit/{ray}/{four}/{field}/{invalid}", () =>
                    InfiniteInvalidUnit(ray, four, field, invalid == 0 ? Vector3.Zero : new Vector3(2, 0, 0)));
            for (int i = 0; i < 5; i++)
            { int n = i; Run($"infinite-affine/reject/{ray}/{four}/{n}", () => InfiniteUnrepresentable(ray, four, n)); }
            Run($"infinite-affine/no-virtual-callback/{ray}/{four}", () =>
            { EntityObject e = ray ? new InfiniteHostileRay() : new InfiniteHostileXLine(); InfiniteApply(e, new(0, -1, 0, 1, 0, 0, 0, 0, 1), new(1, 2, 3), four); Check(InfiniteOrigin(e) == new Vector3(1, 2, 3) && InfiniteDirection(e) == Vector3.UnitY, "Stored geometry not published"); });
        }
        foreach (bool ray in new[] { false, true }) for (int column = 0; column < 4; column++) foreach (double value in new[] { -.5, .5, 2.0 })
        {
            int c = column; Run($"infinite-affine/projective/{ray}/{c}/{value:R}", () =>
            { var e = InfiniteEntity(ray, InfiniteCases()[0]); var m = InfiniteMatrix4(Matrix3.Identity, new(1, 2, 3)); m[3, c] = value; InfiniteReject(e, () => e.TransformBy(m)); });
        }
    }
}
