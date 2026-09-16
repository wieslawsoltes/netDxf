// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using netDxf.Tables;

namespace netDxf.Objects
{
    /// <summary>An immutable request to replace values and/or the LTYPE in one current grid packet.</summary>
    public sealed class DxfCellGridFormatEdit
    {
        internal DxfCellGridFormatEdit(DxfCellGridFormat original) { this.Original = original; }
        private DxfCellGridFormatEdit(DxfCellGridFormatEdit other)
        { this.Original = other.Original; this.Values = other.Values; this.ChangesLinetype = other.ChangesLinetype; this.Linetype = other.Linetype; }
        /// <summary>Gets the source grid snapshot.</summary>
        public DxfCellGridFormat Original { get; }
        /// <summary>Gets replacement scalar values, or null to keep them.</summary>
        public DxfCellGridFormatValues Values { get; private set; }
        /// <summary>Gets whether the LTYPE handle is explicitly selected or cleared.</summary>
        public bool ChangesLinetype { get; private set; }
        /// <summary>Gets the selected registered resource; null with ChangesLinetype clears the handle.</summary>
        public Linetype Linetype { get; private set; }
        /// <summary>Returns a new request with replacement values.</summary>
        public DxfCellGridFormatEdit WithValues(DxfCellGridFormatValues values)
        { if (values == null) throw new ArgumentNullException(nameof(values)); return new DxfCellGridFormatEdit(this) { Values = values }; }
        /// <summary>Returns a new request selecting a source-document LTYPE, or explicitly clearing the handle with null.</summary>
        public DxfCellGridFormatEdit WithLinetype(Linetype linetype)
        { return new DxfCellGridFormatEdit(this) { ChangesLinetype = true, Linetype = linetype }; }
    }

    /// <summary>An immutable composable edit of an existing qualified format snapshot.</summary>
    /// <remarks>Only existing fields can be changed; counts, masks, flags controlling frame presence, and packet order stay fixed.</remarks>
    public sealed class DxfCellStyleFormatEdit
    {
        internal DxfCellStyleFormatEdit(DxfCellStyleFormat original)
        { this.Original = original; this.Borders = new List<DxfCellGridFormatEdit>().AsReadOnly(); }
        private DxfCellStyleFormatEdit(DxfCellStyleFormatEdit other)
        {
            this.Original = other.Original; this.Values = other.Values; this.Content = other.Content;
            this.Margins = other.Margins; this.Borders = other.Borders;
            this.ChangesTextStyle = other.ChangesTextStyle; this.TextStyle = other.TextStyle;
        }
        /// <summary>Gets the source format snapshot.</summary>
        public DxfCellStyleFormat Original { get; }
        /// <summary>Gets replacement TABLEFORMAT values, or null to keep them.</summary>
        public DxfCellStyleFormatValues Values { get; private set; }
        /// <summary>Gets replacement CONTENTFORMAT values, or null to keep them.</summary>
        public DxfCellContentFormatValues Content { get; private set; }
        /// <summary>Gets replacement margins, or null to keep them.</summary>
        public DxfCellMargins Margins { get; private set; }
        /// <summary>Gets the complete immutable list of selected grid edits.</summary>
        public IReadOnlyList<DxfCellGridFormatEdit> Borders { get; private set; }
        /// <summary>Gets whether the content STYLE handle is explicitly selected or cleared.</summary>
        public bool ChangesTextStyle { get; private set; }
        /// <summary>Gets the selected source-document resource; null with ChangesTextStyle clears the handle.</summary>
        public TextStyle TextStyle { get; private set; }
        /// <summary>Returns a new request replacing existing non-structural TABLEFORMAT values.</summary>
        public DxfCellStyleFormatEdit WithValues(DxfCellStyleFormatValues values)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            if (this.Original.Values == null) throw new NotSupportedException("No stored TABLEFORMAT data is present.");
            return new DxfCellStyleFormatEdit(this) { Values = values };
        }
        /// <summary>Returns a new request replacing existing content values, including the decoded format expression.</summary>
        public DxfCellStyleFormatEdit WithContent(DxfCellContentFormatValues values)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            if (this.Original.Content == null) throw new NotSupportedException("No stored CONTENTFORMAT is present.");
            return new DxfCellStyleFormatEdit(this) { Content = values };
        }
        /// <summary>Returns a new request replacing the six existing margins.</summary>
        public DxfCellStyleFormatEdit WithMargins(DxfCellMargins margins)
        {
            if (margins == null) throw new ArgumentNullException(nameof(margins));
            if (this.Original.Margins == null) throw new NotSupportedException("No stored CELLMARGIN frame is present.");
            return new DxfCellStyleFormatEdit(this) { Margins = margins };
        }
        /// <summary>Returns a new request selecting a source-document STYLE, or explicitly clearing its handle with null.</summary>
        public DxfCellStyleFormatEdit WithTextStyle(TextStyle textStyle)
        {
            if (this.Original.Content == null) throw new NotSupportedException("No stored CONTENTFORMAT is present.");
            return new DxfCellStyleFormatEdit(this) { ChangesTextStyle = true, TextStyle = textStyle };
        }
        /// <summary>Returns a new request replacing its selected grid edits with distinct current grid snapshots.</summary>
        /// <remarks>Enumeration is bounded by the source border count and completes, including disposal, before a request is returned.</remarks>
        public DxfCellStyleFormatEdit WithBorders(IEnumerable<DxfCellGridFormatEdit> borders)
        {
            if (borders == null) throw new ArgumentNullException(nameof(borders));
            var result = new List<DxfCellGridFormatEdit>(); var seen = new HashSet<DxfCellGridFormat>();
            foreach (DxfCellGridFormatEdit border in borders)
            {
                if (result.Count >= this.Original.Borders.Count || border == null ||
                    !this.Original.Borders.Contains(border.Original) || !seen.Add(border.Original))
                    throw new ArgumentException("Grid edits require distinct borders from this format snapshot.", nameof(borders));
                result.Add(border);
            }
            return new DxfCellStyleFormatEdit(this) { Borders = result.AsReadOnly() };
        }
    }

    /// <summary>An immutable name and/or formatting edit bound to an existing CELLSTYLEMAP entry.</summary>
    public sealed class DxfStoredCellStyleMapEntryEdit
    {
        internal DxfStoredCellStyleMapEntryEdit(DxfStoredCellStyleMapEntry original) { this.Original = original; }
        private DxfStoredCellStyleMapEntryEdit(DxfStoredCellStyleMapEntryEdit other)
        { this.Original = other.Original; this.Name = other.Name; this.Format = other.Format; }
        /// <summary>Gets the source entry snapshot; identifiers are not used to find or retarget it.</summary>
        public DxfStoredCellStyleMapEntry Original { get; }
        /// <summary>Gets the replacement decoded name, or null to keep it.</summary>
        public string Name { get; private set; }
        /// <summary>Gets replacement formatting requests, or null to keep the source packet.</summary>
        public DxfCellStyleFormatEdit Format { get; private set; }
        /// <summary>Returns a new request with an explicit name; empty and duplicate names are retained.</summary>
        public DxfStoredCellStyleMapEntryEdit WithName(string name)
        { DxfStoredTableContent.CheckEditableText(name, nameof(name)); return new DxfStoredCellStyleMapEntryEdit(this) { Name = name }; }
        /// <summary>Returns a new request for this entry's actual format snapshot.</summary>
        public DxfStoredCellStyleMapEntryEdit WithFormat(DxfCellStyleFormatEdit format)
        {
            if (format == null) throw new ArgumentNullException(nameof(format));
            if (!ReferenceEquals(this.Original.Format, format.Original))
                throw new ArgumentException("The format edit must use this entry's snapshot.", nameof(format));
            return new DxfStoredCellStyleMapEntryEdit(this) { Format = format };
        }
    }

    public sealed partial class DxfStoredCellStyleMapEntry
    {
        /// <summary>Creates an immutable name edit bound to this entry snapshot.</summary>
        public DxfStoredCellStyleMapEntryEdit WithName(string name)
        { return new DxfStoredCellStyleMapEntryEdit(this).WithName(name); }
        /// <summary>Creates an immutable format edit bound to this entry and its format snapshot.</summary>
        public DxfStoredCellStyleMapEntryEdit WithFormat(DxfCellStyleFormatEdit format)
        { return new DxfStoredCellStyleMapEntryEdit(this).WithFormat(format); }
    }
}
