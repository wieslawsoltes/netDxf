// Copyright (c) Daniel Carvajal and netDxf contributors. MIT License; see package LICENSE.
import { Vector3 } from '../Vector3.js';
import { UCS } from '../Tables/UCS.js';
import { View } from '../Tables/View.js';
import { VPort } from '../Tables/VPort.js';
import { ViewUcs } from '../Tables/ViewUcs.js';
import { UcsReferences } from '../Collections/UcsReferences.js';
import { ResolveUcsBaseReferences } from './DxfReader.UcsBase.js';
import { RequireVPortPoint } from './DxfVPort.js';
import { WrappedInvalidData } from '../../runtime/DxfXDataIO.js';
import { ArgumentException, InvalidOperationException, InvalidDataException } from '../../runtime/Errors.js';
export function AddUcsReference(context, owner, code, handle) {
  if (handle !== null && handle !== '0') context.ucsReferences.push([owner, code, handle]);
}
export function ResolveUcsReferences(context) {
  ResolveUcsBaseReferences(context);
  for (const [owner, code, handle] of context.ucsReferences) {
    // VIEW/VPORT source code intentionally uses the document map, unlike UCS base refs.
    const target = context.Document.GetObjectByHandle(handle);
    if (!(target instanceof UCS)) throw new InvalidDataException('Unresolved or non-UCS target for group ' + code + ': ' + handle);
    if (owner instanceof View) {
      if (code === 345) owner.Ucs.NamedUcs = target; else owner.Ucs.BaseUcs = target;
    } else if (owner instanceof VPort) {
      if (code === 345) owner.NamedUcs = target; else owner.BaseUcs = target;
    }
  }
  try {
    for (const view of context.Document.Views) UcsReferences.Validate(view, context.Document);
    for (const port of context.Document.VPorts.Records) UcsReferences.Validate(port, context.Document);
  } catch (error) {
    if (error instanceof ArgumentException || error instanceof InvalidOperationException)
      throw WrappedInvalidData('Invalid associated UCS relationship.', error);
    throw error;
  }
}
export class ViewUcsInput {
  Enabled = false; Seen = new Set(); Origin = Vector3.Zero; XAxis = Vector3.UnitX; YAxis = Vector3.UnitY;
  Type = 0; Elevation = 0; Named = null; Base = null;
}
const pointFields = Object.freeze({110: ['Origin', 'X'],120: ['Origin', 'Y'],130: ['Origin', 'Z'],
  111: ['XAxis', 'X'],121: ['XAxis', 'Y'],131: ['XAxis', 'Z'],112: ['YAxis', 'X'],122: ['YAxis', 'Y'],132: ['YAxis', 'Z']});
export function TryReadViewUcs(chunk, input) {
  const code = chunk.Code;
  if (![72, 110, 120, 130, 111, 121, 131, 112, 122, 132, 79, 146, 345, 346].includes(code)) return false;
  if (input.Seen.has(code)) throw new InvalidDataException('Duplicate VIEW UCS group ' + code + '.');
  input.Seen.add(code);
  if (Object.hasOwn(pointFields, code)) { const [field, component] = pointFields[code]; input[field][component] = chunk.ReadDouble(); }
  else switch (code) {
    case 72: {
      const enabled = chunk.ReadShort();
      if (enabled !== 0 && enabled !== 1) throw new InvalidDataException('VIEW group 72 must be zero or one.');
      input.Enabled = enabled === 1; break;
    }
    case 79: input.Type = chunk.ReadShort(); break;
    case 146: input.Elevation = chunk.ReadDouble(); break;
    case 345: input.Named = chunk.ReadHex(); break;
    case 346: input.Base = chunk.ReadHex(); break;
  }
  chunk.Next(); return true;
}
export function CompleteViewUcs(context, view, input) {
  if (!input.Enabled) {
    if (input.Seen.size > (input.Seen.has(72) ? 1 : 0))
      throw new InvalidDataException('Associated VIEW UCS fields require group 72 equal to one.');
    return;
  }
  RequireVPortPoint(input.Seen, 110, 3); RequireVPortPoint(input.Seen, 111, 3); RequireVPortPoint(input.Seen, 112, 3);
  try { view.Ucs = Object.assign(new ViewUcs(), { Origin: input.Origin, XAxis: input.XAxis, YAxis: input.YAxis,
    OrthographicType: input.Type, Elevation: input.Elevation }); }
  catch (error) { if (error instanceof ArgumentException) throw WrappedInvalidData('Invalid associated VIEW UCS values.', error); throw error; }
  AddUcsReference(context, view, 345, input.Named); AddUcsReference(context, view, 346, input.Base);
}
export function ValidateUcsReferences(document) {
  for (const ucs of document.UCSs) UcsReferences.Validate(ucs, document);
  for (const view of document.Views) { UcsReferences.Validate(view, document); view.ValidateLiveSection(document); }
  for (const port of document.VPorts.Records) UcsReferences.Validate(port, document);
}
export function WriteViewUcs(chunk, value) {
  chunk.Write(72, value === null ? 0 : 1); if (value === null) return;
  chunk.Write(110, value.Origin.X); chunk.Write(120, value.Origin.Y); chunk.Write(130, value.Origin.Z);
  chunk.Write(111, value.XAxis.X); chunk.Write(121, value.XAxis.Y); chunk.Write(131, value.XAxis.Z);
  chunk.Write(112, value.YAxis.X); chunk.Write(122, value.YAxis.Y); chunk.Write(132, value.YAxis.Z);
  chunk.Write(79, value.OrthographicType); chunk.Write(146, value.Elevation);
  if (value.NamedUcs !== null) chunk.Write(345, value.NamedUcs.Handle);
  if (value.BaseUcs !== null) chunk.Write(346, value.BaseUcs.Handle);
}
