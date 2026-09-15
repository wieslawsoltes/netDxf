// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using netDxf.IO;

namespace netDxf.Objects
{
    public sealed partial class DxfStoredTableGeometry
    {
        private bool editing;
        private bool reentered;

        /// <summary>Atomically replaces the qualified stored geometry packet of this loaded object.</summary>
        /// <param name="rowCount">The nonnegative stored row count, at most 1,048,576.</param>
        /// <param name="columnCount">The nonnegative stored column count, at most 1,048,576.</param>
        /// <param name="cells">The ordered immutable cell values. Null entries are not permitted.</param>
        /// <remarks>
        /// The complete request is copied and validated after caller enumeration and disposal.
        /// References must be explicit actual registered identities in this source document,
        /// including retained owner-held metadata; no handles are assigned and foreign objects
        /// are never matched by name or handle. A request equivalent to the current values keeps
        /// the original packet. Earlier Payload, Cells and References snapshots remain unchanged.
        /// Counts are encoded from the supplied lists, without inferring row/column addresses or
        /// requiring row count times column count to equal cell count. Negative finite dimensions
        /// and uninterpreted signed flags remain allowed. The complete emitted record is limited
        /// to 1,048,576 tags. The source profile, owner graph and common metadata are retained.
        /// This operation does not evaluate layout, regenerate a TABLE, create or erase objects,
        /// or roll back independent document changes made by caller enumeration callbacks.
        /// </remarks>
        public void ReplaceGeometry(int rowCount, int columnCount, IEnumerable<DxfStoredTableGeometryCell> cells)
        {
            if (this.editing)
            {
                this.reentered = true;
                throw new InvalidOperationException("TABLEGEOMETRY replacement cannot be reentered.");
            }
            if (cells == null) throw new ArgumentNullException(nameof(cells));
            if (rowCount < 0 || rowCount > MaximumPayloadTags) throw new ArgumentOutOfRangeException(nameof(rowCount));
            if (columnCount < 0 || columnCount > MaximumPayloadTags) throw new ArgumentOutOfRangeException(nameof(columnCount));
            this.editing = true;
            this.reentered = false;
            try
            {
                long tagCount = 4;
                var replacement = new List<DxfStoredTableGeometryCell>();
                foreach (DxfStoredTableGeometryCell cell in cells)
                {
                    if (cell == null) throw new ArgumentException("A stored geometry cell cannot be null.", nameof(cells));
                    tagCount += 5L + 11L * cell.Geometry.Count;
                    if (tagCount > MaximumPayloadTags)
                        throw new ArgumentException("The requested geometry exceeds the record tag limit.", nameof(cells));
                    replacement.Add(cell);
                }
                // The enumerator has been disposed. All remaining work uses sealed values and
                // document-owned collections and cannot invoke caller enumeration again.
                if (this.reentered) throw new InvalidOperationException("TABLEGEOMETRY replacement was reentered during enumeration.");
                this.ValidateReplacementSource();
                if (this.StoredRecordTagCount(tagCount) > MaximumPayloadTags)
                    throw new ArgumentException("The requested geometry and common metadata exceed the record tag limit.", nameof(cells));

                var nextReferences = new List<DxfObject>();
                var nextHandles = new Dictionary<string, DxfObject>(StringComparer.OrdinalIgnoreCase);
                foreach (DxfStoredTableGeometryCell cell in replacement)
                {
                    DxfObject target = cell.GeometryReference;
                    if (target == null) continue;
                    if (target.Handle == null || !ReferenceEquals(this.source.StoredTableHandleTarget(target.Handle), target))
                        throw new ArgumentException("Every geometry reference must be an actual registered identity in the source document.", nameof(cells));
                    nextReferences.Add(target);
                    nextHandles[ReferenceText(cell)] = target;
                }
                if (this.SameGeometry(rowCount, columnCount, replacement)) return;

                var tags = new List<DxfTag>((int)tagCount)
                {
                    new DxfTag(100, "AcDbTableGeometry"), new DxfTag(90, rowCount),
                    new DxfTag(91, columnCount), new DxfTag(92, replacement.Count)
                };
                foreach (DxfStoredTableGeometryCell cell in replacement)
                {
                    tags.Add(new DxfTag(93, cell.GeometryDataFlags));
                    tags.Add(new DxfTag(40, cell.WidthWithGap));
                    tags.Add(new DxfTag(41, cell.HeightWithGap));
                    tags.Add(new DxfTag(330, ReferenceText(cell)));
                    tags.Add(new DxfTag(94, cell.Geometry.Count));
                    foreach (DxfStoredTableCellGeometry value in cell.Geometry)
                    {
                        AddPoint(tags, 10, value.TopLeftDistance); AddPoint(tags, 11, value.CenterDistance);
                        tags.Add(new DxfTag(43, value.ContentWidth)); tags.Add(new DxfTag(44, value.ContentHeight));
                        tags.Add(new DxfTag(45, value.Width)); tags.Add(new DxfTag(46, value.Height));
                        tags.Add(new DxfTag(95, value.StoredValue95));
                    }
                }
                var packet = tags.AsReadOnly();
                var cellSnapshot = replacement.AsReadOnly();
                // Every callback, allocation and validation has finished. Only state swaps remain.
                this.Payload = packet;
                this.Cells = cellSnapshot;
                this.references = nextReferences;
                this.handles = nextHandles;
                this.RowCount = rowCount;
                this.ColumnCount = columnCount;
            }
            finally { this.editing = false; this.reentered = false; }
        }

        private void ValidateReplacementSource()
        {
            if (!this.resolved || this.IsErased || this.Database == null
                || !ReferenceEquals(this.Database.Document, this.source)
                || !ReferenceEquals(this.source.GetObjectByHandle(this.Handle), this))
                throw new InvalidOperationException("TABLEGEOMETRY must remain registered in its source document.");
            // This read-only validation includes source ownership, root dictionary links,
            // declared wrapper slots, dependencies, metadata and every owner ancestor.
            IReadOnlyList<string> errors = this.Database.Validate();
            if (errors.Count != 0)
                throw new InvalidOperationException("Cannot replace TABLEGEOMETRY in an invalid source database: " + string.Join("; ", errors));
        }

        private long StoredRecordTagCount(long payloadCount)
        {
            long count = payloadCount + 2; // Identity and common owner, excluding group 0.
            if (this.ExtensionDictionary != null) count += 3;
            int reactors = this.PersistentReactors.Where(item => item != null).Select(item => item.Handle).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            if (reactors != 0) count += reactors + 2L;
            foreach (XData data in this.XData.Values)
            {
                count++; // Application registry marker.
                foreach (XDataRecord record in data.XDataRecord)
                    count += record.Code == XDataCode.BinaryData ? Math.Max(1L, (((byte[])record.Value).LongLength + 126L) / 127L) : 1L;
            }
            return count;
        }

        private static string ReferenceText(DxfStoredTableGeometryCell cell)
        {
            string current = cell.GeometryReference == null ? "0" : cell.GeometryReference.Handle;
            // Keep source lexical handle spelling when retaining a loaded cell identity.
            if (cell.ReferenceHandle != null && Convert.ToUInt64(cell.ReferenceHandle, 16) == Convert.ToUInt64(current, 16))
                return cell.ReferenceHandle;
            return current;
        }

        private bool SameGeometry(int rows, int columns, IList<DxfStoredTableGeometryCell> cells)
        {
            if (rows != this.RowCount || columns != this.ColumnCount || cells.Count != this.Cells.Count) return false;
            for (int i = 0; i < cells.Count; i++)
            {
                DxfStoredTableGeometryCell a = cells[i], b = this.Cells[i];
                if (a.GeometryDataFlags != b.GeometryDataFlags || !Same(a.WidthWithGap, b.WidthWithGap)
                    || !Same(a.HeightWithGap, b.HeightWithGap) || !ReferenceEquals(a.GeometryReference, b.GeometryReference)
                    || a.Geometry.Count != b.Geometry.Count) return false;
                for (int j = 0; j < a.Geometry.Count; j++)
                {
                    DxfStoredTableCellGeometry x = a.Geometry[j], y = b.Geometry[j];
                    if (!Same(x.TopLeftDistance, y.TopLeftDistance) || !Same(x.CenterDistance, y.CenterDistance)
                        || !Same(x.ContentWidth, y.ContentWidth) || !Same(x.ContentHeight, y.ContentHeight)
                        || !Same(x.Width, y.Width) || !Same(x.Height, y.Height) || x.StoredValue95 != y.StoredValue95) return false;
                }
            }
            return true;
        }

        private static bool Same(double first, double second) { return BitConverter.DoubleToInt64Bits(first) == BitConverter.DoubleToInt64Bits(second); }
        private static bool Same(Vector3 first, Vector3 second) { return Same(first.X, second.X) && Same(first.Y, second.Y) && Same(first.Z, second.Z); }
        private static void AddPoint(ICollection<DxfTag> tags, short code, Vector3 point)
        { tags.Add(new DxfTag(code, point.X)); tags.Add(new DxfTag((short)(code + 10), point.Y)); tags.Add(new DxfTag((short)(code + 20), point.Z)); }
        internal static void CheckFinite(double value, string parameter)
        { if (double.IsNaN(value) || double.IsInfinity(value)) throw new ArgumentOutOfRangeException(parameter, "Stored geometry must be finite."); }
        internal static void CheckFinite(Vector3 value, string parameter)
        { CheckFinite(value.X, parameter); CheckFinite(value.Y, parameter); CheckFinite(value.Z, parameter); }
    }
}
