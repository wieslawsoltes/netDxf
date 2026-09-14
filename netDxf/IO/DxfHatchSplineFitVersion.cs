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
        private void ValidateHatchSplineFitVersions()
        {
            if (this.doc.DrawingVariables.AcadVer >= DxfVersion.AutoCad2010) return;
            // Every registered block is exported, including paper space, nested
            // and unreferenced definitions. Reject before any destination writes
            // or document preprocessing; never silently drop the new metadata.
            foreach (Block block in this.doc.Blocks)
                foreach (EntityObject entity in block.Entities)
                {
                    Hatch hatch = entity as Hatch;
                    if (hatch == null) continue;
                    foreach (HatchBoundaryPath path in hatch.BoundaryPaths)
                        foreach (HatchBoundaryPath.Edge edge in path.Edges)
                        {
                            HatchBoundaryPath.Spline spline = edge as HatchBoundaryPath.Spline;
                            if (spline != null && (spline.FitPoints.Count != 0 ||
                                spline.StartTangent.HasValue || spline.EndTangent.HasValue))
                                throw new NotSupportedException(
                                    "HATCH spline fit points and tangents require AutoCAD 2010 (AC1024) or later " +
                                    "in this writer profile. Choose that version or explicitly clear the fit " +
                                    "metadata before saving. Control points and knots remain independent.");
                        }
                }
        }
    }
}
