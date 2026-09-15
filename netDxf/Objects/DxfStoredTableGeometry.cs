// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using netDxf.Header;
using netDxf.IO;

namespace netDxf.Objects
{
    /// <summary>One immutable content-geometry packet stored by TABLEGEOMETRY.</summary>
    public sealed class DxfStoredTableCellGeometry
    {
        /// <summary>Creates an immutable stored content-geometry value for explicit packet replacement.</summary>
        /// <param name="topLeft">The finite group-10 distance vector.</param>
        /// <param name="center">The finite group-11 distance vector.</param>
        /// <param name="contentWidth">The finite group-43 value.</param>
        /// <param name="contentHeight">The finite group-44 value.</param>
        /// <param name="width">The finite group-45 value.</param>
        /// <param name="height">The finite group-46 value.</param>
        /// <param name="value95">The uninterpreted signed group-95 value.</param>
        public DxfStoredTableCellGeometry(Vector3 topLeft, Vector3 center, double contentWidth,
            double contentHeight, double width, double height, int value95)
        {
            DxfStoredTableGeometry.CheckFinite(topLeft, nameof(topLeft));
            DxfStoredTableGeometry.CheckFinite(center, nameof(center));
            DxfStoredTableGeometry.CheckFinite(contentWidth, nameof(contentWidth));
            DxfStoredTableGeometry.CheckFinite(contentHeight, nameof(contentHeight));
            DxfStoredTableGeometry.CheckFinite(width, nameof(width));
            DxfStoredTableGeometry.CheckFinite(height, nameof(height));
            this.TopLeftDistance = topLeft; this.CenterDistance = center;
            this.ContentWidth = contentWidth; this.ContentHeight = contentHeight;
            this.Width = width; this.Height = height; this.StoredValue95 = value95;
        }
        /// <summary>Gets the stored group-10 distance vector.</summary>
        public Vector3 TopLeftDistance { get; }
        /// <summary>Gets the stored group-11 distance vector.</summary>
        public Vector3 CenterDistance { get; }
        /// <summary>Gets the stored group-43 content width.</summary>
        public double ContentWidth { get; }
        /// <summary>Gets the stored group-44 content height.</summary>
        public double ContentHeight { get; }
        /// <summary>Gets the stored group-45 width.</summary>
        public double Width { get; }
        /// <summary>Gets the stored group-46 height.</summary>
        public double Height { get; }
        /// <summary>Gets the uninterpreted group-95 integer.</summary>
        public int StoredValue95 { get; }
    }

    /// <summary>One immutable cell packet stored by TABLEGEOMETRY.</summary>
    public sealed class DxfStoredTableGeometryCell
    {
        /// <summary>Creates an immutable stored cell value for explicit packet replacement.</summary>
        /// <param name="flags">The uninterpreted signed group-93 flags.</param>
        /// <param name="width">The finite group-40 value, including its stored gap.</param>
        /// <param name="height">The finite group-41 value, including its stored gap.</param>
        /// <param name="reference">An explicit object identity, or null for a null pointer. Replacement validates its source-document membership.</param>
        /// <param name="geometry">Ordered immutable content packets. Null entries are not permitted.</param>
        /// <remarks>The sequence is copied without assigning handles or evaluating geometry.</remarks>
        public DxfStoredTableGeometryCell(int flags, double width, double height, DxfObject reference,
            IEnumerable<DxfStoredTableCellGeometry> geometry)
        {
            DxfStoredTableGeometry.CheckFinite(width, nameof(width));
            DxfStoredTableGeometry.CheckFinite(height, nameof(height));
            if (geometry == null) throw new ArgumentNullException(nameof(geometry));
            var values = new List<DxfStoredTableCellGeometry>();
            foreach (DxfStoredTableCellGeometry value in geometry)
            {
                if (value == null) throw new ArgumentException("A content-geometry packet cannot be null.", nameof(geometry));
                if (values.Count == DxfStoredTableGeometry.MaximumPayloadTags / 11)
                    throw new ArgumentException("Content geometry exceeds the TABLEGEOMETRY storage limit.", nameof(geometry));
                values.Add(value);
            }
            this.GeometryDataFlags = flags; this.WidthWithGap = width; this.HeightWithGap = height;
            this.GeometryReference = reference; this.Geometry = values.AsReadOnly();
        }
        internal DxfStoredTableGeometryCell(int flags, double width, double height, string reference,
            IList<DxfStoredTableCellGeometry> geometry)
        {
            this.GeometryDataFlags = flags; this.WidthWithGap = width; this.HeightWithGap = height;
            this.ReferenceHandle = reference; this.Geometry = new List<DxfStoredTableCellGeometry>(geometry).AsReadOnly();
        }
        /// <summary>Gets the uninterpreted group-93 geometry flags.</summary>
        public int GeometryDataFlags { get; }
        /// <summary>Gets the stored group-40 width including its gap.</summary>
        public double WidthWithGap { get; }
        /// <summary>Gets the stored group-41 height including its gap.</summary>
        public double HeightWithGap { get; }
        /// <summary>Gets the exact source target of group 330, or null for a stored null handle.</summary>
        /// <remarks>The pointer is retained without inferring its target's geometry semantics.</remarks>
        public DxfObject GeometryReference { get; private set; }
        /// <summary>Gets the ordered stored content-geometry packets.</summary>
        public IReadOnlyList<DxfStoredTableCellGeometry> Geometry { get; }
        internal string ReferenceHandle { get; }
        internal void SetReference(DxfObject value) { this.GeometryReference = value; }
    }

    /// <summary>A loaded TABLEGEOMETRY with explicitly replaceable stored cells and exact source dependencies.</summary>
    /// <remarks>
    /// This model preserves stored geometry in its source document and DXF version. It does not
    /// evaluate cell layout or regenerate geometry. Explicit replacement changes only its known
    /// stored packet; creation, cloning and erasure remain unsupported. Common metadata and XData
    /// retain their ordinary interfaces.
    /// </remarks>
    public sealed partial class DxfStoredTableGeometry : DxfDatabaseObject
    {
        internal const int MaximumPayloadTags = 1048576;
        private readonly DxfDocument source;
        private List<DxfObject> references = new List<DxfObject>();
        private Dictionary<string, DxfObject> handles = new Dictionary<string, DxfObject>(StringComparer.OrdinalIgnoreCase);
        private DxfObject sourceOwner;
        private bool resolved;

        internal DxfStoredTableGeometry(DxfDocument source, IList<DxfTag> tags) : base("TABLEGEOMETRY")
        {
            this.source = source; this.SourceVersion = source.DrawingVariables.AcadVer;
            this.Payload = new List<DxfTag>(tags).AsReadOnly();
            int index = 0;
            if ((string)Read(tags, ref index, 100).Value != "AcDbTableGeometry")
                throw new FormatException("TABLEGEOMETRY requires its public subclass.");
            this.RowCount = Count(tags, ref index, 90);
            this.ColumnCount = Count(tags, ref index, 91);
            int count = Count(tags, ref index, 92);
            if (count > (tags.Count - index) / 5) throw new FormatException("TABLEGEOMETRY cell count exceeds its stored payload.");
            var cells = new List<DxfStoredTableGeometryCell>();
            for (int cell = 0; cell < count; cell++)
            {
                int flags = (int)Read(tags, ref index, 93).Value;
                double width = Number(tags, ref index, 40), height = Number(tags, ref index, 41);
                string reference = (string)Read(tags, ref index, 330).Value;
                int geometryCount = Count(tags, ref index, 94);
                if (geometryCount > (tags.Count - index) / 11) throw new FormatException("TABLEGEOMETRY content count exceeds its stored payload.");
                var geometry = new List<DxfStoredTableCellGeometry>();
                for (int item = 0; item < geometryCount; item++)
                {
                    Vector3 topLeft = Point(tags, ref index, 10), center = Point(tags, ref index, 11);
                    double contentWidth = Number(tags, ref index, 43), contentHeight = Number(tags, ref index, 44);
                    double storedWidth = Number(tags, ref index, 45), storedHeight = Number(tags, ref index, 46);
                    int value95 = (int)Read(tags, ref index, 95).Value;
                    geometry.Add(new DxfStoredTableCellGeometry(topLeft, center, contentWidth, contentHeight, storedWidth, storedHeight, value95));
                }
                cells.Add(new DxfStoredTableGeometryCell(flags, width, height, reference, geometry));
            }
            if (index != tags.Count) throw new FormatException("TABLEGEOMETRY contains unexpected data after its counted cells.");
            this.Cells = cells.AsReadOnly();
        }
        /// <summary>Gets the source DXF version. Conversion to another version is not supported.</summary>
        public DxfVersion SourceVersion { get; }
        /// <summary>Gets the complete immutable subclass payload, excluding common metadata and XData.</summary>
        public IReadOnlyList<DxfTag> Payload { get; private set; }
        /// <summary>Gets the stored group-90 row count.</summary>
        public int RowCount { get; private set; }
        /// <summary>Gets the stored group-91 column count.</summary>
        public int ColumnCount { get; private set; }
        /// <summary>Gets the ordered stored cells counted by group 92.</summary>
        /// <remarks>No correspondence between cell index and row/column address is inferred.</remarks>
        public IReadOnlyList<DxfStoredTableGeometryCell> Cells { get; private set; }
        /// <summary>Gets exact source identities for nonzero semantic handles in packet order.</summary>
        public IReadOnlyList<DxfObject> References { get { return this.references.AsReadOnly(); } }
        internal override IEnumerable<DxfObject> DatabaseReferences
        { get { return this.references.Where(item => ReferenceEquals(this.source.GetObjectByHandle(item.Handle), item)); } }
        internal override IEnumerable<DxfTag> AllocationReservations { get { return this.Payload; } }
        internal override DxfDatabaseObject CloneShell() { throw new NotSupportedException("Stored TABLEGEOMETRY cloning requires its complete application schema."); }
        internal void Resolve(Func<string, DxfObject> resolve)
        {
            foreach (DxfTag tag in this.Payload)
            {
                if (!DxfObjectDatabase.IsReference(tag) || Convert.ToUInt64((string)tag.Value, 16) == 0) continue;
                string handle = (string)tag.Value;
                DxfObject target = resolve(handle);
                if (target == null) throw new FormatException("TABLEGEOMETRY requires an exact source reference identity: " + handle);
                this.handles[handle] = target;
                this.references.Add(target);
            }
            foreach (DxfStoredTableGeometryCell cell in this.Cells)
                if (Convert.ToUInt64(cell.ReferenceHandle, 16) != 0) cell.SetReference(this.handles[cell.ReferenceHandle]);
            this.sourceOwner = this.Owner;
            if (this.sourceOwner == null || !ReferenceEquals(this.source.GetObjectByHandle(this.sourceOwner.Handle), this.sourceOwner))
                throw new FormatException("TABLEGEOMETRY requires a registered source owner.");
            var ancestry = new HashSet<DxfObject>();
            for (DxfObject ancestor = this.sourceOwner; ancestor != null && !ReferenceEquals(ancestor, this.source); ancestor = ancestor.Owner)
            {
                if (ReferenceEquals(ancestor, this) || !ancestry.Add(ancestor)) throw new FormatException("TABLEGEOMETRY source ownership contains a cycle.");
                if (!ReferenceEquals(this.source.GetObjectByHandle(ancestor.Handle), ancestor)) throw new FormatException("TABLEGEOMETRY source ancestry contains an unregistered object.");
            }
            this.resolved = true;
        }
        internal override void ValidateDatabaseSchema(DxfObjectDatabase database, List<string> errors)
        {
            if (!this.resolved || !ReferenceEquals(database.Document, this.source))
            { errors.Add("Stored TABLEGEOMETRY must remain in its source document."); return; }
            if (this.source.DrawingVariables.AcadVer != this.SourceVersion) errors.Add("Stored TABLEGEOMETRY conversion requires complete schema regeneration.");
            if (this.StoredRecordTagCount(this.Payload.Count) > MaximumPayloadTags) errors.Add("Stored TABLEGEOMETRY exceeds its record tag limit.");
            if (!ReferenceEquals(this.Owner, this.sourceOwner) || !ReferenceEquals(this.source.GetObjectByHandle(this.sourceOwner.Handle), this.sourceOwner))
                errors.Add("Stored TABLEGEOMETRY source ownership changed.");
            foreach (var pair in this.handles)
                if (!ReferenceEquals(this.source.StoredTableHandleTarget(pair.Key), pair.Value)) errors.Add("A stored TABLEGEOMETRY dependency is no longer registered: " + pair.Key);
            foreach (DxfTag tag in this.Payload)
                if (tag.Value is string text)
                    for (int i = 0; i < text.Length; i++)
                        if (char.IsSurrogate(text[i]) && (!char.IsHighSurrogate(text[i]) || i + 1 == text.Length || !char.IsLowSurrogate(text[++i])))
                        { errors.Add("Stored TABLEGEOMETRY contains invalid UTF-16 text."); break; }
        }
        private static DxfTag Read(IList<DxfTag> tags, ref int index, short code)
        {
            if (index >= tags.Count || tags[index].Code != code)
                throw new FormatException("TABLEGEOMETRY requires ordered group " + code + ".");
            return tags[index++];
        }
        private static int Count(IList<DxfTag> tags, ref int index, short code)
        {
            int value = (int)Read(tags, ref index, code).Value;
            if (value < 0 || value > MaximumPayloadTags) throw new FormatException("TABLEGEOMETRY count exceeds its storage limit.");
            return value;
        }
        private static double Number(IList<DxfTag> tags, ref int index, short code)
        {
            double value = (double)Read(tags, ref index, code).Value;
            if (double.IsNaN(value) || double.IsInfinity(value)) throw new FormatException("TABLEGEOMETRY geometry must be finite.");
            return value;
        }
        private static Vector3 Point(IList<DxfTag> tags, ref int index, short code)
        {
            double x = Number(tags, ref index, code), y = Number(tags, ref index, (short)(code + 10)), z = Number(tags, ref index, (short)(code + 20));
            return new Vector3(x, y, z);
        }
    }
}
