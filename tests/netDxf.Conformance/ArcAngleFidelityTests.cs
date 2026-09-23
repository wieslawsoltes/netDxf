// Copyright (c) netDxf contributors. Licensed under the MIT License.
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;
using netDxf.Tables;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static readonly (double input, double expected)[] ArcAngleAssignments = {
        (0.0, 0.0), (BitConverter.Int64BitsToDouble(long.MinValue), 0.0),
        (double.Epsilon, double.Epsilon), (1e-300, 1e-300), (1e-13, 1e-13),
        (5e-13, 5e-13), (1e-9, 1e-9), (.25, .25), (30, 30), (90, 90),
        (Math.BitIncrement(90), Math.BitIncrement(90)),
        (Math.BitDecrement(360), Math.BitDecrement(360)),
        (359.9999999999, 359.9999999999), (360, 0), (-360, 0),
        (720, 0), (-720, 0), (360.25, .25), (-.25, 359.75),
        (-450, 270), (450, 90), (-double.Epsilon, 0),
        (double.MaxValue, 128), (-double.MaxValue, 232)
    };
    private static readonly (double start, double end)[] ArcAnglePairs = {
        (double.Epsilon, 1e-13), (1e-300, 180), (1e-13, 2e-13),
        (0, Math.BitDecrement(360)), (Math.BitDecrement(360), double.Epsilon),
        (359.999999999999, 1e-12), (Math.BitIncrement(90), Math.BitDecrement(180)),
        (359.9999999999, 359.99999999995), (1e-9, 2e-9), (90, 180)
    };

    private static void RegisterArcAngleFidelityTests()
    {
        foreach (double epsilon in new[] { double.Epsilon, 1e-12, 1e-3, 100.0 })
        for (int index = 0; index < ArcAngleAssignments.Length; index++) foreach (bool start in new[] { false, true })
        {
            int i = index;
            Run($"arc-angle-fidelity/api/{ParameterBits(epsilon)}/{i}/{start}", () => {
                double old = MathHelper.Epsilon;
                try
                {
                    MathHelper.Epsilon = epsilon;
                    var pair = ArcAngleAssignments[i];
                    var arc = new Arc(new Vector3(1, 2, 3), 4, pair.input, pair.input) { Thickness = 2 };
                    SameDoubleBits(pair.expected, arc.StartAngle, "Constructor start angle");
                    SameDoubleBits(pair.expected, arc.EndAngle, "Constructor end angle");
                    arc.ProxyGraphics = CircularMutationProxy;
                    if (start) arc.StartAngle = pair.input; else arc.EndAngle = pair.input;
                    Check(arc.ProxyGraphics!.SequenceEqual(CircularMutationProxy), "Canonical no-op lost proxy");
                    var clone = (Arc)arc.Clone();
                    SameDoubleBits(pair.expected, clone.StartAngle, "Clone start angle");
                    SameDoubleBits(pair.expected, clone.EndAngle, "Clone end angle");
                    Check(clone.ProxyGraphics!.SequenceEqual(CircularMutationProxy), "Clone lost unchanged proxy");
                    double changed = pair.expected == 0 ? double.Epsilon : 0;
                    if (start) clone.StartAngle = changed; else clone.EndAngle = changed;
                    SameDoubleBits(changed, start ? clone.StartAngle : clone.EndAngle, "Changed endpoint snapped");
                    Check(clone.ProxyGraphics == null, "Real endpoint edit retained proxy");
                    SameDoubleBits(pair.expected, arc.StartAngle, "Clone changed source start");
                    SameDoubleBits(pair.expected, arc.EndAngle, "Clone changed source end");
                    Check(arc.ProxyGraphics!.SequenceEqual(CircularMutationProxy), "Clone edit changed source proxy");
                    RawLinePointBits(new Vector3(1, 2, 3), arc.Center);
                    SameDoubleBits(4, arc.Radius, "Radius changed"); SameDoubleBits(2, arc.Thickness, "Thickness changed");
                    SameDoubleBits(epsilon, MathHelper.Epsilon, "Global epsilon changed");
                }
                finally { MathHelper.Epsilon = old; }
            });
        }
        foreach (double value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        foreach (bool start in new[] { false, true })
            Run($"arc-angle-fidelity/nonfinite/{ParameterBits(value)}/{start}", () => {
                var arc = new Arc(Vector3.Zero, 1, 30, 60) { ProxyGraphics = CircularMutationProxy };
                if (start) arc.StartAngle = value; else arc.EndAngle = value;
                Check(double.IsNaN(start ? arc.StartAngle : arc.EndAngle), "Existing nonfinite normalization changed");
                Check(arc.ProxyGraphics == null, "Nonfinite edit retained proxy");
            });
        foreach (DxfVersion version in SupportedVersions) foreach (bool binary in new[] { false, true })
        for (int placement = 0; placement < 3; placement++) foreach (bool tilted in new[] { false, true })
        foreach (bool rawInput in new[] { false, true })
        {
            int p = placement;
            Run($"arc-angle-fidelity/wire/{version}/{binary}/{p}/{tilted}/{rawInput}", () => {
                var doc = new DxfDocument(version); doc.Comments.Clear();
                var arcs = ArcAnglePairs.Select((a, i) => new Arc(new Vector3(1, 2, 3), 4,
                    rawInput ? 30 : a.start, rawInput ? 210 : a.end) {
                    Layer = new Layer("ANGLE_" + i.ToString("D2")),
                    Normal = tilted ? Vector3.UnitX : Vector3.UnitZ, Thickness = 2,
                    ProxyGraphics = CircularMutationProxy
                }).ToArray();
                if (p == 0) foreach (var arc in arcs) doc.Entities.Add(arc);
                else if (p == 1)
                {
                    doc.Layouts.Add(new Layout("ANGLE_PAPER"));
                    foreach (var arc in arcs) doc.Layouts["ANGLE_PAPER"].AssociatedBlock.Entities.Add(arc);
                }
                else doc.Entities.Add(new Insert(new Block("ANGLE_BLOCK", arcs)));
                doc.Entities.Add(new Line(new Vector3(17.25, -4.5, 2), new Vector3(18.5, 9.25, -3)));
                string[] handles = arcs.Select(a => a.Handle).ToArray();
                using var seed = new MemoryStream(); Check(doc.Save(seed, binary), "Angle source save");
                byte[] bytes = seed.ToArray();
                if (rawInput)
                {
                    var raw = LoadRaw(bytes);
                    for (int i = 0; i < ArcAnglePairs.Length; i++)
                    {
                        // WithRecord creates a new immutable snapshot; resolve each record in that snapshot.
                        string layer = "ANGLE_" + i.ToString("D2");
                        var record = raw.Sections.SelectMany(s => s.Records).Single(r => r.Name == "ARC"
                            && r.Tags.Any(t => t.Code == 8 && Equals(t.Value, layer)));
                        var pair = ArcAnglePairs[i];
                        raw = raw.WithRecord(record, record.Tags.Select(t => t.Code == 50 ? new DxfTag(50, pair.start)
                            : t.Code == 51 ? new DxfTag(51, pair.end) : t));
                    }
                    bytes = SaveRaw(raw, binary);
                }
                string stem = $"arc-angle-fidelity-{version}-{binary}-{p}-{tilted}-{rawInput}";
                File.WriteAllBytes(Path.Combine(ArtifactDirectory, stem + "-source.dxf"), bytes);
                double old = MathHelper.Epsilon;
                try
                {
                    // This deliberately exceeds all tiny input endpoints without modifying them.
                    MathHelper.Epsilon = 1e-3;
                    using var input = new MemoryStream(bytes);
                    doc = DxfDocument.Load(input) ?? throw new InvalidOperationException("Angle source load");
                    CheckArcAngleDocument(doc, p, tilted, handles);
                    foreach (bool output in new[] { false, true })
                    {
                        using var stream = new MemoryStream(); Check(doc.Save(stream, output), "Angle resave");
                        File.WriteAllBytes(Path.Combine(ArtifactDirectory, stem + $"-{output}.dxf"), stream.ToArray());
                        stream.Position = 0;
                        var copy = DxfDocument.Load(stream) ?? throw new InvalidOperationException("Angle output load");
                        CheckArcAngleDocument(copy, p, tilted, handles);
                    }
                    Check(input.CanRead, "Angle load closed input");
                    SameDoubleBits(1e-3, MathHelper.Epsilon, "IO changed epsilon");
                }
                finally { MathHelper.Epsilon = old; }
            });
        }
    }

    private static void CheckArcAngleDocument(DxfDocument doc, int placement, bool tilted, string[] handles)
    {
        Block block = placement == 0 ? doc.Layouts[Layout.ModelSpaceName].AssociatedBlock
            : placement == 1 ? doc.Layouts["ANGLE_PAPER"].AssociatedBlock : doc.Blocks["ANGLE_BLOCK"];
        var arcs = block.Entities.OfType<Arc>().OrderBy(a => a.Layer.Name, StringComparer.Ordinal).ToArray();
        Equal(ArcAnglePairs.Length, arcs.Length, "Angle entity inventory");
        for (int i = 0; i < arcs.Length; i++)
        {
            var arc = arcs[i]; var expected = ArcAnglePairs[i];
            SameDoubleBits(expected.start, arc.StartAngle, "Stored start bits");
            SameDoubleBits(expected.end, arc.EndAngle, "Stored end bits");
            Equal(handles[i], arc.Handle, "Angle entity identity");
            RawLinePointBits(new Vector3(1, 2, 3), arc.Center);
            RawLinePointBits(tilted ? Vector3.UnitX : Vector3.UnitZ, arc.Normal);
            SameDoubleBits(4, arc.Radius, "Stored radius"); SameDoubleBits(2, arc.Thickness, "Stored thickness");
            Check(arc.ProxyGraphics!.SequenceEqual(CircularMutationProxy), "Unchanged angle IO lost proxy");
            var clone = (Arc)arc.Clone();
            SameDoubleBits(expected.start, clone.StartAngle, "Loaded clone start");
            SameDoubleBits(expected.end, clone.EndAngle, "Loaded clone end");
            clone.StartAngle = expected.start;
            Check(clone.ProxyGraphics!.SequenceEqual(CircularMutationProxy), "Loaded no-op clone edit invalidated proxy");
        }
        Equal(0, doc.Objects.Validate().Count, "Angle graph validation");
        var line = doc.Entities.Lines.Single();
        RawLinePointBits(new Vector3(17.25, -4.5, 2), line.StartPoint);
        RawLinePointBits(new Vector3(18.5, 9.25, -3), line.EndPoint);
    }
}
