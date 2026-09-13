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
using System.Collections.ObjectModel;

namespace netDxf.Collections
{
    /// <summary>Ordered CLASS definitions with unique, immutable DXF and C++ names.</summary>
    public sealed class DxfClassCollection : KeyedCollection<string, DxfClass>
    {
        /// <summary>Creates an empty collection using ordinal, case-sensitive identity.</summary>
        public DxfClassCollection() : base(StringComparer.Ordinal) { }

        /// <inheritdoc />
        protected override string GetKeyForItem(DxfClass item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            return item.Name;
        }
        /// <inheritdoc />
        protected override void InsertItem(int index, DxfClass item)
        {
            this.ValidateCppIdentity(item, -1);
            base.InsertItem(index, item);
        }
        /// <inheritdoc />
        protected override void SetItem(int index, DxfClass item)
        {
            this.ValidateCppIdentity(item, index);
            base.SetItem(index, item);
        }
        private void ValidateCppIdentity(DxfClass item, int replacedIndex)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            for (int i = 0; i < this.Count; i++)
                if (i != replacedIndex && string.Equals(this[i].CppClassName, item.CppClassName, StringComparison.Ordinal))
                    throw new ArgumentException("A CLASS C++ name must be unique within the collection.", nameof(item));
        }
    }
}
