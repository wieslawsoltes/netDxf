// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using netDxf.Header;
using netDxf.IO;

namespace netDxf.Entities
{
    /// <summary>A read-only projection of the fixed row and column representation stored in AcDbTable.</summary>
    public sealed class StoredTableGrid
    {
        private StoredTableGrid(int rows, int columns, List<double> heights, List<double> widths, List<StoredTableCell> cells)
        { this.RowCount = rows; this.ColumnCount = columns; this.RowHeights = heights.AsReadOnly(); this.ColumnWidths = widths.AsReadOnly(); this.Cells = cells.AsReadOnly(); }
        /// <summary>Gets the declared row count.</summary>
        public int RowCount { get; }
        /// <summary>Gets the declared column count.</summary>
        public int ColumnCount { get; }
        /// <summary>Gets the stored row heights.</summary>
        public IReadOnlyList<double> RowHeights { get; }
        /// <summary>Gets the stored column widths.</summary>
        public IReadOnlyList<double> ColumnWidths { get; }
        /// <summary>Gets cells in row-major order, including empty and merged continuation cells.</summary>
        public IReadOnlyList<StoredTableCell> Cells { get; }
        /// <summary>Gets a cell by zero-based row and column.</summary>
        public StoredTableCell this[int row, int column]
        {
            get
            {
                if (row < 0 || row >= this.RowCount) throw new ArgumentOutOfRangeException(nameof(row));
                if (column < 0 || column >= this.ColumnCount) throw new ArgumentOutOfRangeException(nameof(column));
                return this.Cells[row * this.ColumnCount + column];
            }
        }
        internal static StoredTableGrid TryRead(IReadOnlyList<DxfTag> source, Func<string, string> decode, DxfVersion version)
        {
            var tags = source.ToList();
            int start = tags.FindIndex(t => t.Code == 100 && (string)t.Value == "AcDbTable");
            if (start < 0 || tags.Skip(start + 1).Any(t => t.Code == 100)) return null;
            int first = tags.FindIndex(start + 1, t => t.Code == 171);
            if (first < 0) return null;
            var head = tags.Skip(start + 1).Take(first - start - 1).ToList();
            var schema = head.Where(t => t.Code == 90).ToList();
            if (schema.Count != 1 || (int)schema[0].Value != 22) return null;
            int rows = SingleInt(head, 91), columns = SingleInt(head, 92);
            if (rows <= 0 || columns <= 0 || (long)rows * columns > 1000000)
                throw new InvalidDataException("TABLE fixed-grid dimensions are invalid or exceed one million cells.");
            var heights = head.Where(t => t.Code == 141).Select(t => (double)t.Value).ToList();
            var widths = head.Where(t => t.Code == 142).Select(t => (double)t.Value).ToList();
            if (heights.Count != rows || widths.Count != columns || heights.Any(v => v <= 0) || widths.Any(v => v <= 0))
                throw new InvalidDataException("TABLE fixed-grid dimensions disagree with stored row heights or column widths.");
            var starts = tags.Select((t, i) => new { t, i }).Where(x => x.i >= first && x.t.Code == 171).Select(x => x.i).ToList();
            if (starts.Count != (long)rows * columns) throw new InvalidDataException("TABLE fixed-grid cell count disagrees with its dimensions.");
            var cells = new List<StoredTableCell>();
            for (int i = 0; i < starts.Count; i++)
            {
                int end = i + 1 < starts.Count ? starts[i + 1] : tags.Count;
                var body = tags.GetRange(starts[i], end - starts[i]);
                cells.Add(new StoredTableCell(body, decode, version));
            }
            return new StoredTableGrid(rows, columns, heights, widths, cells);
        }
        private static int SingleInt(List<DxfTag> tags, short code)
        { var found = tags.Where(t => t.Code == code).ToList(); if (found.Count != 1) throw new InvalidDataException("TABLE has missing or duplicate dimension fields."); return (int)found[0].Value; }
    }
    /// <summary>A stored cell with an optional literal value and its complete immutable source tags.</summary>
    public sealed class StoredTableCell
    {
        internal StoredTableCell(List<DxfTag> body, Func<string, string> decode, DxfVersion version)
        {
            this.Tags = new ReadOnlyCollection<DxfTag>(body);
            this.StoredType = (short)body[0].Value;
            if (this.StoredType != 1) return;
            int marker = body.FindIndex(t => t.Code == 301 && (string)t.Value == "CELL_VALUE");
            if (marker >= 0)
            {
                if (body.Count(t => t.Code == 301 && (string)t.Value == "CELL_VALUE") != 1) return;
                int end = body.FindIndex(marker + 1, t => t.Code == 304 && (string)t.Value == "ACVALUE_END");
                if (end < 0) throw new InvalidDataException("TABLE cell has an unterminated ACVALUE envelope.");
                var value = body.Skip(marker + 1).Take(end - marker - 1).ToList();
                var types = value.Where(t => t.Code == 90).ToList();
                if (types.Count != 1) throw new InvalidDataException("TABLE cell has missing or duplicate value types.");
                this.ValueType = (int)types[0].Value;
                var flags = value.Where(t => t.Code == 93).ToList();
                if (flags.Count != 1) throw new InvalidDataException("TABLE cell has missing or duplicate value flags.");
                this.StoredFlags = (int)flags[0].Value;
                // Native packets retain a declared data type while flags mark an absent value.
                // Preserve that distinction instead of inventing a scalar from formatted text.
                if ((this.StoredFlags.Value & 1) != 0) return;
                short code = this.ValueType == 4 ? (short)1 : this.ValueType == 2 ? (short)140 : this.ValueType == 1 ? (short)91 : (short)-1;
                if (this.ValueType == 0) { this.HasLiteralValue = true; return; }
                if (code < 0) return;
                var values = value.Where(t => t.Code == code).ToList();
                if (values.Count != 1) throw new InvalidDataException("TABLE literal value is missing or repeated.");
                this.LiteralValue = values[0].Value is string text ? decode(text) : values[0].Value;
                this.HasLiteralValue = true;
            }
            else if (version == DxfVersion.AutoCad2004)
            {
                var values = body.Where(t => t.Code == 1).ToList();
                if (values.Count != 1) return;
                this.ValueType = 4; this.LiteralValue = decode((string)values[0].Value); this.HasLiteralValue = true;
            }
        }
        /// <summary>Gets the independent stored cell type; block and private content are not coerced to text.</summary>
        public short StoredType { get; }
        /// <summary>Gets the optional ACVALUE data type: 0 empty, 1 integer, 2 real, or 4 string.</summary>
        public int? ValueType { get; }
        /// <summary>Gets the independent stored ACVALUE flags, when present.</summary>
        public int? StoredFlags { get; }
        /// <summary>Gets whether LiteralValue was recognized, including an explicit empty value.</summary>
        public bool HasLiteralValue { get; }
        /// <summary>Gets the decoded literal string, integer, real, or null; formulas and fields are not evaluated.</summary>
        public object LiteralValue { get; }
        /// <summary>Gets the immutable source cell packet, including private and unmodeled fields.</summary>
        public IReadOnlyList<DxfTag> Tags { get; }
    }
}
