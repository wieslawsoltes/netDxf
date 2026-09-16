// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using netDxf.IO;
using netDxf.Tables;

namespace netDxf.Objects
{
    /// <summary>An immutable rectangular inclusive cell range.</summary>
    public sealed class DxfTableCellRange
    {
        /// <summary>Creates a non-reversed inclusive range.</summary>
        public DxfTableCellRange(DxfTableCellAddress first, DxfTableCellAddress last)
        {
            if (first.Row > last.Row || first.Column > last.Column) throw new ArgumentException("Cell ranges must not be reversed.");
            this.First = first; this.Last = last;
        }
        /// <summary>Gets the first cell.</summary>
        public DxfTableCellAddress First { get; }
        /// <summary>Gets the last cell.</summary>
        public DxfTableCellAddress Last { get; }
        /// <summary>Tests whether a cell belongs to this range.</summary>
        public bool Contains(DxfTableCellAddress address)
        { return address.Row >= this.First.Row && address.Row <= this.Last.Row && address.Column >= this.First.Column && address.Column <= this.Last.Column; }
    }
    /// <summary>A stored column width or row height and its scoped cell-style reference and override packet.</summary>
    public sealed class DxfTableContentBand
    {
        internal DxfTableContentBand(double size, int styleId, DxfCellStyleFormat format)
        { this.Size = size; this.StyleId = styleId; this.Format = format; }
        /// <summary>Gets a positive finite width or height.</summary>
        public double Size { get; }
        /// <summary>Gets the stored style identifier; zero is unselected.</summary>
        public int StyleId { get; }
        /// <summary>Gets the bound immutable local formatting packet.</summary>
        public DxfCellStyleFormat Format { get; }
    }
    /// <summary>A cell address associated with the exact stored scalar and formatting snapshots.</summary>
    public sealed class DxfTableContentCell
    {
        internal DxfTableContentCell(DxfTableCellAddress address, int styleId, int count,
            IList<DxfStoredTableContentValue> contents, DxfCellStyleFormat format)
        {
            this.Address = address; this.StyleId = styleId; this.DeclaredContentCount = count;
            this.Contents = new List<DxfStoredTableContentValue>(contents).AsReadOnly(); this.Format = format;
        }
        /// <summary>Gets the zero-based address.</summary>
        public DxfTableCellAddress Address { get; }
        /// <summary>Gets the stored cell-style identifier.</summary>
        public int StyleId { get; }
        /// <summary>Gets the declared count, including content types that have no scalar projection.</summary>
        public int DeclaredContentCount { get; }
        /// <summary>Gets all qualified scalar contents in this cell, in source order.</summary>
        public IReadOnlyList<DxfStoredTableContentValue> Contents { get; }
        /// <summary>Gets whether every declared content has a scalar projection, including an empty cell.</summary>
        public bool HasCompleteScalarContent { get { return this.Contents.Count == this.DeclaredContentCount; } }
        /// <summary>Gets the immutable bound cell-format override packet.</summary>
        public DxfCellStyleFormat Format { get; }
        internal object CalculationValue()
        {
            if (!this.HasCompleteScalarContent || this.Contents.Count > 1) throw new NotSupportedException("Formula operands require one scalar or an empty cell: " + this.Address);
            return this.Contents.Count == 0 ? null : this.Contents[0].Value;
        }
    }
    /// <summary>An immutable, addressed TABLECONTENT projection associated with one original payload snapshot.</summary>
    public sealed class DxfTableContentGrid
    {
        internal DxfTableContentGrid(IReadOnlyList<DxfTag> payload, IList<DxfTableContentBand> rows,
            IList<DxfTableContentBand> columns, IList<DxfTableContentCell> cells,
            DxfCellStyleFormat format, IList<DxfTableCellRange> merges)
        {
            this.Payload = payload; this.Rows = new List<DxfTableContentBand>(rows).AsReadOnly();
            this.Columns = new List<DxfTableContentBand>(columns).AsReadOnly(); this.Cells = new List<DxfTableContentCell>(cells).AsReadOnly();
            this.Format = format; this.Merges = new List<DxfTableCellRange>(merges).AsReadOnly();
        }
        /// <summary>Gets the source payload snapshot, for freshness checks.</summary>
        public IReadOnlyList<DxfTag> Payload { get; }
        /// <summary>Gets rows in source order.</summary>
        public IReadOnlyList<DxfTableContentBand> Rows { get; }
        /// <summary>Gets columns in source order.</summary>
        public IReadOnlyList<DxfTableContentBand> Columns { get; }
        /// <summary>Gets cells in row-major order, including merged continuation cells.</summary>
        public IReadOnlyList<DxfTableContentCell> Cells { get; }
        /// <summary>Gets the table-level format override packet.</summary>
        public DxfCellStyleFormat Format { get; }
        /// <summary>Gets nonoverlapping merged rectangles in stored order.</summary>
        public IReadOnlyList<DxfTableCellRange> Merges { get; }
        /// <summary>Gets a cell by its address, validating both dimensions.</summary>
        public DxfTableContentCell this[DxfTableCellAddress address]
        {
            get
            {
                if (address.Row >= this.Rows.Count || address.Column >= this.Columns.Count) throw new ArgumentOutOfRangeException(nameof(address));
                return this.Cells[address.Row * this.Columns.Count + address.Column];
            }
        }
        /// <summary>Evaluates caller-supplied formulas without changing this snapshot or its document.</summary>
        public IReadOnlyDictionary<DxfTableCellAddress, double> EvaluateFormulas(IEnumerable<KeyValuePair<DxfTableCellAddress, string>> formulas)
        { return DxfTableCalculation.Evaluate(this.Rows.Count, this.Columns.Count, address => this[address].CalculationValue(), formulas); }
    }
}
