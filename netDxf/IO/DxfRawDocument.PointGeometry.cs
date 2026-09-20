// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;

namespace netDxf.IO
{
    /// <summary>Immutable stored POINT geometry, without database ownership.</summary>
    public sealed class DxfRawPointGeometry
    {
        internal DxfRawPointGeometry(Vector3 position, double thickness, Vector3 extrusion, double angle)
        { this.Position = position; this.Thickness = thickness; this.ExtrusionDirection = extrusion; this.UcsXAxisAngle = angle; }
        /// <summary>Gets the point position in world coordinates, irrespective of extrusion.</summary>
        public Vector3 Position { get; }
        /// <summary>Gets the stored signed thickness, defaulting to zero.</summary>
        public double Thickness { get; }
        /// <summary>Gets the stored extrusion vector without normalization, defaulting to (0,0,1).</summary>
        public Vector3 ExtrusionDirection { get; }
        /// <summary>Gets the stored UCS X-axis angle in degrees (group 50), defaulting to zero.</summary>
        public double UcsXAxisAngle { get; }
    }

    public sealed partial class DxfRawDocument
    {
        /// <summary>Reads the selected geometry of an ordinary raw POINT without changing its tags.</summary>
        /// <param name="record">A POINT in ENTITIES or BLOCKS of this exact snapshot.</param>
        /// <returns>Immutable WCS position, thickness, extrusion and UCS X-axis angle.</returns>
        /// <remarks>
        /// Classic markerless and AcDbEntity/AcDbPoint layouts are accepted. X and Y are required;
        /// omitted Z is zero. Duplicate geometry, zero extrusion and malformed schema framing reject.
        /// Application groups and embedded tails do not provide or override core coordinates.
        /// This is selected-schema access, not historical typed DxfDocument loading.
        /// </remarks>
        public DxfRawPointGeometry ReadPointGeometry(DxfRawRecord record)
        { return this.ReadPointPacket(record).Geometry; }

        /// <summary>Replaces a raw POINT's WCS position in a same-version immutable snapshot.</summary>
        /// <param name="record">A POINT belonging to this exact snapshot.</param>
        /// <param name="position">Finite new world-coordinate position.</param>
        /// <returns>A new snapshot, or this snapshot for a bit-identical no-op.</returns>
        /// <remarks>
        /// Thickness, extrusion, UCS angle and every unselected tag retain identity and order.
        /// A missing Z field is inserted after Y only for a value other than positive zero.
        /// Real edits reject private/control/embedded data, proxies, unknown fields, geometry-sensitive
        /// XData and exposed incoming handle uses, instead of silently regenerating dependencies.
        /// Header extents and hidden references are not updated. Source bytes remain unchanged;
        /// changed serialization may normalize lexical spelling. Raw budgets and save rules apply.
        /// </remarks>
        public DxfRawDocument WithPointPosition(DxfRawRecord record, Vector3 position)
        {
            PointPacket packet = this.ReadPointPacket(record);
            CheckRawGeometryPoint(position, nameof(position));
            double[] values = { position.X, position.Y, position.Z };
            bool changed = false;
            for (int i = 0; i < 3; i++) changed |= !SameRawGeometryScalar(values[i], packet.Values[i]);
            if (!changed) return this;
            if (!packet.CanEdit)
                throw new NotSupportedException("This POINT has proxy, private, geometry-sensitive XData or unknown fields requiring explicit regeneration.");
            this.CheckRawGeometryIncomingReferences(record);
            bool addZ = packet.Slots[2] < 0 && !SameRawGeometryScalar(position.Z, 0.0);
            if ((long)this.Tags.Count + (addZ ? 1 : 0) > this.options.MaximumTags)
                throw new InvalidOperationException("The POINT edit exceeds the raw tag budget.");
            var tags = new List<DxfTag>(record.Tags.Count + (addZ ? 1 : 0));
            for (int at = 0; at < record.Tags.Count; at++)
            {
                DxfTag tag = record.Tags[at];
                for (int i = 0; i < 3; i++)
                    if (packet.Slots[i] == at && !SameRawGeometryScalar(values[i], packet.Values[i]))
                    { tag = new DxfTag(PointGeometryCodes[i], values[i]); break; }
                tags.Add(tag);
                if (addZ && at == packet.Slots[1]) tags.Add(new DxfTag(30, position.Z));
            }
            return this.WithRecord(record, tags);
        }

        private static readonly short[] PointGeometryCodes = { 10, 20, 30, 39, 210, 220, 230, 50 };
        private sealed class PointPacket
        {
            internal readonly int[] Slots = new int[8];
            internal readonly double[] Values = new double[8];
            internal bool CanEdit = true;
            internal DxfRawPointGeometry Geometry;
        }

        private PointPacket ReadPointPacket(DxfRawRecord record)
        {
            this.ValidateRecordSnapshot(record);
            if (record.MarkerCode != 0 || !RawGeometryName(record.Name, "POINT") ||
                !(RawGeometryName(record.SectionName, "ENTITIES") || RawGeometryName(record.SectionName, "BLOCKS")))
                throw new ArgumentException("Expected a POINT in ENTITIES or BLOCKS of this snapshot.", nameof(record));
            var packet = new PointPacket();
            for (int i = 0; i < packet.Slots.Length; i++) packet.Slots[i] = -1;
            packet.Values[6] = 1.0;
            int depth = 0, subclass = 0;
            bool xdata = false, embedded = false, geometrySeen = false;
            for (int at = 1; at < record.Tags.Count; at++)
            {
                DxfTag tag = record.Tags[at]; short code = tag.Code;
                if (code == 999 || embedded) continue;
                if (code == 102)
                {
                    if (xdata) throw new FormatException("POINT control framing cannot follow XData.");
                    string name = (string)tag.RawValue;
                    if (name.StartsWith("{", StringComparison.Ordinal)) depth++;
                    else if (name == "}" && depth > 0) depth--;
                    else throw new FormatException("Malformed POINT application control group.");
                    packet.CanEdit = false; continue;
                }
                if (depth != 0) continue;
                if (code == 101)
                {
                    if (xdata) throw new FormatException("POINT embedded data cannot follow XData.");
                    if (!RawGeometryName(tag.RawValue as string, "Embedded Object"))
                        throw new NotSupportedException("Unknown POINT embedded marker.");
                    packet.CanEdit = false; embedded = true; continue;
                }
                if (code == 1001) xdata = true;
                if (xdata)
                {
                    if (code < 1000) throw new FormatException("Ordinary POINT data cannot follow XData.");
                    if (RawGeometrySensitiveXDataCode(code)) packet.CanEdit = false;
                    continue;
                }
                if (code >= 1000) throw new FormatException("POINT XData requires an application marker.");
                if (code == 100)
                {
                    string name = (string)tag.RawValue;
                    if (subclass == 0 && !geometrySeen && RawGeometryName(name, "AcDbEntity")) subclass = 1;
                    else if (subclass == 1 && RawGeometryName(name, "AcDbPoint")) subclass = 2;
                    else throw new NotSupportedException("Unknown or ambiguous POINT subclass layout.");
                    continue;
                }
                int slot = Array.IndexOf(PointGeometryCodes, code);
                if (slot >= 0)
                {
                    if (subclass == 1) throw new FormatException("POINT geometry occurs outside its AcDbPoint subclass.");
                    if (packet.Slots[slot] >= 0) throw new FormatException("Duplicate POINT geometry component.");
                    packet.Slots[slot] = at; packet.Values[slot] = (double)tag.RawValue; geometrySeen = true;
                }
                else if (!RawGeometryCommonCode(code)) packet.CanEdit = false;
            }
            if (depth != 0 || subclass == 1) throw new FormatException("Incomplete POINT control or subclass framing.");
            if (packet.Slots[0] < 0 || packet.Slots[1] < 0)
                throw new FormatException("POINT X and Y coordinates are required.");
            var extrusion = new Vector3(packet.Values[4], packet.Values[5], packet.Values[6]);
            if (extrusion.X == 0 && extrusion.Y == 0 && extrusion.Z == 0)
                throw new FormatException("POINT extrusion direction must be nonzero.");
            packet.Geometry = new DxfRawPointGeometry(new Vector3(packet.Values[0], packet.Values[1], packet.Values[2]),
                packet.Values[3], extrusion, packet.Values[7]);
            return packet;
        }
    }
}
