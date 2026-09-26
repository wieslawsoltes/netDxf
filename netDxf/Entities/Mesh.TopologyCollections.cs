// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;

namespace netDxf.Entities
{
    public partial class Mesh
    {
        /// <summary>Inserts a face using a private copy of its ordered, zero-based vertex indices.</summary>
        /// <param name="index">Insertion position from zero through the existing face count.</param>
        /// <param name="vertexIndices">At least three indices into Vertexes, in the desired winding order.</param>
        /// <remarks>
        /// Validates the complete current topology and the final serialized face-list budget before
        /// mutation. Copies the supplied array, including when it is an existing face of this mesh.
        /// Existing face arrays, edge objects, coordinates, lists, resources and headers are retained.
        /// Clears common graphics on success. Does not infer edges, repair degeneracies, evaluate
        /// subdivision or interpret private subentity references. Uses the four-million-item editing
        /// budgets of SetFaceVertexIndex; these are operation limits, not DXF format limits.
        /// Direct/concurrent mutation and cross-mesh dependencies remain caller-managed.
        /// </remarks>
        public void InsertFace(int index, params int[] vertexIndices)
        {
            if (index < 0 || index > this.faces.Count) throw new ArgumentOutOfRangeException(nameof(index));
            if (vertexIndices == null) throw new ArgumentNullException(nameof(vertexIndices));
            this.ValidateTopologyEditSource();
            if (vertexIndices.Length < 3)
                throw new ArgumentException("A mesh face must contain at least three vertex indices.", nameof(vertexIndices));
            long items = (long)this.faces.Count + 1 + vertexIndices.Length;
            foreach (int[] face in this.faces) items += face.Length;
            if (items > MaximumTopologyEditItems)
                throw new NotSupportedException("Insertion would exceed the mesh face-list editing budget.");
            int[] candidate = (int[])vertexIndices.Clone();
            foreach (int vertex in candidate) this.ValidateTopologyVertexIndex(vertex, nameof(vertexIndices));
            // Allocate before publication. Insert then uses only private List storage.
            ReserveTopologySlot(this.faces);
            this.faces.Insert(index, candidate);
            this.ClearProxyGraphics();
        }

        /// <summary>Removes one face slot without deleting coordinates or explicit edge records.</summary>
        /// <param name="index">Zero-based face slot to remove.</param>
        /// <remarks>
        /// Validates the entire source before removal, including the selected face. This is not a
        /// repair API for malformed source topology. Other face-array identities and their order
        /// remain intact; removing one alias does not change an array still used by another slot.
        /// Does not infer orphan topology or interpret private subentity references. Clears common
        /// graphics. Uses the same resource and concurrency boundaries as InsertFace.
        /// </remarks>
        public void RemoveFaceAt(int index)
        {
            if (index < 0 || index >= this.faces.Count) throw new ArgumentOutOfRangeException(nameof(index));
            this.ValidateTopologyEditSource();
            this.faces.RemoveAt(index);
            this.ClearProxyGraphics();
        }

        /// <summary>Moves a face array to its final list position without changing its winding.</summary>
        /// <param name="index">Zero-based source face slot.</param>
        /// <param name="newIndex">Zero-based position in the final face list.</param>
        /// <remarks>
        /// Preserves all face arrays and other topology. An equal-index request validates the
        /// source then preserves graphics and enumerators. A different-index move clears graphics
        /// even when the slots alias or have identical values. No user equality callbacks run.
        /// Uses the same resource, private-reference and concurrency boundaries as InsertFace.
        /// </remarks>
        public void MoveFace(int index, int newIndex)
        {
            if (index < 0 || index >= this.faces.Count) throw new ArgumentOutOfRangeException(nameof(index));
            if (newIndex < 0 || newIndex >= this.faces.Count) throw new ArgumentOutOfRangeException(nameof(newIndex));
            this.ValidateTopologyEditSource();
            if (index == newIndex) return;
            int[] face = this.faces[index];
            this.faces.RemoveAt(index);
            this.faces.Insert(newIndex, face);
            this.ClearProxyGraphics();
        }

        /// <summary>Inserts an explicit edge, keeping its endpoints and crease value together.</summary>
        /// <param name="index">Insertion position from zero through the existing edge count.</param>
        /// <param name="startVertexIndex">Zero-based start vertex index.</param>
        /// <param name="endVertexIndex">Zero-based end vertex index.</param>
        /// <param name="crease">Finite crease; negative values normalize to -1 as in MeshEdge. Defaults to zero.</param>
        /// <remarks>
        /// Both endpoints, the crease, the current source and the final edge budget validate before
        /// publication. Creates a new MeshEdge rather than borrowing an external object. Existing
        /// edge objects, crease bits, coordinates, faces and common identities are retained.
        /// Does not add a face or infer manifoldness. Clears common graphics. Uses the same editing
        /// budgets and private-reference/concurrency boundaries as InsertFace.
        /// </remarks>
        public void InsertEdge(int index, int startVertexIndex, int endVertexIndex, double crease = 0.0)
        {
            if (index < 0 || index > this.edges.Count) throw new ArgumentOutOfRangeException(nameof(index));
            this.ValidateTopologyEditSource();
            this.ValidateTopologyVertexIndex(startVertexIndex, nameof(startVertexIndex));
            this.ValidateTopologyVertexIndex(endVertexIndex, nameof(endVertexIndex));
            if (double.IsNaN(crease) || double.IsInfinity(crease))
                throw new ArgumentException("The new crease must be finite.", nameof(crease));
            if (this.edges.Count == MaximumTopologyEditItems)
                throw new NotSupportedException("Insertion would exceed the mesh edge editing budget.");
            MeshEdge candidate = new MeshEdge(startVertexIndex, endVertexIndex, crease);
            ReserveTopologySlot(this.edges);
            this.edges.Insert(index, candidate);
            this.ClearProxyGraphics();
        }

        /// <summary>Removes one explicit edge and its crease value without changing faces or vertices.</summary>
        /// <param name="index">Zero-based edge slot to remove.</param>
        /// <remarks>
        /// Validates the complete source first. Other edge identities/order are preserved, including
        /// aliases of the removed object in other slots. Does not remove a face or repair its topology.
        /// Clears common graphics. Uses the same resource, private-reference and concurrency
        /// boundaries as InsertFace. No user equality callbacks run.
        /// </remarks>
        public void RemoveEdgeAt(int index)
        {
            if (index < 0 || index >= this.edges.Count) throw new ArgumentOutOfRangeException(nameof(index));
            this.ValidateTopologyEditSource();
            this.edges.RemoveAt(index);
            this.ClearProxyGraphics();
        }

        /// <summary>Moves an edge object and its crease to a final position in the explicit edge list.</summary>
        /// <param name="index">Zero-based source edge slot.</param>
        /// <param name="newIndex">Zero-based position in the final edge list.</param>
        /// <remarks>
        /// Validates the complete source first. Endpoints and crease bits stay with their object;
        /// faces, coordinates and all list identities are retained. Equal-index moves preserve
        /// graphics and enumerators; other moves clear graphics, even for aliased slots.
        /// Uses the same resource, private-reference and concurrency boundaries as InsertFace.
        /// No user equality callbacks run.
        /// </remarks>
        public void MoveEdge(int index, int newIndex)
        {
            if (index < 0 || index >= this.edges.Count) throw new ArgumentOutOfRangeException(nameof(index));
            if (newIndex < 0 || newIndex >= this.edges.Count) throw new ArgumentOutOfRangeException(nameof(newIndex));
            this.ValidateTopologyEditSource();
            if (index == newIndex) return;
            MeshEdge edge = this.edges[index];
            this.edges.RemoveAt(index);
            this.edges.Insert(newIndex, edge);
            this.ClearProxyGraphics();
        }
        private static void ReserveTopologySlot<T>(List<T> list)
        {
            if (list.Capacity > list.Count) return;
            // Preserve amortized capacity growth without requiring newer List APIs.
            int capacity = (int)Math.Min(MaximumTopologyEditItems, Math.Max(4L, (long)list.Capacity * 2));
            list.Capacity = Math.Max(list.Count + 1, capacity);
        }
    }
}
