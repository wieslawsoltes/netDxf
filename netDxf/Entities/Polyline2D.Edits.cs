// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;

namespace netDxf.Entities
{
    public partial class Polyline2D
    {
        /// <summary>Changes one vertex position without replacing its object or retained VERTEX identity.</summary>
        /// <param name="index">The zero-based vertex index.</param>
        /// <param name="position">The finite replacement position in the entity's object coordinate system.</param>
        /// <remarks>The complete current vertex sequence is validated first. Exact coordinate bits, including
        /// the sign of zero, determine whether parent proxy graphics are invalidated. Widths, bulge and
        /// vertex identifier are unchanged. The existing Vertexes list and its enumeration version are retained.</remarks>
        public void SetVertex(int index, Vector2 position)
        {
            if (!LegacyFinite(position.X) || !LegacyFinite(position.Y))
                throw new ArgumentOutOfRangeException(nameof(position), position, "Vertex coordinates must be finite.");
            Polyline2DVertex vertex = this.EditableVertex(index);
            if (EditBitsEqual(vertex.Position.X, position.X) && EditBitsEqual(vertex.Position.Y, position.Y)) return;
            vertex.Position = position;
            this.ClearProxyGraphics();
        }

        /// <summary>Changes the outgoing segment bulge without replacing the vertex or its retained record.</summary>
        /// <param name="index">The zero-based index of the outgoing segment's vertex.</param>
        /// <param name="bulge">The finite signed bulge; zero represents a straight segment.</param>
        /// <remarks>Position, widths and vertex identifier are unchanged. Changed stored bits clear parent
        /// proxy graphics. Existing optional-field serialization and smoothing policies are not changed.</remarks>
        public void SetVertexBulge(int index, double bulge)
        {
            if (!LegacyFinite(bulge))
                throw new ArgumentOutOfRangeException(nameof(bulge), bulge, "Vertex bulge must be finite.");
            Polyline2DVertex vertex = this.EditableVertex(index);
            this.ValidateEditedVertexPacket(index, vertex.StartWidthOverride, vertex.EndWidthOverride, bulge);
            if (EditBitsEqual(vertex.Bulge, bulge)) return;
            vertex.Bulge = bulge;
            this.ClearProxyGraphics();
        }

        /// <summary>Atomically changes both optional raw widths of one outgoing segment.</summary>
        /// <param name="index">The zero-based index of the outgoing segment's vertex.</param>
        /// <param name="startWidth">Finite nonnegative start width, or null to remove its override.</param>
        /// <param name="endWidth">Finite nonnegative end width, or null to remove its override.</param>
        /// <remarks>Null and explicitly stored zero remain distinct. Both candidates and the complete current
        /// vertex sequence are validated before either width changes. Position, bulge, identifier, legacy
        /// defaults and ConstantWidth are unchanged; a nonzero ConstantWidth may therefore still mask these
        /// raw widths. Use SetConstantWidth for a whole-polyline width change instead.</remarks>
        public void SetVertexWidths(int index, double? startWidth, double? endWidth)
        {
            if (startWidth.HasValue) ValidateWidth(startWidth.Value, nameof(startWidth));
            if (endWidth.HasValue) ValidateWidth(endWidth.Value, nameof(endWidth));
            Polyline2DVertex vertex = this.EditableVertex(index);
            this.ValidateEditedVertexPacket(index, startWidth, endWidth, vertex.Bulge);
            if (EditWidthsEqual(vertex.StartWidthOverride, startWidth) && EditWidthsEqual(vertex.EndWidthOverride, endWidth)) return;
            // Nonvirtual setters receive already validated candidates; the first assignment cannot
            // be left committed by rejection of the second width.
            vertex.StartWidthOverride = startWidth;
            vertex.EndWidthOverride = endWidth;
            this.ClearProxyGraphics();
        }

        private Polyline2DVertex EditableVertex(int index)
        {
            if (index < 0 || index >= this.vertexes.Count) throw new ArgumentOutOfRangeException(nameof(index));
            this.ValidateStoredRecordGeometry();
            this.ValidateVertexEdits("Explicit vertex editing");
            return this.vertexes[index];
        }

        private static bool EditBitsEqual(double a, double b)
        { return BitConverter.DoubleToInt64Bits(a) == BitConverter.DoubleToInt64Bits(b); }

        private static bool EditWidthsEqual(double? a, double? b)
        { return a.HasValue == b.HasValue && (!a.HasValue || EditBitsEqual(a.Value, b.Value)); }

        private void ValidateEditedVertexPacket(int index, double? startWidth, double? endWidth, double bulge)
        {
            if (!this.HasStoredRecords) return;
            Polyline2DRecord record = this.storedVertexRecords[index];
            // Match GeometryTags' existing optional-field policy without staging anything on the
            // live vertex. Adding absent widths or a nonzero bulge must not exceed packet budgets.
            int proposed = 3 + (startWidth.HasValue ? 1 : 0) + (endWidth.HasValue ? 1 : 0)
                + (record.GeometryIndices.ContainsKey(42) || bulge != 0 ? 1 : 0)
                + (record.Vertex.VertexIdentifier.HasValue ? 1 : 0);
            int delta = proposed - record.GeometryTags().Count;
            if (record.TopologyTagCount() + delta > 4096)
                throw new NotSupportedException("The edited retained legacy vertex would exceed its packet tag admission budget.");
            long total = delta;
            foreach (Polyline2DRecord item in this.StoredRecords) total += item.TopologyTagCount();
            if (total > 1048576)
                throw new NotSupportedException("The edited retained legacy chain would exceed its tag admission budget.");
        }
    }
}
