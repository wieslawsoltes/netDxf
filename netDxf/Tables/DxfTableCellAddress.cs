// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Globalization;

namespace netDxf.Tables
{
    /// <summary>A zero-based table cell address with invariant A1 notation.</summary>
    public readonly struct DxfTableCellAddress : IEquatable<DxfTableCellAddress>
    {
        /// <summary>Creates an address. Bounds within a particular table are checked by that table.</summary>
        public DxfTableCellAddress(int row, int column)
        {
            if (row < 0) throw new ArgumentOutOfRangeException(nameof(row));
            if (column < 0) throw new ArgumentOutOfRangeException(nameof(column));
            this.Row = row; this.Column = column;
        }
        /// <summary>Gets the zero-based row.</summary>
        public int Row { get; }
        /// <summary>Gets the zero-based column.</summary>
        public int Column { get; }
        /// <summary>Parses case-insensitive A1 notation. Optional dollar signs are accepted as absolute address markers.</summary>
        public static DxfTableCellAddress Parse(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            if (text.Length == 0 || text.Length > 24) throw new FormatException("Invalid table cell address.");
            int i = 0; long column = 0, row = 0;
            if (text[i] == '$') i++;
            int start = i;
            while (i < text.Length)
            {
                char c = text[i];
                if (c >= 'a' && c <= 'z') c = (char)(c - 'a' + 'A');
                if (c < 'A' || c > 'Z') break;
                column = column * 26 + c - 'A' + 1;
                if (column > (long)int.MaxValue + 1) throw new FormatException("Table column overflows an address.");
                i++;
            }
            if (start == i) throw new FormatException("A table address requires a column.");
            if (i < text.Length && text[i] == '$') i++;
            start = i;
            while (i < text.Length && text[i] >= '0' && text[i] <= '9')
            {
                row = row * 10 + text[i++] - '0';
                if (row > (long)int.MaxValue + 1) throw new FormatException("Table row overflows an address.");
            }
            if (start == i || i != text.Length || row == 0 || text[start] == '0') throw new FormatException("A table address requires a positive row without leading zeros.");
            return new DxfTableCellAddress((int)(row - 1), (int)(column - 1));
        }
        /// <summary>Gets invariant A1 notation without dollar markers.</summary>
        public override string ToString()
        {
            long column = (long)this.Column + 1;
            string text = string.Empty;
            do { column--; text = (char)('A' + column % 26) + text; column /= 26; } while (column != 0);
            return text + ((long)this.Row + 1).ToString(CultureInfo.InvariantCulture);
        }
        /// <summary>Compares both address coordinates.</summary>
        public bool Equals(DxfTableCellAddress other) { return this.Row == other.Row && this.Column == other.Column; }
        /// <inheritdoc/>
        public override bool Equals(object obj) { return obj is DxfTableCellAddress other && this.Equals(other); }
        /// <inheritdoc/>
        public override int GetHashCode() { unchecked { return this.Row * 397 ^ this.Column; } }
        /// <summary>Compares two addresses.</summary>
        public static bool operator ==(DxfTableCellAddress left, DxfTableCellAddress right) { return left.Equals(right); }
        /// <summary>Compares two addresses.</summary>
        public static bool operator !=(DxfTableCellAddress left, DxfTableCellAddress right) { return !left.Equals(right); }
    }
}
