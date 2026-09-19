// Test adapters serialize the unchanged production assembly; no expected geometry is generated here.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using netDxf;
using netDxf.Entities;
internal static partial class Program {
  private static bool CurveRecordWire(object value,out object? result) {
    result=null;
    if(value is Polyline3DRecord r3){
      var t3=(List<netDxf.IO.DxfTag>)typeof(Polyline3DRecord).GetField("Tags",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(r3)!;
      result=new {type="Polyline3DRecord",code=r3.CodeName,handle=r3.Handle,isEnd=r3.IsSequenceEnd,removed=r3.IsRemoved,version=(int)r3.SourceVersion,blockOwner=r3.UsesBlockRecordOwner,layer=Wire(r3.Layer),linetype=Wire(r3.Linetype),tags=t3.Select(t=>new {code=t.Code,value=Wire(t.Value)}).ToArray(),xdata=r3.XData.Values.Select(Wire).ToArray()};return true;
    }
    if(value is not Polyline2DRecord r)return false;
    var tags=(List<netDxf.IO.DxfTag>)typeof(Polyline2DRecord).GetField("Tags",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(r)!;
    result=new {type="Polyline2DRecord",code=r.CodeName,handle=r.Handle,vertex=Wire(r.Vertex),isEnd=r.IsSequenceEnd,version=(int)r.SourceVersion,
      blockOwner=r.UsesBlockRecordOwner,layer=Wire(r.Layer),linetype=Wire(r.Linetype),tags=tags.Select(t=>new {code=t.Code,value=Wire(t.Value)}).ToArray(),xdata=r.XData.Values.Select(Wire).ToArray()};return true;
  }
  private static bool CurveWire(EntityObject value,object common,out object? result) {
    result=null;
    if(value is Spline s)result=new {common,controls=Wire(s.ControlPoints),fit=Wire(s.FitPoints),weights=Wire(s.Weights),knots=Wire(s.Knots),degree=(int)s.Degree,method=(int)s.CreationMethod,closed=s.IsClosed,periodic=s.IsClosedPeriodic,start=Wire(s.StartTangent),end=Wire(s.EndTangent),parameterization=(int)s.KnotParameterization,knotTolerance=Wire(s.KnotTolerance),controlTolerance=Wire(s.CtrlPointTolerance),fitTolerance=Wire(s.FitTolerance)};
    else if(value is Polyline3D p3)result=new {common,vertices=p3.Vertexes.Select(v=>Wire(v)).ToArray(),closed=p3.IsClosed,generation=p3.LinetypeGeneration,smooth=(int)p3.SmoothType,records=p3.VertexRecords.Select(Wire).ToArray(),endRecord=Wire(p3.EndSequenceRecord)};
    else if(value is Ellipse e)result=new {common,center=Wire(e.Center),major=Wire(e.MajorAxis),minor=Wire(e.MinorAxis),rotation=Wire(e.Rotation),start=Wire(e.StartAngle),end=Wire(e.EndAngle),thickness=Wire(e.Thickness),full=e.IsFullEllipse};
    else if(value is Circle c)result=new {common,center=Wire(c.Center),radius=Wire(c.Radius),thickness=Wire(c.Thickness)};
    else if(value is Arc a)result=new {common,center=Wire(a.Center),radius=Wire(a.Radius),start=Wire(a.StartAngle),end=Wire(a.EndAngle),thickness=Wire(a.Thickness)};
    else if(value is Polyline2D p)result=new {common,vertices=p.Vertexes.Select(Wire).ToArray(),closed=p.IsClosed,generation=p.LinetypeGeneration,thickness=Wire(p.Thickness),elevation=Wire(p.Elevation),smooth=(int)p.SmoothType,
      constant=Wire(p.ConstantWidth),legacyStart=Wire(p.LegacyDefaultStartWidth),legacyEnd=Wire(p.LegacyDefaultEndWidth),records=p.VertexRecords.Select(Wire).ToArray(),endRecord=Wire(p.EndSequenceRecord)};
    else return false;
    if(value is Helix helix)result=new {spline=result,major=helix.MajorReleaseNumber,maintenance=helix.MaintenanceReleaseNumber,@base=Wire(helix.AxisBasePoint),start=Wire(helix.StartPoint),axis=Wire(helix.AxisVector),radius=Wire(helix.Radius),turns=Wire(helix.Turns),height=Wire(helix.TurnHeight),right=helix.IsRightHanded,constraint=(int)helix.Constraint};
    return true;
  }
}
