// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;

namespace netDxf.Entities
{
    public partial class PolyfaceMesh
    {
        /// <summary>Replaces a coordinate without replacing its retained VERTEX identity or metadata.</summary>
        /// <param name="index">Zero-based coordinate index.</param>
        /// <param name="position">Finite replacement position.</param>
        /// <remarks>The complete current mesh is validated before mutation. Retained meshes must be registered
        /// in their source document/profile; qualified detached clones must be adopted first. Exact component
        /// bits, including signed zero, decide whether parent proxy graphics are invalidated. No handle is allocated.</remarks>
        public void SetVertex(int index, Vector3 position)
        {
            if (index < 0 || index >= this.vertexes.Length) throw new ArgumentOutOfRangeException(nameof(index));
            if (double.IsNaN(position.X) || double.IsInfinity(position.X)
                || double.IsNaN(position.Y) || double.IsInfinity(position.Y)
                || double.IsNaN(position.Z) || double.IsInfinity(position.Z))
                throw new ArgumentException("Polyface coordinates must be finite.", nameof(position));
            this.ValidateExplicitEdit();
            Vector3 previous = this.vertexes[index];
            if (!PrimitiveGeometryMutation.Assign(ref previous, position)) return;
            this.vertexes[index] = position;
            this.ClearProxyGraphics();
        }

        /// <summary>Changes one signed, one-based face index while retaining the face object, slots and record identity.</summary>
        /// <param name="faceIndex">Zero-based face index.</param>
        /// <param name="cornerIndex">Zero-based slot in that face's existing index array.</param>
        /// <param name="vertexIndex">Signed one-based coordinate index, or zero to terminate the active face.</param>
        /// <remarks>All active candidate indices must resolve and at least one must remain. Values after the first
        /// zero are ignored geometrically but retained as stored slots, matching the existing face model. Changing
        /// a slot clears parent graphics conservatively, including changes after a terminator. No array, face,
        /// VERTEX identity, metadata or dependency is replaced. Direct VertexIndexes array edits remain unobserved.</remarks>
        public void SetFaceVertexIndex(int faceIndex, int cornerIndex, short vertexIndex)
        {
            PolyfaceMeshFace face = this.GetEditableFace(faceIndex, cornerIndex);
            this.ValidateExplicitEdit();
            short[] candidate = (short[])face.VertexIndexes.Clone();
            candidate[cornerIndex] = vertexIndex;
            // Construction and validation have no live-object callbacks or mutations.
            new PolyfaceMeshFace(candidate).ValidateVertexIndexes(this.vertexes.Length);
            if (face.VertexIndexes[cornerIndex] == vertexIndex) return;
            face.VertexIndexes[cornerIndex] = vertexIndex;
            this.ClearProxyGraphics();
        }

        /// <summary>Sets visibility of the edge starting at an active face corner without changing its vertex.</summary>
        /// <param name="faceIndex">Zero-based face index.</param>
        /// <param name="cornerIndex">Zero-based active face corner.</param>
        /// <param name="visible">True for a visible edge; false for an invisible edge.</param>
        /// <remarks>An inactive slot rejects. Making signed index -32768 visible also rejects, since +32768
        /// cannot be represented in a DXF signed 16-bit face index. Refusal and exact no-op preserve all state.</remarks>
        public void SetFaceEdgeVisibility(int faceIndex, int cornerIndex, bool visible)
        {
            PolyfaceMeshFace face = this.GetEditableFace(faceIndex, cornerIndex);
            this.ValidateExplicitEdit();
            if (cornerIndex >= face.ValidateVertexIndexes(this.vertexes.Length))
                throw new ArgumentOutOfRangeException(nameof(cornerIndex), "An edge must begin at an active face corner.");
            int magnitude = Math.Abs((int)face.VertexIndexes[cornerIndex]);
            if (visible && magnitude > short.MaxValue)
                throw new NotSupportedException("The positive face index cannot be represented as a signed 16-bit value.");
            this.SetFaceVertexIndex(faceIndex, cornerIndex, (short)(visible ? magnitude : -magnitude));
        }

        private PolyfaceMeshFace GetEditableFace(int faceIndex, int cornerIndex)
        {
            if (faceIndex < 0 || faceIndex >= this.faces.Length) throw new ArgumentOutOfRangeException(nameof(faceIndex));
            PolyfaceMeshFace face = this.faces[faceIndex];
            if (face == null) throw new InvalidOperationException("The mesh contains a null face.");
            if (cornerIndex < 0 || cornerIndex >= face.VertexIndexes.Length) throw new ArgumentOutOfRangeException(nameof(cornerIndex));
            return face;
        }

        private void ValidateExplicitEdit()
        {
            this.ValidatePolyfaceNormal();
            this.ValidateRetainedGeometry();
            if (!this.HasStoredRecords) return;
            DxfDocument document = this.storedRecordDocument;
            if (document == null || this.Handle == null || !ReferenceEquals(document.GetObjectByHandle(this.Handle), this)
                || this.Owner == null || !ReferenceEquals(document.GetObjectByHandle(this.Owner.Handle), this.Owner))
                throw new InvalidOperationException("Retained polyface edits require a registered parent; adopt a qualified clone before editing.");
            this.ValidateStoredRecords(document, true);
        }
    }
}
