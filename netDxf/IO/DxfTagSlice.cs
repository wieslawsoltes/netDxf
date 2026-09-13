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
    // All slices refer to the immutable document snapshot, never a caller-owned list.
    internal sealed class DxfTagSlice : IReadOnlyList<DxfTag>
    {
        private readonly IReadOnlyList<DxfTag> tags;
        private readonly int start;

        internal DxfTagSlice(IReadOnlyList<DxfTag> tags, int start, int count)
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
