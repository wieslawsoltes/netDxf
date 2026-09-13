#region netDxf library licensed under the MIT License
// 
//                       netDxf library
// Copyright (c) Daniel Carvajal (haplokuon@gmail.com)
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.
// 
#endregion

using System.Collections.Generic;

namespace netDxf.IO
{
    /// <summary>An immutable index into one raw document's ordered record tags.</summary>
    /// <remarks>
    /// A record begins with group 9 in HEADER, or group 0 in any other section. This is
    /// lexical indexing, not a record schema: TABLE/ENDTAB, BLOCK/ENDBLK and VERTEX/SEQEND
    /// remain separate records. Comments after a marker belong to that record's tag range;
    /// their placement does not imply database ownership. Duplicate names are retained.
    /// </remarks>
    public sealed class DxfRawRecord
    {
        internal DxfRawRecord(string sectionName, IReadOnlyList<DxfTag> tags, int start, int end)
        {
            this.SourceTags = tags;
            this.SectionName = sectionName;
            this.StartTagIndex = start;
            this.EndTagIndex = end;
            this.MarkerCode = tags[start].Code;
            this.Name = (string) tags[start].RawValue;
            this.Tags = new DxfTagSlice(tags, start, end - start);
            this.Content = new DxfTagSlice(tags, start + 1, end - start - 1);
        }

        internal IReadOnlyList<DxfTag> SourceTags { get; }
        /// <summary>Gets the containing section's name, retaining its spelling.</summary>
        public string SectionName { get; }
        /// <summary>Gets the record type or HEADER variable name exactly as decoded.</summary>
        public string Name { get; }
        /// <summary>Gets 9 for a HEADER variable, or 0 for another section's record.</summary>
        public short MarkerCode { get; }
        /// <summary>Gets the absolute index of the opening tag in DxfRawDocument.Tags.</summary>
        public int StartTagIndex { get; }
        /// <summary>Gets the exclusive absolute end of the record, before the next record or ENDSEC.</summary>
        public int EndTagIndex { get; }
        /// <summary>Gets the opening marker and all following tags within this record's range.</summary>
        public IReadOnlyList<DxfTag> Tags { get; }
        /// <summary>Gets the record's tags excluding its opening marker.</summary>
        public IReadOnlyList<DxfTag> Content { get; }
    }
}
