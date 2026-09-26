// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace netDxf.Entities
{
    public partial class Mesh
    {
        /// <summary>Inserts a WCS vertex and remaps all existing face and edge indices.</summary>
        /// <param name="index">Insertion slot from zero through the current vertex count.</param>
        /// <param name="position">Finite position of the new, initially unreferenced vertex.</param>
        /// <remarks>
        /// Validates the complete source topology and stages every remapping before publication.
        /// Existing faces and edges still reference the same geometric points. Their arrays, objects,
        /// list identities, order and crease bits are retained. Common proxy graphics are cleared.
        /// Uses the four-million-item editing budgets of SetFaceVertexIndex; the new count must fit.
        /// Direct collection mutation, cross-mesh aliases, concurrent edits and native subdivision
        /// evaluation are outside this entity-local operation. No face or edge is created.
        /// </remarks>
        public void InsertVertex(int index, Vector3 position)
        {
            if (index < 0 || index > this.vertexes.Count) throw new ArgumentOutOfRangeException(nameof(index));
            this.ValidateTopologyEditSource();
            ValidateVertexEditPosition(position, nameof(position));
            int count = this.vertexes.Count;
            if (count == MaximumMeshEditVertexCount)
                throw new NotSupportedException("Insertion would exceed the mesh vertex editing budget.");
            var map = new int[count];
            var positions = new Vector3[count + 1];
            for (int i = 0; i < count; i++)
            {
                map[i] = i < index ? i : i + 1;
                positions[map[i]] = this.vertexes[i];
            }
            positions[index] = position;
            this.PublishVertexReindex(map, positions);
        }

        /// <summary>Removes an unreferenced vertex and remaps all higher face and edge indices.</summary>
        /// <param name="index">Zero-based vertex to remove.</param>
        /// <remarks>
        /// A vertex used by any face corner or either endpoint of any edge cannot be removed.
        /// The entire request rejects before mutation; no dependent topology is silently deleted.
        /// Other coordinates and all face/edge identities, order and crease bits are preserved.
        /// Uses the same source validation and resource policy as InsertVertex. Clears common graphics
        /// on success. References in unknown/private payloads and other meshes are not interpreted.
        /// </remarks>
        public void RemoveVertexAt(int index)
        {
            if (index < 0 || index >= this.vertexes.Count) throw new ArgumentOutOfRangeException(nameof(index));
            this.ValidateTopologyEditSource();
            int count = this.vertexes.Count;
            var map = new int[count];
            var positions = new Vector3[count - 1];
            for (int i = 0; i < count; i++)
            {
                map[i] = i == index ? -1 : i < index ? i : i - 1;
                if (i != index) positions[map[i]] = this.vertexes[i];
            }
            this.PublishVertexReindex(map, positions);
        }

        /// <summary>Moves a vertex to a final list index while preserving face and edge geometry.</summary>
        /// <param name="index">Zero-based source index.</param>
        /// <param name="newIndex">Zero-based position in the final vertex list.</param>
        /// <remarks>
        /// All affected face and edge indices are remapped, not just the coordinate list.
        /// An equal-index request validates the source then preserves caches and enumerators.
        /// Other moves clear common graphics conservatively, even for coincident coordinates.
        /// Arrays or edge objects shared within this mesh are remapped exactly once by reference
        /// identity, without calling user equality/hash overrides. Cross-mesh sharing stays caller-managed.
        /// Uses the same source validation and resource policy as InsertVertex; no geometry is evaluated.
        /// </remarks>
        public void MoveVertex(int index, int newIndex)
        {
            int count = this.vertexes.Count;
            if (index < 0 || index >= count) throw new ArgumentOutOfRangeException(nameof(index));
            if (newIndex < 0 || newIndex >= count) throw new ArgumentOutOfRangeException(nameof(newIndex));
            this.ValidateTopologyEditSource();
            if (index == newIndex) return;
            var map = new int[count];
            var positions = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                int target = i == index ? newIndex : index < newIndex && i > index && i <= newIndex
                    ? i - 1 : index > newIndex && i >= newIndex && i < index ? i + 1 : i;
                map[i] = target;
                positions[target] = this.vertexes[i];
            }
            this.PublishVertexReindex(map, positions);
        }

        private void PublishVertexReindex(int[] map, Vector3[] positions)
        {
            // Stage each distinct storage object exactly once: the same array/edge can occur
            // in multiple list slots. Repeated in-place remapping would corrupt such aliases.
            var faces = new Dictionary<int[], int[]>();
            foreach (int[] face in this.faces)
            {
                if (faces.ContainsKey(face)) continue;
                var next = new int[face.Length];
                for (int i = 0; i < face.Length; i++) next[i] = ReindexedVertex(map, face[i]);
                faces.Add(face, next);
            }
            var seen = new HashSet<MeshEdge>(ReindexedEdgeComparer.Instance);
            var edges = new List<ReindexedEdge>();
            foreach (MeshEdge edge in this.edges)
                if (seen.Add(edge)) edges.Add(new ReindexedEdge(edge,
                    ReindexedVertex(map, edge.StartVertexIndex), ReindexedVertex(map, edge.EndVertexIndex)));

            // This is the final possible allocation. Failure leaves all logical state intact.
            // Publication uses only private lists, arrays and nonvirtual, prevalidated setters.
            if (this.vertexes.Capacity < positions.Length) this.vertexes.Capacity = positions.Length;
            this.vertexes.Clear();
            this.vertexes.AddRange(positions);
            foreach (var face in faces) Array.Copy(face.Value, face.Key, face.Key.Length);
            foreach (var edge in edges)
            {
                edge.Target.StartVertexIndex = edge.Start;
                edge.Target.EndVertexIndex = edge.End;
            }
            this.ClearProxyGraphics();
        }

        private static int ReindexedVertex(int[] map, int index)
        {
            int target = map[index];
            if (target < 0) throw new InvalidOperationException("Cannot remove a vertex referenced by a mesh face or edge.");
            return target;
        }

        private struct ReindexedEdge
        {
            internal readonly MeshEdge Target;
            internal readonly int Start, End;
            internal ReindexedEdge(MeshEdge target, int start, int end)
            { this.Target = target; this.Start = start; this.End = end; }
        }

        private sealed class ReindexedEdgeComparer : IEqualityComparer<MeshEdge>
        {
            internal static readonly ReindexedEdgeComparer Instance = new ReindexedEdgeComparer();
            public bool Equals(MeshEdge left, MeshEdge right) { return ReferenceEquals(left, right); }
            public int GetHashCode(MeshEdge edge) { return RuntimeHelpers.GetHashCode(edge); }
        }
    }
}
