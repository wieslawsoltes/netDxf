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

namespace netDxf.Tables
{
    /// <summary>Flags stored in group 70 of a VIEW symbol table record.</summary>
    [Flags]
    public enum ViewFlags : short
    {
        /// <summary>No flags set; a model-space view.</summary>
        None = 0,
        /// <summary>The view belongs to paper space.</summary>
        PaperSpace = 1,
        /// <summary>The table entry depends on an external reference.</summary>
        ExternallyDependent = 16,
        /// <summary>The external reference has been successfully resolved.</summary>
        ExternalReferenceResolved = 32,
        /// <summary>The entry was referenced when the drawing was last edited.</summary>
        Referenced = 64
    }
}
