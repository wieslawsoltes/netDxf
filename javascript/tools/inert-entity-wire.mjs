import * as api from '../index.js';
/** Test-only structural serialization; never decodes or evaluates embedded content. */
export function inertEntityWire(value,common,wire) {
  if (value instanceof api.OleFrame) return {common,version:value.OleVersion,present:value.HasOleVersion,length:value.BinaryDataLength,bytes:wire(value.GetBinaryData())};
  if (value instanceof api.Ole2Frame) return {common,version:value.OleVersion,fields:value.MetadataFields,upper:wire(value.UpperLeftCorner),lower:wire(value.LowerRightCorner),description:wire(value.Description),kind:value.ObjectType,tile:value.TileMode,length:value.BinaryDataLength,bytes:wire(value.GetBinaryData())};
  if (value instanceof api.AcisEntity) return {common,version:value.ModelerFormatVersion,chunks:Array.from(value.EncodedSatChunks,wire),lines:Array.from(value.SatLines,wire),history:value instanceof api.Solid3D?value.HistoryHandle:null};
  return undefined;
}
