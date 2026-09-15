using System;
using System.Collections.Generic;
using System.Linq;
using netDxf.IO;

namespace netDxf.Entities
{
    public partial class Polyline2D
    {
        private Polyline2DRecord[] storedVertexRecords;
        private IReadOnlyList<Polyline2DRecord> storedVertexView = Array.Empty<Polyline2DRecord>();
        private DxfDocument storedRecordDocument;
        internal List<DxfTag> StoredHeaderTags;
        internal readonly Dictionary<short, int> StoredHeaderIndices = new Dictionary<short, int>();
        internal Vector3 StoredNormal;
        internal int StoredHeaderPublicEnd;
        internal bool HasPrivateHeader;

        /// <summary>Gets the physical VERTEX records of a loaded ordinary legacy POLYLINE.</summary>
        /// <remarks>Each record's Vertex is the existing model at the same Vertexes index. Authored and lightweight polylines have an empty list.</remarks>
        public IReadOnlyList<Polyline2DRecord> VertexRecords { get { return this.storedVertexView; } }
        /// <summary>Gets the retained legacy SEQEND, or null when no retained chain exists.</summary>
        public Polyline2DRecord EndSequenceRecord { get; private set; }
        /// <summary>Gets the optional legacy POLYLINE header's default outgoing start width.</summary>
        /// <remarks>Used only where the corresponding vertex width override is absent; distinct from LWPOLYLINE ConstantWidth.</remarks>
        public double? LegacyDefaultStartWidth { get; internal set; }
        /// <summary>Gets the optional legacy POLYLINE header's default outgoing end width.</summary>
        public double? LegacyDefaultEndWidth { get; internal set; }
        internal bool HasStoredRecords { get { return this.EndSequenceRecord != null; } }
        internal IEnumerable<Polyline2DRecord> StoredRecords
        { get { return this.HasStoredRecords ? this.storedVertexRecords.Concat(new[] { this.EndSequenceRecord }) : Enumerable.Empty<Polyline2DRecord>(); } }
        internal IEnumerable<DxfTag> StoredHeaderReferences
        { get { return this.StoredHeaderTags == null ? Enumerable.Empty<DxfTag>() : this.StoredHeaderTags.Where(netDxf.Objects.DxfObjectDatabase.IsReference); } }

        internal void SetStoredRecords(DxfDocument document, Polyline2DRecord[] vertices, Polyline2DRecord end)
        {
            this.storedRecordDocument = document; this.storedVertexRecords = vertices;
            this.storedVertexView = Array.AsReadOnly(vertices); this.EndSequenceRecord = end;
            this.CodeName = DxfObjectCode.Polyline;
            foreach (Polyline2DRecord record in this.StoredRecords) record.Owner = this;
        }
        internal void BindStoredRecordDocument(DxfDocument document) { this.storedRecordDocument = document; }
        internal Dictionary<short, DxfTag> LegacyHeaderValues()
        {
            var result = new Dictionary<short, DxfTag>();
            if (this.StoredHeaderIndices.ContainsKey(70) || (short)this.Flags != 0) result.Add(70, new DxfTag(70, (short)this.Flags));
            if (this.StoredHeaderIndices.ContainsKey(30) || this.Elevation != 0)
            {
                result.Add(10, this.StoredHeaderIndices.TryGetValue(10, out int x) ? this.StoredHeaderTags[x] : new DxfTag(10, 0.0));
                result.Add(20, this.StoredHeaderIndices.TryGetValue(20, out int y) ? this.StoredHeaderTags[y] : new DxfTag(20, 0.0));
                result.Add(30, new DxfTag(30, this.Elevation));
            }
            if (this.StoredHeaderIndices.ContainsKey(39) || this.Thickness != 0) result.Add(39, new DxfTag(39, this.Thickness));
            if (this.LegacyDefaultStartWidth.HasValue) result.Add(40, new DxfTag(40, this.LegacyDefaultStartWidth.Value));
            if (this.LegacyDefaultEndWidth.HasValue) result.Add(41, new DxfTag(41, this.LegacyDefaultEndWidth.Value));
            if (this.Normal != this.StoredNormal)
            {
                result.Add(210, new DxfTag(210, this.Normal.X)); result.Add(220, new DxfTag(220, this.Normal.Y)); result.Add(230, new DxfTag(230, this.Normal.Z));
            }
            else foreach (short code in new short[] { 210, 220, 230 })
                if (this.StoredHeaderIndices.TryGetValue(code, out int index)) result.Add(code, this.StoredHeaderTags[index]);
            return result;
        }
        internal void ValidateStoredRecords(DxfDocument document, bool registered)
        {
            if (!this.HasStoredRecords) return;
            if (this.storedRecordDocument != null && !ReferenceEquals(this.storedRecordDocument, document))
                throw new NotSupportedException("Retained legacy 2D records cannot be adopted into another document.");
            this.ValidateStoredRecordGeometry();
            foreach (Polyline2DRecord record in this.StoredRecords) record.Validate(document, this, registered);
        }
        private static bool LegacyFinite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
        private void ValidateStoredRecordGeometry()
        {
            if (!this.HasStoredRecords) return;
            if (this.SmoothType != PolylineSmoothType.NoSmooth || this.ConstantWidth.HasValue || ((int)this.Flags & ~129) != 0
                || this.vertexes.Count != this.storedVertexRecords.Length)
                throw new NotSupportedException("Retained legacy 2D records require unchanged ordinary topology and legacy width representation.");
            this.ValidateVertexFidelity();
            long retainedTags = 0;
            foreach (Polyline2DRecord record in this.StoredRecords)
            {
                int count = record.TopologyTagCount();
                if (count > 4096) throw new NotSupportedException("A retained legacy child exceeds its packet tag admission budget.");
                retainedTags += count;
            }
            if (retainedTags > 1048576 || this.StoredHeaderTags.Count + this.LegacyHeaderValues().Count - this.StoredHeaderIndices.Count > 4096)
                throw new NotSupportedException("The retained legacy chain exceeds its tag admission budget.");
            if (!LegacyFinite(this.Elevation) || !LegacyFinite(this.Thickness) || !LegacyFinite(this.Normal.X)
                || !LegacyFinite(this.Normal.Y) || !LegacyFinite(this.Normal.Z) || Vector3.IsZero(this.Normal))
                throw new InvalidOperationException("Retained legacy 2D elevation, thickness and normal must be finite, with a nonzero normal.");
            if (this.LegacyDefaultStartWidth.HasValue) ValidateWidth(this.LegacyDefaultStartWidth.Value, "LegacyDefaultStartWidth");
            if (this.LegacyDefaultEndWidth.HasValue) ValidateWidth(this.LegacyDefaultEndWidth.Value, "LegacyDefaultEndWidth");
            for (int i = 0; i < this.vertexes.Count; i++)
            {
                Polyline2DVertex vertex = this.vertexes[i];
                if (!ReferenceEquals(vertex, this.storedVertexRecords[i].Vertex))
                    throw new NotSupportedException("Replacing or directly reordering retained legacy vertex objects requires a topology mapping.");
                if (!LegacyFinite(vertex.Position.X) || !LegacyFinite(vertex.Position.Y) || !LegacyFinite(vertex.Bulge))
                    throw new InvalidOperationException("Retained legacy 2D coordinates and bulges must be finite.");
            }
        }
        internal void RejectStoredRecordClone()
        {
            if (!this.HasStoredRecords) return;
            this.ValidateStoredRecordGeometry();
            if (this.HasPrivateHeader || this.ExtensionDictionary != null || this.PersistentReactors.Count != 0
                || this.XData.Values.SelectMany(data => data.XDataRecord).Any(tag => tag.Code == XDataCode.DatabaseHandle
                    && ulong.Parse((string)tag.Value, System.Globalization.NumberStyles.AllowHexSpecifier, System.Globalization.CultureInfo.InvariantCulture) != 0)
                || this.StoredRecords.Any(record => !record.CanClone()))
                throw new NotSupportedException("Cloning retained legacy 2D records with external, owned or private dependencies requires a complete graph mapping.");
        }
        internal void CopyStoredRecordsTo(Polyline2D clone)
        {
            if (!this.HasStoredRecords) return;
            this.RejectStoredRecordClone();
            clone.SetStoredRecords(null, this.storedVertexRecords.Select((record, index) => record.CopyForClone(clone.Vertexes[index])).ToArray(), this.EndSequenceRecord.CopyForClone());
            clone.StoredHeaderTags = new List<DxfTag>(this.StoredHeaderTags);
            foreach (var pair in this.StoredHeaderIndices) clone.StoredHeaderIndices.Add(pair.Key, pair.Value);
            clone.StoredNormal = this.StoredNormal; clone.StoredHeaderPublicEnd = this.StoredHeaderPublicEnd;
            clone.LegacyDefaultStartWidth = this.LegacyDefaultStartWidth; clone.LegacyDefaultEndWidth = this.LegacyDefaultEndWidth;
        }
        private void TransformStoredRecords(Matrix3 transformation, Vector3 translation)
        {
            this.ValidateStoredRecordGeometry();
            double widthScale = this.GetWidthTransformScale(transformation);
            Vector3 newNormal = transformation * this.Normal;
            double normalScale = newNormal.Modulus();
            if (!LegacyFinite(normalScale) || normalScale <= 0)
                throw new NotSupportedException("A retained legacy transform requires a finite nonzero normal.");
            Matrix3 transOW = MathHelper.ArbitraryAxis(this.Normal);
            Matrix3 transWO = MathHelper.ArbitraryAxis(newNormal).Transpose();
            Vector3 x = transformation * (transOW * Vector3.UnitX), y = transformation * (transOW * Vector3.UnitY);
            if (!LegacyFinite(x.Modulus()) || !LegacyFinite(y.Modulus()) || x.Modulus() <= 0 || y.Modulus() <= 0
                || Math.Abs(Vector3.DotProduct(x / x.Modulus(), newNormal / normalScale)) > MathHelper.Epsilon
                || Math.Abs(Vector3.DotProduct(y / y.Modulus(), newNormal / normalScale)) > MathHelper.Epsilon)
                throw new NotSupportedException("A retained legacy transform must preserve the entity plane perpendicular to its normal.");
            Vector3 origin = transWO * (transformation * (transOW * new Vector3(0, 0, this.Elevation)) + translation);
            if (!LegacyFinite(origin.X) || !LegacyFinite(origin.Y) || !LegacyFinite(origin.Z))
                throw new InvalidOperationException("The transformed retained legacy plane must be finite.");
            var positions = new Vector2[this.vertexes.Count]; double elevation = origin.Z;
            for (int i = 0; i < this.vertexes.Count; i++)
            {
                Vector2 point = this.vertexes[i].Position;
                Vector3 transformed = transWO * (transformation * (transOW * new Vector3(point.X, point.Y, this.Elevation)) + translation);
                if (!LegacyFinite(transformed.X) || !LegacyFinite(transformed.Y) || !LegacyFinite(transformed.Z))
                    throw new InvalidOperationException("Transformed retained legacy coordinates must be finite.");
                positions[i] = new Vector2(transformed.X, transformed.Y); elevation = transformed.Z;
            }
            double thickness = this.Thickness * normalScale;
            if (!LegacyFinite(thickness)) throw new InvalidOperationException("Transformed retained legacy thickness must be finite.");
            for (int i = 0; i < positions.Length; i++) this.vertexes[i].Position = positions[i];
            this.Elevation = elevation; this.Normal = newNormal; this.Thickness = thickness;
            if (this.LegacyDefaultStartWidth.HasValue) this.LegacyDefaultStartWidth *= widthScale;
            if (this.LegacyDefaultEndWidth.HasValue) this.LegacyDefaultEndWidth *= widthScale;
            foreach (Polyline2DVertex vertex in this.vertexes)
            {
                if (vertex.StartWidthOverride.HasValue) vertex.StartWidthOverride *= widthScale;
                if (vertex.EndWidthOverride.HasValue) vertex.EndWidthOverride *= widthScale;
            }
        }
        private void ReverseStoredRecords()
        {
            if (!this.HasStoredRecords) return;
            Array.Reverse(this.storedVertexRecords);
            double? start = this.LegacyDefaultStartWidth;
            this.LegacyDefaultStartWidth = this.LegacyDefaultEndWidth; this.LegacyDefaultEndWidth = start;
        }
    }
}
