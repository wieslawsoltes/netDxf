// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;

namespace netDxf.IO
{
    /// <summary>Immutable stored CIRCLE geometry. The center is in object coordinates, not WCS.</summary>
    public sealed class DxfRawCircleGeometry
    {
        internal DxfRawCircleGeometry(Vector3 center, double radius, double thickness, Vector3 extrusion)
        { this.CenterInObjectCoordinates = center; this.Radius = radius; this.Thickness = thickness; this.ExtrusionDirection = extrusion; }
        /// <summary>Gets the stored center in the entity's object coordinate system.</summary>
        public Vector3 CenterInObjectCoordinates { get; }
        /// <summary>Gets the positive stored radius.</summary>
        public double Radius { get; }
        /// <summary>Gets signed thickness, defaulting to zero.</summary>
        public double Thickness { get; }
        /// <summary>Gets the stored extrusion vector without normalization, defaulting to (0,0,1).</summary>
        public Vector3 ExtrusionDirection { get; }
    }

    /// <summary>Immutable stored ARC geometry with object-coordinate center and angles in degrees.</summary>
    public sealed class DxfRawArcGeometry
    {
        internal DxfRawArcGeometry(DxfRawCircleGeometry circle, double start, double end)
        {
            this.CenterInObjectCoordinates = circle.CenterInObjectCoordinates;
            this.Radius = circle.Radius; this.Thickness = circle.Thickness;
            this.ExtrusionDirection = circle.ExtrusionDirection;
            this.StartAngle = start; this.EndAngle = end;
        }
        /// <summary>Gets the stored center in the entity's object coordinate system.</summary>
        public Vector3 CenterInObjectCoordinates { get; }
        /// <summary>Gets the positive stored radius.</summary>
        public double Radius { get; }
        /// <summary>Gets signed thickness, defaulting to zero.</summary>
        public double Thickness { get; }
        /// <summary>Gets the stored extrusion vector without normalization, defaulting to (0,0,1).</summary>
        public Vector3 ExtrusionDirection { get; }
        /// <summary>Gets the stored start angle in degrees, without normalization.</summary>
        public double StartAngle { get; }
        /// <summary>Gets the stored end angle in degrees, without normalization.</summary>
        public double EndAngle { get; }
    }

    public sealed partial class DxfRawDocument
    {
        /// <summary>Reads an ordinary raw CIRCLE without changing its coordinate system or tags.</summary>
        /// <param name="record">A CIRCLE in ENTITIES or BLOCKS of this exact snapshot.</param>
        /// <returns>Stored OCS geometry, not a typed database entity.</returns>
        public DxfRawCircleGeometry ReadCircleGeometry(DxfRawRecord record)
        { return this.ReadConicPacket(record, false).Circle; }

        /// <summary>Reads an ordinary raw ARC without normalizing its angles or extrusion.</summary>
        /// <param name="record">An ARC in ENTITIES or BLOCKS of this exact snapshot.</param>
        /// <returns>Stored OCS geometry with angles in degrees.</returns>
        public DxfRawArcGeometry ReadArcGeometry(DxfRawRecord record)
        {
            ConicPacket packet = this.ReadConicPacket(record, true);
            return new DxfRawArcGeometry(packet.Circle, packet.Values[4], packet.Values[5]);
        }

        /// <summary>Replaces a raw CIRCLE's OCS center and radius in a same-version immutable snapshot.</summary>
        /// <param name="record">A CIRCLE belonging to this exact snapshot.</param>
        /// <param name="centerInObjectCoordinates">Finite center in the existing entity OCS, not WCS.</param>
        /// <param name="radius">Finite, strictly positive radius.</param>
        /// <returns>A new snapshot, or this snapshot for a bit-identical no-op.</returns>
        /// <remarks>Uses the guarded edit contract documented by WithArcGeometry.</remarks>
        public DxfRawDocument WithCircleGeometry(DxfRawRecord record, Vector3 centerInObjectCoordinates, double radius)
        {
            ConicPacket packet = this.ReadConicPacket(record, false);
            CheckRawGeometryPoint(centerInObjectCoordinates, nameof(centerInObjectCoordinates));
            CheckConicRadius(radius, nameof(radius));
            return this.ReplaceConicGeometry(record, packet,
                new[] { centerInObjectCoordinates.X, centerInObjectCoordinates.Y, centerInObjectCoordinates.Z, radius });
        }

        /// <summary>Replaces a raw ARC's OCS center, radius and degree angles without regenerating dependencies.</summary>
        /// <param name="record">An ARC belonging to this exact snapshot.</param>
        /// <param name="centerInObjectCoordinates">Finite center in the existing entity OCS, not WCS.</param>
        /// <param name="radius">Finite, strictly positive radius.</param>
        /// <param name="startAngle">Finite start angle in degrees, stored without normalization.</param>
        /// <param name="endAngle">Finite end angle in degrees, stored without normalization.</param>
        /// <returns>A new snapshot, or this snapshot for a bit-identical no-op.</returns>
        /// <remarks>
        /// Only selected scalar slots are replaced; omitted center Z is inserted after Y when needed.
        /// Thickness, extrusion and every unselected tag retain identity and relative order. The source
        /// snapshot remains unchanged. Actual changes reject proxies, control/embedded data, unknown
        /// ordinary fields, geometry-sensitive XData and exposed incoming handle uses. They do not
        /// update extents, hidden dependencies or associative geometry. No typed historical dialect,
        /// general version conversion, OCS/WCS conversion or arc-sweep reinterpretation is introduced.
        /// Existing raw resource, transport and save contracts apply. Changed serialization may
        /// normalize numeric spelling and line endings; only no-ops retain original-byte output.
        /// </remarks>
        public DxfRawDocument WithArcGeometry(DxfRawRecord record, Vector3 centerInObjectCoordinates,
            double radius, double startAngle, double endAngle)
        {
            ConicPacket packet = this.ReadConicPacket(record, true);
            CheckRawGeometryPoint(centerInObjectCoordinates, nameof(centerInObjectCoordinates));
            CheckConicRadius(radius, nameof(radius));
            CheckConicAngle(startAngle, nameof(startAngle)); CheckConicAngle(endAngle, nameof(endAngle));
            return this.ReplaceConicGeometry(record, packet, new[] { centerInObjectCoordinates.X,
                centerInObjectCoordinates.Y, centerInObjectCoordinates.Z, radius, startAngle, endAngle });
        }

        private static readonly short[] ConicCodes = { 10, 20, 30, 40, 50, 51, 39, 210, 220, 230 };
        private sealed class ConicPacket
        {
            internal readonly int[] Slots = new int[10];
            internal readonly double[] Values = new double[10];
            internal bool CanEdit = true;
            internal DxfRawCircleGeometry Circle;
        }

        private ConicPacket ReadConicPacket(DxfRawRecord record, bool arc)
        {
            this.ValidateRecordSnapshot(record);
            if (record.MarkerCode != 0 || !RawGeometryName(record.Name, arc ? "ARC" : "CIRCLE") ||
                !(RawGeometryName(record.SectionName, "ENTITIES") || RawGeometryName(record.SectionName, "BLOCKS")))
                throw new ArgumentException("Expected an ordinary conic in ENTITIES or BLOCKS of this snapshot.", nameof(record));
            var packet = new ConicPacket();
            for (int i = 0; i < packet.Slots.Length; i++) packet.Slots[i] = -1;
            packet.Values[9] = 1.0;
            int depth = 0, subclass = 0;
            bool xdata = false, embedded = false, geometrySeen = false;
            for (int at = 1; at < record.Tags.Count; at++)
            {
                DxfTag tag = record.Tags[at]; short code = tag.Code;
                if (code == 999 || embedded) continue;
                if (code == 102)
                {
                    if (xdata) throw new FormatException("Conic control framing cannot follow XData.");
                    string name = (string)tag.RawValue;
                    if (name.StartsWith("{", StringComparison.Ordinal)) depth++;
                    else if (name == "}" && depth > 0) depth--;
                    else throw new FormatException("Malformed conic application control group.");
                    packet.CanEdit = false; continue;
                }
                if (depth != 0) continue;
                if (code == 101)
                {
                    if (xdata) throw new FormatException("Conic embedded data cannot follow XData.");
                    if (!RawGeometryName(tag.RawValue as string, "Embedded Object"))
                        throw new NotSupportedException("Unknown conic embedded marker.");
                    packet.CanEdit = false; embedded = true; continue;
                }
                if (code == 1001) xdata = true;
                if (xdata)
                {
                    if (code < 1000) throw new FormatException("Ordinary conic data cannot follow XData.");
                    if (RawGeometrySensitiveXDataCode(code))
                        packet.CanEdit = false;
                    continue;
                }
                if (code >= 1000) throw new FormatException("Conic XData requires an application marker.");
                if (code == 100)
                {
                    string name = (string)tag.RawValue;
                    if (subclass == 0 && !geometrySeen && RawGeometryName(name, "AcDbEntity")) subclass = 1;
                    else if (subclass == 1 && RawGeometryName(name, "AcDbCircle")) subclass = 2;
                    else if (arc && subclass == 2 && RawGeometryName(name, "AcDbArc")) subclass = 3;
                    else throw new NotSupportedException("Unknown or ambiguous conic subclass layout.");
                    continue;
                }
                int slot = Array.IndexOf(ConicCodes, code);
                if (slot >= 0)
                {
                    bool angle = slot == 4 || slot == 5;
                    if ((!arc && angle) || subclass == 1 ||
                        (subclass != 0 && (angle ? subclass != 3 : slot <= 3 && subclass != 2)))
                        throw new FormatException("Conic geometry is outside its expected subclass.");
                    if (packet.Slots[slot] >= 0) throw new FormatException("Duplicate conic geometry component.");
                    packet.Slots[slot] = at; packet.Values[slot] = (double)tag.RawValue; geometrySeen = true;
                }
                else if (!RawGeometryCommonCode(code)) packet.CanEdit = false;
            }
            if (depth != 0 || (subclass != 0 && subclass != (arc ? 3 : 2)))
                throw new FormatException("Incomplete conic control or subclass framing.");
            foreach (int required in arc ? new[] { 0, 1, 3, 4, 5 } : new[] { 0, 1, 3 })
                if (packet.Slots[required] < 0) throw new FormatException("Missing required conic geometry component.");
            if (packet.Values[3] <= 0) throw new FormatException("The stored conic radius must be positive.");
            var extrusion = new Vector3(packet.Values[7], packet.Values[8], packet.Values[9]);
            if (extrusion.X == 0 && extrusion.Y == 0 && extrusion.Z == 0)
                throw new FormatException("Conic extrusion direction must be nonzero.");
            packet.Circle = new DxfRawCircleGeometry(new Vector3(packet.Values[0], packet.Values[1], packet.Values[2]),
                packet.Values[3], packet.Values[6], extrusion);
            return packet;
        }

        private DxfRawDocument ReplaceConicGeometry(DxfRawRecord record, ConicPacket packet, double[] values)
        {
            bool changed = false;
            for (int i = 0; i < values.Length; i++) changed |= !SameRawGeometryScalar(values[i], packet.Values[i]);
            if (!changed) return this;
            if (!packet.CanEdit)
                throw new NotSupportedException("This conic has proxy, application, embedded, geometry-sensitive XData or unknown fields requiring explicit regeneration.");
            this.CheckRawGeometryIncomingReferences(record);
            bool addZ = packet.Slots[2] < 0 && !SameRawGeometryScalar(values[2], 0.0);
            if ((long)this.Tags.Count + (addZ ? 1 : 0) > this.options.MaximumTags)
                throw new InvalidOperationException("The conic edit exceeds the raw tag budget.");
            var tags = new List<DxfTag>(record.Tags.Count + (addZ ? 1 : 0));
            for (int at = 0; at < record.Tags.Count; at++)
            {
                DxfTag tag = record.Tags[at];
                for (int i = 0; i < values.Length; i++)
                    if (packet.Slots[i] == at && !SameRawGeometryScalar(values[i], packet.Values[i]))
                    { tag = new DxfTag(ConicCodes[i], values[i]); break; }
                tags.Add(tag);
                if (addZ && at == packet.Slots[1]) tags.Add(new DxfTag(30, values[2]));
            }
            return this.WithRecord(record, tags);
        }

        private static void CheckConicRadius(double radius, string name)
        {
            if (double.IsNaN(radius) || double.IsInfinity(radius) || radius <= 0)
                throw new ArgumentOutOfRangeException(name, "The radius must be finite and positive.");
        }
        private static void CheckConicAngle(double angle, string name)
        {
            if (double.IsNaN(angle) || double.IsInfinity(angle))
                throw new ArgumentOutOfRangeException(name, "The angle must be finite.");
        }
    }
}
