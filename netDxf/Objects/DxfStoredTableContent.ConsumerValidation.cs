// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using netDxf.IO;

namespace netDxf.Objects
{
    public sealed partial class DxfStoredTableContent
    {
        // A source may retain unknown formatted-data fields without projecting them.
        // Coordinated edits require positive evidence that no other ID slots are hidden
        // in this subclass, not merely absence of the three familiar frame markers.
        private void ValidateStyleReferenceFormatting()
        {
            var tags = this.Subclasses[2].Tags;
            if (tags.Count < 7 || tags[1].Code != 300 || (string)tags[1].Value != "TABLEFORMAT")
                throw new NotSupportedException("The formatted-data subclass is not qualified for consumer remapping.");
            int end = QualifiedStyleReferenceFormatEnd(tags, 2);
            int index = end + 1;
            if (index >= tags.Count || tags[index].Code != 90)
                throw new NotSupportedException("The formatted-data merge inventory is unavailable.");
            int count = (int)tags[index++].Value;
            if (count < 0 || (long)count * 4 != tags.Count - index)
                throw new NotSupportedException("The formatted-data merge inventory has unknown or uncounted fields.");
            for (int i = 0; i < count; i++, index += 4)
            {
                for (short j = 0; j < 4; j++)
                    if (tags[index + j].Code != 91 + j)
                        throw new NotSupportedException("The formatted-data merge rectangle is not qualified.");
                int top = (int)tags[index].Value, left = (int)tags[index + 1].Value;
                int bottom = (int)tags[index + 2].Value, right = (int)tags[index + 3].Value;
                if (top < 0 || left < 0 || top > bottom || left > right ||
                    bottom >= this.RowCount.Value || right >= this.ColumnCount.Value)
                    throw new NotSupportedException("A merge rectangle is outside the declared table dimensions.");
            }
        }

        private static int QualifiedStyleReferenceFormatEnd(IReadOnlyList<DxfTag> tags, int start)
        {
            if (start >= tags.Count || tags[start].Code != 1 || (string)tags[start].Value != "TABLEFORMAT_BEGIN")
                throw new NotSupportedException("The public format frame is missing.");
            int end = start + 1;
            while (end < tags.Count && !(tags[end].Code == 309 && (string)tags[end].Value == "TABLEFORMAT_END")) end++;
            if (end == tags.Count || DxfCellStyleFormat.TryRead(tags.Skip(start).Take(end - start + 1).ToList(), DecodeStoredText) == null)
                throw new NotSupportedException("An extended format packet prevents complete consumer qualification.");
            return end;
        }
    }
}
