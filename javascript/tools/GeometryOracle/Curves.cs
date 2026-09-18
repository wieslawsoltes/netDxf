// Test adapters serialize the unchanged production assembly; no expected geometry is generated here.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using netDxf;
using netDxf.Entities;
internal static partial class Program {
  private static bool CurveRecordWire(object value,out object? result) {
    result=null;if(value is not Polyline2DRecord r)return false;
    var tags=(List<netDxf.IO.DxfTag>)typeof(Polyline2DRecord).GetField("Tags",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(r)!;
    result=new {type="Polyline2DRecord",code=r.CodeName,handle=r.Handle,vertex=Wire(r.Vertex),isEnd=r.IsSequenceEnd,version=(int)r.SourceVersion,
      blockOwner=r.UsesBlockRecordOwner,layer=Wire(r.Layer),linetype=Wire(r.Linetype),tags=tags.Select(t=>new {code=t.Code,value=Wire(t.Value)}).ToArray(),xdata=r.XData.Values.Select(Wire).ToArray()};return true;
  }
  private static bool CurveWire(EntityObject value,object common,out object? result) {
    result=null;
    if(value is Circle c)result=new {common,center=Wire(c.Center),radius=Wire(c.Radius),thickness=Wire(c.Thickness)};
    else if(value is Arc a)result=new {common,center=Wire(a.Center),radius=Wire(a.Radius),start=Wire(a.StartAngle),end=Wire(a.EndAngle),thickness=Wire(a.Thickness)};
    else if(value is Polyline2D p)result=new {common,vertices=p.Vertexes.Select(Wire).ToArray(),closed=p.IsClosed,generation=p.LinetypeGeneration,thickness=Wire(p.Thickness),elevation=Wire(p.Elevation),smooth=(int)p.SmoothType,
      constant=Wire(p.ConstantWidth),legacyStart=Wire(p.LegacyDefaultStartWidth),legacyEnd=Wire(p.LegacyDefaultEndWidth),records=p.VertexRecords.Select(Wire).ToArray(),endRecord=Wire(p.EndSequenceRecord)};
    else return false;return true;
  }
}
