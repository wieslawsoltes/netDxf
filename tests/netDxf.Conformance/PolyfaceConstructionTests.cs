// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System.Collections;
using netDxf;
using netDxf.Entities;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private sealed class PolyfaceOnce<T> : IEnumerable<T>
    {
        private readonly IEnumerable<T> source;
        internal int Starts, Reads, Disposals;
        internal PolyfaceOnce(IEnumerable<T> source) { this.source = source; }
        public IEnumerator<T> GetEnumerator()
        {
            if (++Starts != 1) throw new InvalidOperationException("Source enumerated more than once");
            return Enumerate().GetEnumerator();
        }
        private IEnumerable<T> Enumerate()
        {
            try { foreach (T item in source) { Reads++; yield return item; } }
            finally { Disposals++; }
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private static IEnumerable<short[]> PolyfaceReusedBuffer(int count)
    {
        var buffer = new short[3];
        for (int i = 0; i < count; i++)
        {
            buffer[0] = (short)(i % 2 == 0 ? 1 : -1);
            buffer[1] = 2; buffer[2] = 3;
            yield return buffer;
        }
    }

    private static void RegisterPolyfaceConstructionTests()
    {
        foreach (int count in new[] { 1, 2, 3, 17, 257 })
        {
            Run($"polyface-construction/arrays/{count}", () =>
            {
                var vertices = new PolyfaceOnce<Vector3>(new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY });
                var source = new PolyfaceOnce<short[]>(PolyfaceReusedBuffer(count));
                var mesh = new PolyfaceMesh(vertices, source);
                Equal(count, mesh.Faces.Count, "Face count");
                Equal(1, source.Starts, "Face enumerations"); Equal(count, source.Reads, "Face reads");
                Equal(1, source.Disposals, "Face disposal"); Equal(1, vertices.Starts, "Vertex enumerations");
                Equal(1, vertices.Disposals, "Vertex disposal");
                for (int i = 0; i < count; i++)
                {
                    Equal((short)(i % 2 == 0 ? 1 : -1), mesh.Faces[i].VertexIndexes[0], "Reused-buffer snapshot");
                    if (i != 0) Check(!ReferenceEquals(mesh.Faces[i-1].VertexIndexes, mesh.Faces[i].VertexIndexes), "Face index arrays alias");
                }
            });
            Run($"polyface-construction/objects/{count}", () =>
            {
                var faces = Enumerable.Range(0, count).Select(_ => new PolyfaceMeshFace(new short[] { 1, 2, 3 })).ToArray();
                var source = new PolyfaceOnce<PolyfaceMeshFace>(faces);
                var vertices = new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY };
                var mesh = new PolyfaceMesh(vertices, source);
                Equal(1, source.Starts, "Object enumerations"); Equal(count, source.Reads, "Object reads");
                Equal(1, source.Disposals, "Object disposal");
                Check(faces.SequenceEqual(mesh.Faces), "Existing face-identity contract changed");
                vertices[0] = Vector3.UnitZ; Equal(Vector3.Zero, mesh.Vertexes[0], "Input vertex array alias");
            });
        }
        foreach (bool objects in new[] { false, true })
        {
            Run($"polyface-construction/empty/{objects}", () =>
            {
                ArgumentException? failure = null;
                try
                {
                    if (objects) _ = new PolyfaceMesh(new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY }, Array.Empty<PolyfaceMeshFace>());
                    else _ = new PolyfaceMesh(new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY }, Array.Empty<short[]>());
                }
                catch (ArgumentException ex) { failure = ex; }
                Check(failure is ArgumentOutOfRangeException, "Empty faces must reject");
                Equal("faces", failure!.ParamName, "Empty collection parameter");
            });
            Run($"polyface-construction/null/{objects}", () =>
            {
                if (objects) Throws<ArgumentNullException>(() => new PolyfaceMesh(new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY }, (IEnumerable<PolyfaceMeshFace>)null!));
                else Throws<ArgumentNullException>(() => new PolyfaceMesh(new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY }, (IEnumerable<short[]>)null!));
            });
        }
        Run("polyface-construction/dispose-on-invalid-index", () =>
        {
            var faces = new PolyfaceOnce<short[]>(new[] { new short[] { 1, 2, 3 }, new short[] { 0 } });
            Throws<ArgumentException>(() => new PolyfaceMesh(new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY }, faces));
            Equal(1, faces.Starts, "Failed construction enumerations"); Equal(1, faces.Disposals, "Failed construction disposal");
        });
        Run("polyface-construction/no-events-on-failure", () =>
        {
            var face = new PolyfaceMeshFace(new short[] { 1, 2, 3 });
            var invalid = new PolyfaceMeshFace(new short[] { 1, 2, 4 });
            var field = typeof(PolyfaceMeshFace).GetField("LayerChanged", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            object? before = field.GetValue(face);
            Throws<ArgumentOutOfRangeException>(() => new PolyfaceMesh(new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY }, new[] { face, invalid }));
            Check(ReferenceEquals(before, field.GetValue(face)), "Failed construction left a face callback attached");
        });
        Run("polyface-construction/array-isolation", () =>
        {
            var indices = new short[] { 1, -2, 3 }; var source = new[] { indices };
            var mesh = new PolyfaceMesh(new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY }, source);
            indices[0] = 3; source[0] = new short[] { 2 };
            Check(mesh.Faces[0].VertexIndexes.SequenceEqual(new short[] { 1, -2, 3 }), "Input arrays are not snapshotted");
        });
    }
}
