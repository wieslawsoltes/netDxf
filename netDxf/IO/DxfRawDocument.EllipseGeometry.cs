// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using netDxf.Header;

namespace netDxf.IO
{
    /// <summary>Immutable stored ELLIPSE geometry, without angle or axis conversion.</summary>
    public sealed class DxfRawEllipseGeometry
    {
        internal DxfRawEllipseGeometry(Vector3 center, Vector3 major, double ratio,
            double start, double end, Vector3 extrusion)
        {
            this.Center = center; this.MajorAxis = major; this.AxisRatio = ratio;
            this.StartParameter = start; this.EndParameter = end; this.ExtrusionDirection = extrusion;
        }
        /// <summary>Gets the world-coordinate center, not an object-coordinate center.</summary>
        public Vector3 Center { get; }
        /// <summary>Gets the WCS major semi-axis vector relative to the center, not an absolute endpoint.</summary>
        public Vector3 MajorAxis { get; }
        /// <summary>Gets the positive minor-to-major axis ratio, no greater than one.</summary>
        public double AxisRatio { get; }
        /// <summary>Gets the stored start parameter, not a polar angle in degrees.</summary>
        public double StartParameter { get; }
        /// <summary>Gets the stored end parameter, without normalization or sweep reinterpretation.</summary>
        public double EndParameter { get; }
        /// <summary>Gets the stored extrusion vector without normalization, defaulting to (0,0,1).</summary>
        public Vector3 ExtrusionDirection { get; }
    }

    public sealed partial class DxfRawDocument
    {
        /// <summary>Reads ordinary ELLIPSE geometry in R13 or later raw profiles without rewriting tags.</summary>
        /// <param name="record">An ELLIPSE in ENTITIES or BLOCKS belonging to this exact snapshot.</param>
        /// <returns>Immutable stored center, major semi-axis, ratio, parameters and extrusion.</returns>
        public DxfRawEllipseGeometry ReadEllipseGeometry(DxfRawRecord record)
        { return this.ReadEllipsePacket(record).Geometry; }

        /// <summary>Replaces raw ELLIPSE geometry while retaining its extrusion and declared DXF version.</summary>
        /// <param name="record">An ELLIPSE belonging to this exact immutable snapshot.</param>
        /// <param name="center">Finite world-coordinate center.</param>
        /// <param name="majorAxis">Finite nonzero WCS semi-axis relative to the center, in the existing plane.</param>
        /// <param name="axisRatio">Finite minor-to-major ratio greater than zero and no greater than one.</param>
        /// <param name="startParameter">Finite stored curve parameter, not a degree-valued polar angle.</param>
        /// <param name="endParameter">Finite stored end parameter; no normalization is performed.</param>
        /// <returns>A new same-version snapshot, or this snapshot for a bit-identical no-op.</returns>
        /// <remarks>
        /// Stored and replacement axes must be perpendicular to extrusion within absolute normalized
        /// dot-product tolerance 1e-12, independent of MathHelper.Epsilon. Vectors are scaled for this
        /// check, never normalized in the returned data. This tolerance is a library admission rule.
        /// Parameters retain their exact values, including reversed intervals and full-ellipse endpoints.
        /// X/Y, ratio and both parameter fields are required; omitted center/axis Z defaults to zero.
        /// Missing Z fields are inserted after Y only when their new values are not positive zero.
        /// Unselected tags retain object identity and order. Actual edits reject proxies, application or
        /// embedded data, unknown fields, geometry-sensitive XData and exposed incoming handle uses.
        /// Hidden dependencies, extents and private caches are not regenerated. No-op original bytes
        /// are retained; changed serialization may normalize spelling. Existing raw/index budgets and
        /// save rules apply. This is not historical typed loading or native surface qualification.
        /// </remarks>
        public DxfRawDocument WithEllipseGeometry(DxfRawRecord record, Vector3 center,
            Vector3 majorAxis, double axisRatio, double startParameter, double endParameter)
        {
            EllipsePacket packet = this.ReadEllipsePacket(record);
            CheckRawGeometryPoint(center, nameof(center));
            CheckRawGeometryPoint(majorAxis, nameof(majorAxis));
            if (!RawEllipsePlane(majorAxis, packet.Geometry.ExtrusionDirection))
                throw new ArgumentOutOfRangeException(nameof(majorAxis), "A nonzero major semi-axis in the existing ellipse plane is required.");
            if (!RawEllipseRatio(axisRatio))
                throw new ArgumentOutOfRangeException(nameof(axisRatio), "The axis ratio must be finite and in (0,1].");
            CheckConicAngle(startParameter, nameof(startParameter));
            CheckConicAngle(endParameter, nameof(endParameter));
            double[] values = { center.X, center.Y, center.Z, majorAxis.X, majorAxis.Y, majorAxis.Z,
                axisRatio, startParameter, endParameter };
            bool changed = false;
            for (int i = 0; i < values.Length; i++) changed |= !SameRawGeometryScalar(values[i], packet.Values[i]);
            if (!changed) return this;
            if (!packet.CanEdit)
                throw new NotSupportedException("ELLIPSE has proxy, private, geometry-sensitive XData or unknown fields requiring explicit regeneration.");
            this.CheckRawGeometryIncomingReferences(record);
            int additions = 0;
            for (int i = 2; i < 6; i += 3)
                if (packet.Slots[i] < 0 && !SameRawGeometryScalar(values[i], 0.0)) additions++;
            if ((long)this.Tags.Count + additions > this.options.MaximumTags)
                throw new InvalidOperationException("The ellipse edit exceeds the raw tag budget.");
            var tags = new List<DxfTag>(record.Tags.Count + additions);
            for (int at = 0; at < record.Tags.Count; at++)
            {
                DxfTag tag = record.Tags[at];
                for (int i = 0; i < values.Length; i++)
                    if (packet.Slots[i] == at && !SameRawGeometryScalar(values[i], packet.Values[i]))
                    { tag = new DxfTag(RawEllipseCodes[i], values[i]); break; }
                tags.Add(tag);
                for (int i = 2; i < 6; i += 3)
                    if (packet.Slots[i] < 0 && packet.Slots[i - 1] == at && !SameRawGeometryScalar(values[i], 0.0))
                        tags.Add(new DxfTag(RawEllipseCodes[i], values[i]));
            }
            return this.WithRecord(record, tags);
        }

        private static readonly short[] RawEllipseCodes = { 10, 20, 30, 11, 21, 31, 40, 41, 42, 210, 220, 230 };
        private sealed class EllipsePacket
        {
            internal readonly int[] Slots = new int[12];
            internal readonly double[] Values = new double[12];
            internal bool CanEdit = true;
            internal DxfRawEllipseGeometry Geometry;
        }
        private EllipsePacket ReadEllipsePacket(DxfRawRecord record)
        {
            this.ValidateRecordSnapshot(record);
            if (record.MarkerCode != 0 || !RawGeometryName(record.Name, "ELLIPSE") ||
                !(RawGeometryName(record.SectionName, "ENTITIES") || RawGeometryName(record.SectionName, "BLOCKS")))
                throw new ArgumentException("Expected an ELLIPSE in ENTITIES or BLOCKS of this snapshot.", nameof(record));
            if (this.Version < DxfVersion.AutoCad13)
                throw new NotSupportedException("ELLIPSE geometry requires an R13 or later raw profile.");
            var packet = new EllipsePacket();
            for (int i = 0; i < packet.Slots.Length; i++) packet.Slots[i] = -1;
            packet.Values[11] = 1;
            int depth = 0, subclass = 0;
            bool xdata = false, embedded = false, geometrySeen = false;
            for (int at = 1; at < record.Tags.Count; at++)
            {
                DxfTag tag = record.Tags[at]; short code = tag.Code;
                if (code == 999 || embedded) continue;
                if (code == 102)
                {
                    if (xdata) throw new FormatException("Ellipse application controls cannot follow XData.");
                    string name = (string)tag.RawValue;
                    if (name.StartsWith("{", StringComparison.Ordinal)) depth++;
                    else if (name == "}" && depth > 0) depth--;
                    else throw new FormatException("Malformed ellipse application controls.");
                    packet.CanEdit = false; continue;
                }
                if (depth != 0) continue;
                if (code == 101)
                {
                    if (xdata) throw new FormatException("Ellipse embedded data cannot follow XData.");
                    if (!RawGeometryName(tag.RawValue as string, "Embedded Object"))
                        throw new NotSupportedException("Unknown ellipse embedded marker.");
                    packet.CanEdit = false; embedded = true; continue;
                }
                if (code == 1001) xdata = true;
                if (xdata)
                {
                    if (code < 1000) throw new FormatException("Ordinary ellipse fields cannot follow XData.");
                    if (RawGeometrySensitiveXDataCode(code)) packet.CanEdit = false;
                    continue;
                }
                if (code >= 1000) throw new FormatException("Ellipse XData requires an application marker.");
                if (code == 100)
                {
                    string name = (string)tag.RawValue;
                    if (subclass == 0 && !geometrySeen && RawGeometryName(name, "AcDbEntity")) subclass = 1;
                    else if (subclass == 1 && RawGeometryName(name, "AcDbEllipse")) subclass = 2;
                    else throw new NotSupportedException("Unknown or ambiguous ellipse subclass layout.");
                    continue;
                }
                int slot = Array.IndexOf(RawEllipseCodes, code);
                if (slot >= 0)
                {
                    if (subclass == 1) throw new FormatException("Ellipse geometry is outside its entity subclass.");
                    if (packet.Slots[slot] >= 0) throw new FormatException("Duplicate ellipse geometry component.");
                    packet.Slots[slot] = at; packet.Values[slot] = (double)tag.RawValue; geometrySeen = true;
                }
                else if (!RawGeometryCommonCode(code)) packet.CanEdit = false;
            }
            if (depth != 0 || subclass == 1) throw new FormatException("Incomplete ellipse control or subclass framing.");
            foreach (int i in new[] { 0, 1, 3, 4, 6, 7, 8 })
                if (packet.Slots[i] < 0) throw new FormatException("Ellipse center/axis X/Y, ratio and both parameter fields are required.");
            var center = new Vector3(packet.Values[0], packet.Values[1], packet.Values[2]);
            var major = new Vector3(packet.Values[3], packet.Values[4], packet.Values[5]);
            var normal = new Vector3(packet.Values[9], packet.Values[10], packet.Values[11]);
            if (!RawEllipseRatio(packet.Values[6]) || !RawEllipsePlane(major, normal))
                throw new FormatException("Ellipse ratio, major semi-axis or extrusion plane is invalid.");
            packet.Geometry = new DxfRawEllipseGeometry(center, major, packet.Values[6], packet.Values[7], packet.Values[8], normal);
            return packet;
        }
        private static bool RawEllipseRatio(double ratio)
        { return ratio > 0 && ratio <= 1; }
        private static bool RawEllipsePlane(Vector3 major, Vector3 normal)
        {
            double a = Math.Max(Math.Abs(major.X), Math.Max(Math.Abs(major.Y), Math.Abs(major.Z)));
            double n = Math.Max(Math.Abs(normal.X), Math.Max(Math.Abs(normal.Y), Math.Abs(normal.Z)));
            if (!(a > 0) || !(n > 0) || double.IsInfinity(a) || double.IsInfinity(n)) return false;
            double x = major.X / a, y = major.Y / a, z = major.Z / a;
            double nx = normal.X / n, ny = normal.Y / n, nz = normal.Z / n;
            return Math.Abs(x * nx + y * ny + z * nz) <= 1e-12 * Math.Sqrt((x*x+y*y+z*z)*(nx*nx+ny*ny+nz*nz));
        }
    }
}
