using System;
using System.Collections.Generic;
using System.Linq;

namespace netDxf.Entities
{
    public partial class PolygonMesh
    {
        private PolygonMeshRecord[] storedVertexRecords;
        private IReadOnlyList<PolygonMeshRecord> storedVertexView = Array.Empty<PolygonMeshRecord>();
        private PolygonMeshRecord storedEndSequence;
        private DxfDocument storedRecordDocument;

        /// <summary>Gets the retained VERTEX identities of a loaded ordinary unsmoothed polygon mesh.</summary>
        /// <remarks>Record indices match the Vertexes array, including its U/V indexing. Authored and smoothed meshes have an empty list.</remarks>
        public IReadOnlyList<PolygonMeshRecord> VertexRecords { get { return this.storedVertexView; } }
        /// <summary>Gets the retained SEQEND, or null when no stored ordinary sequence exists.</summary>
        public PolygonMeshRecord EndSequenceRecord { get { return this.storedEndSequence; } }
        internal bool HasStoredRecords { get { return this.storedEndSequence != null; } }
        internal IEnumerable<PolygonMeshRecord> StoredRecords
        { get { return this.HasStoredRecords ? this.storedVertexRecords.Concat(new[] { this.storedEndSequence }) : Enumerable.Empty<PolygonMeshRecord>(); } }

        internal void SetStoredRecords(DxfDocument document, PolygonMeshRecord[] vertices, PolygonMeshRecord end)
        {
            this.storedRecordDocument = document; this.storedVertexRecords = vertices;
            this.storedVertexView = Array.AsReadOnly(vertices); this.storedEndSequence = end;
            foreach (PolygonMeshRecord record in this.StoredRecords) record.Owner = this;
        }

        internal void BindStoredRecordDocument(DxfDocument document) { this.storedRecordDocument = document; }

        internal void ValidateStoredRecords(DxfDocument document, bool registered)
        {
            if (!this.HasStoredRecords) return;
            if (this.storedRecordDocument != null && !ReferenceEquals(this.storedRecordDocument, document))
                throw new NotSupportedException("Retained polygon mesh records cannot be adopted into another document.");
            if (this.SmoothType != PolylineSmoothType.NoSmooth || this.Vertexes.Length != this.storedVertexRecords.Length)
                throw new NotSupportedException("Changing the retained polygon mesh sequence requires complete schema regeneration.");
            this.ValidateSurface();
            foreach (PolygonMeshRecord record in this.StoredRecords) record.Validate(document, this, registered);
        }

        internal void RejectStoredRecordClone()
        {
            if (!this.HasStoredRecords) return;
            if (this.SmoothType != PolylineSmoothType.NoSmooth)
                throw new NotSupportedException("A retained polygon mesh cannot be cloned after an unsupported smoothing change.");
            this.ValidateSurface();
            if (this.ExtensionDictionary != null || this.PersistentReactors.Count != 0
                || this.XData.Values.SelectMany(data => data.XDataRecord).Any(tag => tag.Code == XDataCode.DatabaseHandle
                    && ulong.Parse((string)tag.Value, System.Globalization.NumberStyles.AllowHexSpecifier, System.Globalization.CultureInfo.InvariantCulture) != 0)
                || this.StoredRecords.Any(record => !record.CanClone()))
                throw new NotSupportedException("Cloning retained polygon mesh records with external, owned or private dependencies requires a complete graph mapping.");
        }

        internal void CopyStoredRecordsTo(PolygonMesh clone)
        {
            if (!this.HasStoredRecords) return;
            this.RejectStoredRecordClone();
            clone.SetStoredRecords(null, this.storedVertexRecords.Select(record => record.CopyForClone()).ToArray(), this.storedEndSequence.CopyForClone());
        }
    }
}
