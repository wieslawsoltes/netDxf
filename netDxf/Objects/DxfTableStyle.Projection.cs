// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using netDxf.IO;
using netDxf.Tables;

namespace netDxf.Objects
{
    /// <summary>Immutable values from the recognized classic TABLESTYLE header.</summary>
    public sealed class DxfTableStyleHeader
    {
        private DxfTableStyleHeader() { }
        /// <summary>Creates replacement values for a recognized classic stored header.</summary>
        /// <remarks>Description is decoded text of at most 255 UTF-16 code units. Stored flags are not interpreted.</remarks>
        public DxfTableStyleHeader(string description, short flowDirection, short storedFlags,
            double horizontalCellMargin, double verticalCellMargin, bool suppressTitle, bool suppressColumnHeading)
        {
            DxfStoredTableContent.CheckEditableText(description, nameof(description));
            if (description.Length > 255) throw new ArgumentOutOfRangeException(nameof(description));
            if (flowDirection < 0 || flowDirection > 1) throw new ArgumentOutOfRangeException(nameof(flowDirection));
            if (!FiniteNonnegative(horizontalCellMargin)) throw new ArgumentOutOfRangeException(nameof(horizontalCellMargin));
            if (!FiniteNonnegative(verticalCellMargin)) throw new ArgumentOutOfRangeException(nameof(verticalCellMargin));
            this.Description = description; this.FlowDirection = flowDirection; this.StoredFlags = storedFlags;
            this.HorizontalCellMargin = horizontalCellMargin; this.VerticalCellMargin = verticalCellMargin;
            this.SuppressTitle = suppressTitle; this.SuppressColumnHeading = suppressColumnHeading;
        }
        /// <summary>Gets the stored description, independently of the owning dictionary name.</summary>
        public string Description { get; private set; }
        /// <summary>Gets the stored flow direction: zero down, one up.</summary>
        public short FlowDirection { get; private set; }
        /// <summary>Gets the retained group-71 flags without assigning private flag meanings.</summary>
        public short StoredFlags { get; private set; }
        /// <summary>Gets the horizontal cell margin.</summary>
        public double HorizontalCellMargin { get; private set; }
        /// <summary>Gets the vertical cell margin.</summary>
        public double VerticalCellMargin { get; private set; }
        /// <summary>Gets whether the title is suppressed.</summary>
        public bool SuppressTitle { get; private set; }
        /// <summary>Gets whether the column heading is suppressed.</summary>
        public bool SuppressColumnHeading { get; private set; }
        internal static DxfTableStyleHeader TryRead(List<DxfTag> tags, Func<string, string> decode)
        {
            if (!tags.Select(t => t.Code).SequenceEqual(new short[] { 3, 70, 71, 40, 41, 280, 281 })) return null;
            string description = decode((string)tags[0].Value);
            short flow = (short)tags[1].Value, title = (short)tags[5].Value, heading = (short)tags[6].Value;
            double horizontal = (double)tags[3].Value, vertical = (double)tags[4].Value;
            if (description.Length > 255 || flow < 0 || flow > 1 || title < 0 || title > 1 || heading < 0 || heading > 1 ||
                !FiniteNonnegative(horizontal) || !FiniteNonnegative(vertical)) return null;
            return new DxfTableStyleHeader { Description = description, FlowDirection = flow, StoredFlags = (short)tags[2].Value,
                HorizontalCellMargin = horizontal, VerticalCellMargin = vertical, SuppressTitle = title != 0, SuppressColumnHeading = heading != 0 };
        }
        internal static bool FiniteNonnegative(double value) { return !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0; }
    }
    /// <summary>An immutable ordered classic row packet with optional public scalar values.</summary>
    public sealed class DxfTableStyleRow
    {
        internal DxfTableStyleRow(List<DxfTag> tags, Func<string, string> decode)
        {
            this.Tags = new ReadOnlyCollection<DxfTag>(tags);
            this.StoredTextStyleName = decode((string)tags[0].Value);
            this.Values = DxfTableStyleRowValues.TryRead(tags);
        }
        /// <summary>Gets the decoded source STYLE name before any resource rename.</summary>
        public string StoredTextStyleName { get; }
        /// <summary>Gets the exact registered STYLE resource, or null for an unresolved source name.</summary>
        public TextStyle TextStyle { get; private set; }
        /// <summary>Gets public row scalars, or null when they are missing, repeated or invalid.</summary>
        public DxfTableStyleRowValues Values { get; }
        /// <summary>Gets the ordered public row packet; private application groups remain in the parent object's complete Tags.</summary>
        public IReadOnlyList<DxfTag> Tags { get; }
        /// <summary>Creates a scalar edit bound to this row snapshot.</summary>
        public DxfTableStyleRowEdit WithValues(DxfTableStyleRowValues values)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            if (this.Values == null) throw new NotSupportedException("The stored row scalars are not qualified for editing.");
            return new DxfTableStyleRowEdit(this, values);
        }
        internal void BindTextStyle(TextStyle style) { this.TextStyle = style; }
    }
    /// <summary>An immutable scalar replacement tied to one current TABLESTYLE row snapshot.</summary>
    public sealed class DxfTableStyleRowEdit
    {
        internal DxfTableStyleRowEdit(DxfTableStyleRow original, DxfTableStyleRowValues values)
        { this.Original = original; this.Values = values; }
        /// <summary>Gets the original row snapshot.</summary>
        public DxfTableStyleRow Original { get; }
        /// <summary>Gets the replacement scalar values.</summary>
        public DxfTableStyleRowValues Values { get; }
    }
    /// <summary>Immutable public row scalars; raw data/unit and border fields are not interpreted.</summary>
    public sealed class DxfTableStyleRowValues
    {
        private DxfTableStyleRowValues() { }
        /// <summary>Creates immutable row scalar values without evaluating layout, alignment or color semantics.</summary>
        /// <remarks>The height must be finite and nonnegative. Alignment and color codes retain signed stored values.</remarks>
        public DxfTableStyleRowValues(double textHeight, short cellAlignment, short storedTextColor,
            short storedFillColor, bool backgroundColorEnabled)
        {
            if (!DxfTableStyleHeader.FiniteNonnegative(textHeight)) throw new ArgumentOutOfRangeException(nameof(textHeight));
            this.TextHeight = textHeight; this.CellAlignment = cellAlignment; this.StoredTextColor = storedTextColor;
            this.StoredFillColor = storedFillColor; this.BackgroundColorEnabled = backgroundColorEnabled;
        }
        /// <summary>Gets the stored text height.</summary>
        public double TextHeight { get; private set; }
        /// <summary>Gets the stored cell alignment code without evaluating layout.</summary>
        public short CellAlignment { get; private set; }
        /// <summary>Gets the source text color index, including any special stored value.</summary>
        public short StoredTextColor { get; private set; }
        /// <summary>Gets the source fill color index, including any special stored value.</summary>
        public short StoredFillColor { get; private set; }
        /// <summary>Gets whether background color is enabled.</summary>
        public bool BackgroundColorEnabled { get; private set; }
        internal static DxfTableStyleRowValues TryRead(List<DxfTag> tags)
        {
            var fields = new Dictionary<short, DxfTag>();
            foreach (short code in new short[] { 140, 170, 62, 63, 283 })
            {
                var values = tags.Where(t => t.Code == code).ToList();
                if (values.Count != 1) return null;
                fields.Add(code, values[0]);
            }
            double height = (double)fields[140].Value;
            short fill = (short)fields[283].Value;
            if (!DxfTableStyleHeader.FiniteNonnegative(height) || fill < 0 || fill > 1) return null;
            return new DxfTableStyleRowValues { TextHeight = height, CellAlignment = (short)fields[170].Value,
                StoredTextColor = (short)fields[62].Value, StoredFillColor = (short)fields[63].Value, BackgroundColorEnabled = fill != 0 };
        }
    }
}
