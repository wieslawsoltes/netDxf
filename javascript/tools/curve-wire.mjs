import { PolygonMesh, PolygonMeshRecord, Helix, Spline, Polyline3D, Polyline3DRecord, Ellipse, Circle, Arc, Polyline2D, Polyline2DRecord } from '../index.js';
export function curveRecordWire(value,wire) {
  if(value instanceof PolygonMeshRecord)return {type:'PolygonMeshRecord',code:value.CodeName,handle:value.Handle,isEnd:value.IsSequenceEnd,version:value.SourceVersion,blockOwner:value.UsesBlockRecordOwner,layer:wire(value.Layer),linetype:wire(value.Linetype),tags:Array.from(value.Tags,t=>({code:t.Code,value:wire(t.Value)})),xdata:Array.from(value.XData.Values,wire)};
  if(value instanceof Polyline3DRecord)return {type:'Polyline3DRecord',code:value.CodeName,handle:value.Handle,isEnd:value.IsSequenceEnd,removed:value.IsRemoved,version:value.SourceVersion,blockOwner:value.UsesBlockRecordOwner,layer:wire(value.Layer),linetype:wire(value.Linetype),tags:Array.from(value.Tags,t=>({code:t.Code,value:wire(t.Value)})),xdata:Array.from(value.XData.Values,wire)};
  if(!(value instanceof Polyline2DRecord))return undefined;
  return {type:'Polyline2DRecord',code:value.CodeName,handle:value.Handle,vertex:wire(value.Vertex),isEnd:value.IsSequenceEnd,
    version:value.SourceVersion,blockOwner:value.UsesBlockRecordOwner,layer:wire(value.Layer),linetype:wire(value.Linetype),
    tags:Array.from(value.Tags,t=>({code:t.Code,value:wire(t.Value)})),xdata:Array.from(value.XData.Values,wire)};
}
export function curveWire(value,common,wire) {
  if(value instanceof PolygonMesh)return {common,u:value.U,v:value.V,vertices:wire(value.Vertexes),densityU:value.DensityU,densityV:value.DensityV,smooth:value.SmoothType,closedU:value.IsClosedInU,closedV:value.IsClosedInV,records:wire(value.VertexRecords),endRecord:wire(value.EndSequenceRecord)};
  if(value instanceof Spline){const spline={common,controls:wire(value.ControlPoints),fit:wire(value.FitPoints),weights:wire(value.Weights),knots:wire(value.Knots),degree:value.Degree,method:value.CreationMethod,closed:value.IsClosed,periodic:value.IsClosedPeriodic,start:wire(value.StartTangent),end:wire(value.EndTangent),parameterization:value.KnotParameterization,knotTolerance:wire(value.KnotTolerance),controlTolerance:wire(value.CtrlPointTolerance),fitTolerance:wire(value.FitTolerance)};return value instanceof Helix ? {spline,major:value.MajorReleaseNumber,maintenance:value.MaintenanceReleaseNumber,base:wire(value.AxisBasePoint),start:wire(value.StartPoint),axis:wire(value.AxisVector),radius:wire(value.Radius),turns:wire(value.Turns),height:wire(value.TurnHeight),right:value.IsRightHanded,constraint:value.Constraint}:spline;}
  if(value instanceof Polyline3D)return {common,vertices:Array.from(value.Vertexes,wire),closed:value.IsClosed,generation:value.LinetypeGeneration,smooth:value.SmoothType,records:Array.from(value.VertexRecords,wire),endRecord:wire(value.EndSequenceRecord)};
  if(value instanceof Ellipse)return {common,center:wire(value.Center),major:wire(value.MajorAxis),minor:wire(value.MinorAxis),rotation:wire(value.Rotation),start:wire(value.StartAngle),end:wire(value.EndAngle),thickness:wire(value.Thickness),full:value.IsFullEllipse};
  if(value instanceof Circle)return {common,center:wire(value.Center),radius:wire(value.Radius),thickness:wire(value.Thickness)};
  if(value instanceof Arc)return {common,center:wire(value.Center),radius:wire(value.Radius),start:wire(value.StartAngle),end:wire(value.EndAngle),thickness:wire(value.Thickness)};
  if(value instanceof Polyline2D)return {common,vertices:Array.from(value.Vertexes,wire),closed:value.IsClosed,generation:value.LinetypeGeneration,
    thickness:wire(value.Thickness),elevation:wire(value.Elevation),smooth:value.SmoothType,constant:wire(value.ConstantWidth),
    legacyStart:wire(value.LegacyDefaultStartWidth),legacyEnd:wire(value.LegacyDefaultEndWidth),records:Array.from(value.VertexRecords,wire),endRecord:wire(value.EndSequenceRecord)};
  return undefined;
}
