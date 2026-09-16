// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using netDxf.Units;

namespace netDxf.Objects
{
    /// <summary>Value-format evaluation helpers for qualified stored scalar snapshots.</summary>
    public static class DxfStoredTableContentValueFormatting
    {
        /// <summary>Evaluates this value's own supported format without changing stored text.</summary>
        /// <remarks>Compact values, angles, fields, unsupported controls and non-scalar values reject. No inherited cell style is inferred.</remarks>
        public static string EvaluateFormattedText(this DxfStoredTableContentValue value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (value.DisplayIndex < 0) throw new NotSupportedException("The compact encoding has no independent format/display fields.");
            return DxfValueFormat.Parse(value.FormatString).Format(value.Value, value.StoredUnitType.Value);
        }

        /// <summary>Creates a same-kind scalar edit with display text evaluated from the current stored format.</summary>
        /// <remarks>Uses the existing ReplaceContent transaction. The format and units are unchanged; unsupported evaluation fails before a request is published.</remarks>
        public static DxfStoredTableContentValueEdit WithEvaluatedValue(this DxfStoredTableContentValue original, object value)
        {
            if (original == null) throw new ArgumentNullException(nameof(original));
            if (original.DisplayIndex < 0) throw new NotSupportedException("The compact encoding has no independent format/display fields.");
            // Validate exact kind before compiling or evaluating; no type coercion is permitted.
            original.WithValue(value, original.FormattedText);
            string display = DxfValueFormat.Parse(original.FormatString).Format(value, original.StoredUnitType.Value);
            return original.WithValue(value, display);
        }
    }

    public sealed partial class DxfStoredTableContent
    {
        /// <summary>Atomically refreshes display text for the selected current scalar snapshots.</summary>
        /// <remarks>
        /// All selected values must have supported explicit/default scalar formats. One unsupported value
        /// aborts the complete request. Enumeration and reentry are guarded by ReplaceContent. Actual
        /// scalar values, format strings, flags, unit codes, references and unselected fields remain fixed.
        /// TABLE inline caches, geometry, linked fields and inherited style expressions are not regenerated.
        /// </remarks>
        public void RefreshFormattedText(IEnumerable<DxfStoredTableContentValue> values)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            this.ReplaceContent(this.Name, this.Description, this.TableStyle, values.Select(value =>
            {
                if (value == null) throw new ArgumentException("A selected value cannot be null.", nameof(values));
                return value.WithEvaluatedValue(value.Value);
            }));
        }
    }
}
