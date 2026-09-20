// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using netDxf.Header;

namespace netDxf.IO
{
    /// <summary>Immutable stored lightweight-polyline vertex, in object coordinates.</summary>
    public sealed class DxfRawLwPolylineVertex
    {
        internal DxfRawLwPolylineVertex(Vector2 position, double start, double end, double bulge,
            int? identifier, bool hasStart, bool hasEnd, bool hasBulge)
        {
            this.Position = position; this.StartWidth = start; this.EndWidth = end; this.Bulge = bulge;
            this.Identifier = identifier; this.HasStartWidth = hasStart; this.HasEndWidth = hasEnd;
            this.HasBulge = hasBulge;
        }
        /// <summary>Gets the stored X/Y position in the entity's OCS, not WCS.</summary>
        public Vector2 Position { get; }
        /// <summary>Gets stored starting width, default zero; constant-width precedence is not applied.</summary>
        public double StartWidth { get; }
        /// <summary>Gets stored ending width, default zero; constant-width precedence is not applied.</summary>
        public double EndWidth { get; }
        /// <summary>Gets the outgoing segment bulge, default zero, including the final vertex.</summary>
        public double Bulge { get; }
        /// <summary>Gets the optional opaque signed group-91 identifier.</summary>
        public int? Identifier { get; }
        /// <summary>Gets whether a starting-width tag is explicitly present.</summary>
        public bool HasStartWidth { get; }
        /// <summary>Gets whether an ending-width tag is explicitly present.</summary>
        public bool HasEndWidth { get; }
        /// <summary>Gets whether a bulge tag is explicitly present.</summary>
        public bool HasBulge { get; }
    }

    /// <summary>Immutable stored LWPOLYLINE definition, without tessellation or OCS conversion.</summary>
    public sealed class DxfRawLwPolylineGeometry
    {
        internal DxfRawLwPolylineGeometry(DxfRawLwPolylineVertex[] vertices, short flags,
            double width, double elevation, double thickness, Vector3 extrusion)
        {
            this.Vertices = Array.AsReadOnly(vertices); this.Flags = flags; this.ConstantWidth = width;
            this.Elevation = elevation; this.Thickness = thickness; this.ExtrusionDirection = extrusion;
        }
        /// <summary>Gets independent read-only vertices in original traversal order.</summary>
        public IReadOnlyList<DxfRawLwPolylineVertex> Vertices { get; }
        /// <summary>Gets the stored closed and continuous-linetype flags (bits 1 and 128).</summary>
        public short Flags { get; }
        /// <summary>Gets whether the closing segment is enabled.</summary>
        public bool IsClosed { get { return (this.Flags & 1) != 0; } }
        /// <summary>Gets stored constant width, default zero, without resolving per-vertex widths.</summary>
        public double ConstantWidth { get; }
        /// <summary>Gets the OCS elevation, default zero.</summary>
        public double Elevation { get; }
        /// <summary>Gets the signed extrusion thickness, default zero.</summary>
        public double Thickness { get; }
        /// <summary>Gets the stored extrusion vector without normalization.</summary>
        public Vector3 ExtrusionDirection { get; }
    }

    public sealed partial class DxfRawDocument
    {
        /// <summary>Maximum stored vertices admitted by the selected raw lightweight-polyline API.</summary>
        public const int MaximumRawLwPolylineVertices = 1000000;

        /// <summary>Reads repeated LWPOLYLINE vertex packets in R14 or later raw profiles.</summary>
        /// <param name="record">A LWPOLYLINE in ENTITIES or BLOCKS of this exact snapshot.</param>
        /// <returns>Independent immutable OCS geometry, widths, bulges and vertex identifiers.</returns>
        /// <remarks>Declared count must match actual packets. Empty definitions are retained; surface validity is not inferred.</remarks>
        public DxfRawLwPolylineGeometry ReadLwPolylineGeometry(DxfRawRecord record)
        { return this.ReadLwPolylinePacket(record).Geometry; }

        /// <summary>Edits one existing LWPOLYLINE vertex without changing topology, identifiers or the entity plane.</summary>
        /// <param name="record">A LWPOLYLINE belonging to this exact snapshot.</param>
        /// <param name="vertexIndex">Zero-based index in the original traversal order.</param>
        /// <param name="position">Finite X/Y position in the existing OCS.</param>
        /// <param name="startWidth">Finite nonnegative starting width.</param>
        /// <param name="endWidth">Finite nonnegative ending width.</param>
        /// <param name="bulge">Finite outgoing-segment bulge, without arc fitting or normalization.</param>
        /// <returns>A new same-version snapshot, or this snapshot for a bit-identical no-op.</returns>
        /// <remarks>
        /// Optional zero fields stay absent unless their value changes, including signed-zero changes.
        /// Existing slots are replaced in place; missing optional fields follow the vertex's last field.
        /// Count, flags, constant width, plane and all vertex identifiers are retained. Width changes
        /// reject when nonzero constant width is present, rather than choosing conflicting precedence.
        /// Actual edits use the shared conservative dependency guards; hidden caches, extents and
        /// application constraints are not regenerated. Changed serialization may normalize spelling.
        /// Parsing is linear in tags/vertices, with no allocation based solely on an untrusted count.
        /// Existing raw/tag/index budgets also apply. Historical typed loading is not enabled.
        /// </remarks>
        public DxfRawDocument WithLwPolylineVertex(DxfRawRecord record, int vertexIndex, Vector2 position,
            double startWidth, double endWidth, double bulge)
        {
            LwPolylinePacket packet = this.ReadLwPolylinePacket(record);
            if (vertexIndex < 0 || vertexIndex >= packet.Vertices.Count)
                throw new ArgumentOutOfRangeException(nameof(vertexIndex));
            CheckRawGeometryPoint(new Vector3(position.X, position.Y, 0), nameof(position));
            CheckRawLwWidth(startWidth, nameof(startWidth)); CheckRawLwWidth(endWidth, nameof(endWidth));
            if (double.IsNaN(bulge) || double.IsInfinity(bulge)) throw new ArgumentOutOfRangeException(nameof(bulge));
            LwVertexPacket vertex = packet.Vertices[vertexIndex];
            double[] values = { position.X, position.Y, startWidth, endWidth, bulge };
            bool changed = false;
            for (int i = 0; i < 5; i++) changed |= !SameRawGeometryScalar(values[i], vertex.Values[i]);
            if (!changed) return this;
            if (!packet.CanEdit) throw new NotSupportedException("LWPOLYLINE contains data requiring explicit regeneration.");
            if (packet.Geometry.ConstantWidth != 0 &&
                (!SameRawGeometryScalar(values[2], vertex.Values[2]) || !SameRawGeometryScalar(values[3], vertex.Values[3])))
                throw new NotSupportedException("Changing per-vertex widths while constant width is nonzero requires an explicit width-policy edit.");
            this.CheckRawGeometryIncomingReferences(record);
            int additions = 0;
            for (int i = 2; i < 5; i++)
                if (vertex.Slots[i] < 0 && !SameRawGeometryScalar(values[i], 0.0)) additions++;
            if ((long)this.Tags.Count + additions > this.options.MaximumTags)
                throw new InvalidOperationException("The vertex edit exceeds the raw tag budget.");
            var tags = new List<DxfTag>(record.Tags.Count + additions);
            for (int at = 0; at < record.Tags.Count; at++)
            {
                DxfTag tag = record.Tags[at];
                for (int i = 0; i < 5; i++)
                    if (vertex.Slots[i] == at && !SameRawGeometryScalar(values[i], vertex.Values[i]))
                    { tag = new DxfTag(RawLwVertexCodes[i], values[i]); break; }
                tags.Add(tag);
                if (at == vertex.LastSlot)
                    for (int i = 2; i < 5; i++)
                        if (vertex.Slots[i] < 0 && !SameRawGeometryScalar(values[i], 0.0))
                            tags.Add(new DxfTag(RawLwVertexCodes[i], values[i]));
            }
            return this.WithRecord(record, tags);
        }

        private static readonly short[] RawLwVertexCodes = { 10, 20, 40, 41, 42 };
        private sealed class LwVertexPacket
        {
            internal readonly int[] Slots = { -1, -1, -1, -1, -1 };
            internal readonly double[] Values = new double[5];
            internal int LastSlot;
            internal int? Identifier;
        }
        private sealed class LwPolylinePacket
        {
            internal readonly List<LwVertexPacket> Vertices = new List<LwVertexPacket>();
            internal bool CanEdit = true;
            internal DxfRawLwPolylineGeometry Geometry;
        }
        private LwPolylinePacket ReadLwPolylinePacket(DxfRawRecord record)
        {
            this.ValidateRecordSnapshot(record);
            if (record.MarkerCode != 0 || !RawGeometryName(record.Name, "LWPOLYLINE") ||
                !(RawGeometryName(record.SectionName, "ENTITIES") || RawGeometryName(record.SectionName, "BLOCKS")))
                throw new ArgumentException("Expected a LWPOLYLINE in this snapshot's ENTITIES or BLOCKS section.", nameof(record));
            if (this.Version < DxfVersion.AutoCad14) throw new NotSupportedException("LWPOLYLINE geometry requires R14 or later.");
            var packet = new LwPolylinePacket(); var headers = new HashSet<short>();
            int depth = 0, subclass = 0, count = -1; short flags = 0;
            double width = 0, elevation = 0, thickness = 0, nx = 0, ny = 0, nz = 1;
            bool xdata = false, embedded = false, geometrySeen = false;
            LwVertexPacket current = null;
            for (int at = 1; at < record.Tags.Count; at++)
            {
                DxfTag tag = record.Tags[at]; short code = tag.Code;
                if (code == 999 || embedded) continue;
                if (code == 102)
                {
                    if (xdata) throw new FormatException("Application controls cannot follow LWPOLYLINE XData.");
                    string name = (string)tag.RawValue;
                    if (name.StartsWith("{", StringComparison.Ordinal)) depth++;
                    else if (name == "}" && depth > 0) depth--;
                    else throw new FormatException("Malformed LWPOLYLINE application control.");
                    packet.CanEdit = false; continue;
                }
                if (depth != 0) continue;
                if (code == 101)
                {
                    if (xdata) throw new FormatException("Embedded data cannot follow LWPOLYLINE XData.");
                    if (!RawGeometryName(tag.RawValue as string, "Embedded Object")) throw new NotSupportedException("Unknown embedded marker.");
                    packet.CanEdit = false; embedded = true; continue;
                }
                if (code == 1001) xdata = true;
                if (xdata)
                {
                    if (code < 1000) throw new FormatException("Ordinary fields cannot follow LWPOLYLINE XData.");
                    if (RawGeometrySensitiveXDataCode(code)) packet.CanEdit = false;
                    continue;
                }
                if (code >= 1000) throw new FormatException("LWPOLYLINE XData requires an application marker.");
                if (code == 100)
                {
                    string name = (string)tag.RawValue;
                    if (subclass == 0 && !geometrySeen && RawGeometryName(name, "AcDbEntity")) subclass = 1;
                    else if (subclass == 1 && RawGeometryName(name, "AcDbPolyline")) subclass = 2;
                    else throw new NotSupportedException("Unknown or ambiguous LWPOLYLINE subclass layout.");
                    continue;
                }
                int slot = Array.IndexOf(RawLwVertexCodes, code);
                bool header = code == 90 || code == 70 || code == 43 || code == 38 || code == 39 || code == 210 || code == 220 || code == 230;
                if (slot >= 0 || code == 91 || header)
                {
                    if (subclass == 1) throw new FormatException("LWPOLYLINE geometry is outside AcDbPolyline.");
                    geometrySeen = true;
                    if (header)
                    {
                        if (!headers.Add(code)) throw new FormatException("Duplicate LWPOLYLINE header field.");
                        switch (code)
                        {
                            case 90:
                                count = (int)tag.RawValue;
                                if (count < 0 || count > MaximumRawLwPolylineVertices) throw new FormatException("Invalid or excessive LWPOLYLINE vertex count.");
                                break;
                            case 70: flags = (short)tag.RawValue; if ((flags & ~129) != 0) throw new FormatException("Undefined LWPOLYLINE flags."); break;
                            case 43: width = (double)tag.RawValue; if (width < 0) throw new FormatException("Negative constant width."); break;
                            case 38: elevation = (double)tag.RawValue; break;
                            case 39: thickness = (double)tag.RawValue; break;
                            case 210: nx = (double)tag.RawValue; break;
                            case 220: ny = (double)tag.RawValue; break;
                            case 230: nz = (double)tag.RawValue; break;
                        }
                        continue;
                    }
                    if (code == 10)
                    {
                        if (current != null && current.Slots[1] < 0) throw new FormatException("Vertex Y is missing before the next vertex.");
                        if (packet.Vertices.Count >= MaximumRawLwPolylineVertices) throw new FormatException("Vertex budget exceeded.");
                        current = new LwVertexPacket(); packet.Vertices.Add(current);
                    }
                    if (current == null) throw new FormatException("Vertex attributes require a preceding vertex X.");
                    if (code == 91)
                    {
                        if (current.Identifier.HasValue) throw new FormatException("Duplicate vertex identifier.");
                        current.Identifier = (int)tag.RawValue;
                    }
                    else
                    {
                        if (current.Slots[slot] >= 0) throw new FormatException("Duplicate per-vertex field.");
                        current.Slots[slot] = at; current.Values[slot] = (double)tag.RawValue;
                        if ((code == 40 || code == 41) && current.Values[slot] < 0) throw new FormatException("Negative vertex width.");
                    }
                    current.LastSlot = at;
                }
                else if (!RawGeometryCommonCode(code)) packet.CanEdit = false;
            }
            if (depth != 0 || subclass == 1) throw new FormatException("Incomplete LWPOLYLINE control or subclass framing.");
            if (count < 0 || count != packet.Vertices.Count || (current != null && current.Slots[1] < 0))
                throw new FormatException("Declared count or vertex X/Y packets are incomplete.");
            if (nx == 0 && ny == 0 && nz == 0) throw new FormatException("Extrusion must be nonzero.");
            var vertices = new DxfRawLwPolylineVertex[count];
            for (int i = 0; i < count; i++)
            {
                LwVertexPacket v = packet.Vertices[i];
                vertices[i] = new DxfRawLwPolylineVertex(new Vector2(v.Values[0], v.Values[1]), v.Values[2], v.Values[3],
                    v.Values[4], v.Identifier, v.Slots[2] >= 0, v.Slots[3] >= 0, v.Slots[4] >= 0);
            }
            packet.Geometry = new DxfRawLwPolylineGeometry(vertices, flags, width, elevation, thickness, new Vector3(nx, ny, nz));
            return packet;
        }
        private static void CheckRawLwWidth(double value, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0) throw new ArgumentOutOfRangeException(name);
        }
    }
}
