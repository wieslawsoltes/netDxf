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
using System.Diagnostics;
using netDxf.Tables;

namespace netDxf.IO
{
    internal sealed partial class DxfReader
    {
        private View ReadView()
        {
            Debug.Assert(this.chunk.ReadString() == SubclassMarker.View);
            string name = string.Empty;
            Vector2 center = Vector2.Zero;
            Vector3 direction = Vector3.UnitZ;
            Vector3 target = Vector3.Zero;
            double height = 1.0, width = 1.0, lens = 40.0, front = 0.0, back = 0.0, rotation = 0.0;
            ViewFlags flags = ViewFlags.None;
            ViewModeFlags mode = ViewModeFlags.Off;
            ViewRenderMode renderMode = ViewRenderMode.TwoDimensionalOptimized;
            bool cameraPlottable = false;
            var associatedUcs = new ViewUcsInput();
            List<XData> xData = new List<XData>();
            this.chunk.Next();
            while (this.chunk.Code != 0)
            {
                if (this.TryReadViewUcs(associatedUcs)) continue;
                switch (this.chunk.Code)
                {
                    case 2: name = this.DecodeEncodedNonAsciiCharacters(this.chunk.ReadString()); break;
                    case 70: flags = (ViewFlags)this.chunk.ReadShort(); break;
                    case 10: center.X = this.chunk.ReadDouble(); break;
                    case 20: center.Y = this.chunk.ReadDouble(); break;
                    case 11: direction.X = this.chunk.ReadDouble(); break;
                    case 21: direction.Y = this.chunk.ReadDouble(); break;
                    case 31: direction.Z = this.chunk.ReadDouble(); break;
                    case 12: target.X = this.chunk.ReadDouble(); break;
                    case 22: target.Y = this.chunk.ReadDouble(); break;
                    case 32: target.Z = this.chunk.ReadDouble(); break;
                    case 40: height = this.chunk.ReadDouble(); break;
                    case 41: width = this.chunk.ReadDouble(); break;
                    case 42: lens = this.chunk.ReadDouble(); break;
                    case 43: front = this.chunk.ReadDouble(); break;
                    case 44: back = this.chunk.ReadDouble(); break;
                    case 50: rotation = this.chunk.ReadDouble(); break;
                    case 71: mode = (ViewModeFlags)this.chunk.ReadShort(); break;
                    case 281: renderMode = (ViewRenderMode)this.chunk.ReadShort(); break;
                    case 73:
                        short cameraFlag = this.chunk.ReadShort();
                        if (cameraFlag != 0 && cameraFlag != 1)
                        {
                            throw new FormatException("The VIEW camera-plottable flag must be zero or one.");
                        }
                        cameraPlottable = cameraFlag == 1;
                        break;
                    case 1001:
                        string appId = this.DecodeEncodedNonAsciiCharacters(this.chunk.ReadString());
                        xData.Add(this.ReadXDataRecord(new ApplicationRegistry(appId)));
                        continue; // ReadXDataRecord already advanced to the following record.
                    default:
                        Debug.Assert(!(this.chunk.Code >= 1000 && this.chunk.Code <= 1071),
                            "Extended data must start with an application registry code.");
                        break;
                }
                this.chunk.Next();
            }
            if (!TableObject.IsValidName(name))
            {
                throw new FormatException("The VIEW table record has an invalid or missing name.");
            }
            View view = new View(name, false)
            {
                ViewCenter = center,
                ViewDirection = direction,
                Target = target,
                Height = height,
                Width = width,
                LensLength = lens,
                FrontClippingPlane = front,
                BackClippingPlane = back,
                Rotation = rotation,
                Flags = flags,
                ViewMode = mode,
                RenderMode = renderMode,
                IsCameraPlottable = cameraPlottable
            };
            this.CompleteViewUcs(view, associatedUcs);
            if (xData.Count > 0) this.tableEntryXData.Add(view, xData);
            return view;
        }
    }
}
