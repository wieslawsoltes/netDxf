// Test-only observations. No production calculations are implemented in this adapter.
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using netDxf;
using netDxf.Objects;
using netDxf.Entities;
internal static partial class Program {
  private static object? LayoutViewportOwner(DxfObject? value)=>value==null?null:new {code=value.CodeName,handle=value.Handle};
  private static bool LayoutViewportValueWire(object value,out object? result){
    result=null;
    if(value is EntityChangeEventArgs e){result=new {type="EntityChangeEventArgs",item=Wire(e.Item)};return true;}
    if(value is not Layout layout)return false;
    var fields=new Dictionary<string,object?>();
    foreach(string key in new[]{"Name","TabOrder","IsPaperSpace","IsReserved","MinLimit","MaxLimit","BasePoint","MinExtents","MaxExtents","Elevation","UcsOrigin","UcsXAxis","UcsYAxis"})fields[key]=Wire(typeof(Layout).GetProperty(key)!.GetValue(layout));
    result=new {type="Layout",code=layout.CodeName,handle=layout.Handle,owner=LayoutViewportOwner(layout.Owner),fields,plot=Wire(layout.PlotSettings),viewport=Wire(layout.Viewport),block=Wire(layout.AssociatedBlock),xdata=layout.XData.Values.Select(Wire).ToArray()};return true;
  }
  private static bool ViewportEntityWire(EntityObject value,object common,out object? result){
    result=null;if(value is not Viewport viewport)return false;
    var fields=new Dictionary<string,object?>();
    foreach(string key in new[]{"Center","Width","Height","Stacking","Id","ViewCenter","SnapBase","SnapSpacing","GridSpacing","ViewDirection","ViewTarget",
      "LensLength","FrontClipPlane","BackClipPlane","ViewHeight","SnapAngle","TwistAngle","CircleZoomPercent","Status","UcsOrigin","UcsXAxis","UcsYAxis","Elevation","SunHandlePresent"})
      fields[key]=Wire(typeof(Viewport).GetProperty(key,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)!.GetValue(viewport));
    result=new {common,fields,boundary=Wire(viewport.ClippingBoundary),frozen=viewport.FrozenLayers.Select(Wire).ToArray(),sun=LayoutViewportOwner(viewport.Sun)};return true;
  }
}
