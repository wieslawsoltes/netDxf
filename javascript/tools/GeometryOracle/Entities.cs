// Supplemental oracle serialization; all entity operations use the pinned production assembly.
using System;
using System.Linq;
using netDxf;
using netDxf.Entities;
internal static partial class Program
{
    private static bool EntityWire(object value, out object? result)
    {
        result = null;
        if (value is Polyline2DVertex vertex) {
            result = new {type="Polyline2DVertex",position=Wire(vertex.Position),bulge=Wire(vertex.Bulge),start=Wire(vertex.StartWidth),end=Wire(vertex.EndWidth),
                startOverride=Wire(vertex.StartWidthOverride),endOverride=Wire(vertex.EndWidthOverride),identifier=Wire(vertex.VertexIdentifier)};
            return true;
        }
        if (value is not EntityObject entity) return false;
        var common = new {type=entity.GetType().Name,kind=(int)entity.Type,code=entity.CodeName,handle=entity.Handle,owner=entity.Owner?.CodeName,
            color=Wire(entity.Color),layer=Wire(entity.Layer),linetype=Wire(entity.Linetype),lineweight=(int)entity.Lineweight,transparency=Wire(entity.Transparency),
            linetypeScale=Wire(entity.LinetypeScale),normal=Wire(entity.Normal),visible=entity.IsVisible,colorName=entity.ColorName,shadow=Wire(entity.ShadowMode),proxy=Wire(entity.ProxyGraphics),
            reactors=entity.Reactors.Select(r=>r is null?null:new {code=r.CodeName,handle=r.Handle}).ToArray(),xdata=entity.XData.Values.Select(Wire).ToArray()};
        if (entity is Point point) result=new {common,position=Wire(point.Position),rotation=Wire(point.Rotation),thickness=Wire(point.Thickness)};
        else if (entity is Line line) result=new {common,start=Wire(line.StartPoint),end=Wire(line.EndPoint),direction=Wire(line.Direction),thickness=Wire(line.Thickness)};
        else if (entity is Ray ray) result=new {common,origin=Wire(ray.Origin),direction=Wire(ray.Direction)};
        else if (entity is XLine xline) result=new {common,origin=Wire(xline.Origin),direction=Wire(xline.Direction)};
        else if (entity is Face3D face) result=new {common,vertices=new[]{Wire(face.FirstVertex),Wire(face.SecondVertex),Wire(face.ThirdVertex),Wire(face.FourthVertex)},edgeFlags=(int)face.EdgeFlags};
        else if (entity is Solid solid) result=new {common,vertices=new[]{Wire(solid.FirstVertex),Wire(solid.SecondVertex),Wire(solid.ThirdVertex),Wire(solid.FourthVertex)},elevation=Wire(solid.Elevation),thickness=Wire(solid.Thickness)};
        else if (entity is Trace trace) result=new {common,vertices=new[]{Wire(trace.FirstVertex),Wire(trace.SecondVertex),Wire(trace.ThirdVertex),Wire(trace.FourthVertex)},elevation=Wire(trace.Elevation),thickness=Wire(trace.Thickness)};
        else throw new ArgumentException("Unmapped entity result: "+entity.GetType().Name);
        return true;
    }
}
