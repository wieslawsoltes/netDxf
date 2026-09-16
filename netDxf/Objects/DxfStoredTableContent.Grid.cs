// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using netDxf.IO;
using netDxf.Tables;
using netDxf.Units;

namespace netDxf.Objects
{
    public sealed partial class DxfStoredTableContent
    {
        /// <summary>Projects a validated addressed grid without inventing values for unsupported content.</summary>
        /// <remarks>
        /// Uses complete linked frame structure, not scalar ordinal position. Widths and heights must
        /// be positive and finite, and merges in bounds and nonoverlapping. Local formats bind exact
        /// source STYLE/LTYPE identities. Unknown contents remain explicitly incomplete; they are
        /// never shifted into another cell or converted into strings. This operation does not mutate.
        /// </remarks>
        public DxfTableContentGrid GetGrid()
        {
            this.GetCellStyleReferences();
            int rows = this.RowCount.Value, columns = this.ColumnCount.Value;
            if (rows < 1 || columns < 1 || (long)rows * columns > 1000000) throw new NotSupportedException("An addressed table must contain one through one million cells.");
            var tags = this.Subclasses[1].Tags;
            var root = this.ReadGridFrames(tags);
            var expected = new List<string>();
            for (int c = 0; c < columns; c++) expected.AddRange(new[] { "LINKEDTABLEDATACOLUMN", "FORMATTEDTABLEDATACOLUMN", "TABLECOLUMN" });
            for (int r = 0; r < rows; r++) expected.AddRange(new[] { "LINKEDTABLEDATAROW", "FORMATTEDTABLEDATAROW", "TABLEROW" });
            if (!root.Children.Select(frame => frame.Name).SequenceEqual(expected)) throw new NotSupportedException("Unqualified table band ordering.");
            var rowBands = new List<DxfTableContentBand>(); var columnBands = new List<DxfTableContentBand>();
            var cells = new List<DxfTableContentCell>();
            var scalars = this.StoredValues.ToDictionary(value => value.PayloadIndex);
            int offset = this.Subclasses[0].Tags.Count;
            for (int c = 0; c < columns; c++) columnBands.Add(this.GridBand(root.Children[c * 3 + 1], root.Children[c * 3 + 2], tags));
            for (int r = 0; r < rows; r++)
            {
                int first = columns * 3 + r * 3;
                GridFrame row = root.Children[first];
                rowBands.Add(this.GridBand(root.Children[first + 1], root.Children[first + 2], tags));
                GridFrame[] frames = row.Children.Where(frame => frame.Name != "DATAMAP").ToArray();
                if (frames.Length != columns * 3) throw new NotSupportedException("The complete row cell inventory is unavailable.");
                for (int c = 0; c < columns; c++)
                {
                    GridFrame linked = frames[c * 3], formatted = frames[c * 3 + 1], reference = frames[c * 3 + 2];
                    if (linked.Name != "LINKEDTABLEDATACELL" || formatted.Name != "FORMATTEDTABLEDATACELL" || reference.Name != "TABLECELL")
                        throw new NotSupportedException("Unqualified linked cell order.");
                    int count = (int)GridSingle(linked, tags, 95).Value;
                    if (count < 0 || count > MaximumPayloadTags) throw new NotSupportedException("Invalid cell content count.");
                    var contentFrames = linked.Children.Where(frame => frame.Name == "CELLCONTENT").ToArray();
                    if (contentFrames.Length != count) throw new NotSupportedException("Declared and actual cell content counts differ.");
                    var contents = new List<DxfStoredTableContentValue>();
                    foreach (GridFrame frame in contentFrames)
                        if (scalars.TryGetValue(offset + frame.Start, out DxfStoredTableContentValue value)) contents.Add(value);
                    cells.Add(new DxfTableContentCell(new DxfTableCellAddress(r, c), (int)GridSingle(reference, tags, 90).Value,
                        count, contents, this.GridFormat(formatted, tags)));
                }
            }
            if (cells.Sum(cell => cell.Contents.Count) != this.StoredValues.Count) throw new NotSupportedException("Some scalar content cannot be associated with a unique cell.");
            var formattedTags = this.Subclasses[2].Tags;
            int formatEnd = QualifiedStyleReferenceFormatEnd(formattedTags, 2);
            DxfCellStyleFormat format = this.ReadBoundGridFormat(formattedTags, 2, formatEnd);
            var merges = new List<DxfTableCellRange>(); var used = new bool[rows * columns];
            for (int i = formatEnd + 2; i < formattedTags.Count; i += 4)
            {
                var range = new DxfTableCellRange(new DxfTableCellAddress((int)formattedTags[i].Value, (int)formattedTags[i + 1].Value),
                    new DxfTableCellAddress((int)formattedTags[i + 2].Value, (int)formattedTags[i + 3].Value));
                for (int r = range.First.Row; r <= range.Last.Row; r++)
                for (int c = range.First.Column; c <= range.Last.Column; c++)
                {
                    int index = r * columns + c;
                    if (used[index]) throw new NotSupportedException("Overlapping merged cells cannot be laid out unambiguously.");
                    used[index] = true;
                }
                merges.Add(range);
            }
            return new DxfTableContentGrid(this.Payload, rowBands, columnBands, cells, format, merges);
        }

        /// <summary>Calculates explicit formulas and atomically writes same-kind numeric values and evaluated display strings.</summary>
        /// <remarks>
        /// Formulas are input requests, not persisted FIELD definitions. Targets must already contain
        /// exactly one integer or real scalar. Fractional/overflowing integer results reject rather than
        /// truncate. One invalid formula, target, format or callback aborts every edit. The existing
        /// content transaction guards enumeration and disposal. Inline TABLE and geometry caches are not changed.
        /// </remarks>
        public void ApplyFormulaResults(IEnumerable<KeyValuePair<DxfTableCellAddress, string>> formulas)
        {
            if (formulas == null) throw new ArgumentNullException(nameof(formulas));
            this.ReplaceContent(this.Name, this.Description, this.TableStyle, this.FormulaEdits(formulas));
        }
        private IEnumerable<DxfStoredTableContentValueEdit> FormulaEdits(IEnumerable<KeyValuePair<DxfTableCellAddress, string>> formulas)
        {
            var grid = this.GetGrid();
            var results = grid.EvaluateFormulas(formulas);
            foreach (var result in results)
            {
                DxfTableContentCell cell = grid[result.Key];
                if (!cell.HasCompleteScalarContent || cell.Contents.Count != 1) throw new NotSupportedException("A formula result target must contain exactly one numeric scalar: " + result.Key);
                var original = cell.Contents[0]; object value;
                if (original.Kind == DxfStoredTableContentValueKind.Double) value = result.Value;
                else if (original.Kind == DxfStoredTableContentValueKind.Integer && result.Value >= int.MinValue && result.Value <= int.MaxValue && result.Value == Math.Truncate(result.Value)) value = (int)result.Value;
                else throw new NotSupportedException("The formula result cannot retain the target scalar kind: " + result.Key);
                string display = original.DisplayIndex < 0 ? null : DxfValueFormat.Parse(original.FormatString).Format(value, original.StoredUnitType.Value);
                yield return original.WithValue(value, display);
            }
        }
        private DxfTableContentBand GridBand(GridFrame formatted, GridFrame reference, IReadOnlyList<DxfTag> tags)
        {
            double size = (double)GridSingle(reference, tags, 40).Value;
            if (size <= 0 || double.IsNaN(size) || double.IsInfinity(size)) throw new NotSupportedException("Addressed table widths/heights must be positive and finite.");
            return new DxfTableContentBand(size, (int)GridSingle(reference, tags, 90).Value, this.GridFormat(formatted, tags));
        }
        private DxfCellStyleFormat GridFormat(GridFrame frame, IReadOnlyList<DxfTag> tags)
        {
            if (frame.Children.Count != 1 || frame.Children[0].Name != "TABLEFORMAT") throw new NotSupportedException("A cell or band requires one complete local TABLEFORMAT.");
            return this.ReadBoundGridFormat(tags, frame.Children[0].Start, frame.Children[0].End);
        }
        private DxfCellStyleFormat ReadBoundGridFormat(IReadOnlyList<DxfTag> tags, int start, int end)
        {
            var format = DxfCellStyleFormat.TryRead(tags.Skip(start).Take(end - start + 1).ToList(), DecodeStoredText);
            if (format == null) throw new NotSupportedException("An extended formatting packet cannot be projected.");
            format.Bind(this.handles); return format;
        }
        private static DxfTag GridSingle(GridFrame frame, IReadOnlyList<DxfTag> tags, short code)
        {
            var fields = frame.Fields.Where(index => tags[index].Code == code).ToArray();
            if (fields.Length != 1) throw new NotSupportedException("A required addressed-grid field is absent or duplicated: " + code);
            return tags[fields[0]];
        }
        private sealed class GridFrame
        {
            internal string Name; internal int Start, End;
            internal readonly List<int> Fields = new List<int>();
            internal readonly List<GridFrame> Children = new List<GridFrame>();
        }
        private GridFrame ReadGridFrames(IReadOnlyList<DxfTag> tags)
        {
            var root = new GridFrame { Name = "ROOT", Start = 0, End = tags.Count - 1 };
            var stack = new Stack<GridFrame>(); stack.Push(root);
            for (int i = 1; i < tags.Count; i++)
            {
                DxfTag tag = tags[i]; string text = tag.Value as string;
                if (tag.Code == 300 && text == "VALUE" && stack.Peek().Name == "CELLCONTENT")
                { i = StyleReferenceValueEnd(tags, i); continue; }
                if (tag.Code == 1 && text == "DATAMAP_BEGIN")
                {
                    if (!TryReadDataMapEnd(tags, i, out int end)) throw new NotSupportedException("Unqualified DATAMAP framing.");
                    stack.Peek().Children.Add(new GridFrame { Name = "DATAMAP", Start = i, End = end }); i = end; continue;
                }
                if (tag.Code == 1)
                {
                    if (stack.Count >= 64 || text == null || !text.EndsWith("_BEGIN", StringComparison.Ordinal)) throw new NotSupportedException("Unknown or excessive grid frame nesting.");
                    var frame = new GridFrame { Name = text.Substring(0, text.Length - 6), Start = i };
                    stack.Peek().Children.Add(frame); stack.Push(frame);
                }
                else if (tag.Code == 309)
                {
                    if (stack.Count <= 1 || text != stack.Peek().Name + "_END") throw new NotSupportedException("Unbalanced grid framing.");
                    stack.Pop().End = i;
                }
                else stack.Peek().Fields.Add(i);
            }
            if (stack.Count != 1) throw new NotSupportedException("Unterminated grid framing.");
            return root;
        }
    }
}
