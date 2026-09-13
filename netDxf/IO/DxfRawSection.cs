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
using System.Collections;
using System.Collections.Generic;

namespace netDxf.IO
{
    /// <summary>An immutable index into a raw document's ordered section tags.</summary>
    public sealed class DxfRawSection
    {
        internal DxfRawSection(string name, IReadOnlyList<DxfTag> tags, int start, int content, int end)
        {
            this.Name = name;
            this.StartTagIndex = start;
            this.EndTagIndex = end;
            this.Content = new TagSlice(tags, content, end - content - 1);
        }

        /// <summary>Gets the section name exactly as decoded, including unknown application names.</summary>
        public string Name { get; }
        /// <summary>Gets the index of the section's 0/SECTION tag in DxfRawDocument.Tags.</summary>
        public int StartTagIndex { get; }
        /// <summary>Gets the exclusive end index, just after 0/ENDSEC.</summary>
        public int EndTagIndex { get; }
        /// <summary>Gets all body tags, excluding the section name and terminating ENDSEC.</summary>
        /// <remarks>Comments between SECTION and its name remain in the document's complete Tags sequence.</remarks>
        public IReadOnlyList<DxfTag> Content { get; }

        private sealed class TagSlice : IReadOnlyList<DxfTag>
        {
            private readonly IReadOnlyList<DxfTag> tags;
            private readonly int start;
            internal TagSlice(IReadOnlyList<DxfTag> tags, int start, int count)
            {
                this.tags = tags;
                this.start = start;
                this.Count = count;
            }
            public int Count { get; }
            public DxfTag this[int index]
            {
                get
                {
                    if (index < 0 || index >= this.Count) throw new ArgumentOutOfRangeException(nameof(index));
                    return this.tags[this.start + index];
                }
            }
            public IEnumerator<DxfTag> GetEnumerator()
            {
                for (int i = 0; i < this.Count; i++) yield return this.tags[this.start + i];
            }
            IEnumerator IEnumerable.GetEnumerator() { return this.GetEnumerator(); }
        }
    }
}
