// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.IO;

namespace netDxf.IO
{
    /// <summary>Decoded WCS geometry of an ordinary raw LINE; no database ownership is transferred.</summary>
    public sealed class DxfRawLineGeometry
    {
        internal DxfRawLineGeometry(Vector3 start, Vector3 end, double thickness, Vector3 extrusion)
        { this.StartPoint = start; this.EndPoint = end; this.Thickness = thickness; this.ExtrusionDirection = extrusion; }
        /// <summary>Gets the start point in world coordinates.</summary>
        public Vector3 StartPoint { get; }
        /// <summary>Gets the end point in world coordinates.</summary>
        public Vector3 EndPoint { get; }
        /// <summary>Gets the stored signed thickness, or zero when omitted.</summary>
        public double Thickness { get; }
        /// <summary>Gets the stored extrusion vector, defaulting to (0,0,1), without renormalization.</summary>
        public Vector3 ExtrusionDirection { get; }
    }

    public sealed partial class DxfRawDocument
    {
        /// <summary>Reads ordinary LINE endpoints, thickness and extrusion without changing raw tags.</summary>
        /// <param name="record">A LINE in ENTITIES or BLOCKS belonging to this exact snapshot.</param>
        /// <returns>Independent, immutable geometry; omitted endpoint Z values are zero.</returns>
        /// <remarks>
        /// Supports the existing raw R11/R12 through R2018 profiles without enabling historical
        /// DxfDocument loading. X/Y coordinates are required; duplicate geometry tags, malformed
        /// control framing and unrecognized subclass layouts reject rather than guessing.
        /// Application control groups and embedded tails are not treated as LINE coordinates.
        /// This is a selected schema read, not validation of every tag or an ownership graph.
        /// </remarks>
        public DxfRawLineGeometry ReadLineGeometry(DxfRawRecord record)
        {
            return this.ReadLinePacket(record).Geometry;
        }

        /// <summary>Replaces only a raw LINE's WCS endpoints in a new same-version document snapshot.</summary>
        /// <param name="record">An ordinary LINE belonging to this exact immutable snapshot.</param>
        /// <param name="startPoint">Finite new WCS start point.</param>
        /// <param name="endPoint">Finite new WCS end point.</param>
        /// <returns>A new snapshot, or this snapshot for bit-identical endpoints.</returns>
        /// <remarks>
        /// Existing coordinate slots are replaced in place; omitted Z slots are inserted only when
        /// their new value is not positive zero. Every other tag retains identity and relative order.
        /// No-op edits retain original-byte output. Changed output uses the normal raw serializer.
        /// Thickness and extrusion are unchanged. Coincident endpoints are allowed.
        /// Changes reject proxies, application control/embedded data, unknown non-XData fields,
        /// coordinate/handle XData and exposed incoming handle uses. No private graph, hidden
        /// binary/string reference, associative geometry or header extents are regenerated.
        /// Input and handle scans use the existing raw/index budgets; this is not a whole-document
        /// semantic certificate. Existing raw save/atomic-save and transport restrictions still apply.
        /// </remarks>
        public DxfRawDocument WithLineEndpoints(DxfRawRecord record, Vector3 startPoint, Vector3 endPoint)
        {
            LinePacket packet = this.ReadLinePacket(record);
            CheckLinePoint(startPoint, nameof(startPoint));
            CheckLinePoint(endPoint, nameof(endPoint));
            double[] coordinates = { startPoint.X, startPoint.Y, startPoint.Z, endPoint.X, endPoint.Y, endPoint.Z };
            bool changed = false;
            for (int i = 0; i < coordinates.Length; i++)
                changed |= !SameLineScalar(packet.Values[i], coordinates[i]);
            if (!changed) return this;
            if (!packet.CanEdit)
                throw new NotSupportedException("This LINE contains proxy, application, embedded, coordinate XData or unrecognized fields requiring explicit regeneration.");
            this.CheckLineIncomingReferences(record);

            int additions = 0;
            for (int i = 0; i < 6; i++)
                if (packet.Slots[i] < 0 && !SameLineScalar(coordinates[i], 0.0)) additions++;
            if ((long)this.Tags.Count + additions > this.options.MaximumTags)
                throw new InvalidOperationException("The endpoint edit exceeds the raw document tag budget.");
            var tags = new List<DxfTag>(record.Tags.Count + additions);
            for (int at = 0; at < record.Tags.Count; at++)
            {
                DxfTag tag = record.Tags[at];
                for (int i = 0; i < 6; i++)
                    if (packet.Slots[i] == at && !SameLineScalar(packet.Values[i], coordinates[i]))
                    { tag = new DxfTag(LineGeometryCodes[i], coordinates[i]); break; }
                tags.Add(tag);
                // Missing Z follows its Y slot, before trailing control/XData data.
                for (int i = 2; i < 6; i += 3)
                    if (packet.Slots[i] < 0 && packet.Slots[i - 1] == at && !SameLineScalar(coordinates[i], 0.0))
                        tags.Add(new DxfTag(LineGeometryCodes[i], coordinates[i]));
            }
            return this.WithRecord(record, tags);
        }

        private static readonly short[] LineGeometryCodes = { 10, 20, 30, 11, 21, 31, 39, 210, 220, 230 };
        private sealed class LinePacket
        {
            internal readonly int[] Slots = new int[10];
            internal readonly double[] Values = new double[10];
            internal bool CanEdit = true;
            internal DxfRawLineGeometry Geometry;
        }

        private LinePacket ReadLinePacket(DxfRawRecord record)
        {
            this.ValidateRecordSnapshot(record);
            if (record.MarkerCode != 0 || !LineName(record.Name, "LINE") ||
                !(LineName(record.SectionName, "ENTITIES") || LineName(record.SectionName, "BLOCKS")))
                throw new ArgumentException("Expected a LINE in ENTITIES or BLOCKS of this snapshot.", nameof(record));
            var result = new LinePacket();
            for (int i = 0; i < result.Slots.Length; i++) result.Slots[i] = -1;
            result.Values[9] = 1;
            int depth = 0, subclass = 0;
            bool xdata = false, embedded = false, geometrySeen = false;
            for (int at = 1; at < record.Tags.Count; at++)
            {
                DxfTag tag = record.Tags[at]; short code = tag.Code;
                if (code == 999 || embedded) continue;
                if (code == 102)
                {
                    if (xdata) throw new FormatException("LINE control framing cannot follow XData.");
                    string name = (string)tag.RawValue;
                    if (name.StartsWith("{", StringComparison.Ordinal)) depth++;
                    else if (name == "}" && depth > 0) depth--;
                    else throw new FormatException("Malformed LINE application control group.");
                    result.CanEdit = false;
                    continue;
                }
                if (depth != 0) continue;
                if (code == 101)
                {
                    if (!LineName(tag.RawValue as string, "Embedded Object"))
                        throw new NotSupportedException("Unrecognized LINE embedded-data marker.");
                    result.CanEdit = false; embedded = true; continue;
                }
                if (code == 1001) xdata = true;
                if (xdata)
                {
                    if (code < 1000) throw new FormatException("Ordinary LINE data cannot follow XData.");
                    if (code == 1005 || (code >= 1010 && code <= 1033)) result.CanEdit = false;
                    continue;
                }
                if (code >= 1000) throw new FormatException("LINE XData requires an application marker.");
                if (code == 100)
                {
                    string name = (string)tag.RawValue;
                    if (subclass == 0 && !geometrySeen && LineName(name, "AcDbEntity")) subclass = 1;
                    else if (subclass == 1 && LineName(name, "AcDbLine")) subclass = 2;
                    else throw new NotSupportedException("Unrecognized or ambiguous LINE subclass layout.");
                    continue;
                }
                int slot = Array.IndexOf(LineGeometryCodes, code);
                if (slot >= 0)
                {
                    if (subclass == 1) throw new FormatException("LINE geometry occurs outside its AcDbLine subclass.");
                    if (result.Slots[slot] >= 0) throw new FormatException("Duplicate LINE geometry component.");
                    result.Slots[slot] = at; result.Values[slot] = (double)tag.RawValue; geometrySeen = true;
                }
                else if (!LineCommonCode(code)) result.CanEdit = false;
            }
            if (depth != 0 || subclass == 1) throw new FormatException("Incomplete LINE control or subclass framing.");
            foreach (int required in new[] { 0, 1, 3, 4 })
                if (result.Slots[required] < 0) throw new FormatException("LINE endpoint X and Y components are required.");
            var normal = new Vector3(result.Values[7], result.Values[8], result.Values[9]);
            if (normal.X == 0 && normal.Y == 0 && normal.Z == 0)
                throw new FormatException("LINE extrusion direction must be nonzero.");
            result.Geometry = new DxfRawLineGeometry(
                new Vector3(result.Values[0], result.Values[1], result.Values[2]),
                new Vector3(result.Values[3], result.Values[4], result.Values[5]), result.Values[6], normal);
            return result;
        }

        private void CheckLineIncomingReferences(DxfRawRecord record)
        {
            DxfRawHandleIndex index = DxfRawHandleIndex.Create(this);
            string identity = null;
            foreach (DxfRawHandleOccurrence item in index.GetOccurrences(record))
                if (item.Role == DxfRawHandleRole.Identity)
                {
                    if (identity != null || item.NumericHandle == 0)
                        throw new NotSupportedException("LINE identity must be unambiguous and nonzero when present.");
                    identity = item.Handle;
                }
            if (identity == null) return; // Legacy handle-free LINEs are permitted.
            var definitions = index.FindDefinitions(identity);
            if (definitions.Count != 1)
                throw new NotSupportedException("LINE identity is duplicated in the raw document.");
            ulong target = definitions[0].NumericHandle;
            foreach (DxfRawHandleOccurrence item in index.Occurrences)
                if (item.NumericHandle == target && item.Role != DxfRawHandleRole.HeaderSeed &&
                    !(ReferenceEquals(item.Record, record) && item.Role == DxfRawHandleRole.Identity))
                    throw new NotSupportedException("An exposed handle use depends on this LINE; regenerate or edit the dependency explicitly.");
        }

        private static bool LineCommonCode(short code)
        {
            switch (code)
            {
                case 5: case 330: case 67: case 410: case 8: case 6: case 62: case 420: case 430:
                case 440: case 370: case 48: case 60: case 347: case 390: case 284: return true;
                default: return false;
            }
        }
        private static bool LineName(string value, string expected)
        { return string.Equals(value, expected, StringComparison.OrdinalIgnoreCase); }
        private static bool SameLineScalar(double a, double b)
        { return BitConverter.DoubleToInt64Bits(a) == BitConverter.DoubleToInt64Bits(b); }
        private static void CheckLinePoint(Vector3 point, string parameter)
        {
            if (double.IsNaN(point.X) || double.IsInfinity(point.X) || double.IsNaN(point.Y) ||
                double.IsInfinity(point.Y) || double.IsNaN(point.Z) || double.IsInfinity(point.Z))
                throw new ArgumentOutOfRangeException(parameter, "LINE endpoints must be finite.");
        }
    }
}
