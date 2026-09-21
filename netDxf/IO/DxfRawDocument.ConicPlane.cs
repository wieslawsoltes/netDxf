// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;

namespace netDxf.IO
{
    public sealed partial class DxfRawDocument
    {
        /// <summary>Edits a raw CIRCLE's complete OCS definition, signed thickness and extrusion.</summary>
        /// <param name="record">A CIRCLE belonging to this exact immutable snapshot.</param>
        /// <param name="centerInObjectCoordinates">Finite center in the replacement OCS, not WCS.</param>
        /// <param name="radius">Finite positive radius.</param>
        /// <param name="thickness">Finite signed thickness.</param>
        /// <param name="extrusionDirection">Finite nonzero extrusion, retained without normalization.</param>
        /// <returns>A new same-version snapshot, or this snapshot for a bit-identical no-op.</returns>
        /// <remarks>See WithArcGeometryAndPlane for preservation and dependency restrictions.</remarks>
        public DxfRawDocument WithCircleGeometryAndPlane(DxfRawRecord record,
            Vector3 centerInObjectCoordinates, double radius, double thickness, Vector3 extrusionDirection)
        {
            return this.ReplaceConicPlane(record, false, centerInObjectCoordinates, radius,
                0, 0, thickness, extrusionDirection);
        }

        /// <summary>Edits a raw ARC's OCS definition, stored degree angles, thickness and extrusion.</summary>
        /// <param name="record">An ARC belonging to this exact immutable snapshot.</param>
        /// <param name="centerInObjectCoordinates">Finite center in the replacement OCS, not WCS.</param>
        /// <param name="radius">Finite positive radius.</param>
        /// <param name="startAngle">Finite stored start angle in degrees, without normalization.</param>
        /// <param name="endAngle">Finite stored end angle in degrees, without normalization.</param>
        /// <param name="thickness">Finite signed thickness.</param>
        /// <param name="extrusionDirection">Finite nonzero extrusion, retained without normalization.</param>
        /// <returns>A new same-version snapshot, or this snapshot for a bit-identical no-op.</returns>
        /// <remarks>
        /// This is definition editing, not a shape-preserving affine transform. Changing the plane
        /// changes the OCS and therefore the WCS center and curve; no coordinates or angles are
        /// converted or swapped. The nine existing raw profiles retain their declared dialect.
        /// Changed extrusion is emitted as one complete adjacent 210/220/230 vector after radius.
        /// Changed thickness is also emitted there, before extrusion. Unchanged component tags
        /// are reused; all non-extrusion/non-thickness fields retain relative order. Missing Z
        /// is added after center Y only when needed. Absent default thickness remains absent.
        /// Real edits retain existing proxy/private/XData/incoming-reference guards and raw budgets.
        /// Source bytes stay unchanged; no-op output retains original bytes, while edited output
        /// may normalize spelling. Hidden dependencies, extents and private caches are not rebuilt.
        /// The preceding geometry-only APIs and their existing-plane contracts are unchanged.
        /// </remarks>
        public DxfRawDocument WithArcGeometryAndPlane(DxfRawRecord record,
            Vector3 centerInObjectCoordinates, double radius, double startAngle, double endAngle,
            double thickness, Vector3 extrusionDirection)
        {
            return this.ReplaceConicPlane(record, true, centerInObjectCoordinates, radius,
                startAngle, endAngle, thickness, extrusionDirection);
        }

        private DxfRawDocument ReplaceConicPlane(DxfRawRecord record, bool arc, Vector3 center,
            double radius, double start, double end, double thickness, Vector3 extrusion)
        {
            ConicPacket packet = this.ReadConicPacket(record, arc);
            CheckRawGeometryPoint(center, nameof(center));
            CheckRawGeometryPoint(extrusion, nameof(extrusion));
            CheckConicRadius(radius, nameof(radius));
            CheckConicAngle(start, nameof(start)); CheckConicAngle(end, nameof(end));
            CheckConicAngle(thickness, nameof(thickness));
            if (extrusion.X == 0 && extrusion.Y == 0 && extrusion.Z == 0)
                throw new ArgumentOutOfRangeException(nameof(extrusion), "Conic extrusion must be nonzero.");
            double[] values = { center.X, center.Y, center.Z, radius, start, end,
                thickness, extrusion.X, extrusion.Y, extrusion.Z };
            bool changed = false;
            for (int i = 0; i < values.Length; i++)
                changed |= !SameRawGeometryScalar(values[i], packet.Values[i]);
            if (!changed) return this;
            if (!packet.CanEdit)
                throw new NotSupportedException("Conic private data or geometry-sensitive caches require explicit regeneration.");
            this.CheckRawGeometryIncomingReferences(record);
            bool planeChanged = !SameRawGeometryScalar(values[7], packet.Values[7]) ||
                !SameRawGeometryScalar(values[8], packet.Values[8]) || !SameRawGeometryScalar(values[9], packet.Values[9]);
            bool thicknessChanged = !SameRawGeometryScalar(thickness, packet.Values[6]);
            bool addZ = packet.Slots[2] < 0 && !SameRawGeometryScalar(center.Z, 0.0);
            int additions = addZ ? 1 : 0;
            if (thicknessChanged && packet.Slots[6] < 0) additions++;
            if (planeChanged)
                for (int i = 7; i < 10; i++) if (packet.Slots[i] < 0) additions++;
            if ((long)this.Tags.Count + additions > this.options.MaximumTags)
                throw new InvalidOperationException("The conic plane edit exceeds the raw tag budget.");
            var tags = new List<DxfTag>(record.Tags.Count + additions);
            for (int at = 0; at < record.Tags.Count; at++)
            {
                if (thicknessChanged && at == packet.Slots[6]) continue;
                if (planeChanged && (at == packet.Slots[7] || at == packet.Slots[8] || at == packet.Slots[9])) continue;
                DxfTag tag = record.Tags[at];
                for (int i = 0; i < (arc ? 6 : 4); i++)
                    if (at == packet.Slots[i]) { tag = ConicPlaneTag(record, packet, values, i); break; }
                tags.Add(tag);
                if (addZ && at == packet.Slots[1]) tags.Add(new DxfTag(30, center.Z));
                if (at == packet.Slots[3])
                {
                    if (thicknessChanged) tags.Add(ConicPlaneTag(record, packet, values, 6));
                    if (planeChanged)
                        for (int i = 7; i < 10; i++) tags.Add(ConicPlaneTag(record, packet, values, i));
                }
            }
            return this.WithRecord(record, tags);
        }

        private static DxfTag ConicPlaneTag(DxfRawRecord record, ConicPacket packet, double[] values, int index)
        {
            return packet.Slots[index] >= 0 && SameRawGeometryScalar(packet.Values[index], values[index])
                ? record.Tags[packet.Slots[index]] : new DxfTag(ConicCodes[index], values[index]);
        }
    }
}
