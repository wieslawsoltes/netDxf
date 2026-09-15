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
using System.Diagnostics;
using netDxf.Header;
using netDxf.Tables;

namespace netDxf.IO
{
    internal sealed partial class DxfWriter
    {
        private void WriteView(View view)
        {
            Debug.Assert(this.activeTable == DxfObjectCode.ViewTable);
            if (view.IsCameraPlottable && this.doc.DrawingVariables.AcadVer < DxfVersion.AutoCad2007)
            {
                throw new NotSupportedException("A plottable named-view camera requires AutoCAD 2007 DXF or later.");
            }
            this.chunk.Write(0, view.CodeName);
            this.chunk.Write(5, view.Handle);
            this.WriteDatabaseMetadata(view);
            this.chunk.Write(330, view.Owner.Handle);
            this.chunk.Write(100, SubclassMarker.TableRecord);
            this.chunk.Write(100, SubclassMarker.View);
            this.chunk.Write(2, this.EncodeNonAsciiCharacters(view.Name));
            this.chunk.Write(70, (short)view.Flags);
            this.chunk.Write(40, view.Height);
            this.chunk.Write(10, view.ViewCenter.X);
            this.chunk.Write(20, view.ViewCenter.Y);
            this.chunk.Write(41, view.Width);
            this.chunk.Write(11, view.ViewDirection.X);
            this.chunk.Write(21, view.ViewDirection.Y);
            this.chunk.Write(31, view.ViewDirection.Z);
            this.chunk.Write(12, view.Target.X);
            this.chunk.Write(22, view.Target.Y);
            this.chunk.Write(32, view.Target.Z);
            this.chunk.Write(42, view.LensLength);
            this.chunk.Write(43, view.FrontClippingPlane);
            this.chunk.Write(44, view.BackClippingPlane);
            this.chunk.Write(50, view.Rotation);
            this.chunk.Write(71, (short)view.ViewMode);
            this.chunk.Write(281, (short)view.RenderMode);
            this.WriteViewUcs(view.Ucs);
            if (this.doc.DrawingVariables.AcadVer >= DxfVersion.AutoCad2007)
            {
                this.chunk.Write(73, view.IsCameraPlottable ? (short)1 : (short)0);
            }
            this.WriteSunReference(view);
            this.WriteXData(view.XData);
        }
    }
}
