using System;
using System.Collections.Generic;
using System.Linq;
using netDxf.Blocks;

namespace netDxf.Entities
{
    public partial class Polyline3D
    {
        private List<Polyline3DRecord> storedVertexRecords;
        private IReadOnlyList<Polyline3DRecord> storedVertexView = Array.Empty<Polyline3DRecord>();
        private Polyline3DRecord storedEndSequence;
        private DxfDocument storedRecordDocument;

        /// <summary>Gets retained VERTEX identities of a loaded ordinary unsmoothed 3D polyline.</summary>
        /// <remarks>
        /// Each identity belongs to an index slot in Vertexes. Replacing coordinates at the same
        /// count keeps slot identities; Reverse reverses both lists. Count or smoothing changes
        /// reject on output. Authored and other polyline forms have an empty record list.
        /// </remarks>
        public IReadOnlyList<Polyline3DRecord> VertexRecords { get { return this.storedVertexView; } }
        /// <summary>Gets the retained sequence terminator, or null when no stored sequence exists.</summary>
        public Polyline3DRecord EndSequenceRecord { get { return this.storedEndSequence; } }
        internal bool HasStoredRecords { get { return this.storedEndSequence != null; } }
        internal IEnumerable<Polyline3DRecord> StoredRecords
        { get { return this.HasStoredRecords ? this.storedVertexRecords.Concat(new[] { this.storedEndSequence }) : Enumerable.Empty<Polyline3DRecord>(); } }

        internal void SetStoredRecords(DxfDocument document, List<Polyline3DRecord> vertices, Polyline3DRecord end)
        {
            this.storedRecordDocument = document; this.storedVertexRecords = vertices;
            this.storedVertexView = vertices.AsReadOnly(); this.storedEndSequence = end;
            foreach (Polyline3DRecord record in this.StoredRecords) record.Owner = this;
        }
        internal void ValidateStoredRecords(DxfDocument document, bool registered)
        {
            if (!this.HasStoredRecords) return;
            if (this.storedRecordDocument != null && !ReferenceEquals(this.storedRecordDocument, document))
                throw new NotSupportedException("Retained VERTEX/SEQEND records cannot be adopted into another document.");
            this.ValidateStoredRecordGeometry();
            foreach (Polyline3DRecord record in this.StoredRecords) record.Validate(document, this, registered);
        }
        private void ValidateStoredRecordGeometry()
        {
            if (this.Vertexes.Count != this.storedVertexRecords.Count || this.SmoothType != PolylineSmoothType.NoSmooth)
                throw new NotSupportedException("Changing the count or smoothing of retained VERTEX records requires a topology mapping.");
            foreach (Vector3 point in this.Vertexes)
                if (double.IsNaN(point.X) || double.IsInfinity(point.X) || double.IsNaN(point.Y) || double.IsInfinity(point.Y) || double.IsNaN(point.Z) || double.IsInfinity(point.Z))
                    throw new InvalidOperationException("Retained polyline coordinates must be finite.");
        }
        internal void RejectStoredRecordClone()
        {
            if (this.HasStoredRecords && (this.ExtensionDictionary != null || this.PersistentReactors.Count != 0
                || this.XData.Values.SelectMany(data => data.XDataRecord).Any(tag => tag.Code == XDataCode.DatabaseHandle
                    && ulong.Parse((string)tag.Value, System.Globalization.NumberStyles.AllowHexSpecifier, System.Globalization.CultureInfo.InvariantCulture) != 0)
                || this.StoredRecords.Any(record => !record.CanClone())))
                throw new NotSupportedException("Cloning retained VERTEX/SEQEND with external, owned or private dependencies requires a complete graph mapping.");
        }
        internal void CopyStoredRecordsTo(Polyline3D clone)
        {
            if (!this.HasStoredRecords) return;
            var vertices = this.storedVertexRecords.Select(record => record.CopyForClone()).ToList();
            Polyline3DRecord end = this.storedEndSequence.CopyForClone();
            this.RejectStoredRecordClone();
            clone.SetStoredRecords(null, vertices, end);
        }
        internal void BindStoredRecordDocument(DxfDocument document) { this.storedRecordDocument = document; }
        internal static void RejectStoredRecordBlockClone(Block root)
        {
            var visited = new HashSet<DxfObject>(new PolylineRecordIdentityComparer());
            Action<Block> visit = null;
            visit = block =>
            {
                if (block == null || !visited.Add(block)) return;
                foreach (EntityObject entity in block.Entities)
                {
                    if (entity is DxfOpaqueEntity) throw new NotSupportedException("Cloning blocks with unknown entities requires their complete application schema.");
                    if (entity is Polyline3D polyline) polyline.RejectStoredRecordClone();
                    if (entity is PolygonMesh mesh) mesh.RejectStoredRecordClone();
                    if (entity is Insert insert) visit(insert.Block);
                    if (entity is Dimension dimension) visit(dimension.Block);
                }
            };
            visit(root);
        }
        internal static void RejectStoredRecordOwnershipClone(DxfObject source)
        {
            var visited = new HashSet<DxfObject>(new PolylineRecordIdentityComparer());
            for (DxfObject current = source; current != null; current = current.Owner)
            {
                if (!visited.Add(current)) throw new InvalidOperationException("The clone source has cyclic ownership.");
                if (current is DxfOpaqueEntity) throw new NotSupportedException("Cloning unknown entity metadata requires its complete application schema.");
                if (current is Polyline3DRecord || current is PolygonMeshRecord)
                    throw new NotSupportedException("Cloning a retained polyline record's owned metadata requires its complete source graph.");
            }
        }
        private sealed class PolylineRecordIdentityComparer : IEqualityComparer<DxfObject>
        {
            public bool Equals(DxfObject first, DxfObject second) { return ReferenceEquals(first, second); }
            public int GetHashCode(DxfObject value) { return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value); }
        }
    }
}
