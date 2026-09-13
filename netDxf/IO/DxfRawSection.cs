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

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace netDxf.IO
{
    /// <summary>An immutable index into a raw document's ordered section tags.</summary>
    public sealed class DxfRawSection
    {
        private readonly Lazy<IReadOnlyList<DxfRawRecord>> records;

        internal DxfRawSection(string name, IReadOnlyList<DxfTag> tags, int start, int content, int end)
        {
            this.Name = name;
            this.StartTagIndex = start;
            this.ContentStartTagIndex = content;
            this.EndTagIndex = end;
            this.Content = new DxfTagSlice(tags, content, end - content - 1);

            short marker = string.Equals(name, "HEADER", StringComparison.OrdinalIgnoreCase) ? (short)9 : (short)0;
            int first = content;
            while (first < end - 1 && tags[first].Code != marker) first++;
            this.Preamble = new DxfTagSlice(tags, content, first - content);
            // Byte-preserving load/save need not allocate an object for every record.
            // Lazy's default publication mode also keeps repeated/concurrent index access stable.
            this.records = new Lazy<IReadOnlyList<DxfRawRecord>>(() => IndexRecords(name, tags, first, end - 1, marker));
        }

        private static IReadOnlyList<DxfRawRecord> IndexRecords(string name, IReadOnlyList<DxfTag> tags, int first, int end, short marker)
        {
            List<DxfRawRecord> result = new List<DxfRawRecord>();
            if (first < end)
            {
                int previous = first;
                for (int i = first + 1; i < end; i++)
                {
                    if (tags[i].Code != marker) continue;
                    result.Add(new DxfRawRecord(name, tags, previous, i));
                    previous = i;
                }
                result.Add(new DxfRawRecord(name, tags, previous, end));
            }
            return new ReadOnlyCollection<DxfRawRecord>(result);
        }

        /// <summary>Gets the section name exactly as decoded, including unknown application names.</summary>
        public string Name { get; }
        /// <summary>Gets the index of the section's 0/SECTION tag in DxfRawDocument.Tags.</summary>
        public int StartTagIndex { get; }
        /// <summary>Gets the absolute index just after the group-2 section name.</summary>
        public int ContentStartTagIndex { get; }
        /// <summary>Gets the exclusive end index, just after 0/ENDSEC.</summary>
        public int EndTagIndex { get; }
        /// <summary>Gets all body tags, excluding the section name and terminating ENDSEC.</summary>
        /// <remarks>Comments between SECTION and its name remain in the document's complete Tags sequence.</remarks>
        public IReadOnlyList<DxfTag> Content { get; }
        /// <summary>Gets body tags before the first record marker, including comments.</summary>
        /// <remarks>For a marker-free section such as THUMBNAILIMAGE, this contains its complete body.</remarks>
        public IReadOnlyList<DxfTag> Preamble { get; }
        /// <summary>Gets ordered lexical records, including repeated names and unknown record types.</summary>
        /// <remarks>HEADER uses group-9 boundaries; other sections use group 0. No entity or object schema is inferred.</remarks>
        public IReadOnlyList<DxfRawRecord> Records { get { return this.records.Value; } }
    }
}
