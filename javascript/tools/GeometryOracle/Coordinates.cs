// Expected values are read only from the unchanged pinned C# production assembly.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using netDxf;
using netDxf.Tables;
internal static partial class Program {
    private static readonly string[] ViewFields={"Target","ViewDirection","ViewCenter","Height","Width","Rotation","ViewMode","LensLength","FrontClippingPlane","BackClippingPlane","Flags","RenderMode","IsCameraPlottable","IsPaperSpace"};
    private static readonly string[] VPortFields={"ViewCenter","SnapBasePoint","SnapSpacing","GridSpacing","ViewDirection","ViewTarget","ViewHeight","ViewAspectRatio","ShowGrid","SnapMode","LowerLeftCorner","UpperRightCorner","Flags","LensLength","FrontClippingPlane","BackClippingPlane","SnapRotation","ViewTwist","ViewMode","CircleSides","FastZoom","UcsIcon","SnapStyle","SnapIsopair","RenderMode","UcsPerViewport","UcsOrigin","UcsXAxis","UcsYAxis","UcsOrthographicType","UcsElevation"};
    private static object? CoordinateReference(TableObject? value) => value is null ? null : new {name=value.Name,code=value.CodeName,handle=value.Handle};
    private static bool CoordinateWire(object value,out object? result) {
        result=null;
        if(value is IReadOnlyDictionary<UcsOrthographicType,Vector3> dictionary) {
            result=new {origins=dictionary.Select(pair=>new {key=(int)pair.Key,point=Wire(pair.Value)}).ToArray()};return true;
        }
        if(value is UCS ucs) {
            result=new {type="UCS",name=ucs.Name,code=ucs.CodeName,handle=ucs.Handle,
                origin=Wire(ucs.Origin),x=Wire(ucs.XAxis),y=Wire(ucs.YAxis),z=Wire(ucs.ZAxis),elevation=Wire(ucs.Elevation),flags=(int)ucs.Flags,
                orthographicType=ucs.OrthographicViewType,@base=CoordinateReference(ucs.BaseUcs),basePresent=(bool)typeof(UCS).GetProperty("BaseUcsHandlePresent",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(ucs)!,
                origins=Wire(ucs.OrthographicOrigins),xdata=ucs.XData.Values.Select(Wire).ToArray()};return true;
        }
        if(value is ViewUcs bundle) {
            result=new {type="ViewUcs",origin=Wire(bundle.Origin),x=Wire(bundle.XAxis),y=Wire(bundle.YAxis),elevation=Wire(bundle.Elevation),
                kind=Wire(bundle.OrthographicType),named=CoordinateReference(bundle.NamedUcs),@base=CoordinateReference(bundle.BaseUcs),
                view=CoordinateReference((View?)typeof(ViewUcs).GetProperty("View",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(bundle))};return true;
        }
        if(value is View || value is VPort) {
            var table=(TableObject)value;var type=value.GetType();
            var common=new {name=table.Name,code=table.CodeName,handle=table.Handle,reserved=table.IsReserved,xdata=table.XData.Values.Select(Wire).ToArray()};
            var fields=value is View?ViewFields:VPortFields;
            var state=fields.ToDictionary(name=>name,name=>Wire(type.GetProperty(name)!.GetValue(value)));
            var sun=new {value=Wire(type.GetProperty("Sun",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public)!.GetValue(value)),
                present=(bool)type.GetProperty("SunHandlePresent",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(value)!};
            if(value is View view) result=new {type="View",common,state,sun,ucs=Wire(view.Ucs),
                liveSection=view.LiveSection is null?null:new {code=view.LiveSection.CodeName,handle=view.LiveSection.Handle},livePresent=view.HasStoredLiveSection};
            else { var viewport=(VPort)value;result=new {type="VPort",common,state,sun,named=CoordinateReference(viewport.NamedUcs),@base=CoordinateReference(viewport.BaseUcs)}; }
            return true;
        }
        return false;
    }
}
