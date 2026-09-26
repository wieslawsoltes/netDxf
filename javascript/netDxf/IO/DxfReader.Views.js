// Copyright (c) Daniel Carvajal and netDxf contributors. MIT License; see package LICENSE.
import { View } from '../Tables/View.js';
import { TableObject } from '../Tables/TableObject.js';
import { SubclassMarker } from '../SubclassMarker.js';
import { Vector2 } from '../Vector2.js';
import { Vector3 } from '../Vector3.js';
import { ReferenceList } from '../../runtime/ReferenceList.js';
import { ReadXDataRecord } from '../../runtime/DxfXDataIO.js';
import { DecodeDxfText } from '../../runtime/DxfStringEncoding.js';
import { SunOwnerContext, AddSunReference } from './DxfReader.Sun.js';
import { ViewUcsInput, TryReadViewUcs, CompleteViewUcs } from './DxfViewUcs.js';
import { FormatException } from '../../runtime/Errors.js';
export function ReadView(chunk, context) {
  // The native initial subclass check is Debug.Assert only; retain Release cursor behavior.
  chunk.ReadString();
  let name = '', sunHandle = null, liveSectionHandle = null;
  const sun = new SunOwnerContext(SubclassMarker.View), center = Vector2.Zero, direction = Vector3.UnitZ, target = Vector3.Zero;
  let height = 1, width = 1, lens = 40, front = 0, back = 0, rotation = 0, flags = 0, mode = 0, renderMode = 0, cameraPlottable = false;
  const associatedUcs = new ViewUcsInput(), xdata = new ReferenceList(); chunk.Next();
  while (chunk.Code !== 0) {
    sun.Observe(chunk.Code, chunk.Value); if (TryReadViewUcs(chunk, associatedUcs)) continue;
    switch (chunk.Code) {
      case 334:
        if (!sun.IsPublic) break;
        if (liveSectionHandle !== null) throw new FormatException('Repeated VIEW live-section group 334.');
        liveSectionHandle = chunk.ReadHex(); break;
      case 361:
        if (!sun.IsPublic) break;
        if (sunHandle !== null) throw new FormatException('Repeated VIEW SUN group 361.');
        sunHandle = chunk.ReadHex(); break;
      case 2: name = DecodeDxfText(chunk.ReadString()); break;
      case 70: flags = chunk.ReadShort(); break;
      case 10: center.X = chunk.ReadDouble(); break; case 20: center.Y = chunk.ReadDouble(); break;
      case 11: direction.X = chunk.ReadDouble(); break; case 21: direction.Y = chunk.ReadDouble(); break; case 31: direction.Z = chunk.ReadDouble(); break;
      case 12: target.X = chunk.ReadDouble(); break; case 22: target.Y = chunk.ReadDouble(); break; case 32: target.Z = chunk.ReadDouble(); break;
      case 40: height = chunk.ReadDouble(); break; case 41: width = chunk.ReadDouble(); break;
      case 42: lens = chunk.ReadDouble(); break; case 43: front = chunk.ReadDouble(); break; case 44: back = chunk.ReadDouble(); break;
      case 50: rotation = chunk.ReadDouble(); break; case 71: mode = chunk.ReadShort(); break; case 281: renderMode = chunk.ReadShort(); break;
      case 73: {
        const flag = chunk.ReadShort();
        if (flag !== 0 && flag !== 1) throw new FormatException('The VIEW camera-plottable flag must be zero or one.');
        cameraPlottable = flag === 1; break;
      }
      case 1001: xdata.Add(ReadXDataRecord(chunk, null, true)); continue;
      // Native Debug.Assert for orphan XData has no process-abort substitute.
    }
    chunk.Next();
  }
  if (!TableObject.IsValidName(name)) throw new FormatException('The VIEW table record has an invalid or missing name.');
  const view = Object.assign(new View(name, false), { ViewCenter: center, ViewDirection: direction, Target: target,
    Height: height, Width: width, LensLength: lens, FrontClippingPlane: front, BackClippingPlane: back, Rotation: rotation,
    Flags: flags, ViewMode: mode, RenderMode: renderMode, IsCameraPlottable: cameraPlottable });
  if (sunHandle !== null) AddSunReference(context, view, sunHandle);
  if (liveSectionHandle !== null) context.loadedViewSections.push([view, liveSectionHandle]);
  CompleteViewUcs(context, view, associatedUcs);
  if (xdata.Count > 0) context.tableEntryXData.Add(view, xdata);
  return view;
}
