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
using System.IO;
using netDxf.Blocks;
using netDxf.Collections;
using netDxf.Entities;
using netDxf.Header;

namespace netDxf.IO
{
    internal sealed partial class DxfReader
    {
        private Helix ReadHelix()
        {
            if (this.chunk.Code != 100 || this.chunk.ReadString() != SubclassMarker.Spline)
                throw new InvalidDataException("HELIX requires its AcDbSpline subclass before AcDbHelix.");
            Spline spline = this.ReadSpline(true);
            if (this.chunk.Code != 100 || this.chunk.ReadString() != SubclassMarker.Helix)
                throw new InvalidDataException("HELIX is missing its AcDbHelix subclass.");
            Helix helix = new Helix(spline);
            var seen = new HashSet<short>();
            Vector3 axisBase = helix.AxisBasePoint, start = helix.StartPoint, axis = helix.AxisVector;
            this.chunk.Next();
            while (this.chunk.Code != 0)
            {
                short code = this.chunk.Code;
                if (code == 1001)
                {
                    string appId = this.DecodeEncodedNonAsciiCharacters(this.chunk.ReadString());
                    helix.XData.Add(this.ReadXDataRecord(this.GetApplicationRegistry(appId)));
                    continue;
                }
                if (code == 100 && this.chunk.ReadString() == SubclassMarker.Helix)
                    throw new InvalidDataException("HELIX has a duplicate AcDbHelix subclass.");
                if (code == 90 || code == 91 || code == 10 || code == 20 || code == 30 ||
                    code == 11 || code == 21 || code == 31 || code == 12 || code == 22 || code == 32 ||
                    code == 40 || code == 41 || code == 42 || code == 290 || code == 280)
                    if (!seen.Add(code)) throw new InvalidDataException("HELIX has duplicate group " + code + ".");
                try
                {
                    switch (code)
                    {
                        case 90: helix.MajorReleaseNumber = this.chunk.ReadInt(); break;
                        case 91: helix.MaintenanceReleaseNumber = this.chunk.ReadInt(); break;
                        case 10: axisBase.X = this.chunk.ReadDouble(); break;
                        case 20: axisBase.Y = this.chunk.ReadDouble(); break;
                        case 30: axisBase.Z = this.chunk.ReadDouble(); break;
                        case 11: start.X = this.chunk.ReadDouble(); break;
                        case 21: start.Y = this.chunk.ReadDouble(); break;
                        case 31: start.Z = this.chunk.ReadDouble(); break;
                        case 12: axis.X = this.chunk.ReadDouble(); break;
                        case 22: axis.Y = this.chunk.ReadDouble(); break;
                        case 32: axis.Z = this.chunk.ReadDouble(); break;
                        case 40: helix.Radius = this.chunk.ReadDouble(); break;
                        case 41: helix.Turns = this.chunk.ReadDouble(); break;
                        case 42: helix.TurnHeight = this.chunk.ReadDouble(); break;
                        case 290: helix.IsRightHanded = this.chunk.ReadBool(); break;
                        case 280: helix.Constraint = (HelixConstraint) this.chunk.ReadShort(); break;
                        default:
                            if (code >= 1000 && code <= 1071)
                                throw new InvalidDataException("HELIX extended data must begin with an application registry.");
                            break;
                    }
                }
                catch (ArgumentException error)
                {
                    throw new InvalidDataException("Invalid HELIX group " + code + " at position " + this.chunk.CurrentPosition + ".", error);
                }
                this.chunk.Next();
            }
            for (short component = 10; component <= 12; ++component)
            {
                int count = (seen.Contains(component) ? 1 : 0) + (seen.Contains((short)(component + 10)) ? 1 : 0) +
                    (seen.Contains((short)(component + 20)) ? 1 : 0);
                if (count != 0 && count != 3) throw new InvalidDataException("Incomplete HELIX vector at group " + component + ".");
            }
            try { helix.AxisBasePoint = axisBase; helix.StartPoint = start; helix.AxisVector = axis; }
            catch (ArgumentException error) { throw new InvalidDataException("Invalid HELIX position or axis vector.", error); }
            return helix;
        }
    }

    internal sealed partial class DxfWriter
    {
        private void ValidateHelixVersions()
        {
            if (this.doc.DrawingVariables.AcadVer >= DxfVersion.AutoCad2007) return;
            foreach (Block block in this.doc.Blocks)
                foreach (EntityObject entity in block.Entities)
                    if (entity is Helix)
                        throw new NotSupportedException("HELIX requires AutoCAD 2007 (AC1021) or later in this writer profile. Convert explicitly with ToSpline for older profiles.");
        }

        private void PrepareHelixClass(DxfClassCollection definitions)
        {
            int count = 0;
            foreach (Block block in this.doc.Blocks)
                foreach (EntityObject entity in block.Entities)
                    if (entity is Helix) count = checked(count + 1);
            if (definitions.Contains(DxfObjectCode.Helix))
            {
                DxfClass definition = definitions[DxfObjectCode.Helix];
                if (count != 0 && (definition.CppClassName != SubclassMarker.Helix || !definition.IsEntity))
                    throw new InvalidDataException("CLASS conflicts with the generated HELIX entity definition.");
                if (definition.CppClassName == SubclassMarker.Helix && definition.IsEntity) definition.InstanceCount = count;
            }
            else if (count != 0)
                definitions.Add(new DxfClass(DxfObjectCode.Helix, SubclassMarker.Helix, "ObjectDBX Classes")
                { ProxyFlags = 4095, IsEntity = true, InstanceCount = count });
        }

        private void WriteHelix(Helix helix)
        {
            this.WriteSpline(helix, false);
            this.chunk.Write(210, helix.Normal.X); this.chunk.Write(220, helix.Normal.Y); this.chunk.Write(230, helix.Normal.Z);
            this.chunk.Write(100, SubclassMarker.Helix);
            this.chunk.Write(90, helix.MajorReleaseNumber); this.chunk.Write(91, helix.MaintenanceReleaseNumber);
            this.chunk.Write(10, helix.AxisBasePoint.X); this.chunk.Write(20, helix.AxisBasePoint.Y); this.chunk.Write(30, helix.AxisBasePoint.Z);
            this.chunk.Write(11, helix.StartPoint.X); this.chunk.Write(21, helix.StartPoint.Y); this.chunk.Write(31, helix.StartPoint.Z);
            this.chunk.Write(12, helix.AxisVector.X); this.chunk.Write(22, helix.AxisVector.Y); this.chunk.Write(32, helix.AxisVector.Z);
            this.chunk.Write(40, helix.Radius); this.chunk.Write(41, helix.Turns); this.chunk.Write(42, helix.TurnHeight);
            this.chunk.Write(290, helix.IsRightHanded); this.chunk.Write(280, (short) helix.Constraint);
            this.WriteXData(helix.XData);
        }
    }
}
