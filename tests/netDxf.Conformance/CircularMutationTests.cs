// Copyright (c) netDxf contributors. Licensed under the MIT License.
using netDxf;
using netDxf.Entities;
using netDxf.Header;

namespace NetDxf.Conformance;
internal static partial class Program
{
    private static readonly byte[] CircularMutationProxy = { 1, 3, 7, 255 };
    private static string[] CircularMutationProperties(bool arc) => arc
        ? new[] { "Center", "Radius", "Thickness", "StartAngle", "EndAngle" }
        : new[] { "Center", "Radius", "Thickness" };
    private static EntityObject CircularMutationSubject(bool arc, bool tilted = false)
    {
        EntityObject e = arc ? new Arc(new Vector3(1, 2, 3), 3, 30, 210) { Thickness = 1 }
            : new Circle(new Vector3(1, 2, 3), 3) { Thickness = 1 };
        e.Normal = tilted ? Vector3.UnitX : Vector3.UnitZ;
        e.Color = new AciColor(3); e.IsVisible = false; e.ProxyGraphics = CircularMutationProxy;
        return e;
    }
    private static void CircularMutationSet(EntityObject e, string name, object value)
    {
        if (e is Circle c)
        {
            switch (name) {
                case "Center": c.Center = (Vector3)value; break;
                case "Radius": c.Radius = (double)value; break;
                case "Thickness": c.Thickness = (double)value; break;
                default: throw new ArgumentException(name);
            }
        }
        else
        {
            var a = (Arc)e;
            switch (name) {
                case "Center": a.Center = (Vector3)value; break;
                case "Radius": a.Radius = (double)value; break;
                case "Thickness": a.Thickness = (double)value; break;
                case "StartAngle": a.StartAngle = (double)value; break;
                case "EndAngle": a.EndAngle = (double)value; break;
                default: throw new ArgumentException(name);
            }
        }
    }
    private static object CircularMutationGet(EntityObject e, string name) => e.GetType().GetProperty(name)!.GetValue(e)!;
    private static long[] CircularMutationState(EntityObject e)
    {
        var v = ReviewedCircularValues(e);
        return new[] { v.center.X, v.center.Y, v.center.Z, v.radius, v.thickness,
            e.Normal.X, e.Normal.Y, e.Normal.Z, e is Arc a ? a.StartAngle : 0, e is Arc b ? b.EndAngle : 0 }
            .Select(BitConverter.DoubleToInt64Bits).ToArray();
    }
    private static object CircularMutationValue(EntityObject e, string name, int mode)
    {
        object prior = CircularMutationGet(e, name);
        if (name == "Center") return mode == 0 ? prior : mode == 1 ? new Vector3(-8, 9, 10)
            : mode == 2 ? new Vector3(Math.BitIncrement(1), 2, 3)
            : mode == 3 ? new Vector3(1, Math.BitIncrement(2), 3) : new Vector3(1, 2, Math.BitIncrement(3));
        double x = (double)prior;
        return mode == 0 ? x : mode == 1 ? (name == "Radius" ? 4 : name == "Thickness" ? -2 : 90)
            : mode == 2 ? Math.BitIncrement(x) : mode == 3 ? x + 360 : x - (name == "Radius" ? 1 : 360);
    }
    private static void RegisterCircularMutationTests()
    {
        foreach (bool arc in new[] { false, true }) foreach (string name in CircularMutationProperties(arc))
        foreach (bool owned in new[] { false, true }) for (int mode = 0; mode < 5; mode++)
        {
            int m = mode;
            Run($"circular-mutation/set/{arc}/{name}/{owned}/{m}", () => {
                var e = CircularMutationSubject(arc); var doc = new DxfDocument(); if (owned) doc.Entities.Add(e);
                var owner = e.Owner; string handle = e.Handle; var color = e.Color; var layer = e.Layer;
                var before = CircularMutationState(e); var properties = CircularMutationProperties(arc);
                var old = properties.ToDictionary(n => n, n => CircularMutationGet(e, n));
                object value = CircularMutationValue(e, name, m);
                CircularMutationSet(e, name, value);
                object expected = name.EndsWith("Angle", StringComparison.Ordinal) ? MathHelper.NormalizeAngle((double)value) : value;
                if (expected is Vector3 p) RawLinePointBits(p, (Vector3)CircularMutationGet(e, name));
                else SameDoubleBits((double)expected, (double)CircularMutationGet(e, name), "Stored assignment");
                bool changed = !before.SequenceEqual(CircularMutationState(e));
                Check(changed ? e.ProxyGraphics == null : e.ProxyGraphics!.SequenceEqual(CircularMutationProxy), "Proxy change/no-op policy");
                foreach (string n in properties.Where(n => n != name)) {
                    if (old[n] is Vector3 point) RawLinePointBits(point, (Vector3)CircularMutationGet(e, n));
                    else SameDoubleBits((double)old[n], (double)CircularMutationGet(e, n), "Other property changed");
                }
                Check(ReferenceEquals(owner, e.Owner) && handle == e.Handle && ReferenceEquals(color, e.Color) && ReferenceEquals(layer, e.Layer), "Metadata changed");
                var clone = (EntityObject)e.Clone(); clone.ProxyGraphics = CircularMutationProxy;
                CircularMutationSet(clone, name, name == "Center" ? (object)new Vector3(11, 22, 33) : 19.0);
                Check(clone.ProxyGraphics == null, "Clone retained stale proxy");
                Check(changed ? e.ProxyGraphics == null : e.ProxyGraphics!.SequenceEqual(CircularMutationProxy), "Clone edit leaked");
            });
        }
        foreach (bool arc in new[] { false, true }) foreach (string name in CircularMutationProperties(arc))
        foreach (double v in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            Run($"circular-mutation/nonfinite/{arc}/{name}/{ParameterBits(v)}", () => {
                var e = CircularMutationSubject(arc); var before = CircularMutationState(e);
                if (name == "Radius" && v == double.NegativeInfinity) {
                    Throws<ArgumentOutOfRangeException>(() => CircularMutationSet(e, name, v));
                    Check(before.SequenceEqual(CircularMutationState(e)) && e.ProxyGraphics!.SequenceEqual(CircularMutationProxy), "Rejected radius mutated source"); return;
                }
                object value = name == "Center" ? (object)new Vector3(v, 2, 3) : v;
                CircularMutationSet(e, name, value); Check(e.ProxyGraphics == null, "Changed nonfinite geometry retained proxy");
                e.ProxyGraphics = CircularMutationProxy; var prior = CircularMutationState(e); CircularMutationSet(e, name, value);
                Check(prior.SequenceEqual(CircularMutationState(e)) && e.ProxyGraphics!.SequenceEqual(CircularMutationProxy), "Identical stored nonfinite bits invalidated proxy");
            });
        foreach (bool arc in new[] { false, true })
        {
            foreach (double value in new[] { 0.0, -0.0, -1.0 })
                Run($"circular-mutation/rejected-radius/{arc}/{ParameterBits(value)}", () => {
                    var e = CircularMutationSubject(arc); var before = CircularMutationState(e);
                    Throws<ArgumentOutOfRangeException>(() => CircularMutationSet(e, "Radius", value));
                    Check(before.SequenceEqual(CircularMutationState(e)) && e.ProxyGraphics!.SequenceEqual(CircularMutationProxy), "Rejected radius changed source");
                });
            for (int axis = 0; axis < 3; axis++) {
                int a = axis;
                Run($"circular-mutation/center-zero/{arc}/{a}", () => {
                    var e = CircularMutationSubject(arc); CircularMutationSet(e, "Center", Vector3.Zero); e.ProxyGraphics = CircularMutationProxy;
                    var v = Vector3.Zero; v[a] = -0.0; CircularMutationSet(e, "Center", v);
                    Check(e.ProxyGraphics == null, "Signed-zero center retained proxy"); SameDoubleBits(-0.0, ((Vector3)CircularMutationGet(e, "Center"))[a], "Center sign bit");
                });
            }
            Run($"circular-mutation/thickness-zero/{arc}", () => {
                var e = CircularMutationSubject(arc); CircularMutationSet(e, "Thickness", 0.0); e.ProxyGraphics = CircularMutationProxy;
                CircularMutationSet(e, "Thickness", -0.0); Check(e.ProxyGraphics == null, "Signed thickness zero retained proxy");
                SameDoubleBits(-0.0, (double)CircularMutationGet(e, "Thickness"), "Thickness sign bit");
            });
            Run($"circular-mutation/struct-cache/{arc}", () => {
                var e = CircularMutationSubject(arc); var n = Vector3.Normalize(new Vector3(3,4,0));
                CircularMutationSet(e, "Center", new Vector3(n.X,n.Y,n.Z)); e.ProxyGraphics = CircularMutationProxy; CircularMutationSet(e, "Center", n);
                Equal(n.IsNormalized, ((Vector3)CircularMutationGet(e, "Center")).IsNormalized, "Complete struct not assigned");
                Check(e.ProxyGraphics!.SequenceEqual(CircularMutationProxy), "Cache-only edit invalidated proxy");
            });
        }
        foreach (DxfVersion version in SupportedVersions) foreach (bool binary in new[] { false, true })
        foreach (bool arc in new[] { false, true }) foreach (string name in CircularMutationProperties(arc))
        foreach (bool changed in new[] { false, true }) foreach (bool tilted in new[] { false, true })
            Run($"circular-mutation/wire/{version}/{binary}/{arc}/{name}/{changed}/{tilted}", () => {
                var e = CircularMutationSubject(arc, tilted); CircularMutationSet(e, name, CircularMutationValue(e, name, changed ? 1 : 0));
                var doc = new DxfDocument(version); doc.Comments.Clear(); doc.Entities.Add(e);
                using var output = new MemoryStream(); Check(doc.Save(output, binary), "Circular mutation save");
                File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"circular-mutation-{version}-{binary}-{arc}-{name}-{changed}-{tilted}.dxf"), output.ToArray());
                output.Position = 0; var loaded = DxfDocument.Load(output)!; EntityObject result = arc ? loaded.Entities.Arcs.Single() : loaded.Entities.Circles.Single();
                Check(CircularMutationState(e).SequenceEqual(CircularMutationState(result)), "Geometry round trip");
                Check(changed ? result.ProxyGraphics == null : result.ProxyGraphics!.SequenceEqual(CircularMutationProxy), "Proxy round trip");
                Equal(0, loaded.Objects.Validate().Count, "Circular mutation graph");
            });
    }
}
