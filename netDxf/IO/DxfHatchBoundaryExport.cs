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

using System.IO;
using netDxf.Blocks;
using netDxf.Entities;

namespace netDxf.IO
{
    internal partial class DxfWriter
    {
        private void ValidateHatchBoundaryPresence()
        {
            // All registered blocks are serialized, including unused definitions.
            foreach (Block block in this.doc.Blocks)
                foreach (EntityObject entity in block.Entities)
                {
                    Hatch hatch = entity as Hatch;
                    if (hatch != null) RequireHatchBoundary(hatch);
                }
        }

        private static void RequireHatchBoundary(Hatch hatch)
        {
            if (hatch.BoundaryPaths.Count == 0)
                throw new InvalidDataException("HATCH has no boundary paths. Typed export requires at least one boundary; " +
                    "add a boundary, explicitly remove the entity, or use DxfRawDocument for original-data preservation.");
        }
    }
}
