// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using netDxf.Header;
using netDxf.IO;

namespace netDxf.Objects
{
    /// <summary>The scalar types qualified for explicit stored TABLECONTENT replacement.</summary>
    public enum DxfStoredTableContentValueKind
    {
        /// <summary>A signed 32-bit integer.</summary>
        Integer = 1,
        /// <summary>A finite double precision value.</summary>
        Double = 2,
        /// <summary>A decoded string in one stored group-1 tag.</summary>
        String = 4,
        /// <summary>A finite three dimensional point in groups 11, 21 and 31.</summary>
        Point3D = 32
    }

    /// <summary>An immutable qualified scalar CELLCONTENT frame in a loaded TABLECONTENT snapshot.</summary>
    public sealed class DxfStoredTableContentValue
    {
        private DxfStoredTableContentValue() { }
        /// <summary>Gets the scalar kind; replacement must retain this kind.</summary>
        public DxfStoredTableContentValueKind Kind { get; private set; }
        /// <summary>Gets the exact int, double, string or Vector3 scalar value.</summary>
        public object Value { get; private set; }
        /// <summary>Gets the beginning of this frame in its containing Payload, without implying a cell address.</summary>
        public int PayloadIndex { get; private set; }
        /// <summary>Gets the complete immutable CELLCONTENT frame, including its opening and closing tags.</summary>
        public IReadOnlyList<DxfTag> Tags { get; private set; }
        /// <summary>Gets the retained group-93 value flags, or null for the older compact encoding.</summary>
        public int? StoredFormatFlags { get; private set; }
        /// <summary>Gets the retained group-94 unit value, or null for the older compact encoding.</summary>
        public int? StoredUnitType { get; private set; }
        /// <summary>Gets the decoded stored format string, or null for the older compact encoding.</summary>
        public string FormatString { get; private set; }
        /// <summary>Gets the decoded stored display text, or null when this encoding has no display-text tag.</summary>
        public string FormattedText { get; private set; }
        internal int ScalarIndex { get; private set; }
        internal int DisplayIndex { get; private set; }

        /// <summary>Creates an immutable same-kind edit request tied to this exact value snapshot.</summary>
        /// <param name="value">An int, finite double, string or finite Vector3 matching Kind exactly.</param>
        /// <param name="formattedText">Explicit display text for the newer encoding; null is required for R2004.</param>
        /// <returns>A request for ReplaceContent; creating it does not modify the document.</returns>
        /// <remarks>Formatting, unit conversion and formula evaluation are not performed. A successful change
        /// invalidates requests made from earlier snapshots, including requests for unchanged values.</remarks>
        public DxfStoredTableContentValueEdit WithValue(object value, string formattedText = null)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            Type expected = this.Kind == DxfStoredTableContentValueKind.Integer ? typeof(int)
                : this.Kind == DxfStoredTableContentValueKind.Double ? typeof(double)
                : this.Kind == DxfStoredTableContentValueKind.String ? typeof(string) : typeof(Vector3);
            if (value.GetType() != expected) throw new ArgumentException("Replacement must retain the exact stored scalar kind.", nameof(value));
            if (value is string text) DxfStoredTableContent.CheckEditableText(text, nameof(value));
            if (value is double real) DxfStoredTableGeometry.CheckFinite(real, nameof(value));
            if (value is Vector3 point) DxfStoredTableGeometry.CheckFinite(point, nameof(value));
            if (this.DisplayIndex >= 0) DxfStoredTableContent.CheckEditableText(formattedText, nameof(formattedText));
            else if (formattedText != null) throw new ArgumentException("The compact value encoding has no stored display text.", nameof(formattedText));
            return new DxfStoredTableContentValueEdit(this, value, formattedText);
        }

        internal static bool TryLegacyScalarEnd(IReadOnlyList<DxfTag> tags, int marker, out int end)
        {
            end = marker;
            if (marker + 2 >= tags.Count || tags[marker + 1].Code != 90) return false;
            int kind = (int)tags[marker + 1].Value;
            int length = ScalarLength(tags, marker + 2, kind);
            if (length == 0) return false;
            end = marker + 1 + length;
            return true;
        }

        private static int ScalarLength(IReadOnlyList<DxfTag> tags, int index, int kind)
        {
            if (index >= tags.Count) return 0;
            if (kind == 1 && tags[index].Code == 91 || kind == 2 && tags[index].Code == 140 || kind == 4 && tags[index].Code == 1) return 1;
            if (kind == 32 && index + 2 < tags.Count && tags[index].Code == 11 && tags[index + 1].Code == 21 && tags[index + 2].Code == 31) return 3;
            return 0;
        }

        internal static DxfStoredTableContentValue TryRead(IReadOnlyList<DxfTag> tags, int start, int offset, DxfVersion version, Func<string, string> decode)
        {
            int index = start + 1;
            if (index + 3 >= tags.Count || tags[index].Code != 90 || (int)tags[index++].Value != 1
                || tags[index].Code != 300 || (string)tags[index++].Value != "VALUE") return null;
            bool modern = version >= DxfVersion.AutoCad2007;
            int? flags = null, units = null;
            if (modern)
            {
                if (tags[index].Code != 93) return null;
                flags = (int)tags[index++].Value;
                if (flags != 2 && flags != 4 && flags != 6) return null;
            }
            if (tags[index].Code != 90) return null;
            int kind = (int)tags[index++].Value;
            int scalar = index, length = ScalarLength(tags, scalar, kind);
            if (length == 0) return null;
            object value = length == 3 ? (object)new Vector3((double)tags[index].Value, (double)tags[index + 1].Value, (double)tags[index + 2].Value) : tags[index].Value;
            if (value is string text) value = decode(text);
            index += length;
            string format = null, display = null;
            int displayIndex = -1;
            if (modern)
            {
                if (index + 3 >= tags.Count || tags[index].Code != 94 || tags[index + 1].Code != 300
                    || tags[index + 2].Code != 302 || tags[index + 3].Code != 304 || (string)tags[index + 3].Value != "ACVALUE_END") return null;
                units = (int)tags[index].Value;
                format = decode((string)tags[index + 1].Value); display = decode((string)tags[index + 2].Value);
                displayIndex = offset + index + 2; index += 4;
            }
            if (index + 1 >= tags.Count || tags[index].Code != 91 || (int)tags[index].Value != 0
                || tags[index + 1].Code != 309 || (string)tags[index + 1].Value != "CELLCONTENT_END") return null;
            return new DxfStoredTableContentValue
            {
                Kind = (DxfStoredTableContentValueKind)kind, Value = value, PayloadIndex = offset + start,
                ScalarIndex = offset + scalar, DisplayIndex = displayIndex, StoredFormatFlags = flags,
                StoredUnitType = units, FormatString = format, FormattedText = display,
                Tags = tags.Skip(start).Take(index + 2 - start).ToList().AsReadOnly()
            };
        }
    }

    /// <summary>An immutable request to replace one scalar from an exact TABLECONTENT snapshot.</summary>
    public sealed class DxfStoredTableContentValueEdit
    {
        internal DxfStoredTableContentValueEdit(DxfStoredTableContentValue original, object value, string formattedText)
        { this.Original = original; this.Value = value; this.FormattedText = formattedText; }
        /// <summary>Gets the exact source value snapshot to be replaced.</summary>
        public DxfStoredTableContentValue Original { get; }
        /// <summary>Gets the requested scalar, with the original kind.</summary>
        public object Value { get; }
        /// <summary>Gets the explicit display text, or null for the compact encoding.</summary>
        public string FormattedText { get; }
    }
}
