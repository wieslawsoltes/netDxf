// Independent observation: all parsing, geometry and cloning use the unchanged C# library.
using netDxf.Entities;
using System.Linq;
internal static partial class Program {
    private static bool ToleranceValueWire(object value,out object? result){
        result=null;
        if(value is ToleranceEntry e)result=new {type="ToleranceEntry",symbol=(int)e.GeometricSymbol,tolerance1=Wire(e.Tolerance1),tolerance2=Wire(e.Tolerance2),datum1=Wire(e.Datum1),datum2=Wire(e.Datum2),datum3=Wire(e.Datum3)};
        else if(value is ToleranceValue t)result=new {type="ToleranceValue",diameter=t.ShowDiameterSymbol,text=Wire(t.Value),material=(int)t.MaterialCondition};
        else if(value is DatumReferenceValue d)result=new {type="DatumReferenceValue",text=Wire(d.Value),material=(int)d.MaterialCondition};
        return result is not null;
    }
    private static bool ToleranceWire(object value,out object? result){
        result=null;if(LeaderWire(value,out result))return true;if(ToleranceValueWire(value,out result))return true;if(value is not Tolerance t)return false;
        var common=new {type=t.GetType().Name,kind=(int)t.Type,code=t.CodeName,handle=t.Handle,owner=t.Owner?.CodeName,
            color=Wire(t.Color),layer=Wire(t.Layer),linetype=Wire(t.Linetype),lineweight=(int)t.Lineweight,transparency=Wire(t.Transparency),
            linetypeScale=Wire(t.LinetypeScale),normal=Wire(t.Normal),visible=t.IsVisible,colorName=t.ColorName,shadow=Wire(t.ShadowMode),proxy=Wire(t.ProxyGraphics),
            reactors=t.Reactors.Select(r=>r is null?null:new {code=r.CodeName,handle=r.Handle}).ToArray(),xdata=t.XData.Values.Select(Wire).ToArray()};
        result=new {common,entry1=Wire(t.Entry1),entry2=Wire(t.Entry2),position=Wire(t.Position),rotation=Wire(t.Rotation),height=Wire(t.TextHeight),style=Wire(t.Style),projected=Wire(t.ProjectedToleranceZoneValue),show=t.ShowProjectedToleranceZoneSymbol,datum=Wire(t.DatumIdentifier)};return true;
    }
}
