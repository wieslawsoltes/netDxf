// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using netDxf.Header;

namespace netDxf.IO
{
    /// <summary>Immutable stored WCS origin and unit direction of a raw RAY or XLINE.</summary>
    public sealed class DxfRawInfiniteLineGeometry
    {
        internal DxfRawInfiniteLineGeometry(Vector3 origin, Vector3 direction)
        { this.Origin = origin; this.Direction = direction; }
        /// <summary>Gets the world-coordinate origin, not an object-coordinate point.</summary>
        public Vector3 Origin { get; }
        /// <summary>Gets the stored unit direction without normalization or sign reversal.</summary>
        public Vector3 Direction { get; }
    }

    public sealed partial class DxfRawDocument
    {
        /// <summary>Reads a raw RAY origin and unit direction in R13 or later profiles.</summary>
        /// <param name="record">A RAY in ENTITIES or BLOCKS of this exact snapshot.</param>
        /// <returns>Immutable stored world-coordinate geometry.</returns>
        public DxfRawInfiniteLineGeometry ReadRayGeometry(DxfRawRecord record)
        { return this.ReadInfinitePacket(record, false).Geometry; }

        /// <summary>Reads a raw XLINE origin and unit direction in R13 or later profiles.</summary>
        /// <param name="record">An XLINE in ENTITIES or BLOCKS of this exact snapshot.</param>
        /// <returns>Immutable stored world-coordinate geometry.</returns>
        public DxfRawInfiniteLineGeometry ReadXLineGeometry(DxfRawRecord record)
        { return this.ReadInfinitePacket(record, true).Geometry; }

        /// <summary>Replaces a raw RAY's WCS origin and unit direction without regenerating dependencies.</summary>
        /// <param name="record">A RAY belonging to this exact immutable snapshot.</param>
        /// <param name="origin">Finite world-coordinate origin.</param>
        /// <param name="unitDirection">Finite unit direction, not an endpoint; see WithXLineGeometry.</param>
        /// <returns>A new snapshot, or this snapshot for a bit-identical no-op.</returns>
        public DxfRawDocument WithRayGeometry(DxfRawRecord record, Vector3 origin, Vector3 unitDirection)
        { return this.ReplaceInfiniteGeometry(record, false, origin, unitDirection); }

        /// <summary>Replaces a raw XLINE's WCS origin and unit direction without changing its declared dialect.</summary>
        /// <param name="record">An XLINE belonging to this exact immutable snapshot.</param>
        /// <param name="origin">Finite world-coordinate origin.</param>
        /// <param name="unitDirection">Finite unit direction with squared-length error at most 1e-12.</param>
        /// <returns>A new snapshot, or this snapshot for a bit-identical no-op.</returns>
        /// <remarks>
        /// The same absolute squared-length bound applies when reading stored directions. This
        /// epsilon-independent admission bound is a library policy, not an AutoCAD rounding claim.
        /// No normalization, endpoint subtraction, direction reversal or OCS conversion occurs.
        /// X/Y components are required; omitted origin/direction Z values default to positive zero.
        /// Existing slots are replaced in place and a missing Z is inserted after its Y only when
        /// needed, retaining signed zero and finite subnormals. All other tags retain identity and
        /// order. No-ops preserve original bytes; changed output may normalize lexical spelling.
        /// Actual changes reject proxies, private/application/embedded data, unknown fields,
        /// geometry-sensitive XData and exposed incoming references. Hidden dependencies, extents
        /// and private caches are not regenerated. Existing raw/index budgets and save rules apply.
        /// R11/R12 records reject; this does not enable historical typed DxfDocument loading.
        /// </remarks>
        public DxfRawDocument WithXLineGeometry(DxfRawRecord record, Vector3 origin, Vector3 unitDirection)
        { return this.ReplaceInfiniteGeometry(record, true, origin, unitDirection); }

        private DxfRawDocument ReplaceInfiniteGeometry(DxfRawRecord record, bool xline, Vector3 origin, Vector3 direction)
        {
            InfinitePacket packet = this.ReadInfinitePacket(record, xline);
            CheckRawGeometryPoint(origin, nameof(origin));
            if (!RawInfiniteUnit(direction))
                throw new ArgumentOutOfRangeException("unitDirection", "A finite unit direction is required; the value is not an endpoint.");
            double[] values = { origin.X, origin.Y, origin.Z, direction.X, direction.Y, direction.Z };
            bool changed = false;
            for (int i = 0; i < values.Length; i++) changed |= !SameRawGeometryScalar(values[i], packet.Values[i]);
            if (!changed) return this;
            if (!packet.CanEdit)
                throw new NotSupportedException("Infinite-line geometry has proxy, private, geometry-sensitive XData or unknown data requiring explicit regeneration.");
            this.CheckRawGeometryIncomingReferences(record);
            int additions = 0;
            for (int i = 2; i < 6; i += 3)
                if (packet.Slots[i] < 0 && !SameRawGeometryScalar(values[i], 0.0)) additions++;
            if ((long)this.Tags.Count + additions > this.options.MaximumTags)
                throw new InvalidOperationException("The infinite-line edit exceeds the raw tag budget.");
            var tags = new List<DxfTag>(record.Tags.Count + additions);
            for (int at = 0; at < record.Tags.Count; at++)
            {
                DxfTag tag = record.Tags[at];
                for (int i = 0; i < values.Length; i++)
                    if (packet.Slots[i] == at && !SameRawGeometryScalar(values[i], packet.Values[i]))
                    { tag = new DxfTag(RawInfiniteCodes[i], values[i]); break; }
                tags.Add(tag);
                for (int i = 2; i < 6; i += 3)
                    if (packet.Slots[i] < 0 && packet.Slots[i - 1] == at && !SameRawGeometryScalar(values[i], 0.0))
                        tags.Add(new DxfTag(RawInfiniteCodes[i], values[i]));
            }
            return this.WithRecord(record, tags);
        }

        private static readonly short[] RawInfiniteCodes = { 10, 20, 30, 11, 21, 31 };
        private sealed class InfinitePacket
        {
            internal readonly int[] Slots = { -1, -1, -1, -1, -1, -1 };
            internal readonly double[] Values = new double[6];
            internal bool CanEdit = true;
            internal DxfRawInfiniteLineGeometry Geometry;
        }
        private InfinitePacket ReadInfinitePacket(DxfRawRecord record, bool xline)
        {
            this.ValidateRecordSnapshot(record);
            string entity = xline ? "XLINE" : "RAY", marker = xline ? "AcDbXline" : "AcDbRay";
            if (record.MarkerCode != 0 || !RawGeometryName(record.Name, entity) ||
                !(RawGeometryName(record.SectionName, "ENTITIES") || RawGeometryName(record.SectionName, "BLOCKS")))
                throw new ArgumentException("Expected the requested infinite-line entity in ENTITIES or BLOCKS of this snapshot.", nameof(record));
            if (this.Version < DxfVersion.AutoCad13)
                throw new NotSupportedException("RAY and XLINE geometry requires an R13 or later raw profile.");
            var packet = new InfinitePacket();
            int depth = 0, subclass = 0;
            bool xdata = false, embedded = false, geometrySeen = false;
            for (int at = 1; at < record.Tags.Count; at++)
            {
                DxfTag tag = record.Tags[at]; short code = tag.Code;
                if (code == 999 || embedded) continue;
                if (code == 102)
                {
                    if (xdata) throw new FormatException("Infinite-line application controls cannot follow XData.");
                    string name = (string)tag.RawValue;
                    if (name.StartsWith("{", StringComparison.Ordinal)) depth++;
                    else if (name == "}" && depth > 0) depth--;
                    else throw new FormatException("Malformed infinite-line application controls.");
                    packet.CanEdit = false; continue;
                }
                if (depth != 0) continue;
                if (code == 101)
                {
                    if (xdata) throw new FormatException("Infinite-line embedded data cannot follow XData.");
                    if (!RawGeometryName(tag.RawValue as string, "Embedded Object"))
                        throw new NotSupportedException("Unknown infinite-line embedded marker.");
                    packet.CanEdit = false; embedded = true; continue;
                }
                if (code == 1001) xdata = true;
                if (xdata)
                {
                    if (code < 1000) throw new FormatException("Ordinary infinite-line data cannot follow XData.");
                    if (RawGeometrySensitiveXDataCode(code)) packet.CanEdit = false;
                    continue;
                }
                if (code >= 1000) throw new FormatException("Infinite-line XData requires an application marker.");
                if (code == 100)
                {
                    string name = (string)tag.RawValue;
                    if (subclass == 0 && !geometrySeen && RawGeometryName(name, "AcDbEntity")) subclass = 1;
                    else if (subclass == 1 && RawGeometryName(name, marker)) subclass = 2;
                    else throw new NotSupportedException("Unknown or ambiguous infinite-line subclass layout.");
                    continue;
                }
                int slot = Array.IndexOf(RawInfiniteCodes, code);
                if (slot >= 0)
                {
                    if (subclass == 1) throw new FormatException("Infinite-line geometry is outside its entity subclass.");
                    if (packet.Slots[slot] >= 0) throw new FormatException("Duplicate infinite-line coordinate component.");
                    packet.Slots[slot] = at; packet.Values[slot] = (double)tag.RawValue; geometrySeen = true;
                }
                else if (!RawGeometryCommonCode(code)) packet.CanEdit = false;
            }
            if (depth != 0 || subclass == 1) throw new FormatException("Incomplete infinite-line control or subclass framing.");
            foreach (int required in new[] { 0, 1, 3, 4 })
                if (packet.Slots[required] < 0) throw new FormatException("Infinite-line origin and direction X/Y components are required.");
            var origin = new Vector3(packet.Values[0], packet.Values[1], packet.Values[2]);
            var direction = new Vector3(packet.Values[3], packet.Values[4], packet.Values[5]);
            if (!RawInfiniteUnit(direction)) throw new FormatException("Stored infinite-line direction must be finite and unit length.");
            packet.Geometry = new DxfRawInfiniteLineGeometry(origin, direction);
            return packet;
        }
        private static bool RawInfiniteUnit(Vector3 direction)
        {
            double squared = direction.X * direction.X + direction.Y * direction.Y + direction.Z * direction.Z;
            return !double.IsNaN(squared) && !double.IsInfinity(squared) && Math.Abs(squared - 1.0) <= 1e-12;
        }
    }
}
