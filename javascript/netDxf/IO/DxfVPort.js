// Copyright (c) Daniel Carvajal and netDxf contributors. MIT License; see package LICENSE.
import { VPort } from '../Tables/VPort.js';
import { TableObject } from '../Tables/TableObject.js';
import { SubclassMarker } from '../SubclassMarker.js';
import { DecodeDxfText, EncodeDxfText } from '../../runtime/DxfStringEncoding.js';
import { NativeString } from '../../runtime/GeometryRuntime.js';
import { TrimDotNet } from '../../runtime/InvariantFloat.js';
import { ReferenceList } from '../../runtime/ReferenceList.js';
import { ReadXDataRecord, WriteXData, WrappedInvalidData } from '../../runtime/DxfXDataIO.js';
import { SunOwnerContext, AddSunReference } from './DxfReader.Sun.js';
import { WriteSunReference } from './DxfWriter.Sun.js';
import { WriteDatabaseMetadata } from './DxfWriter.Objects.js';
import { AddUcsReference } from './DxfViewUcs.js';
import { ArgumentException, InvalidDataException } from '../../runtime/Errors.js';
const vectorFields = Object.freeze({"10": ["LowerLeftCorner", "X"], "20": ["LowerLeftCorner", "Y"], "11": ["UpperRightCorner", "X"], "21": ["UpperRightCorner", "Y"], "12": ["ViewCenter", "X"], "22": ["ViewCenter", "Y"], "13": ["SnapBasePoint", "X"], "23": ["SnapBasePoint", "Y"], "14": ["SnapSpacing", "X"], "24": ["SnapSpacing", "Y"], "15": ["GridSpacing", "X"], "25": ["GridSpacing", "Y"], "16": ["ViewDirection", "X"], "26": ["ViewDirection", "Y"], "36": ["ViewDirection", "Z"], "17": ["ViewTarget", "X"], "27": ["ViewTarget", "Y"], "37": ["ViewTarget", "Z"], "110": ["UcsOrigin", "X"], "120": ["UcsOrigin", "Y"], "130": ["UcsOrigin", "Z"], "111": ["UcsXAxis", "X"], "121": ["UcsXAxis", "Y"], "131": ["UcsXAxis", "Z"], "112": ["UcsYAxis", "X"], "122": ["UcsYAxis", "Y"], "132": ["UcsYAxis", "Z"]});
const scalarFields = Object.freeze({"40": ["ViewHeight", "Double"], "41": ["ViewAspectRatio", "Double"], "42": ["LensLength", "Double"], "43": ["FrontClippingPlane", "Double"], "44": ["BackClippingPlane", "Double"], "50": ["SnapRotation", "Double"], "51": ["ViewTwist", "Double"], "70": ["Flags", "Short"], "71": ["ViewMode", "Short"], "72": ["CircleSides", "Short"], "73": ["FastZoom", "Boolean"], "74": ["UcsIcon", "Short"], "75": ["SnapMode", "Boolean"], "76": ["ShowGrid", "Boolean"], "77": ["SnapStyle", "Short"], "78": ["SnapIsopair", "Short"], "281": ["RenderMode", "Short"], "65": ["UcsPerViewport", "Boolean"], "79": ["UcsOrthographicType", "Short"], "146": ["UcsElevation", "Double"]});
export function ReadVPortBoolean(chunk) {
  const value = chunk.ReadShort();
  if (value !== 0 && value !== 1) throw new InvalidDataException('VPORT Boolean field must be zero or one.');
  return value !== 0;
}
export function RequireVPortPoint(seen, first, dimension) {
  let count = 0; for (let offset = 0; offset < dimension * 10; offset += 10) if (seen.has(first + offset)) count++;
  if (count !== 0 && count !== dimension) throw new InvalidDataException('Incomplete VPORT point at group ' + first + '.');
}
export function ReadVPort(chunk, context) {
  if (chunk.Code !== 100 || chunk.ReadString() !== SubclassMarker.VPort)
    throw new InvalidDataException('VPORT requires the AcDbViewportTableRecord subclass.');
  const vport = new VPort('_reading'), sun = new SunOwnerContext(SubclassMarker.VPort), seen = new Set(), xdata = new ReferenceList();
  let name = null;
  const vectors = {};
  vectors.LowerLeftCorner = vport.LowerLeftCorner;
  vectors.UpperRightCorner = vport.UpperRightCorner;
  vectors.ViewCenter = vport.ViewCenter;
  vectors.SnapBasePoint = vport.SnapBasePoint;
  vectors.SnapSpacing = vport.SnapSpacing;
  vectors.GridSpacing = vport.GridSpacing;
  vectors.ViewDirection = vport.ViewDirection;
  vectors.ViewTarget = vport.ViewTarget;
  vectors.UcsOrigin = vport.UcsOrigin;
  vectors.UcsXAxis = vport.UcsXAxis;
  vectors.UcsYAxis = vport.UcsYAxis;
  chunk.Next();
  while (chunk.Code !== 0) {
    const code = chunk.Code; sun.Observe(code, chunk.Value);
    if (code === 1001) { xdata.Add(ReadXDataRecord(chunk, null, true)); continue; }
    const point = Object.hasOwn(vectorFields, code) ? vectorFields[code] : null;
    const scalar = Object.hasOwn(scalarFields, code) ? scalarFields[code] : null;
    if (point !== null || scalar !== null || code === 2 || code === 345 || code === 346) {
      if (seen.has(code)) throw new InvalidDataException('Duplicate VPORT group ' + code + '.'); seen.add(code);
    }
    try {
      if (point !== null) vectors[point[0]][point[1]] = chunk.ReadDouble();
      else if (scalar !== null) vport[scalar[0]] = scalar[1] === 'Boolean' ? ReadVPortBoolean(chunk) : chunk['Read' + scalar[1]]();
      else switch (code) {
        case 361: if (sun.IsPublic) AddSunReference(context, vport, chunk.ReadHex()); break;
        case 2: name = DecodeDxfText(chunk.ReadString()); break;
        case 345: case 346: AddUcsReference(context, vport, code, chunk.ReadHex()); break;
        default:
          if (code >= 1000 && code <= 1071) throw new InvalidDataException('VPORT XData must start with an application registry.');
      }
    } catch (error) { if (error instanceof ArgumentException) throw WrappedInvalidData('Invalid VPORT group ' + code + '.', error); throw error; }
    chunk.Next();
  }
  if (NativeString.IsNullOrWhiteSpace(name) || (!VPort.IsActiveName(name) && !TableObject.IsValidName(name)))
    throw new InvalidDataException('VPORT has an invalid or missing configuration name.');
  vport.SetName(TrimDotNet(name), false); vport.IsReserved = VPort.IsActiveName(name);
  try {
    RequireVPortPoint(seen, 10, 2); vport.LowerLeftCorner = vectors.LowerLeftCorner;
    RequireVPortPoint(seen, 11, 2); vport.UpperRightCorner = vectors.UpperRightCorner;
    RequireVPortPoint(seen, 12, 2); vport.ViewCenter = vectors.ViewCenter;
    RequireVPortPoint(seen, 13, 2); vport.SnapBasePoint = vectors.SnapBasePoint;
    RequireVPortPoint(seen, 14, 2); vport.SnapSpacing = vectors.SnapSpacing;
    RequireVPortPoint(seen, 15, 2); vport.GridSpacing = vectors.GridSpacing;
    RequireVPortPoint(seen, 16, 3); vport.ViewDirection = vectors.ViewDirection;
    RequireVPortPoint(seen, 17, 3); vport.ViewTarget = vectors.ViewTarget;
    RequireVPortPoint(seen, 110, 3); vport.UcsOrigin = vectors.UcsOrigin;
    RequireVPortPoint(seen, 111, 3); vport.UcsXAxis = vectors.UcsXAxis;
    RequireVPortPoint(seen, 112, 3); vport.UcsYAxis = vectors.UcsYAxis;
  } catch (error) { if (error instanceof ArgumentException) throw WrappedInvalidData('Invalid VPORT coordinate data.', error); throw error; }
  if (xdata.Count > 0) context.tableEntryXData.Add(vport, xdata);
  return vport;
}
export function WriteVPort(chunk, document, vp) {
            chunk.Write(0, vp.CodeName);
            chunk.Write(5, vp.Handle);
            WriteDatabaseMetadata(chunk, document, vp);
            chunk.Write(330, vp.Owner.Handle);
            chunk.Write(100, SubclassMarker.TableRecord);
            chunk.Write(100, SubclassMarker.VPort);
            chunk.Write(2, EncodeDxfText(vp.Name, document.DrawingVariables.AcadVer));
            chunk.Write(40, vp.ViewHeight);
            chunk.Write(41, vp.ViewAspectRatio);
            chunk.Write(42, vp.LensLength);
            chunk.Write(43, vp.FrontClippingPlane);
            chunk.Write(44, vp.BackClippingPlane);
            chunk.Write(50, vp.SnapRotation);
            chunk.Write(51, vp.ViewTwist);
            chunk.Write(10, vp.LowerLeftCorner.X);
            chunk.Write(20, vp.LowerLeftCorner.Y);
            chunk.Write(11, vp.UpperRightCorner.X);
            chunk.Write(21, vp.UpperRightCorner.Y);
            chunk.Write(12, vp.ViewCenter.X);
            chunk.Write(22, vp.ViewCenter.Y);
            chunk.Write(13, vp.SnapBasePoint.X);
            chunk.Write(23, vp.SnapBasePoint.Y);
            chunk.Write(14, vp.SnapSpacing.X);
            chunk.Write(24, vp.SnapSpacing.Y);
            chunk.Write(15, vp.GridSpacing.X);
            chunk.Write(25, vp.GridSpacing.Y);
            chunk.Write(16, vp.ViewDirection.X);
            chunk.Write(26, vp.ViewDirection.Y);
            chunk.Write(36, vp.ViewDirection.Z);
            chunk.Write(17, vp.ViewTarget.X);
            chunk.Write(27, vp.ViewTarget.Y);
            chunk.Write(37, vp.ViewTarget.Z);
            chunk.Write(110, vp.UcsOrigin.X);
            chunk.Write(120, vp.UcsOrigin.Y);
            chunk.Write(130, vp.UcsOrigin.Z);
            chunk.Write(111, vp.UcsXAxis.X);
            chunk.Write(121, vp.UcsXAxis.Y);
            chunk.Write(131, vp.UcsXAxis.Z);
            chunk.Write(112, vp.UcsYAxis.X);
            chunk.Write(122, vp.UcsYAxis.Y);
            chunk.Write(132, vp.UcsYAxis.Z);
            chunk.Write(70, vp.Flags);
            chunk.Write(71, vp.ViewMode);
            chunk.Write(72, vp.CircleSides);
            chunk.Write(73, vp.FastZoom ? 1 : 0);
            chunk.Write(74, vp.UcsIcon);
            chunk.Write(75, vp.SnapMode ? 1 : 0);
            chunk.Write(76, vp.ShowGrid ? 1 : 0);
            chunk.Write(77, vp.SnapStyle);
            chunk.Write(78, vp.SnapIsopair);
            chunk.Write(281, vp.RenderMode);
            chunk.Write(65, vp.UcsPerViewport ? 1 : 0);
            chunk.Write(79, vp.UcsOrthographicType);
            chunk.Write(146, vp.UcsElevation);
            if (vp.NamedUcs !== null) chunk.Write(345, vp.NamedUcs.Handle);
            if (vp.BaseUcs !== null) chunk.Write(346, vp.BaseUcs.Handle);
            WriteSunReference(chunk, document.DrawingVariables.AcadVer, vp);
            WriteXData(chunk, () => document.DrawingVariables.AcadVer, vp.XData);
}
