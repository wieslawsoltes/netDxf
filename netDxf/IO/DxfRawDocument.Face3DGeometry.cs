// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using netDxf.Entities;

namespace netDxf.IO
{
    /// <summary>Immutable stored 3DFACE corners and invisible-edge flags; no ownership is transferred.</summary>
    public sealed class DxfRawFace3DGeometry
    {
        internal DxfRawFace3DGeometry(Vector3 first, Vector3 second, Vector3 third, Vector3 fourth, Face3DEdgeFlags flags)
        {
            this.FirstVertex = first; this.SecondVertex = second;
            this.ThirdVertex = third; this.FourthVertex = fourth; this.EdgeFlags = flags;
        }
        /// <summary>Gets the first world-coordinate corner (groups 10/20/30).</summary>
        public Vector3 FirstVertex { get; }
        /// <summary>Gets the second world-coordinate corner (groups 11/21/31).</summary>
        public Vector3 SecondVertex { get; }
        /// <summary>Gets the third world-coordinate corner (groups 12/22/32).</summary>
        public Vector3 ThirdVertex { get; }
        /// <summary>Gets the fourth world-coordinate corner; triangular faces repeat the third corner.</summary>
        public Vector3 FourthVertex { get; }
        /// <summary>Gets the four stored invisible-edge bits, defaulting to no hidden edges.</summary>
        public Face3DEdgeFlags EdgeFlags { get; }
    }

    public sealed partial class DxfRawDocument
    {
        /// <summary>Reads ordinary 3DFACE geometry in the original raw DXF profile.</summary>
        /// <param name="record">A 3DFACE in ENTITIES or BLOCKS belonging to this exact snapshot.</param>
        /// <returns>An immutable view of the four WCS corners and invisible-edge flags.</returns>
        /// <remarks>
        /// All four X/Y corner pairs are required, including an explicit repeated fourth corner for
        /// triangles. Omitted Z components default to zero. Only edge bits 1, 2, 4 and 8 are admitted.
        /// Classic markerless and AcDbEntity/AcDbFace layouts are supported. Private control groups
        /// and embedded tails cannot supply core geometry. This is not full historical typed loading.
        /// </remarks>
        public DxfRawFace3DGeometry ReadFace3DGeometry(DxfRawRecord record)
        { return this.ReadFace3DPacket(record).Geometry; }

        /// <summary>Replaces four WCS corners and edge visibility in a same-version raw snapshot.</summary>
        /// <param name="record">A 3DFACE belonging to this exact snapshot.</param>
        /// <param name="firstVertex">Finite first WCS corner.</param>
        /// <param name="secondVertex">Finite second WCS corner.</param>
        /// <param name="thirdVertex">Finite third WCS corner.</param>
        /// <param name="fourthVertex">Finite fourth WCS corner; repeat the third to represent a triangle.</param>
        /// <param name="edgeFlags">Invisible-edge bits; no flags outside the low four bits are accepted.</param>
        /// <returns>A new snapshot, or this snapshot for a bit-identical no-op.</returns>
        /// <remarks>
        /// Corners retain DXF ordering, with no OCS transform, triangulation or planarity inference.
        /// Missing Z and edge-flag fields are materialized only when their new values require them.
        /// Untouched tags keep their identity and relative order. Source bytes are unchanged; edited
        /// serialization may normalize spelling and line endings. Actual changes conservatively reject
        /// proxies, application/embedded data, unknown fields, geometry-sensitive XData and exposed
        /// incoming handle uses. Header extents, hidden references and private caches are not updated.
        /// Existing raw/index budgets and output restrictions apply; source geometry may be degenerate.
        /// </remarks>
        public DxfRawDocument WithFace3DGeometry(DxfRawRecord record, Vector3 firstVertex,
            Vector3 secondVertex, Vector3 thirdVertex, Vector3 fourthVertex, Face3DEdgeFlags edgeFlags)
        {
            Face3DPacket packet = this.ReadFace3DPacket(record);
            CheckRawGeometryPoint(firstVertex, nameof(firstVertex));
            CheckRawGeometryPoint(secondVertex, nameof(secondVertex));
            CheckRawGeometryPoint(thirdVertex, nameof(thirdVertex));
            CheckRawGeometryPoint(fourthVertex, nameof(fourthVertex));
            if (((int)edgeFlags & ~15) != 0)
                throw new ArgumentOutOfRangeException(nameof(edgeFlags), "Only the four invisible-edge bits are defined.");
            double[] values = { firstVertex.X, firstVertex.Y, firstVertex.Z, secondVertex.X, secondVertex.Y,
                secondVertex.Z, thirdVertex.X, thirdVertex.Y, thirdVertex.Z, fourthVertex.X, fourthVertex.Y, fourthVertex.Z };
            bool changed = edgeFlags != packet.Geometry.EdgeFlags;
            for (int i = 0; i < 12; i++) changed |= !SameRawGeometryScalar(values[i], packet.Values[i]);
            if (!changed) return this;
            if (!packet.CanEdit)
                throw new NotSupportedException("This 3DFACE has proxy, private, geometry-sensitive XData or unknown fields requiring explicit regeneration.");
            this.CheckRawGeometryIncomingReferences(record);
            int additions = packet.FlagsSlot < 0 && edgeFlags != Face3DEdgeFlags.None ? 1 : 0;
            for (int i = 2; i < 12; i += 3)
                if (packet.Slots[i] < 0 && !SameRawGeometryScalar(values[i], 0.0)) additions++;
            if ((long)this.Tags.Count + additions > this.options.MaximumTags)
                throw new InvalidOperationException("The 3DFACE edit exceeds the raw tag budget.");
            var tags = new List<DxfTag>(record.Tags.Count + additions);
            for (int at = 0; at < record.Tags.Count; at++)
            {
                DxfTag tag = record.Tags[at];
                for (int i = 0; i < 12; i++)
                    if (packet.Slots[i] == at && !SameRawGeometryScalar(values[i], packet.Values[i]))
                    { tag = new DxfTag(Face3DGeometryCodes[i], values[i]); break; }
                if (at == packet.FlagsSlot && edgeFlags != packet.Geometry.EdgeFlags)
                    tag = new DxfTag(70, (short)edgeFlags);
                tags.Add(tag);
                for (int i = 2; i < 12; i += 3)
                    if (packet.Slots[i] < 0 && at == packet.Slots[i - 1] && !SameRawGeometryScalar(values[i], 0.0))
                        tags.Add(new DxfTag(Face3DGeometryCodes[i], values[i]));
                if (at == packet.LastGeometrySlot && packet.FlagsSlot < 0 && edgeFlags != Face3DEdgeFlags.None)
                    tags.Add(new DxfTag(70, (short)edgeFlags));
            }
            return this.WithRecord(record, tags);
        }

        private static readonly short[] Face3DGeometryCodes = { 10, 20, 30, 11, 21, 31, 12, 22, 32, 13, 23, 33 };
        private sealed class Face3DPacket
        {
            internal readonly int[] Slots = new int[12];
            internal readonly double[] Values = new double[12];
            internal int FlagsSlot = -1, LastGeometrySlot = -1;
            internal bool CanEdit = true;
            internal DxfRawFace3DGeometry Geometry;
        }

        private Face3DPacket ReadFace3DPacket(DxfRawRecord record)
        {
            this.ValidateRecordSnapshot(record);
            if (record.MarkerCode != 0 || !RawGeometryName(record.Name, "3DFACE") ||
                !(RawGeometryName(record.SectionName, "ENTITIES") || RawGeometryName(record.SectionName, "BLOCKS")))
                throw new ArgumentException("Expected a 3DFACE in ENTITIES or BLOCKS of this snapshot.", nameof(record));
            var packet = new Face3DPacket();
            for (int i = 0; i < packet.Slots.Length; i++) packet.Slots[i] = -1;
            int depth = 0, subclass = 0;
            bool xdata = false, embedded = false, geometrySeen = false;
            short flags = 0;
            for (int at = 1; at < record.Tags.Count; at++)
            {
                DxfTag tag = record.Tags[at]; short code = tag.Code;
                if (code == 999 || embedded) continue;
                if (code == 102)
                {
                    if (xdata) throw new FormatException("3DFACE control framing cannot follow XData.");
                    string name = (string)tag.RawValue;
                    if (name.StartsWith("{", StringComparison.Ordinal)) depth++;
                    else if (name == "}" && depth > 0) depth--;
                    else throw new FormatException("Malformed 3DFACE application control group.");
                    packet.CanEdit = false; continue;
                }
                if (depth != 0) continue;
                if (code == 101)
                {
                    if (xdata) throw new FormatException("3DFACE embedded data cannot follow XData.");
                    if (!RawGeometryName(tag.RawValue as string, "Embedded Object"))
                        throw new NotSupportedException("Unknown 3DFACE embedded marker.");
                    packet.CanEdit = false; embedded = true; continue;
                }
                if (code == 1001) xdata = true;
                if (xdata)
                {
                    if (code < 1000) throw new FormatException("Ordinary 3DFACE data cannot follow XData.");
                    if (RawGeometrySensitiveXDataCode(code)) packet.CanEdit = false;
                    continue;
                }
                if (code >= 1000) throw new FormatException("3DFACE XData requires an application marker.");
                if (code == 100)
                {
                    string name = (string)tag.RawValue;
                    if (subclass == 0 && !geometrySeen && RawGeometryName(name, "AcDbEntity")) subclass = 1;
                    else if (subclass == 1 && RawGeometryName(name, "AcDbFace")) subclass = 2;
                    else throw new NotSupportedException("Unknown or ambiguous 3DFACE subclass layout.");
                    continue;
                }
                int slot = Array.IndexOf(Face3DGeometryCodes, code);
                if (slot >= 0 || code == 70)
                {
                    if (subclass == 1) throw new FormatException("3DFACE geometry occurs outside its AcDbFace subclass.");
                    if (code == 70)
                    {
                        if (packet.FlagsSlot >= 0) throw new FormatException("Duplicate 3DFACE edge flags.");
                        packet.FlagsSlot = at; flags = (short)tag.RawValue;
                        if ((flags & ~15) != 0) throw new FormatException("Undefined 3DFACE invisible-edge bits.");
                    }
                    else
                    {
                        if (packet.Slots[slot] >= 0) throw new FormatException("Duplicate 3DFACE coordinate component.");
                        packet.Slots[slot] = at; packet.Values[slot] = (double)tag.RawValue;
                        packet.LastGeometrySlot = at;
                    }
                    geometrySeen = true;
                }
                else if (!RawGeometryCommonCode(code)) packet.CanEdit = false;
            }
            if (depth != 0 || subclass == 1) throw new FormatException("Incomplete 3DFACE control or subclass framing.");
            for (int i = 0; i < 12; i += 3)
                if (packet.Slots[i] < 0 || packet.Slots[i + 1] < 0)
                    throw new FormatException("All four 3DFACE X/Y corner pairs are required, including the repeated corner of a triangle.");
            packet.Geometry = new DxfRawFace3DGeometry(
                new Vector3(packet.Values[0], packet.Values[1], packet.Values[2]),
                new Vector3(packet.Values[3], packet.Values[4], packet.Values[5]),
                new Vector3(packet.Values[6], packet.Values[7], packet.Values[8]),
                new Vector3(packet.Values[9], packet.Values[10], packet.Values[11]), (Face3DEdgeFlags)flags);
            return packet;
        }
    }
}
