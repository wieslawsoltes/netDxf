import { Circle, Arc, Polyline2D, Polyline2DRecord } from '../index.js';
export function curveRecordWire(value,wire) {
  if(!(value instanceof Polyline2DRecord))return undefined;
  return {type:'Polyline2DRecord',code:value.CodeName,handle:value.Handle,vertex:wire(value.Vertex),isEnd:value.IsSequenceEnd,
    version:value.SourceVersion,blockOwner:value.UsesBlockRecordOwner,layer:wire(value.Layer),linetype:wire(value.Linetype),
    tags:Array.from(value.Tags,t=>({code:t.Code,value:wire(t.Value)})),xdata:Array.from(value.XData.Values,wire)};
}
export function curveWire(value,common,wire) {
  if(value instanceof Circle)return {common,center:wire(value.Center),radius:wire(value.Radius),thickness:wire(value.Thickness)};
  if(value instanceof Arc)return {common,center:wire(value.Center),radius:wire(value.Radius),start:wire(value.StartAngle),end:wire(value.EndAngle),thickness:wire(value.Thickness)};
  if(value instanceof Polyline2D)return {common,vertices:Array.from(value.Vertexes,wire),closed:value.IsClosed,generation:value.LinetypeGeneration,
    thickness:wire(value.Thickness),elevation:wire(value.Elevation),smooth:value.SmoothType,constant:wire(value.ConstantWidth),
    legacyStart:wire(value.LegacyDefaultStartWidth),legacyEnd:wire(value.LegacyDefaultEndWidth),records:Array.from(value.VertexRecords,wire),endRecord:wire(value.EndSequenceRecord)};
  return undefined;
}
