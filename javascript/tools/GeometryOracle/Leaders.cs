// Snapshot only; the unchanged production C# implementation supplies all operations.
using netDxf.Entities;
using System.Linq;
internal static partial class Program {
    private static bool LeaderWire(object value,out object? result){
        result=null;if(value is not Leader t)return false;
        var common=new {type=t.GetType().Name,kind=(int)t.Type,code=t.CodeName,handle=t.Handle,owner=t.Owner?.CodeName,
            color=Wire(t.Color),layer=Wire(t.Layer),linetype=Wire(t.Linetype),lineweight=(int)t.Lineweight,transparency=Wire(t.Transparency),
            linetypeScale=Wire(t.LinetypeScale),normal=Wire(t.Normal),visible=t.IsVisible,colorName=t.ColorName,shadow=Wire(t.ShadowMode),proxy=Wire(t.ProxyGraphics),
            reactors=t.Reactors.Select(r=>r is null?null:new {code=r.CodeName,handle=r.Handle}).ToArray(),xdata=t.XData.Values.Select(Wire).ToArray()};
        result=new {common,style=Wire(t.Style),overrides=Wire(t.StyleOverrides),arrow=t.ShowArrowhead,path=(int)t.PathType,
            vertices=t.Vertexes.Select(v=>Wire(v)).ToArray(),annotation=Wire(t.Annotation),hookline=t.HasHookline,lineColor=Wire(t.LineColor),elevation=Wire(t.Elevation),offset=Wire(t.Offset),direction=Wire(t.Direction)};return true;
    }
}
