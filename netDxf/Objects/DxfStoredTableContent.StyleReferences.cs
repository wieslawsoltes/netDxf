// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using netDxf.IO;

namespace netDxf.Objects
{
    /// <summary>The public TABLECONTENT frame selecting a stored cell-style identifier.</summary>
    public enum DxfTableContentStyleReferenceKind
    {
        /// <summary>A TABLECOLUMN frame.</summary>
        Column,
        /// <summary>A TABLEROW frame.</summary>
        Row,
        /// <summary>A TABLECELL frame.</summary>
        Cell
    }

    /// <summary>An immutable reference occurrence in one TABLECONTENT payload snapshot.</summary>
    public sealed class DxfTableContentStyleReference
    {
        internal DxfTableContentStyleReference(DxfTableContentStyleReferenceKind kind, int id, int index)
        { this.Kind = kind; this.StoredId = id; this.PayloadIndex = index; }
        /// <summary>Gets the containing public frame kind.</summary>
        public DxfTableContentStyleReferenceKind Kind { get; }
        /// <summary>Gets the stored identifier; zero retains the source's default/unselected slot.</summary>
        public int StoredId { get; }
        /// <summary>Gets the zero-based group-90 position in the associated immutable Payload snapshot.</summary>
        public int PayloadIndex { get; }
    }

    public sealed partial class DxfStoredTableContent
    {
        /// <summary>Reads all qualified column, row and cell style-ID slots in stored order.</summary>
        /// <remarks>Rejects unsupported frames, misplaced/duplicate slots and incomplete counts. AcValue text and DATAMAP values are not mistaken for frame delimiters. This reads a snapshot, not a mutable reference list.</remarks>
        public IReadOnlyList<DxfTableContentStyleReference> GetCellStyleReferences()
        {
            if (!this.ColumnCount.HasValue || !this.RowCount.HasValue || this.Name == null || this.Description == null)
                throw new NotSupportedException("TABLECONTENT outer structure is not qualified for style-ID remapping.");
            this.ValidateStyleReferenceFormatting();
            var tags = this.Subclasses[1].Tags;
            int offset = this.Subclasses[0].Tags.Count;
            int columns = this.ColumnCount.Value, rows = this.RowCount.Value;
            int linkedColumns = 0, linkedRows = 0, columnRefs = 0, rowRefs = 0;
            int rowCells = 0, rowCellRefs = 0, cellRefs = 0;
            var stack = new Stack<string>();
            var result = new List<DxfTableContentStyleReference>();
            for (int i = 1; i < tags.Count; i++)
            {
                DxfTag tag = tags[i]; string text = tag.Value as string;
                if (tag.Code == 300 && text == "VALUE" && stack.Count > 0 && stack.Peek() == "CELLCONTENT")
                { i = StyleReferenceValueEnd(tags, i); continue; }
                if (tag.Code == 1 && text == "DATAMAP_BEGIN")
                {
                    if (!TryReadDataMapEnd(tags, i, out int end)) throw new NotSupportedException("Unqualified DATAMAP may hide style-reference framing.");
                    i = end; continue;
                }
                if (tag.Code == 1)
                {
                    if (!text.EndsWith("_BEGIN", StringComparison.Ordinal) || !FrameNames.Contains(text.Substring(0, text.Length - 6)))
                        throw new NotSupportedException("Unknown TABLECONTENT frame prevents complete style-reference qualification.");
                    string frame = text.Substring(0, text.Length - 6);
                    if (frame == "TABLECOLUMN" || frame == "TABLEROW" || frame == "TABLECELL")
                    {
                        bool cell = frame == "TABLECELL";
                        string previous = "FORMATTEDTABLEDATA" + (cell ? "CELL" : frame == "TABLEROW" ? "ROW" : "COLUMN") + "_END";
                        if (i == 0 || tags[i - 1].Code != 309 || (string)tags[i - 1].Value != previous ||
                            (cell ? stack.Count != 1 || stack.Peek() != "LINKEDTABLEDATAROW" : stack.Count != 0))
                            throw new NotSupportedException("A style-ID frame is outside its qualified public scope.");
                        int end = ReadStyleReferenceFrame(tags, i, frame);
                        int id = (int)tags[i + 1].Value;
                        if (id < 0) throw new NotSupportedException("Negative style-ID sentinel semantics are not qualified.");
                        if (cell)
                        {
                            if (rowCells != rowCellRefs + 1) throw new NotSupportedException("A cell has duplicate or missing style-ID slots.");
                            rowCellRefs++; cellRefs++;
                        }
                        else if (frame == "TABLECOLUMN")
                        {
                            if (linkedRows != 0 || linkedColumns != columnRefs + 1) throw new NotSupportedException("Column style-ID framing is ambiguous.");
                            columnRefs++;
                        }
                        else
                        {
                            if (linkedRows != rowRefs + 1) throw new NotSupportedException("Row style-ID framing is ambiguous.");
                            rowRefs++;
                        }
                        var kind = cell ? DxfTableContentStyleReferenceKind.Cell : frame == "TABLEROW" ? DxfTableContentStyleReferenceKind.Row : DxfTableContentStyleReferenceKind.Column;
                        result.Add(new DxfTableContentStyleReference(kind, id, offset + i + 1));
                        i = end; continue;
                    }
                    if (frame == "TABLEFORMAT")
                    { i = QualifiedStyleReferenceFormatEnd(tags, i); continue; }
                    if (frame == "LINKEDTABLEDATACOLUMN")
                    {
                        if (stack.Count != 0 || linkedRows != 0 || linkedColumns != columnRefs)
                            throw new NotSupportedException("Misplaced linked column prevents style-reference qualification.");
                        linkedColumns++;
                    }
                    if (frame == "LINKEDTABLEDATAROW")
                    {
                        if (stack.Count != 0 || linkedColumns != columns || columnRefs != columns || linkedRows != rowRefs ||
                            i + 1 >= tags.Count || tags[i + 1].Code != 90 || (int)tags[i + 1].Value != columns)
                            throw new NotSupportedException("The linked row does not have a complete qualified cell inventory.");
                        linkedRows++; rowCells = 0; rowCellRefs = 0;
                    }
                    if (frame == "LINKEDTABLEDATACELL")
                    {
                        if (stack.Count != 1 || stack.Peek() != "LINKEDTABLEDATAROW" || rowCells != rowCellRefs)
                            throw new NotSupportedException("Misplaced linked cell prevents style-reference qualification.");
                        rowCells++;
                    }
                    if (stack.Count >= 64) throw new NotSupportedException("Style-reference frame nesting exceeds the storage limit.");
                    stack.Push(frame);
                }
                else if (tag.Code == 309)
                {
                    if (stack.Count == 0 || text != stack.Pop() + "_END")
                        throw new NotSupportedException("Mismatched style-reference frame nesting.");
                    if (text == "LINKEDTABLEDATAROW_END" && (rowCells != columns || rowCellRefs != columns))
                        throw new NotSupportedException("The row's complete cell style-ID inventory is unavailable.");
                }
            }
            if (stack.Count != 0 || linkedColumns != columns || columnRefs != columns || linkedRows != rows || rowRefs != rows || (long)cellRefs != (long)rows * columns)
                throw new NotSupportedException("TABLECONTENT style-reference counts are incomplete.");
            return result.AsReadOnly();
        }

        private static int ReadStyleReferenceFrame(IReadOnlyList<DxfTag> tags, int start, string frame)
        {
            bool cell = frame == "TABLECELL";
            if (start + 3 >= tags.Count || tags[start + 1].Code != 90 || tags[start + 2].Code != (cell ? 91 : 40))
                throw new NotSupportedException("Incomplete public style-ID frame.");
            int end = start + 3;
            if (cell && (int)tags[start + 2].Value != 0)
            {
                // Pinned producer shape: a linked geometry handle with zero inline geometry items.
                short[] codes = { 91, 40, 41, 330, 92 };
                if ((int)tags[start + 2].Value != 1 || start + 8 >= tags.Count)
                    throw new NotSupportedException("Unsupported cell geometry envelope around style ID.");
                for (int j = 0; j < codes.Length; j++)
                    if (tags[start + 3 + j].Code != codes[j]) throw new NotSupportedException("Unsupported cell geometry envelope around style ID.");
                if ((int)tags[start + 7].Value != 0) throw new NotSupportedException("Inline cell geometry must be qualified before style-ID remapping.");
                end = start + 8;
            }
            if (tags[end].Code != 309 || (string)tags[end].Value != frame + "_END")
                throw new NotSupportedException("Extended or repeated style-ID fields are not qualified.");
            return end;
        }

        private static int StyleReferenceValueEnd(IReadOnlyList<DxfTag> tags, int index)
        {
            if (index + 2 < tags.Count && tags[index + 1].Code == 93 && tags[index + 2].Code == 90)
            {
                for (int end = index + 3; end < tags.Count; end++)
                    if (tags[end].Code == 304 && (string)tags[end].Value == "ACVALUE_END") return end;
                throw new NotSupportedException("Unterminated AcValue in style-reference scan.");
            }
            if (DxfStoredTableContentValue.TryLegacyScalarEnd(tags, index, out int scalarEnd)) return scalarEnd;
            if (index + 2 < tags.Count && tags[index + 1].Code == 90)
            {
                int kind = (int)tags[index + 1].Value;
                if (kind == 0 && tags[index + 2].Code == 91) return index + 2;
                if (kind == 8 && tags[index + 2].Code == 92)
                {
                    int size = (int)tags[index + 2].Value, end = index + 2; long bytes = 0;
                    while (end + 1 < tags.Count && tags[end + 1].Code == 310) bytes += ((byte[])tags[++end].Value).LongLength;
                    if (size >= 0 && size == bytes) return end;
                }
            }
            throw new NotSupportedException("Unqualified legacy AcValue prevents complete style-reference scanning.");
        }

        internal DxfStoredTableContent PrepareStyleReferenceRemap(HashSet<int> oldIds, HashSet<int> newIds,
            IReadOnlyDictionary<int, int> mapping, out int changed)
        {
            if (this.editing) { this.reentered = true; throw new InvalidOperationException("Cannot remap a TABLECONTENT during another content transaction."); }
            this.ValidateReplacementSource();
            changed = 0;
            var tags = this.Payload.ToList();
            foreach (var reference in this.GetCellStyleReferences())
            {
                int id = reference.StoredId;
                if (id == 0) continue;
                if (!oldIds.Contains(id)) throw new InvalidOperationException("A consumer already refers to a missing cell-style identifier: " + id);
                int target = mapping.TryGetValue(id, out int selected) ? selected : id;
                if (target != 0 && !newIds.Contains(target)) throw new InvalidOperationException("Removing a referenced style requires an explicit surviving replacement: " + id);
                if (target == id) continue;
                tags[reference.PayloadIndex] = new DxfTag(90, target); changed++;
            }
            if (changed == 0) return null;
            var candidate = new DxfStoredTableContent(this.source, tags, DecodeStoredText);
            candidate.GetCellStyleReferences();
            if (candidate.StoredValues.Count != this.StoredValues.Count) throw new InvalidOperationException("Remapping changed scalar projection admission.");
            return candidate;
        }

        internal void PublishStyleReferenceRemap(DxfStoredTableContent candidate)
        {
            if (candidate == null) return;
            this.Payload = candidate.Payload; this.Subclasses = candidate.Subclasses; this.StoredValues = candidate.StoredValues;
        }
    }
}
