// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using netDxf.Blocks;
using netDxf.Entities;

namespace netDxf.Objects
{
    /// <summary>How explicitly supplied adjacent cell border styles are reconciled.</summary>
    public enum DxfTableBorderConflictPolicy
    {
        /// <summary>Reject unequal styles on a shared segment.</summary>
        Reject,
        /// <summary>Use the later row-major cell's style, an explicit application policy rather than inferred native precedence.</summary>
        LastCell
    }
    /// <summary>An immutable physical cell rectangle, excluding merged continuation cells.</summary>
    public sealed class DxfTableCellLayout
    {
        internal DxfTableCellLayout(DxfTableCellRange range, DxfResolvedCellStyle style, string text, double left, double top, double width, double height)
        { this.Range = range; this.Style = style; this.Text = text; this.Left = left; this.Top = top; this.Width = width; this.Height = height; }
        /// <summary>Gets the inclusive logical cell range.</summary>
        public DxfTableCellRange Range { get; }
        /// <summary>Gets the resolved style.</summary>
        public DxfResolvedCellStyle Style { get; }
        /// <summary>Gets literal text, not executable MTEXT control codes.</summary>
        public string Text { get; }
        /// <summary>Gets the distance from the table's left edge.</summary>
        public double Left { get; }
        /// <summary>Gets the distance down from the table's top edge.</summary>
        public double Top { get; }
        /// <summary>Gets the cell width including margins.</summary>
        public double Width { get; }
        /// <summary>Gets the cell height including margins.</summary>
        public double Height { get; }
    }
    /// <summary>A bounded literal-text layout with merged cells, measured row growth and independently generated display geometry.</summary>
    /// <remarks>
    /// Column widths are constraints. Row heights can grow from caller-supplied text metrics; no
    /// installed font or native AutoCAD metrics are guessed. Only horizontal, non-autoscaled text
    /// and top-to-bottom flow are admitted. This is detached display generation, not automatic
    /// replacement of a source TABLE, its private caches, or its backing graph.
    /// </remarks>
    public sealed class DxfTableLayout
    {
        private readonly double[] xs, ys;
        private DxfTableLayout(double[] xs, double[] ys, IList<DxfTableCellLayout> cells)
        { this.xs = xs; this.ys = ys; this.Cells = new List<DxfTableCellLayout>(cells).AsReadOnly(); }
        /// <summary>Gets physical cells in row-major order, without merged continuation cells.</summary>
        public IReadOnlyList<DxfTableCellLayout> Cells { get; }
        /// <summary>Gets the complete table width.</summary>
        public double Width { get { return this.xs[this.xs.Length - 1]; } }
        /// <summary>Gets the complete table height after row growth.</summary>
        public double Height { get { return this.ys[this.ys.Length - 1]; } }
        /// <summary>Calculates cell geometry from an immutable grid and explicit style, literal-text and text-height providers.</summary>
        /// <param name="grid">Source grid, limited to 100,000 logical cells.</param>
        /// <param name="style">Complete style for each visible anchor cell.</param>
        /// <param name="text">Literal text for each visible anchor cell. Continuation contents are not displayed.</param>
        /// <param name="measureHeight">Required height for the text at the supplied usable width and resolved style, excluding margins.</param>
        /// <param name="growRows">Whether to expand rows to fit measured text. Otherwise overflow rejects.</param>
        public static DxfTableLayout Create(DxfTableContentGrid grid,
            Func<DxfTableContentCell, DxfResolvedCellStyle> style, Func<DxfTableContentCell, string> text,
            Func<string, double, DxfResolvedCellStyle, double> measureHeight, bool growRows)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            if (style == null) throw new ArgumentNullException(nameof(style));
            if (text == null) throw new ArgumentNullException(nameof(text));
            if (measureHeight == null) throw new ArgumentNullException(nameof(measureHeight));
            if (grid.Cells.Count > 100000) throw new NotSupportedException("Display layout is limited to 100,000 logical cells.");
            int columns = grid.Columns.Count;
            var heights = grid.Rows.Select(row => row.Size).ToArray();
            var xs = Prefix(grid.Columns.Select(column => column.Size));
            var covered = new bool[grid.Cells.Count];
            var merges = new Dictionary<int, DxfTableCellRange>();
            foreach (var range in grid.Merges)
            {
                int anchor = range.First.Row * columns + range.First.Column;
                merges.Add(anchor, range);
                for (int r = range.First.Row; r <= range.Last.Row; r++)
                for (int c = range.First.Column; c <= range.Last.Column; c++)
                    if (r * columns + c != anchor) covered[r * columns + c] = true;
            }
            var pending = new List<Tuple<DxfTableCellRange, DxfResolvedCellStyle, string>>();
            long characters = 0;
            for (int i = 0; i < grid.Cells.Count; i++)
            {
                if (covered[i]) continue;
                var cell = grid.Cells[i];
                if (!merges.TryGetValue(i, out DxfTableCellRange range)) range = new DxfTableCellRange(cell.Address, cell.Address);
                var resolved = style(cell) ?? throw new ArgumentException("A cell style provider returned null.", nameof(style));
                ValidateStyle(resolved);
                string literal = text(cell) ?? throw new ArgumentException("A cell text provider returned null.", nameof(text));
                DxfStoredTableContent.CheckEditableText(literal, nameof(text));
                if (literal.IndexOf("%%", StringComparison.Ordinal) >= 0 || literal.IndexOf("%<", StringComparison.Ordinal) >= 0 ||
                    literal.IndexOf(">%", StringComparison.Ordinal) >= 0)
                    throw new NotSupportedException("Native symbol or FIELD sequences cannot be admitted as literal cell text.");
                characters += literal.Length;
                if (characters > 1048576) throw new NotSupportedException("A display layout is limited to one million text characters.");
                var margin = resolved.Format.Margins;
                double width = xs[range.Last.Column + 1] - xs[range.First.Column];
                double usable = width - margin.HorizontalMargin - margin.RightMargin;
                if (!(usable > 0)) throw new InvalidOperationException("Cell margins consume the full width: " + cell.Address);
                double measured = measureHeight(literal, usable, resolved);
                if (!Finite(measured) || measured < 0) throw new ArgumentException("Text metrics must return a finite nonnegative height.", nameof(measureHeight));
                double required = measured + margin.VerticalMargin + margin.BottomMargin;
                if (!Finite(required)) throw new ArithmeticException("Cell text extent overflowed.");
                double available = 0;
                for (int r = range.First.Row; r <= range.Last.Row; r++) available += heights[r];
                if (required > available)
                {
                    if (!growRows) throw new InvalidOperationException("Measured text does not fit the fixed row height: " + cell.Address);
                    // Later growth can only help previously satisfied constraints. Distribute a
                    // merged-cell deficit equally across its rows in deterministic row-major order.
                    double delta = (required - available) / (range.Last.Row - range.First.Row + 1);
                    for (int r = range.First.Row; r <= range.Last.Row; r++) heights[r] += delta;
                }
                pending.Add(Tuple.Create(range, resolved, literal));
            }
            var ys = Prefix(heights);
            var cells = pending.Select(item => new DxfTableCellLayout(item.Item1, item.Item2, item.Item3,
                xs[item.Item1.First.Column], ys[item.Item1.First.Row],
                xs[item.Item1.Last.Column + 1] - xs[item.Item1.First.Column],
                ys[item.Item1.Last.Row + 1] - ys[item.Item1.First.Row])).ToList();
            return new DxfTableLayout(xs, ys, cells);
        }
        /// <summary>Builds a fresh unregistered Block with fills, reconciled borders and literal MTEXT at an upper-left origin.</summary>
        /// <remarks>Entities use XY coordinates, with row progression in negative Y. No source block or document is mutated; attaching the returned block is a separate operation.</remarks>
        public Block BuildDisplayBlock(string name, DxfTableBorderConflictPolicy conflicts = DxfTableBorderConflictPolicy.Reject)
        {
            if (conflicts != DxfTableBorderConflictPolicy.Reject && conflicts != DxfTableBorderConflictPolicy.LastCell) throw new ArgumentOutOfRangeException(nameof(conflicts));
            var block = new Block(name);
            var fills = new List<EntityObject>(); var texts = new List<EntityObject>();
            var edges = new SortedDictionary<long, Edge>();
            foreach (var cell in this.Cells)
            {
                var format = cell.Style.Format; var content = format.Content; var margin = format.Margins;
                short fill = format.Values.StoredBackgroundColor;
                if (fill != 257)
                    fills.Add(new Solid(new Vector2(cell.Left, -cell.Top), new Vector2(cell.Left + cell.Width, -cell.Top),
                        new Vector2(cell.Left, -cell.Top - cell.Height), new Vector2(cell.Left + cell.Width, -cell.Top - cell.Height)) { Color = Color(fill) });
                if (cell.Text.Length > 0)
                {
                    int alignment = content.StoredAlignment - 1;
                    double x = cell.Left + margin.HorizontalMargin;
                    double y = cell.Top + margin.VerticalMargin;
                    double width = cell.Width - margin.HorizontalMargin - margin.RightMargin;
                    double height = cell.Height - margin.VerticalMargin - margin.BottomMargin;
                    x += (alignment % 3) * width / 2;
                    y += (alignment / 3) * height / 2;
                    texts.Add(new MText(LiteralMText(cell.Text), new Vector3(x, -y, 0), content.TextHeight, width, format.TextStyle)
                    { AttachmentPoint = (MTextAttachmentPoint)content.StoredAlignment, Color = Color(content.StoredColor) });
                }
                var borders = format.Borders.ToDictionary(border => border.StoredIndexMask);
                int top = cell.Range.First.Row, bottom = cell.Range.Last.Row + 1, left = cell.Range.First.Column, right = cell.Range.Last.Column + 1;
                for (int c = left; c < right; c++)
                {
                    this.AddEdge(edges, true, top, c, borders, top == 0 ? 1 : 2, conflicts);
                    this.AddEdge(edges, true, bottom, c, borders, bottom == this.ys.Length - 1 ? 4 : 2, conflicts);
                }
                for (int r = top; r < bottom; r++)
                {
                    this.AddEdge(edges, false, r, left, borders, left == 0 ? 8 : 16, conflicts);
                    this.AddEdge(edges, false, r, right, borders, right == this.xs.Length - 1 ? 32 : 16, conflicts);
                }
            }
            var lines = new List<EntityObject>();
            foreach (var edge in edges.Values)
            {
                var border = edge.Style;
                if (border.Values.StoredVisibility == 1) continue;
                int count = border.Values.StoredBorderType == 2 ? 2 : 1;
                for (int line = 0; line < count; line++)
                {
                    double offset = count == 1 ? 0 : (line == 0 ? -0.5 : 0.5) * border.Values.DoubleLineSpacing;
                    var shift = edge.Horizontal ? new Vector3(0, offset, 0) : new Vector3(offset, 0, 0);
                    lines.Add(new Line(edge.First + shift, edge.Last + shift) { Color = Color(border.Values.StoredColor),
                        Linetype = border.Linetype, Lineweight = (Lineweight)border.Values.StoredLineweight });
                }
            }
            block.Entities.AddRange(fills); block.Entities.AddRange(lines); block.Entities.AddRange(texts);
            return block;
        }
        private sealed class Edge
        { internal bool Horizontal; internal Vector3 First, Last; internal DxfCellGridFormatDefinition Style; }
        private void AddEdge(IDictionary<long, Edge> edges, bool horizontal, int row, int column,
            IDictionary<int, DxfCellGridFormatDefinition> borders, int kind, DxfTableBorderConflictPolicy conflicts)
        {
            if (!borders.TryGetValue(kind, out DxfCellGridFormatDefinition border)) return;
            long key = (horizontal ? 0L : 1L << 62) | ((long)row << 31) | (uint)column;
            if (edges.TryGetValue(key, out Edge prior) && conflicts == DxfTableBorderConflictPolicy.Reject && !SameBorder(prior.Style, border))
                throw new InvalidOperationException("Adjacent cells provide conflicting shared-border styles.");
            edges[key] = new Edge { Horizontal = horizontal, Style = border, First = new Vector3(this.xs[column], -this.ys[row], 0),
                Last = horizontal ? new Vector3(this.xs[column + 1], -this.ys[row], 0) : new Vector3(this.xs[column], -this.ys[row + 1], 0) };
        }
        private static bool SameBorder(DxfCellGridFormatDefinition a, DxfCellGridFormatDefinition b)
        {
            var x = a.Values; var y = b.Values;
            return ReferenceEquals(a.Linetype, b.Linetype) && x.StoredBorderType == y.StoredBorderType && x.StoredColor == y.StoredColor &&
                x.StoredLineweight == y.StoredLineweight && x.StoredVisibility == y.StoredVisibility && x.DoubleLineSpacing == y.DoubleLineSpacing;
        }
        private static void ValidateStyle(DxfResolvedCellStyle resolved)
        {
            var format = resolved.Format; var c = format.Content; var m = format.Margins;
            if (c.Rotation != 0 || c.StoredPropertyFlags != 0 || format.Values.StoredMergeFlags != 0 || format.Values.StoredContentLayout != 1)
                throw new NotSupportedException("Layout requires horizontal, non-autoscaled, top-to-bottom literal text without style-driven merge-all.");
            if (c.TextHeight <= 0 || format.TextStyle == null || c.StoredAlignment < 1 || c.StoredAlignment > 9)
                throw new NotSupportedException("Layout requires positive text height, a STYLE and alignment 1 through 9.");
            if (m.HorizontalMargin < 0 || m.RightMargin < 0 || m.VerticalMargin < 0 || m.BottomMargin < 0 || m.HorizontalSpacing < 0 || m.VerticalSpacing < 0)
                throw new NotSupportedException("Negative margins cannot be laid out.");
            Color(c.StoredColor);
            if (format.Values.StoredBackgroundColor != 257) Color(format.Values.StoredBackgroundColor);
            foreach (var border in format.Borders)
            {
                var value = border.Values;
                if (value.StoredBorderType != 1 && value.StoredBorderType != 2 || value.StoredVisibility != 0 && value.StoredVisibility != 1 ||
                    border.Linetype == null || value.DoubleLineSpacing < 0 || !Enum.IsDefined(typeof(Lineweight), value.StoredLineweight))
                    throw new NotSupportedException("Unknown border type, visibility, spacing, lineweight or LTYPE.");
                Color(value.StoredColor);
            }
        }
        private static AciColor Color(short value)
        {
            if (value == 0) return AciColor.ByBlock;
            if (value == 256) return AciColor.ByLayer;
            if (value > 0 && value < 256) return new AciColor(value);
            throw new NotSupportedException("Display generation requires an indexed, ByLayer or ByBlock color.");
        }
        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
        private static double[] Prefix(IEnumerable<double> sizes)
        {
            var result = new List<double> { 0 };
            foreach (double size in sizes)
            {
                double next = result[result.Count - 1] + size;
                if (!Finite(next) || !(next > result[result.Count - 1])) throw new ArithmeticException("Table dimensions overflow or lose a positive extent.");
                result.Add(next);
            }
            return result.ToArray();
        }
        private static string LiteralMText(string value)
        {
            return value.Replace("\\", "\\\\").Replace("{", "\\{").Replace("}", "\\}")
                .Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\\P");
        }
    }
}
