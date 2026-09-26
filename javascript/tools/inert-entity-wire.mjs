import * as api from '../index.js';
/** Test-only structural serialization; never decodes or evaluates embedded content. */
export function inertEntityWire(value,common,wire) {
  if (value instanceof api.OleFrame) return {common,version:value.OleVersion,present:value.HasOleVersion,length:value.BinaryDataLength,bytes:wire(value.GetBinaryData())};
  if (value instanceof api.Ole2Frame) return {common,version:value.OleVersion,fields:value.MetadataFields,upper:wire(value.UpperLeftCorner),lower:wire(value.LowerRightCorner),description:wire(value.Description),kind:value.ObjectType,tile:value.TileMode,length:value.BinaryDataLength,bytes:wire(value.GetBinaryData())};
  if (value instanceof api.AcisEntity) return {common,version:value.ModelerFormatVersion,chunks:Array.from(value.EncodedSatChunks,wire),lines:Array.from(value.SatLines,wire),history:value instanceof api.Solid3D?value.HistoryHandle:null};
  if (value instanceof api.Section) return {common,state:value.State,flags:value.Flags,name:wire(value.Name),vertical:wire(value.VerticalDirection),top:wire(value.TopHeight),bottom:wire(value.BottomHeight),transparency:value.IndicatorTransparency,color:wire(value.StoredIndicatorColor),nativeColor:wire(value.StoredNativeIndicatorColor),colorName:wire(value.IndicatorColorName),vertices:Array.from(value.Vertices,wire),back:Array.from(value.BackLineVertices,wire),settings:value.GeometrySettings===null?null:{code:value.GeometrySettings.CodeName,handle:value.GeometrySettings.Handle},settingsPresent:value.HasStoredGeometrySettings,erased:value.IsErased};
  return undefined;
}
