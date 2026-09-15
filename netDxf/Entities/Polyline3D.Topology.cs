using System;

namespace netDxf.Entities
{
    public partial class Polyline3D
    {
        /// <summary>Inserts a point and, for a retained sequence, a new owned VERTEX identity.</summary>
        /// <param name="index">The insertion index, from zero through Vertexes.Count.</param>
        /// <param name="position">The finite position of the inserted point.</param>
        /// <remarks>
        /// Retained sequences must be registered in their source document and keep their source
        /// DXF profile. The new record uses the current parent layer and ordinary POLYLINE ownership;
        /// it does not copy optional fields or metadata from another vertex. Unaffected records and
        /// SEQEND keep their identities. No handle is allocated when validation rejects the edit.
        /// </remarks>
        public void InsertVertex(int index, Vector3 position)
        {
            if (index < 0 || index > this.vertexes.Count) throw new ArgumentOutOfRangeException(nameof(index));
            ValidateTopologyPosition(position, nameof(position));
            if (this.vertexes.Count >= 65536) throw new NotSupportedException("A polyline topology edit cannot exceed 65,536 vertices.");
            DxfDocument document = this.ValidateTopologyEdit();
            if (!this.HasStoredRecords) { this.vertexes.Insert(index, position); return; }
            Polyline3DRecord inserted = document.PreparePolylineVertexInsertion(this, position);
            // Reserve list storage before committing registration. The new record has no user
            // metadata or callbacks; registration only installs internal metadata bookkeeping.
            if (this.vertexes.Capacity < this.vertexes.Count + 1) this.vertexes.Capacity = this.vertexes.Count + 1;
            if (this.storedVertexRecords.Capacity < this.storedVertexRecords.Count + 1) this.storedVertexRecords.Capacity = this.storedVertexRecords.Count + 1;
            document.RegisterPolylineVertexInsertion(inserted);
            this.vertexes.Insert(index, position);
            this.storedVertexRecords.Insert(index, inserted);
        }

        /// <summary>Removes a point and its retained VERTEX after checking incoming dependencies.</summary>
        /// <param name="index">The zero-based index to remove.</param>
        /// <remarks>
        /// Retained sequences must be registered in their source document. Incoming semantic
        /// references and private or owned record payloads reject before mutation. Removal does
        /// not cascade to referenced objects. A removed record retains its retired handle for
        /// inspection, has no owner and cannot be inserted or adopted again. A retained sequence
        /// must keep at least two points, as required by the existing writer. SEQEND stays stable.
        /// </remarks>
        public void RemoveVertexAt(int index)
        {
            if (index < 0 || index >= this.vertexes.Count) throw new ArgumentOutOfRangeException(nameof(index));
            DxfDocument document = this.ValidateTopologyEdit();
            if (!this.HasStoredRecords) { this.vertexes.RemoveAt(index); return; }
            if (this.vertexes.Count <= 2) throw new InvalidOperationException("A retained 3D polyline must keep at least two vertices for output.");
            Polyline3DRecord removed = this.storedVertexRecords[index];
            document.ValidatePolylineVertexRemoval(removed);
            document.UnregisterPolylineVertexRemoval(removed);
            this.vertexes.RemoveAt(index);
            this.storedVertexRecords.RemoveAt(index);
            removed.Owner = null;
            removed.IsRemoved = true;
        }

        /// <summary>Moves a point and its retained VERTEX identity to a final zero-based index.</summary>
        /// <param name="fromIndex">The current zero-based index.</param>
        /// <param name="toIndex">The final zero-based index after moving the point.</param>
        /// <remarks>
        /// Both indices must identify existing points. Metadata follows the moved identity and
        /// no handles are allocated. Equal indices still validate the current sequence. Retained
        /// sequences must be registered in their source document and keep their source profile.
        /// </remarks>
        public void MoveVertex(int fromIndex, int toIndex)
        {
            if (fromIndex < 0 || fromIndex >= this.vertexes.Count) throw new ArgumentOutOfRangeException(nameof(fromIndex));
            if (toIndex < 0 || toIndex >= this.vertexes.Count) throw new ArgumentOutOfRangeException(nameof(toIndex));
            this.ValidateTopologyEdit();
            if (fromIndex == toIndex) return;
            Vector3 point = this.vertexes[fromIndex];
            this.vertexes.RemoveAt(fromIndex);
            this.vertexes.Insert(toIndex, point);
            if (this.HasStoredRecords)
            {
                Polyline3DRecord record = this.storedVertexRecords[fromIndex];
                this.storedVertexRecords.RemoveAt(fromIndex);
                this.storedVertexRecords.Insert(toIndex, record);
            }
        }

        private DxfDocument ValidateTopologyEdit()
        {
            if (this.vertexes.Count > 65536) throw new NotSupportedException("A polyline topology edit cannot exceed 65,536 vertices.");
            foreach (Vector3 position in this.vertexes) ValidateTopologyPosition(position, "Vertexes");
            if (!this.HasStoredRecords) return null;
            DxfDocument document = this.storedRecordDocument;
            if (document == null || this.Handle == null || !ReferenceEquals(document.GetObjectByHandle(this.Handle), this)
                || this.Owner == null || !ReferenceEquals(document.GetObjectByHandle(this.Owner.Handle), this.Owner))
                throw new InvalidOperationException("Retained polyline topology edits require a registered parent in its source document; adopt a qualified clone before editing.");
            this.ValidateStoredRecords(document, true);
            return document;
        }

        private static void ValidateTopologyPosition(Vector3 position, string parameter)
        {
            if (double.IsNaN(position.X) || double.IsInfinity(position.X)
                || double.IsNaN(position.Y) || double.IsInfinity(position.Y)
                || double.IsNaN(position.Z) || double.IsInfinity(position.Z))
                throw new ArgumentException("Polyline topology coordinates must be finite.", parameter);
        }
    }
}
