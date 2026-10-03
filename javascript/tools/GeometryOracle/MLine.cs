// Independent serialization only: all model operations use the unchanged pinned assembly.
using System.Linq;
using netDxf.Entities;
using netDxf.Objects;
internal static partial class Program {
  private static bool MLineValueWire(object value,out object? result){
    result=null;
    if(value is MLineStyleElementChangeEventArgs args)result=new {type="MLineStyleElementChangeEventArgs",item=Wire(args.Item)};
    else if(value is MLineStyleElement e)result=new {type="MLineStyleElement",offset=Wire(e.Offset),color=Wire(e.Color),linetype=Wire(e.Linetype)};
    else if(value is MLineStyle s)result=new {type="MLineStyle",name=s.Name,code=s.CodeName,handle=s.Handle,owner=s.Owner?.CodeName,reserved=s.IsReserved,flags=(int)s.Flags,description=Wire(s.Description),fill=Wire(s.FillColor),start=Wire(s.StartAngle),end=Wire(s.EndAngle),elements=s.Elements.Select(Wire).ToArray(),xdata=s.XData.Values.Select(Wire).ToArray()};
    else if(value is MLineVertex v)result=new {type="MLineVertex",position=Wire(v.Position),direction=Wire(v.Direction),miter=Wire(v.Miter),distances=Wire(v.Distances)};
    else return false;return true;
  }
  private static bool MLineEntityWire(EntityObject value,object common,out object? result){
    result=null;if(value is not MLine m)return false;
    result=new {common,style=Wire(m.Style),scale=Wire(m.Scale),elevation=Wire(m.Elevation),justification=(int)m.Justification,closed=m.IsClosed,start=m.NoStartCaps,end=m.NoEndCaps,vertices=m.Vertexes.Select(Wire).ToArray()};return true;
  }
}
