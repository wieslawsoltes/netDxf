// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;

namespace netDxf.IO
{
    public sealed partial class DxfRawDocument
    {
        /// <summary>Reverses raw LWPOLYLINE traversal, including outgoing bulges and width endpoints.</summary>
        /// <param name="record">A LWPOLYLINE in ENTITIES or BLOCKS belonging to this exact snapshot.</param>
        /// <returns>A same-version snapshot; valid zero/one-vertex definitions return this snapshot.</returns>
        /// <remarks>
        /// Positions and opaque identifiers reverse together. Each reversed segment negates its
        /// previous outgoing bulge and swaps its start/end widths, preserving optional-field presence.
        /// An open curve's inactive last segment fields follow the same reversible cyclic mapping;
        /// they are not discarded. Two reversals restore component bits and presence, not lexical
        /// packet order. New vertex packets use adjacent X/Y followed by ID, widths and bulge.
        /// Count, flags, constant width, plane and all nonvertex tags retain values and object identity.
        /// Private/application/embedded data, geometry-sensitive XData and exposed incoming references
        /// reject through the existing topology guards. No extents, private caches, associations or
        /// linetype phase are regenerated. Centerlines and width progression reverse without fitting;
        /// this is not a general native-rendering equivalence guarantee. Source bytes are unchanged.
        /// The tag count is unchanged; parsing and temporary storage remain linear in source size.
        /// </remarks>
        public DxfRawDocument ReverseLwPolyline(DxfRawRecord record)
        {
            LwPolylinePacket packet = this.ReadLwPolylinePacket(record);
            int count = packet.Vertices.Count;
            if (count < 2) return this;
            this.ValidateLwTopologyEdit(record, packet);

            var selected = new bool[record.Tags.Count];
            var identifiers = new int[count];
            for (int i = 0; i < count; i++)
            {
                identifiers[i] = -1;
                foreach (int at in packet.Vertices[i].Slots)
                    if (at >= 0) selected[at] = true;
            }
            // Private contexts have already been refused. Visible group 91 belongs
            // to the vertex begun by the last group 10; XData cannot contain either.
            int vertex = -1;
            for (int at = 1; at < record.Tags.Count; at++)
            {
                short code = record.Tags[at].Code;
                if (code == 10) vertex++;
                else if (code == 91) { identifiers[vertex] = at; selected[at] = true; }
            }

            var tags = new List<DxfTag>(record.Tags.Count);
            int destination = 0;
            for (int at = 0; at < record.Tags.Count; at++)
            {
                if (!selected[at]) { tags.Add(record.Tags[at]); continue; }
                if (record.Tags[at].Code != 10) continue;
                int sourcePoint = count - 1 - destination++;
                int sourceEdge = sourcePoint == 0 ? count - 1 : sourcePoint - 1;
                LwVertexPacket point = packet.Vertices[sourcePoint], edge = packet.Vertices[sourceEdge];
                tags.Add(record.Tags[point.Slots[0]]);
                tags.Add(record.Tags[point.Slots[1]]);
                if (identifiers[sourcePoint] >= 0) tags.Add(record.Tags[identifiers[sourcePoint]]);
                if (edge.Slots[3] >= 0) tags.Add(new DxfTag(40, edge.Values[3]));
                if (edge.Slots[2] >= 0) tags.Add(new DxfTag(41, edge.Values[2]));
                if (edge.Slots[4] >= 0) tags.Add(new DxfTag(42, -edge.Values[4]));
            }
            return this.WithRecord(record, tags);
        }
    }
}
