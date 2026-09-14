// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.IO;
using netDxf.Tables;

namespace netDxf.IO
{
    internal sealed partial class DxfReader
    {
        private VPort ReadVPort()
        {
            if (this.chunk.Code != 100 || this.chunk.ReadString() != SubclassMarker.VPort)
                throw new InvalidDataException("VPORT requires the AcDbViewportTableRecord subclass.");
            VPort vport = new VPort("_reading");
            string name = null;
            var seen = new HashSet<short>();
            var xdata = new List<XData>();
            Vector2 lowerLeftCorner = vport.LowerLeftCorner;
            Vector2 upperRightCorner = vport.UpperRightCorner;
            Vector2 viewCenter = vport.ViewCenter;
            Vector2 snapBasePoint = vport.SnapBasePoint;
            Vector2 snapSpacing = vport.SnapSpacing;
            Vector2 gridSpacing = vport.GridSpacing;
            Vector3 viewDirection = vport.ViewDirection;
            Vector3 viewTarget = vport.ViewTarget;
            Vector3 ucsOrigin = vport.UcsOrigin;
            Vector3 ucsXAxis = vport.UcsXAxis;
            Vector3 ucsYAxis = vport.UcsYAxis;
            this.chunk.Next();
            while (this.chunk.Code != 0)
            {
                short code = this.chunk.Code;
                if (code == 1001)
                {
                    string appId = this.DecodeEncodedNonAsciiCharacters(this.chunk.ReadString());
                    xdata.Add(this.ReadXDataRecord(new ApplicationRegistry(appId)));
                    continue;
                }
                bool known = code == 2 || code == 10 || code == 20 || code == 11 || code == 21 || code == 12 || code == 22 ||
                    code == 13 || code == 23 || code == 14 || code == 24 || code == 15 || code == 25 || code == 16 ||
                    code == 26 || code == 36 || code == 17 || code == 27 || code == 37 || code == 110 || code == 120 ||
                    code == 130 || code == 111 || code == 121 || code == 131 || code == 112 || code == 122 || code == 132 ||
                    code == 40 || code == 41 || code == 42 || code == 43 || code == 44 || code == 50 || code == 51 ||
                    code == 70 || code == 71 || code == 72 || code == 73 || code == 74 || code == 75 || code == 76 ||
                    code == 77 || code == 78 || code == 281 || code == 65 || code == 79 || code == 146 || code == 345 || code == 346;
                if (known && !seen.Add(code)) throw new InvalidDataException("Duplicate VPORT group " + code + ".");
                try
                {
                    switch (code)
                    {
                        case 2: name = this.DecodeEncodedNonAsciiCharacters(this.chunk.ReadString()); break;
                        case 10: lowerLeftCorner.X = this.chunk.ReadDouble(); break;
                        case 20: lowerLeftCorner.Y = this.chunk.ReadDouble(); break;
                        case 11: upperRightCorner.X = this.chunk.ReadDouble(); break;
                        case 21: upperRightCorner.Y = this.chunk.ReadDouble(); break;
                        case 12: viewCenter.X = this.chunk.ReadDouble(); break;
                        case 22: viewCenter.Y = this.chunk.ReadDouble(); break;
                        case 13: snapBasePoint.X = this.chunk.ReadDouble(); break;
                        case 23: snapBasePoint.Y = this.chunk.ReadDouble(); break;
                        case 14: snapSpacing.X = this.chunk.ReadDouble(); break;
                        case 24: snapSpacing.Y = this.chunk.ReadDouble(); break;
                        case 15: gridSpacing.X = this.chunk.ReadDouble(); break;
                        case 25: gridSpacing.Y = this.chunk.ReadDouble(); break;
                        case 16: viewDirection.X = this.chunk.ReadDouble(); break;
                        case 26: viewDirection.Y = this.chunk.ReadDouble(); break;
                        case 36: viewDirection.Z = this.chunk.ReadDouble(); break;
                        case 17: viewTarget.X = this.chunk.ReadDouble(); break;
                        case 27: viewTarget.Y = this.chunk.ReadDouble(); break;
                        case 37: viewTarget.Z = this.chunk.ReadDouble(); break;
                        case 110: ucsOrigin.X = this.chunk.ReadDouble(); break;
                        case 120: ucsOrigin.Y = this.chunk.ReadDouble(); break;
                        case 130: ucsOrigin.Z = this.chunk.ReadDouble(); break;
                        case 111: ucsXAxis.X = this.chunk.ReadDouble(); break;
                        case 121: ucsXAxis.Y = this.chunk.ReadDouble(); break;
                        case 131: ucsXAxis.Z = this.chunk.ReadDouble(); break;
                        case 112: ucsYAxis.X = this.chunk.ReadDouble(); break;
                        case 122: ucsYAxis.Y = this.chunk.ReadDouble(); break;
                        case 132: ucsYAxis.Z = this.chunk.ReadDouble(); break;
                        case 40: vport.ViewHeight = this.chunk.ReadDouble(); break;
                        case 41: vport.ViewAspectRatio = this.chunk.ReadDouble(); break;
                        case 42: vport.LensLength = this.chunk.ReadDouble(); break;
                        case 43: vport.FrontClippingPlane = this.chunk.ReadDouble(); break;
                        case 44: vport.BackClippingPlane = this.chunk.ReadDouble(); break;
                        case 50: vport.SnapRotation = this.chunk.ReadDouble(); break;
                        case 51: vport.ViewTwist = this.chunk.ReadDouble(); break;
                        case 70: vport.Flags = (VPortFlags)this.chunk.ReadShort(); break;
                        case 71: vport.ViewMode = (ViewModeFlags)this.chunk.ReadShort(); break;
                        case 72: vport.CircleSides = this.chunk.ReadShort(); break;
                        case 73: vport.FastZoom = this.ReadVPortBoolean(); break;
                        case 74: vport.UcsIcon = this.chunk.ReadShort(); break;
                        case 75: vport.SnapMode = this.ReadVPortBoolean(); break;
                        case 76: vport.ShowGrid = this.ReadVPortBoolean(); break;
                        case 77: vport.SnapStyle = this.chunk.ReadShort(); break;
                        case 78: vport.SnapIsopair = this.chunk.ReadShort(); break;
                        case 281: vport.RenderMode = (ViewRenderMode)this.chunk.ReadShort(); break;
                        case 65: vport.UcsPerViewport = this.ReadVPortBoolean(); break;
                        case 79: vport.UcsOrthographicType = this.chunk.ReadShort(); break;
                        case 146: vport.UcsElevation = this.chunk.ReadDouble(); break;
                        case 345:
                        case 346: this.AddUcsReference(vport, code, this.chunk.ReadHex()); break;
                        default:
                            if (code >= 1000 && code <= 1071)
                                throw new InvalidDataException("VPORT XData must start with an application registry.");
                            break;
                    }
                }
                catch (ArgumentException error)
                {
                    throw new InvalidDataException("Invalid VPORT group " + code + ".", error);
                }
                this.chunk.Next();
            }
            if (string.IsNullOrWhiteSpace(name) || (!VPort.IsActiveName(name) && !TableObject.IsValidName(name)))
                throw new InvalidDataException("VPORT has an invalid or missing configuration name.");
            vport.SetName(name.Trim(), false);
            vport.IsReserved = VPort.IsActiveName(name);
            try
            {
                RequireVPortPoint(seen, 10, 2); vport.LowerLeftCorner = lowerLeftCorner;
                RequireVPortPoint(seen, 11, 2); vport.UpperRightCorner = upperRightCorner;
                RequireVPortPoint(seen, 12, 2); vport.ViewCenter = viewCenter;
                RequireVPortPoint(seen, 13, 2); vport.SnapBasePoint = snapBasePoint;
                RequireVPortPoint(seen, 14, 2); vport.SnapSpacing = snapSpacing;
                RequireVPortPoint(seen, 15, 2); vport.GridSpacing = gridSpacing;
                RequireVPortPoint(seen, 16, 3); vport.ViewDirection = viewDirection;
                RequireVPortPoint(seen, 17, 3); vport.ViewTarget = viewTarget;
                RequireVPortPoint(seen, 110, 3); vport.UcsOrigin = ucsOrigin;
                RequireVPortPoint(seen, 111, 3); vport.UcsXAxis = ucsXAxis;
                RequireVPortPoint(seen, 112, 3); vport.UcsYAxis = ucsYAxis;
            }
            catch (ArgumentException error) { throw new InvalidDataException("Invalid VPORT coordinate data.", error); }
            if (xdata.Count > 0) this.tableEntryXData.Add(vport, xdata);
            return vport;
        }

        private bool ReadVPortBoolean()
        {
            short value = this.chunk.ReadShort();
            if (value != 0 && value != 1) throw new InvalidDataException("VPORT Boolean field must be zero or one.");
            return value != 0;
        }

        private static void RequireVPortPoint(HashSet<short> seen, short first, int dimension)
        {
            int count = 0;
            for (short offset = 0; offset < dimension * 10; offset += 10)
                if (seen.Contains((short)(first + offset))) count++;
            if (count != 0 && count != dimension)
                throw new InvalidDataException("Incomplete VPORT point at group " + first + ".");
        }
    }

    internal sealed partial class DxfWriter
    {
        private void WriteVPort(VPort vp)
        {
            this.chunk.Write(0, vp.CodeName);
            this.chunk.Write(5, vp.Handle);
            this.WriteDatabaseMetadata(vp);
            this.chunk.Write(330, vp.Owner.Handle);
            this.chunk.Write(100, SubclassMarker.TableRecord);
            this.chunk.Write(100, SubclassMarker.VPort);
            this.chunk.Write(2, this.EncodeNonAsciiCharacters(vp.Name));
            this.chunk.Write(40, vp.ViewHeight);
            this.chunk.Write(41, vp.ViewAspectRatio);
            this.chunk.Write(42, vp.LensLength);
            this.chunk.Write(43, vp.FrontClippingPlane);
            this.chunk.Write(44, vp.BackClippingPlane);
            this.chunk.Write(50, vp.SnapRotation);
            this.chunk.Write(51, vp.ViewTwist);
            this.chunk.Write(10, vp.LowerLeftCorner.X);
            this.chunk.Write(20, vp.LowerLeftCorner.Y);
            this.chunk.Write(11, vp.UpperRightCorner.X);
            this.chunk.Write(21, vp.UpperRightCorner.Y);
            this.chunk.Write(12, vp.ViewCenter.X);
            this.chunk.Write(22, vp.ViewCenter.Y);
            this.chunk.Write(13, vp.SnapBasePoint.X);
            this.chunk.Write(23, vp.SnapBasePoint.Y);
            this.chunk.Write(14, vp.SnapSpacing.X);
            this.chunk.Write(24, vp.SnapSpacing.Y);
            this.chunk.Write(15, vp.GridSpacing.X);
            this.chunk.Write(25, vp.GridSpacing.Y);
            this.chunk.Write(16, vp.ViewDirection.X);
            this.chunk.Write(26, vp.ViewDirection.Y);
            this.chunk.Write(36, vp.ViewDirection.Z);
            this.chunk.Write(17, vp.ViewTarget.X);
            this.chunk.Write(27, vp.ViewTarget.Y);
            this.chunk.Write(37, vp.ViewTarget.Z);
            this.chunk.Write(110, vp.UcsOrigin.X);
            this.chunk.Write(120, vp.UcsOrigin.Y);
            this.chunk.Write(130, vp.UcsOrigin.Z);
            this.chunk.Write(111, vp.UcsXAxis.X);
            this.chunk.Write(121, vp.UcsXAxis.Y);
            this.chunk.Write(131, vp.UcsXAxis.Z);
            this.chunk.Write(112, vp.UcsYAxis.X);
            this.chunk.Write(122, vp.UcsYAxis.Y);
            this.chunk.Write(132, vp.UcsYAxis.Z);
            this.chunk.Write(70, (short)vp.Flags);
            this.chunk.Write(71, (short)vp.ViewMode);
            this.chunk.Write(72, vp.CircleSides);
            this.chunk.Write(73, vp.FastZoom ? (short)1 : (short)0);
            this.chunk.Write(74, vp.UcsIcon);
            this.chunk.Write(75, vp.SnapMode ? (short)1 : (short)0);
            this.chunk.Write(76, vp.ShowGrid ? (short)1 : (short)0);
            this.chunk.Write(77, vp.SnapStyle);
            this.chunk.Write(78, vp.SnapIsopair);
            this.chunk.Write(281, (short)vp.RenderMode);
            this.chunk.Write(65, vp.UcsPerViewport ? (short)1 : (short)0);
            this.chunk.Write(79, vp.UcsOrthographicType);
            this.chunk.Write(146, vp.UcsElevation);
            if (vp.NamedUcs != null) this.chunk.Write(345, vp.NamedUcs.Handle);
            if (vp.BaseUcs != null) this.chunk.Write(346, vp.BaseUcs.Handle);
            this.WriteXData(vp.XData);
        }
    }
}
