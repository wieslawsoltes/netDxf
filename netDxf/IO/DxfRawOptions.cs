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

namespace netDxf.IO
{
    /// <summary>Immutable resource budgets for raw DXF loading and normalized output.</summary>
    /// <remarks>These bound encoded bytes, tag count and string length, not the process's total heap.</remarks>
    public sealed class DxfRawOptions
    {
        /// <summary>Creates limits for a raw document operation.</summary>
        /// <param name="maximumBytes">Maximum input or normalized output bytes.</param>
        /// <param name="maximumTags">Maximum number of tags, including structure and comments.</param>
        /// <param name="maximumStringLength">Maximum UTF-16 code units in a decoded tag string.</param>
        public DxfRawOptions(int maximumBytes = 64 * 1024 * 1024, int maximumTags = 1000000,
            int maximumStringLength = 1024 * 1024)
        {
            if (maximumBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
            if (maximumTags <= 0) throw new ArgumentOutOfRangeException(nameof(maximumTags));
            if (maximumStringLength <= 0) throw new ArgumentOutOfRangeException(nameof(maximumStringLength));
            this.MaximumBytes = maximumBytes;
            this.MaximumTags = maximumTags;
            this.MaximumStringLength = maximumStringLength;
        }

        /// <summary>Gets the encoded byte budget.</summary>
        public int MaximumBytes { get; }
        /// <summary>Gets the tag-count budget.</summary>
        public int MaximumTags { get; }
        /// <summary>Gets the decoded string-length budget.</summary>
        public int MaximumStringLength { get; }
    }
}
