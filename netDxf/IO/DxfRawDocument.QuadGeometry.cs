// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;

namespace netDxf.IO
{
    /// <summary>Immutable stored SOLID/TRACE corners in OCS, thickness and extrusion.</summary>
    public sealed class DxfRawQuadGeometry
    {
        internal DxfRawQuadGeometry(double[] values)
        {
            this.FirstVertexInObjectCoordinates = new Vector3(values[0], values[1], values[2]);
            this.SecondVertexInObjectCoordinates = new Vector3(values[3], values[4], values[5]);
            this.ThirdVertexInObjectCoordinates = new Vector3(values[6], values[7], values[8]);
            this.FourthVertexInObjectCoordinates = new Vector3(values[9], values[10], values[11]);
            this.Thickness = values[12];
            this.ExtrusionDirection = new Vector3(values[13], values[14], values[15]);
        }
        /// <summary>Gets corner 10/20/30 in the unchanged object coordinate system.</summary>
        public Vector3 FirstVertexInObjectCoordinates { get; }
        /// <summary>Gets corner 11/21/31 in the unchanged object coordinate system.</summary>
        public Vector3 SecondVertexInObjectCoordinates { get; }
        /// <summary>Gets corner 12/22/32; DXF storage order is not perimeter order.</summary>
        public Vector3 ThirdVertexInObjectCoordinates { get; }
        /// <summary>Gets corner 13/23/33, repeated from the third corner for triangles.</summary>
        public Vector3 FourthVertexInObjectCoordinates { get; }
        /// <summary>Gets signed thickness, defaulting to positive zero.</summary>
        public double Thickness { get; }
        /// <summary>Gets unchanged, unnormalized extrusion, defaulting to (0,0,1).</summary>
        public Vector3 ExtrusionDirection { get; }
    }

    public sealed partial class DxfRawDocument
    {
        /// <summary>Reads an ordinary SOLID's stored OCS corners without flattening their Z components.</summary>
        /// <param name="record">A SOLID in ENTITIES or BLOCKS belonging to this exact snapshot.</param>
        /// <returns>An immutable stored-geometry view, not a typed entity or surface-validity certificate.</returns>
        public DxfRawQuadGeometry ReadSolidGeometry(DxfRawRecord record)
        { return this.ReadQuadPacket(record, "SOLID").Geometry; }

        /// <summary>Reads an ordinary TRACE's stored OCS corners, signed thickness and extrusion.</summary>
        /// <param name="record">A TRACE in ENTITIES or BLOCKS belonging to this exact snapshot.</param>
        /// <returns>An immutable stored-geometry view without OCS/WCS conversion.</returns>
        public DxfRawQuadGeometry ReadTraceGeometry(DxfRawRecord record)
        { return this.ReadQuadPacket(record, "TRACE").Geometry; }

        /// <summary>Replaces a SOLID's stored OCS corners and thickness in a same-version snapshot.</summary>
        /// <param name="record">A SOLID belonging to this exact snapshot.</param>
        /// <param name="first">Finite first corner in the existing OCS.</param>
        /// <param name="second">Finite second corner in the existing OCS.</param>
        /// <param name="third">Finite third stored corner in the existing OCS.</param>
        /// <param name="fourth">Finite fourth stored corner; repeat third for a triangle.</param>
        /// <param name="thickness">Finite signed thickness in the unchanged extrusion direction.</param>
        /// <returns>A new snapshot, or this snapshot for bit-identical geometry.</returns>
        /// <remarks>Uses the preservation and admission contract of WithTraceGeometry.</remarks>
        public DxfRawDocument WithSolidGeometry(DxfRawRecord record, Vector3 first, Vector3 second,
            Vector3 third, Vector3 fourth, double thickness)
        { return this.ReplaceQuadGeometry(record, "SOLID", first, second, third, fourth, thickness); }

        /// <summary>Replaces a TRACE's OCS corners and thickness without regenerating dependencies.</summary>
        /// <param name="record">A TRACE belonging to this exact snapshot.</param>
        /// <param name="first">Finite first corner in the existing OCS.</param>
        /// <param name="second">Finite second corner in the existing OCS.</param>
        /// <param name="third">Finite third stored corner in the existing OCS.</param>
        /// <param name="fourth">Finite fourth stored corner in the existing OCS.</param>
        /// <param name="thickness">Finite signed thickness.</param>
        /// <returns>A new snapshot, or this snapshot for bit-identical geometry.</returns>
        /// <remarks>
        /// Stored corner order is 10,11,12,13, not perimeter order 10,11,13,12. All four X/Y pairs
        /// are required. Missing Z values default independently to zero. Unequal elevations are
        /// retained, not flattened or certified as valid planar geometry. Extrusion is never changed.
        /// Untouched tags retain identity and order. Missing Z/thickness is inserted only when needed.
        /// Actual edits reject proxies, private/application/embedded data, geometry-sensitive XData
        /// and exposed incoming handle uses. Hidden references, extents and private caches are not
        /// regenerated. Existing raw budgets, historical profile and save restrictions apply.
        /// Changed serialization can normalize spelling/line endings; source bytes remain intact.
        /// </remarks>
        public DxfRawDocument WithTraceGeometry(DxfRawRecord record, Vector3 first, Vector3 second,
            Vector3 third, Vector3 fourth, double thickness)
        { return this.ReplaceQuadGeometry(record, "TRACE", first, second, third, fourth, thickness); }

        private static readonly short[] QuadGeometryCodes =
            { 10, 20, 30, 11, 21, 31, 12, 22, 32, 13, 23, 33, 39, 210, 220, 230 };
        private sealed class RawQuadPacket
        {
            internal readonly int[] Slots = new int[16];
            internal readonly double[] Values = new double[16];
            internal int LastCornerSlot = -1;
            internal bool CanEdit = true;
            internal DxfRawQuadGeometry Geometry;
        }

        private RawQuadPacket ReadQuadPacket(DxfRawRecord record, string kind)
        {
            this.ValidateRecordSnapshot(record);
            if (record.MarkerCode != 0 || !RawGeometryName(record.Name, kind) ||
                !(RawGeometryName(record.SectionName, "ENTITIES") || RawGeometryName(record.SectionName, "BLOCKS")))
                throw new ArgumentException("Expected " + kind + " in ENTITIES or BLOCKS of this snapshot.", nameof(record));
            var packet = new RawQuadPacket();
            for (int i = 0; i < packet.Slots.Length; i++) packet.Slots[i] = -1;
            packet.Values[15] = 1.0;
            int depth = 0, subclass = 0;
            bool xdata = false, embedded = false, geometrySeen = false;
            for (int at = 1; at < record.Tags.Count; at++)
            {
                DxfTag tag = record.Tags[at]; short code = tag.Code;
                if (code == 999 || embedded) continue;
                if (code == 102)
                {
                    if (xdata) throw new FormatException("Quad control framing cannot follow XData.");
                    string name = (string)tag.RawValue;
                    if (name.StartsWith("{", StringComparison.Ordinal)) depth++;
                    else if (name == "}" && depth > 0) depth--;
                    else throw new FormatException("Malformed quad application control group.");
                    packet.CanEdit = false; continue;
                }
                if (depth != 0) continue;
                if (code == 101)
                {
                    if (xdata) throw new FormatException("Quad embedded data cannot follow XData.");
                    if (!RawGeometryName(tag.RawValue as string, "Embedded Object"))
                        throw new NotSupportedException("Unknown quad embedded marker.");
                    packet.CanEdit = false; embedded = true; continue;
                }
                if (code == 1001) xdata = true;
                if (xdata)
                {
                    if (code < 1000) throw new FormatException("Ordinary quad data cannot follow XData.");
                    if (RawGeometrySensitiveXDataCode(code)) packet.CanEdit = false;
                    continue;
                }
                if (code >= 1000) throw new FormatException("Quad XData requires an application marker.");
                if (code == 100)
                {
                    string name = (string)tag.RawValue;
                    if (subclass == 0 && !geometrySeen && RawGeometryName(name, "AcDbEntity")) subclass = 1;
                    else if (subclass == 1 && RawGeometryName(name, "AcDbTrace")) subclass = 2;
                    else throw new NotSupportedException("Unknown or ambiguous quad subclass layout.");
                    continue;
                }
                int slot = Array.IndexOf(QuadGeometryCodes, code);
                if (slot >= 0)
                {
                    if (subclass == 1) throw new FormatException("Quad geometry is outside AcDbTrace.");
                    if (packet.Slots[slot] >= 0) throw new FormatException("Duplicate quad geometry component.");
                    packet.Slots[slot] = at; packet.Values[slot] = (double)tag.RawValue;
                    if (slot < 12) packet.LastCornerSlot = at;
                    geometrySeen = true;
                }
                else if (!RawGeometryCommonCode(code)) packet.CanEdit = false;
            }
            if (depth != 0 || subclass == 1) throw new FormatException("Incomplete quad control or subclass framing.");
            for (int i = 0; i < 12; i += 3)
                if (packet.Slots[i] < 0 || packet.Slots[i + 1] < 0)
                    throw new FormatException("All four quad X/Y corner pairs are required.");
            if (packet.Values[13] == 0 && packet.Values[14] == 0 && packet.Values[15] == 0)
                throw new FormatException("Quad extrusion must be nonzero.");
            packet.Geometry = new DxfRawQuadGeometry(packet.Values);
            return packet;
        }

        private DxfRawDocument ReplaceQuadGeometry(DxfRawRecord record, string kind, Vector3 first,
            Vector3 second, Vector3 third, Vector3 fourth, double thickness)
        {
            RawQuadPacket packet = this.ReadQuadPacket(record, kind);
            CheckRawGeometryPoint(first, nameof(first)); CheckRawGeometryPoint(second, nameof(second));
            CheckRawGeometryPoint(third, nameof(third)); CheckRawGeometryPoint(fourth, nameof(fourth));
            if (double.IsNaN(thickness) || double.IsInfinity(thickness))
                throw new ArgumentOutOfRangeException(nameof(thickness), "Thickness must be finite.");
            double[] values = { first.X, first.Y, first.Z, second.X, second.Y, second.Z,
                third.X, third.Y, third.Z, fourth.X, fourth.Y, fourth.Z, thickness };
            bool changed = false;
            for (int i = 0; i < values.Length; i++) changed |= !SameRawGeometryScalar(values[i], packet.Values[i]);
            if (!changed) return this;
            if (!packet.CanEdit)
                throw new NotSupportedException("Quad geometry has proxy, private, unknown or geometry-sensitive data requiring regeneration.");
            this.CheckRawGeometryIncomingReferences(record);
            int additions = 0;
            for (int i = 0; i < values.Length; i++)
                if (packet.Slots[i] < 0 && !SameRawGeometryScalar(values[i], 0.0)) additions++;
            if ((long)this.Tags.Count + additions > this.options.MaximumTags)
                throw new InvalidOperationException("The quad edit exceeds the raw tag budget.");
            var tags = new List<DxfTag>(record.Tags.Count + additions);
            for (int at = 0; at < record.Tags.Count; at++)
            {
                DxfTag tag = record.Tags[at];
                for (int i = 0; i < values.Length; i++)
                    if (packet.Slots[i] == at && !SameRawGeometryScalar(values[i], packet.Values[i]))
                    { tag = new DxfTag(QuadGeometryCodes[i], values[i]); break; }
                tags.Add(tag);
                for (int i = 2; i < 12; i += 3)
                    if (packet.Slots[i] < 0 && at == packet.Slots[i - 1] && !SameRawGeometryScalar(values[i], 0.0))
                        tags.Add(new DxfTag(QuadGeometryCodes[i], values[i]));
                if (at == packet.LastCornerSlot && packet.Slots[12] < 0 && !SameRawGeometryScalar(thickness, 0.0))
                    tags.Add(new DxfTag(39, thickness));
            }
            return this.WithRecord(record, tags);
        }
    }
}
