// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;

namespace netDxf.Entities
{
    public partial class Mesh
    {
        // Resource policy for these edits, not a DXF format cardinality limit.
        private const int MaximumTopologyEditItems = 4000000;

        /// <summary>Replaces one zero-based vertex index in an existing face.</summary>
        /// <param name="faceIndex">Zero-based face index.</param>
        /// <param name="cornerIndex">Zero-based slot in that face.</param>
        /// <param name="vertexIndex">Replacement index in Vertexes.</param>
        /// <remarks>
        /// Validates current coordinates, face packets, edge indices and crease values before mutation.
        /// Face arity and all collection/array identities are retained. A changed face shared by
        /// multiple slots in this mesh rejects rather than changing additional faces through aliasing.
        /// Exact no-ops preserve common graphics; changed data clears it. No subdivision, winding,
        /// manifoldness or cross-mesh alias tracking is performed. Direct collection edits remain
        /// caller-managed. The editing budget is four million vertices, face-list items and edges.
        /// </remarks>
        public void SetFaceVertexIndex(int faceIndex, int cornerIndex, int vertexIndex)
        {
            if (faceIndex < 0 || faceIndex >= this.faces.Count)
                throw new ArgumentOutOfRangeException(nameof(faceIndex));
            this.ValidateTopologyEditSource();
            int[] face = this.faces[faceIndex];
            if (cornerIndex < 0 || cornerIndex >= face.Length)
                throw new ArgumentOutOfRangeException(nameof(cornerIndex));
            this.ValidateTopologyVertexIndex(vertexIndex, nameof(vertexIndex));
            if (face[cornerIndex] == vertexIndex) return;
            for (int i = 0; i < this.faces.Count; i++)
                if (i != faceIndex && ReferenceEquals(face, this.faces[i]))
                    throw new InvalidOperationException("The selected face array is shared by multiple mesh faces.");
            face[cornerIndex] = vertexIndex;
            this.ClearProxyGraphics();
        }

        /// <summary>Replaces both endpoints of an existing edge without replacing its object.</summary>
        /// <param name="edgeIndex">Zero-based edge index.</param>
        /// <param name="startVertexIndex">Replacement start index in Vertexes.</param>
        /// <param name="endVertexIndex">Replacement end index in Vertexes.</param>
        /// <remarks>
        /// Both candidates and the current topology validate before either endpoint changes.
        /// Counts, coordinates, face arrays, edge objects and crease values are preserved.
        /// A changed edge object shared within this mesh rejects; exact no-ops preserve graphics.
        /// Uses the same source validation and resource policy as SetFaceVertexIndex.
        /// This is local data editing, not automatic repair or subdivision evaluation.
        /// </remarks>
        public void SetEdgeVertexIndices(int edgeIndex, int startVertexIndex, int endVertexIndex)
        {
            if (edgeIndex < 0 || edgeIndex >= this.edges.Count)
                throw new ArgumentOutOfRangeException(nameof(edgeIndex));
            this.ValidateTopologyEditSource();
            this.ValidateTopologyVertexIndex(startVertexIndex, nameof(startVertexIndex));
            this.ValidateTopologyVertexIndex(endVertexIndex, nameof(endVertexIndex));
            MeshEdge edge = this.edges[edgeIndex];
            if (edge.StartVertexIndex == startVertexIndex && edge.EndVertexIndex == endVertexIndex) return;
            this.ValidateUniqueTopologyEdge(edgeIndex);
            edge.StartVertexIndex = startVertexIndex;
            edge.EndVertexIndex = endVertexIndex;
            this.ClearProxyGraphics();
        }

        /// <summary>Sets an existing edge's crease through its owning mesh.</summary>
        /// <param name="edgeIndex">Zero-based edge index.</param>
        /// <param name="crease">Finite crease value; negative values normalize to -1 as in MeshEdge.</param>
        /// <remarks>
        /// Validates the complete current topology before mutation. Compares the normalized
        /// candidate's exact binary64 bits, including signed zero. A true no-op retains graphics.
        /// Changed aliases within this mesh reject. No edge or geometry object is replaced.
        /// Uses the same budgets as SetFaceVertexIndex. Cross-mesh sharing, concurrent mutation
        /// and direct MeshEdge writes are outside this entity-local cache-invalidation contract.
        /// </remarks>
        public void SetEdgeCrease(int edgeIndex, double crease)
        {
            if (edgeIndex < 0 || edgeIndex >= this.edges.Count)
                throw new ArgumentOutOfRangeException(nameof(edgeIndex));
            this.ValidateTopologyEditSource();
            if (double.IsNaN(crease) || double.IsInfinity(crease))
                throw new ArgumentException("The replacement crease must be finite.", nameof(crease));
            double candidate = crease < 0.0 ? -1.0 : crease;
            MeshEdge edge = this.edges[edgeIndex];
            if (BitConverter.DoubleToInt64Bits(edge.Crease) == BitConverter.DoubleToInt64Bits(candidate)) return;
            this.ValidateUniqueTopologyEdge(edgeIndex);
            edge.Crease = candidate;
            this.ClearProxyGraphics();
        }

        private void ValidateUniqueTopologyEdge(int index)
        {
            for (int i = 0; i < this.edges.Count; i++)
                if (i != index && ReferenceEquals(this.edges[i], this.edges[index]))
                    throw new InvalidOperationException("The selected edge object is shared by multiple mesh edges.");
        }

        private void ValidateTopologyVertexIndex(int index, string parameterName)
        {
            if (index < 0 || index >= this.vertexes.Count)
                throw new ArgumentOutOfRangeException(parameterName, "A topology index references a missing mesh vertex.");
        }

        private void ValidateTopologyEditSource()
        {
            if (this.faces.Count > MaximumTopologyEditItems || this.edges.Count > MaximumTopologyEditItems)
                throw new NotSupportedException("The topology edit exceeds the four-million-item budget.");
            // Check serialized lengths before scanning aliased arrays repeatedly.
            long faceItems = this.faces.Count;
            for (int i = 0; i < this.faces.Count; i++)
            {
                int[] face = this.faces[i];
                if (face == null || face.Length < 3)
                    throw new InvalidOperationException("Every mesh face must contain at least three indices.");
                faceItems += face.Length;
                if (faceItems > MaximumTopologyEditItems)
                    throw new NotSupportedException("The topology edit exceeds the four-million-face-list-item budget.");
            }
            this.ValidateVertexEditSource();
            foreach (int[] face in this.faces)
                foreach (int index in face) this.ValidateTopologyVertexIndex(index, "Faces");
            foreach (MeshEdge edge in this.edges)
            {
                if (edge == null) throw new InvalidOperationException("A mesh edge is null.");
                this.ValidateTopologyVertexIndex(edge.StartVertexIndex, "Edges");
                this.ValidateTopologyVertexIndex(edge.EndVertexIndex, "Edges");
                if (double.IsNaN(edge.Crease) || double.IsInfinity(edge.Crease))
                    throw new InvalidOperationException("Every existing mesh crease must be finite.");
            }
        }
    }
}
