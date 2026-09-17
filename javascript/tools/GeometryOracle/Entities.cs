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
        if (MTextValueWire(value,out result)) return true;
        if (value is MeshEdge edge) { result=new {type="MeshEdge",start=edge.StartVertexIndex,end=edge.EndVertexIndex,crease=Wire(edge.Crease)};return true; }
        if (value is Polyline2DVertex vertex) {
            result = new {type="Polyline2DVertex",position=Wire(vertex.Position),bulge=Wire(vertex.Bulge),start=Wire(vertex.StartWidth),end=Wire(vertex.EndWidth),
                startOverride=Wire(vertex.StartWidthOverride),endOverride=Wire(vertex.EndWidthOverride),identifier=Wire(vertex.VertexIdentifier)};
            return true;
        }
        if (value is netDxf.Objects.UnderlayDefinition underlayDefinition) {
            var definition=new {type=value.GetType().Name,name=underlayDefinition.Name,code=underlayDefinition.CodeName,kind=(int)underlayDefinition.Type,file=underlayDefinition.File,xdata=underlayDefinition.XData.Values.Select(Wire).ToArray()};
            if(value is netDxf.Objects.UnderlayPdfDefinition pdf) result=new {definition,page=Wire(pdf.Page)};
            else if(value is netDxf.Objects.UnderlayDgnDefinition dgn) result=new {definition,layout=Wire(dgn.Layout)};
            else result=new {definition};
            return true;
        }
        if (value is not EntityObject entity) return false;
        var common = new {type=entity.GetType().Name,kind=(int)entity.Type,code=entity.CodeName,handle=entity.Handle,owner=entity.Owner?.CodeName,
            color=Wire(entity.Color),layer=Wire(entity.Layer),linetype=Wire(entity.Linetype),lineweight=(int)entity.Lineweight,transparency=Wire(entity.Transparency),
            linetypeScale=Wire(entity.LinetypeScale),normal=Wire(entity.Normal),visible=entity.IsVisible,colorName=entity.ColorName,shadow=Wire(entity.ShadowMode),proxy=Wire(entity.ProxyGraphics),
            reactors=entity.Reactors.Select(r=>r is null?null:new {code=r.CodeName,handle=r.Handle}).ToArray(),xdata=entity.XData.Values.Select(Wire).ToArray()};
        if (entity is Underlay underlay) result=new {common,definition=Wire(underlay.Definition),position=Wire(underlay.Position),scale=Wire(underlay.Scale),rotation=Wire(underlay.Rotation),
            contrast=underlay.Contrast,fade=underlay.Fade,display=(int)underlay.DisplayOptions,boundary=Wire(underlay.ClippingBoundary)};
        else if (entity is MText mtext) result=new {common,position=Wire(mtext.Position),rotation=Wire(mtext.Rotation),height=Wire(mtext.Height),width=Wire(mtext.RectangleWidth),
            attachment=(int)mtext.AttachmentPoint,spacing=Wire(mtext.LineSpacingFactor),spacingStyle=(int)mtext.LineSpacingStyle,direction=(int)mtext.DrawingDirection,style=Wire(mtext.Style),
            text=Wire(mtext.Value),background=Wire(mtext.BackgroundFill),columns=Wire(mtext.Columns),definedHeight=Wire(mtext.DefinedHeight)};
        else if (entity is Text text) result=new {common,position=Wire(text.Position),rotation=Wire(text.Rotation),height=Wire(text.Height),width=Wire(text.Width),
            widthFactor=Wire(text.WidthFactor),oblique=Wire(text.ObliqueAngle),alignment=(int)text.Alignment,backward=text.IsBackward,upsideDown=text.IsUpsideDown,style=Wire(text.Style),text=text.Value};
        else if (entity is Shape shape) result=new {common,name=shape.Name,position=Wire(shape.Position),rotation=Wire(shape.Rotation),size=Wire(shape.Size),
            widthFactor=Wire(shape.WidthFactor),oblique=Wire(shape.ObliqueAngle),thickness=Wire(shape.Thickness),style=Wire(shape.Style)};
        else if (entity is Mesh mesh) result=new {common,vertexes=mesh.Vertexes.Select(v=>Wire(v)).ToArray(),faces=mesh.Faces.Select(Wire).ToArray(),edges=mesh.Edges.Select(Wire).ToArray(),
            subdivision=mesh.SubdivisionLevel,blend=mesh.BlendCrease};
        else if (entity is Point point) result=new {common,position=Wire(point.Position),rotation=Wire(point.Rotation),thickness=Wire(point.Thickness)};
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
