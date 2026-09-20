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
        if(AttributeWire(value,out result))return true;
        if(CurveRecordWire(value,out result))return true;
        if (value is PolyfaceMeshFace pf) { result=new {type="PolyfaceMeshFace",indices=pf.VertexIndexes.Select(v=>Wire(v)).ToArray(),color=Wire(pf.Color),layer=Wire(pf.Layer)};return true; }
        if(value is PolyfaceMeshRecord pr) {
            result=new {type="PolyfaceMeshRecord",code=pr.CodeName,handle=pr.Handle,face=Wire(pr.Face),isFace=pr.IsFaceRecord,isEnd=pr.IsSequenceEnd,version=(int)pr.SourceVersion,blockOwner=pr.UsesBlockRecordOwner,layer=Wire(pr.Layer),linetype=Wire(pr.Linetype),
                canClone=pr.GetType().GetMethod("CanClone",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(pr,null),
                tagCount=pr.GetType().GetMethod("TopologyTagCount",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(pr,null),
                tags=((System.Collections.Generic.List<netDxf.IO.DxfTag>)pr.GetType().GetField("Tags",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(pr)!).Select(t=>new {code=t.Code,value=Wire(t.Value)}).ToArray(),xdata=pr.XData.Values.Select(Wire).ToArray()};return true;
        }
        if (value is AcisSatChunk chunk) { result=new {type="AcisSatChunk",code=chunk.GroupCode,text=chunk.Text};return true; }
        if (MTextValueWire(value,out result)) return true;
        if (value is MeshEdge edge) { result=new {type="MeshEdge",start=edge.StartVertexIndex,end=edge.EndVertexIndex,crease=Wire(edge.Crease)};return true; }
        if (value is Polyline2DVertex vertex) {
            result = new {type="Polyline2DVertex",position=Wire(vertex.Position),bulge=Wire(vertex.Bulge),start=Wire(vertex.StartWidth),end=Wire(vertex.EndWidth),
                startOverride=Wire(vertex.StartWidthOverride),endOverride=Wire(vertex.EndWidthOverride),identifier=Wire(vertex.VertexIdentifier)};
            return true;
        }
        if(value is netDxf.Objects.ImageDefinition imageDefinition) {
            result=new {type="ImageDefinition",name=imageDefinition.Name,code=imageDefinition.CodeName,file=imageDefinition.File,width=imageDefinition.Width,height=imageDefinition.Height,
                horizontal=Wire(imageDefinition.HorizontalResolution),vertical=Wire(imageDefinition.VerticalResolution),units=(int)imageDefinition.ResolutionUnits,xdata=imageDefinition.XData.Values.Select(Wire).ToArray()};
            return true;
        }
        // The original internal reactor has a public constructor; reflection observes its immutable data.
        if(value.GetType().FullName=="netDxf.Objects.ImageDefinitionReactor") {
            result=new {type="ImageDefinitionReactor",code=((DxfObject)value).CodeName,imageHandle=(string?)value.GetType().GetProperty("ImageHandle")!.GetValue(value)};
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
        if(HatchEntityWire(entity,common,out result))return true;
        if(CurveWire(entity,common,out result))return true;
        if (InertEntityWire(entity,common,out result)) return true;
        if(entity is Light light) result=new {common,name=Wire(light.Name),version=light.VersionNumber,kind=(int)light.LightType,on=light.IsOn,plot=light.PlotGlyph,intensity=Wire(light.Intensity),position=Wire(light.Position),target=Wire(light.Target),attenuation=(int)light.AttenuationType,limits=light.UseAttenuationLimits,start=Wire(light.AttenuationStartLimit),end=Wire(light.AttenuationEndLimit),hotspot=Wire(light.HotspotAngle),falloff=Wire(light.FalloffAngle),cast=light.CastShadows,shadow=(int)light.ShadowType,map=light.ShadowMapSize,softness=light.ShadowMapSoftness};
        else if(entity is PolyfaceMesh meshFace) result=new {common,vertices=meshFace.Vertexes.Select(v=>Wire(v)).ToArray(),faces=meshFace.Faces.Select(Wire).ToArray(),vertexRecords=meshFace.VertexRecords.Select(Wire).ToArray(),faceRecords=meshFace.FaceRecords.Select(Wire).ToArray(),sequence=meshFace.RecordSequence.Select(Wire).ToArray(),endRecord=Wire(meshFace.EndSequenceRecord),declaredVertices=Wire(meshFace.DeclaredVertexCount),declaredFaces=Wire(meshFace.DeclaredFaceCount)};
        else if(entity is Image image) result=new {common,definition=Wire(image.Definition),position=Wire(image.Position),u=Wire(image.Uvector),v=Wire(image.Vvector),
            width=Wire(image.Width),height=Wire(image.Height),rotation=Wire(image.Rotation),clipping=image.Clipping,brightness=image.Brightness,contrast=image.Contrast,fade=image.Fade,display=(int)image.DisplayOptions,boundary=Wire(image.ClippingBoundary)};
        else if(entity is Wipeout wipeout) result=new {common,elevation=Wire(wipeout.Elevation),boundary=Wire(wipeout.ClippingBoundary)};
        else if (entity is Underlay underlay) result=new {common,definition=Wire(underlay.Definition),position=Wire(underlay.Position),scale=Wire(underlay.Scale),rotation=Wire(underlay.Rotation),
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
