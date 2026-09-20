// Copyright (c) netDxf contributors. Licensed under the MIT License.
using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static Face3D VisibilityFace(int flags)
        => new(new Vector3(1, 2, 3), new Vector3(4, 5, 6), new Vector3(7, 8, 9), new Vector3(10, 11, 12))
        { EdgeFlags = (Face3DEdgeFlags)flags };
    private static readonly byte[] VisibilityProxy = { 1, 3, 7, 11 };

    private static void RegisterFace3DEdgeVisibilityTests()
    {
        for (int before = 0; before < 16; before++) for (int after = 0; after < 16; after++)
        {
            int a = before, b = after;
            Run($"face-edge-visibility/transition/{a}/{b}", () =>
            {
                var face = VisibilityFace(a); face.ProxyGraphics = VisibilityProxy;
                var points = VertexAffinePoints(face); var normal = face.Normal; var color = face.Color;
                face.EdgeFlags = (Face3DEdgeFlags)b;
                Equal((Face3DEdgeFlags)b, face.EdgeFlags, "Visibility assignment failed");
                if (a == b) Check(face.ProxyGraphics != null && face.ProxyGraphics.SequenceEqual(VisibilityProxy), "No-op cleared proxy");
                else Check(face.ProxyGraphics == null, "Visibility edit retained stale proxy");
                for (int i = 0; i < 4; i++) RawLinePointBits(points[i], VertexAffinePoints(face)[i]);
                RawLinePointBits(normal, face.Normal); Check(ReferenceEquals(color, face.Color), "Visibility edit changed color identity");
            });
        }
        foreach (int bad in new[] { -1, -16, 16, 32, 255, 32768, 65536, int.MaxValue, int.MinValue })
            foreach (bool owned in new[] { false, true })
                Run($"face-edge-visibility/reject/{bad}/{owned}", () =>
                {
                    var face = VisibilityFace(5); var doc = new DxfDocument(); if (owned) doc.Entities.Add(face);
                    var owner = face.Owner; string handle = face.Handle; var points = VertexAffinePoints(face);
                    face.ProxyGraphics = VisibilityProxy;
                    ArgumentOutOfRangeException? error = null;
                    try { face.EdgeFlags = (Face3DEdgeFlags)bad; } catch (ArgumentOutOfRangeException e) { error = e; }
                    Check(error != null, "Undefined edge bits were accepted and can be narrowed on output");
                    Equal("value", error!.ParamName, "Exception argument"); Equal((Face3DEdgeFlags)5, face.EdgeFlags, "Failed assignment changed flags");
                    Check(face.ProxyGraphics != null && face.ProxyGraphics.SequenceEqual(VisibilityProxy), "Failed assignment changed proxy");
                    Check(ReferenceEquals(owner, face.Owner) && handle == face.Handle, "Failed assignment changed ownership");
                    for (int i = 0; i < 4; i++) RawLinePointBits(points[i], VertexAffinePoints(face)[i]);
                });
        for (int mask = 0; mask < 16; mask++)
        {
            int m = mask;
            Run($"face-edge-visibility/clone/{m}", () =>
            {
                var source = VisibilityFace(m); source.ProxyGraphics = VisibilityProxy;
                var copy = (Face3D)source.Clone(); Equal(source.EdgeFlags, copy.EdgeFlags, "Clone lost edge flags");
                Check(copy.ProxyGraphics != null && copy.ProxyGraphics.SequenceEqual(VisibilityProxy), "Clone lost valid proxy");
                copy.EdgeFlags = (Face3DEdgeFlags)(m ^ 1);
                Equal((Face3DEdgeFlags)m, source.EdgeFlags, "Clone edit changed source flags");
                Check(source.ProxyGraphics != null && source.ProxyGraphics.SequenceEqual(VisibilityProxy), "Clone edit changed source proxy");
                Check(copy.ProxyGraphics == null, "Clone edit retained stale proxy");
            });
        }
        foreach (DxfVersion version in SupportedVersions) foreach (bool binary in new[] { false, true })
            for (int mask = 0; mask < 16; mask++)
            {
                int m = mask;
                Run($"face-edge-visibility/wire/{version}/{binary}/{m}", () =>
                {
                    var face = VisibilityFace(m ^ 1); face.ProxyGraphics = VisibilityProxy; face.EdgeFlags = (Face3DEdgeFlags)m;
                    Check(face.ProxyGraphics == null, "Geometry-changing edge edit retained proxy before save");
                    var doc = new DxfDocument(version); doc.Comments.Clear(); doc.Entities.Add(face);
                    using var stream = new MemoryStream(); Check(doc.Save(stream, binary), "Visibility save failed");
                    byte[] bytes = stream.ToArray(); var raw = LoadRaw(bytes); var record = RawFaceRecord(raw);
                    Equal((short)m, (short)record.Tags.Single(t => t.Code == 70).Value, "Stored flags were narrowed incorrectly");
                    Check(!record.Tags.Any(t => t.Code == 92 || t.Code == 160 || t.Code == 310), "Stale proxy packet was written");
                    stream.Position = 0; var loaded = DxfDocument.Load(stream)!.Entities.Faces3D.Single();
                    Equal((Face3DEdgeFlags)m, loaded.EdgeFlags, "Restored edge flags differ");
                    Check(loaded.ProxyGraphics == null, "Restored stale proxy");
                    File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"face-edge-visibility-{version}-{binary}-{m}.dxf"), bytes);
                });
            }
    }
}
