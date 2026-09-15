using System;
using System.Collections.Generic;
using System.Linq;
using netDxf.IO;

namespace netDxf.Entities
{
    public partial class PolyfaceMesh
    {
        private IReadOnlyList<PolyfaceMeshRecord> storedVertexRecords = Array.Empty<PolyfaceMeshRecord>();
        private IReadOnlyList<PolyfaceMeshRecord> storedFaceRecords = Array.Empty<PolyfaceMeshRecord>();
        private IReadOnlyList<PolyfaceMeshRecord> storedSequence = Array.Empty<PolyfaceMeshRecord>();
        private DxfDocument storedRecordDocument;
        internal List<DxfTag> StoredHeaderTags;
        internal readonly Dictionary<short, int> StoredNormalIndices = new Dictionary<short, int>();
        internal Vector3 StoredNormal;
        internal bool HasPrivateHeader;

        /// <summary>Gets retained coordinate VERTEX records aligned with the Vertexes array.</summary>
        public IReadOnlyList<PolyfaceMeshRecord> VertexRecords { get { return this.storedVertexRecords; } }
        /// <summary>Gets retained face VERTEX records aligned with the existing Faces collection.</summary>
        public IReadOnlyList<PolyfaceMeshRecord> FaceRecords { get { return this.storedFaceRecords; } }
        /// <summary>Gets retained coordinate, face and SEQEND records in their original physical order.</summary>
        public IReadOnlyList<PolyfaceMeshRecord> RecordSequence { get { return this.storedSequence; } }
        /// <summary>Gets the retained terminator, or null for an authored mesh.</summary>
        public PolyfaceMeshRecord EndSequenceRecord { get; private set; }
        /// <summary>Gets the source's advisory group 71, including null when it was omitted.</summary>
        public short? DeclaredVertexCount { get; internal set; }
        /// <summary>Gets the source's advisory group 72, including null when it was omitted.</summary>
        public short? DeclaredFaceCount { get; internal set; }
        internal bool HasStoredRecords { get { return this.EndSequenceRecord != null; } }
        internal IEnumerable<PolyfaceMeshRecord> StoredRecords { get { return this.storedSequence; } }
        internal IEnumerable<DxfTag> StoredHeaderReferences
        { get { return this.StoredHeaderTags == null ? Enumerable.Empty<DxfTag>() : this.StoredHeaderTags.Where(netDxf.Objects.DxfObjectDatabase.IsReference); } }

        internal void SetStoredRecords(DxfDocument document, PolyfaceMeshRecord[] sequence)
        {
            this.storedRecordDocument = document; this.storedSequence = Array.AsReadOnly(sequence);
            this.storedVertexRecords = Array.AsReadOnly(sequence.Where(record => !record.IsFaceRecord && !record.IsSequenceEnd).ToArray());
            this.storedFaceRecords = Array.AsReadOnly(sequence.Where(record => record.IsFaceRecord).ToArray());
            this.EndSequenceRecord = sequence.Last();
            foreach (PolyfaceMeshRecord record in sequence) record.Owner = this;
        }
        internal void BindStoredRecordDocument(DxfDocument document) { this.storedRecordDocument = document; }
        internal void ValidateStoredRecords(DxfDocument document, bool registered)
        {
            if (!this.HasStoredRecords) return;
            if (this.storedRecordDocument != null && !ReferenceEquals(document, this.storedRecordDocument))
                throw new NotSupportedException("Retained polyface records cannot be adopted into another document.");
            if (this.Vertexes.Length != this.VertexRecords.Count || this.Faces.Count != this.FaceRecords.Count)
                throw new NotSupportedException("Changing retained polyface record counts requires complete topology regeneration.");
            this.ValidateRetainedGeometry();
            foreach (PolyfaceMeshRecord record in this.StoredRecords) record.Validate(document, this, registered);
        }
        private void ValidateRetainedGeometry()
        {
            this.ValidateFaceIndexes();
            foreach (Vector3 point in this.Vertexes)
                if (double.IsNaN(point.X) || double.IsInfinity(point.X) || double.IsNaN(point.Y) || double.IsInfinity(point.Y) || double.IsNaN(point.Z) || double.IsInfinity(point.Z))
                    throw new InvalidOperationException("Retained polyface coordinates must be finite.");
            for (int i = 0; i < this.FaceRecords.Count; i++)
                if (!ReferenceEquals(this.Faces[i], this.FaceRecords[i].Face))
                    throw new NotSupportedException("Replacing a retained face object requires complete topology regeneration.");
        }
        internal void RejectStoredRecordClone()
        {
            if (!this.HasStoredRecords) return;
            this.ValidateRetainedGeometry();
            if (this.HasPrivateHeader || this.ExtensionDictionary != null || this.PersistentReactors.Count != 0
                || this.XData.Values.SelectMany(data => data.XDataRecord).Any(tag => tag.Code == XDataCode.DatabaseHandle
                    && ulong.Parse((string)tag.Value, System.Globalization.NumberStyles.AllowHexSpecifier, System.Globalization.CultureInfo.InvariantCulture) != 0)
                || this.StoredRecords.Any(record => !record.CanClone()))
                throw new NotSupportedException("Cloning retained polyface records with external, owned or private dependencies requires a complete graph mapping.");
        }
        internal void CopyStoredRecordsTo(PolyfaceMesh clone)
        {
            if (!this.HasStoredRecords) return;
            this.RejectStoredRecordClone();
            clone.SetStoredRecords(null, this.StoredRecords.Select(record => record.CopyForClone(record.IsFaceRecord ? clone.Faces[record.FaceIndex] : null)).ToArray());
            clone.StoredHeaderTags = new List<DxfTag>(this.StoredHeaderTags);
            clone.StoredNormal = this.StoredNormal;
            foreach (var pair in this.StoredNormalIndices) clone.StoredNormalIndices.Add(pair.Key, pair.Value);
            clone.DeclaredVertexCount = this.DeclaredVertexCount; clone.DeclaredFaceCount = this.DeclaredFaceCount;
        }
    }
}
