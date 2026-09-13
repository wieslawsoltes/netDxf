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
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;

namespace netDxf.IO
{
    internal partial class DxfWriter
    {
        private void ValidateMeshVersions()
        {
            if (this.doc.DrawingVariables.AcadVer >= DxfVersion.AutoCad2010) return;

            // All model/paper spaces and registered block definitions are exported. Checking
            // only the active layout would miss nested and unreferenced block contents.
            // PolygonMesh and PolyfaceMesh use POLYLINE, not the 2010 MESH record.
            foreach (Block block in this.doc.Blocks)
                foreach (EntityObject entity in block.Entities)
                    if (entity is Mesh)
                        throw new NotSupportedException(
                            "MESH entities require AutoCAD 2010 (AC1024) or later DXF output. " +
                            "Choose that version, remove the MESH, or explicitly convert its geometry " +
                            "to a legacy representation before saving. No automatic lossy conversion is performed.");
        }
    }
}
