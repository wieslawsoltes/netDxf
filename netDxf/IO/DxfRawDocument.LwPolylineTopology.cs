// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;

namespace netDxf.IO
{
    public sealed partial class DxfRawDocument
    {
        /// <summary>Inserts a vertex before an existing index, or appends at the current count.</summary>
        /// <param name="record">A LWPOLYLINE belonging to this exact snapshot.</param>
        /// <param name="vertexIndex">Insertion index in the inclusive range zero through vertex count.</param>
        /// <param name="position">Finite X/Y position in the existing OCS.</param>
        /// <param name="startWidth">Finite nonnegative outgoing start width; default positive zero stays absent.</param>
        /// <param name="endWidth">Finite nonnegative outgoing end width; default positive zero stays absent.</param>
        /// <param name="bulge">Finite outgoing bulge; default positive zero stays absent.</param>
        /// <param name="identifier">Optional opaque signed identifier, not already used by another vertex.</param>
        /// <returns>A new same-version snapshot with the count updated and existing tags retained.</returns>
        /// <remarks>
        /// This is structural editing, not shape-preserving segment subdivision. Preceding bulges and
        /// widths are unchanged and therefore apply to the newly adjacent vertex. Closed flags, the
        /// plane and surviving identifiers are retained, without renumbering. Nonzero per-vertex widths
        /// reject under nonzero constant width. The shared schema, private-data and incoming-reference
        /// guards apply. No header extents, associations or hidden references are regenerated.
        /// Both the vertex and total tag budgets are checked before constructing the replacement.
        /// </remarks>
        public DxfRawDocument InsertLwPolylineVertex(DxfRawRecord record, int vertexIndex, Vector2 position,
            double startWidth = 0, double endWidth = 0, double bulge = 0, int? identifier = null)
        {
            LwPolylinePacket packet = this.ReadLwPolylinePacket(record);
            int count = packet.Vertices.Count;
            if (vertexIndex < 0 || vertexIndex > count) throw new ArgumentOutOfRangeException(nameof(vertexIndex));
            CheckRawGeometryPoint(new Vector3(position.X, position.Y, 0), nameof(position));
            CheckRawLwWidth(startWidth, nameof(startWidth)); CheckRawLwWidth(endWidth, nameof(endWidth));
            if (double.IsNaN(bulge) || double.IsInfinity(bulge)) throw new ArgumentOutOfRangeException(nameof(bulge));
            if (count >= MaximumRawLwPolylineVertices) throw new InvalidOperationException("The insertion exceeds the raw vertex budget.");
            if (identifier.HasValue)
                foreach (LwVertexPacket vertex in packet.Vertices)
                    if (vertex.Identifier == identifier) throw new ArgumentException("The new vertex identifier is already present.", nameof(identifier));
            if (packet.Geometry.ConstantWidth != 0 && (startWidth != 0 || endWidth != 0))
                throw new NotSupportedException("Nonzero new vertex widths conflict with the existing constant width.");
            this.ValidateLwTopologyEdit(record, packet);

            var inserted = new List<DxfTag>(6) { new DxfTag(10, position.X), new DxfTag(20, position.Y) };
            if (identifier.HasValue) inserted.Add(new DxfTag(91, identifier.Value));
            if (!SameRawGeometryScalar(startWidth, 0.0)) inserted.Add(new DxfTag(40, startWidth));
            if (!SameRawGeometryScalar(endWidth, 0.0)) inserted.Add(new DxfTag(41, endWidth));
            if (!SameRawGeometryScalar(bulge, 0.0)) inserted.Add(new DxfTag(42, bulge));
            if ((long)this.Tags.Count + inserted.Count > this.options.MaximumTags)
                throw new InvalidOperationException("The insertion exceeds the raw tag budget.");
            int insertion;
            if (vertexIndex < count) insertion = packet.Vertices[vertexIndex].Slots[0];
            else if (count != 0) insertion = packet.Vertices[count - 1].LastSlot + 1;
            else
            {
                // Empty definitions have no vertex anchor. Keep existing entity headers
                // ahead of new vertices and do not place ordinary fields after XData.
                insertion = 1;
                for (int i = 1; i < record.Tags.Count; i++)
                {
                    short code = record.Tags[i].Code;
                    if (code == 90 || code == 70 || code == 43 || code == 38 || code == 39 ||
                        code == 210 || code == 220 || code == 230) insertion = i + 1;
                }
            }
            var tags = new List<DxfTag>(record.Tags.Count + inserted.Count);
            for (int i = 0; i <= record.Tags.Count; i++)
            {
                if (i == insertion) tags.AddRange(inserted);
                if (i < record.Tags.Count)
                    tags.Add(record.Tags[i].Code == 90 ? new DxfTag(90, count + 1) : record.Tags[i]);
            }
            return this.WithRecord(record, tags);
        }

        /// <summary>Removes exactly one raw vertex and updates the declared count.</summary>
        /// <param name="record">A LWPOLYLINE belonging to this exact snapshot.</param>
        /// <param name="vertexIndex">Zero-based index of the vertex to remove.</param>
        /// <returns>A new snapshot; all unselected tags retain identity and relative order.</returns>
        /// <remarks>
        /// Only the selected X/Y, width, bulge and identifier slots are removed. Interleaved entity
        /// headers, appearance and comments are retained, not deleted as part of a contiguous slice.
        /// Surviving segment values are not refitted; removing a vertex can change neighboring arcs.
        /// Removing the final vertex produces a raw empty definition with the existing flags retained;
        /// this is not a promise that native renderers accept empty or degenerate geometry.
        /// Private data and exposed incoming references reject through the existing edit guards.
        /// </remarks>
        public DxfRawDocument RemoveLwPolylineVertex(DxfRawRecord record, int vertexIndex)
        {
            LwPolylinePacket packet = this.ReadLwPolylinePacket(record);
            if (vertexIndex < 0 || vertexIndex >= packet.Vertices.Count) throw new ArgumentOutOfRangeException(nameof(vertexIndex));
            this.ValidateLwTopologyEdit(record, packet);
            LwVertexPacket vertex = packet.Vertices[vertexIndex];
            var tags = new List<DxfTag>(record.Tags.Count);
            for (int i = 0; i < record.Tags.Count; i++)
            {
                DxfTag tag = record.Tags[i];
                if (Array.IndexOf(vertex.Slots, i) >= 0) continue;
                // Editing was already refused for private/application payloads. A visible
                // group 91 between this vertex's bounds is its unique optional identifier.
                if (tag.Code == 91 && i >= vertex.Slots[0] && i <= vertex.LastSlot) continue;
                tags.Add(tag.Code == 90 ? new DxfTag(90, packet.Vertices.Count - 1) : tag);
            }
            return this.WithRecord(record, tags);
        }

        private void ValidateLwTopologyEdit(DxfRawRecord record, LwPolylinePacket packet)
        {
            if (!packet.CanEdit) throw new NotSupportedException("LWPOLYLINE topology contains data requiring explicit regeneration.");
            this.CheckRawGeometryIncomingReferences(record);
        }
    }
}
