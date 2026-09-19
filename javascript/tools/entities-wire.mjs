import { curveWire, curveRecordWire } from './curve-wire.mjs';
import { inertEntityWire } from './inert-entity-wire.mjs';
import * as api from '../index.js';
import { mtextValueWire } from './mtext-wire.mjs';
export function entityWire(value, wire) {
  const curveRecord=curveRecordWire(value,wire);if(curveRecord!==undefined)return curveRecord;
  if (value instanceof api.PolyfaceMeshFace) return {type:'PolyfaceMeshFace',indices:Array.from(value.VertexIndexes,wire),color:wire(value.Color),layer:wire(value.Layer)};
  if (value instanceof api.PolyfaceMeshRecord) return {type:'PolyfaceMeshRecord',code:value.CodeName,handle:value.Handle,face:wire(value.Face),isFace:value.IsFaceRecord,isEnd:value.IsSequenceEnd,version:value.SourceVersion,blockOwner:value.UsesBlockRecordOwner,layer:wire(value.Layer),linetype:wire(value.Linetype),canClone:value.CanClone(),tagCount:value.TopologyTagCount(),tags:Array.from(value.Tags,t=>({code:t.Code,value:wire(t.Value)})),xdata:Array.from(value.XData.Values,wire)};
  if (value instanceof api.AcisSatChunk) return {type:'AcisSatChunk',code:value.GroupCode,text:value.Text};
  const mtextValue = mtextValueWire(value,wire); if (mtextValue !== undefined) return mtextValue;
  if (value instanceof api.MeshEdge) return {type:'MeshEdge',start:value.StartVertexIndex,end:value.EndVertexIndex,crease:wire(value.Crease)};
  if (value instanceof api.Polyline2DVertex) return {type:'Polyline2DVertex',position:wire(value.Position),bulge:wire(value.Bulge),
    start:wire(value.StartWidth),end:wire(value.EndWidth),startOverride:wire(value.StartWidthOverride),endOverride:wire(value.EndWidthOverride),identifier:wire(value.VertexIdentifier)};
  if (value instanceof api.ImageDefinition) return {type:'ImageDefinition',name:value.Name,code:value.CodeName,file:value.File,width:value.Width,height:value.Height,
    horizontal:wire(value.HorizontalResolution),vertical:wire(value.VerticalResolution),units:value.ResolutionUnits,xdata:Array.from(value.XData.Values,wire)};
  if (value instanceof api.ImageDefinitionReactor) return {type:'ImageDefinitionReactor',code:value.CodeName,imageHandle:value.ImageHandle};
  if (value instanceof api.UnderlayDefinition) {
    const definition = {type:value.constructor.name,name:value.Name,code:value.CodeName,kind:value.Type,file:value.File,xdata:Array.from(value.XData.Values,wire)};
    if (value instanceof api.UnderlayPdfDefinition) return {definition,page:wire(value.Page)};
    if (value instanceof api.UnderlayDgnDefinition) return {definition,layout:wire(value.Layout)};
    return {definition};
  }
  if (!(value instanceof api.EntityObject)) return undefined;
  const common = {type:value.constructor.name,kind:value.Type,code:value.CodeName,handle:value.Handle,
    owner:value.Owner?.CodeName??null,color:wire(value.Color),layer:wire(value.Layer),linetype:wire(value.Linetype),lineweight:value.Lineweight,
    transparency:wire(value.Transparency),linetypeScale:wire(value.LinetypeScale),normal:wire(value.Normal),visible:value.IsVisible,
    colorName:value.ColorName,shadow:wire(value.ShadowMode),proxy:wire(value.ProxyGraphics),
    reactors:Array.from(value.Reactors,r=>r===null?null:{code:r.CodeName,handle:r.Handle}),
    xdata:Array.from(value.XData.Values,wire)};
  const curve=curveWire(value,common,wire);if(curve!==undefined)return curve;
  const inert=inertEntityWire(value,common,wire); if(inert!==undefined)return inert;
  if (value instanceof api.Light) return {common,name:wire(value.Name),version:value.VersionNumber,kind:value.LightType,on:value.IsOn,plot:value.PlotGlyph,intensity:wire(value.Intensity),position:wire(value.Position),target:wire(value.Target),attenuation:value.AttenuationType,limits:value.UseAttenuationLimits,start:wire(value.AttenuationStartLimit),end:wire(value.AttenuationEndLimit),hotspot:wire(value.HotspotAngle),falloff:wire(value.FalloffAngle),cast:value.CastShadows,shadow:value.ShadowType,map:value.ShadowMapSize,softness:value.ShadowMapSoftness};
  if (value instanceof api.PolyfaceMesh) return {common,vertices:Array.from(value.Vertexes,wire),faces:Array.from(value.Faces,wire),vertexRecords:Array.from(value.VertexRecords,wire),faceRecords:Array.from(value.FaceRecords,wire),sequence:Array.from(value.RecordSequence,wire),endRecord:wire(value.EndSequenceRecord),declaredVertices:wire(value.DeclaredVertexCount),declaredFaces:wire(value.DeclaredFaceCount)};
  if (value instanceof api.Image) return {common,definition:wire(value.Definition),position:wire(value.Position),u:wire(value.Uvector),v:wire(value.Vvector),
    width:wire(value.Width),height:wire(value.Height),rotation:wire(value.Rotation),clipping:value.Clipping,brightness:value.Brightness,contrast:value.Contrast,fade:value.Fade,display:value.DisplayOptions,boundary:wire(value.ClippingBoundary)};
  if (value instanceof api.Wipeout) return {common,elevation:wire(value.Elevation),boundary:wire(value.ClippingBoundary)};
  if (value instanceof api.Underlay) return {common,definition:wire(value.Definition),position:wire(value.Position),scale:wire(value.Scale),rotation:wire(value.Rotation),
    contrast:value.Contrast,fade:value.Fade,display:value.DisplayOptions,boundary:wire(value.ClippingBoundary)};
  if (value instanceof api.MText) return {common,position:wire(value.Position),rotation:wire(value.Rotation),height:wire(value.Height),width:wire(value.RectangleWidth),
    attachment:value.AttachmentPoint,spacing:wire(value.LineSpacingFactor),spacingStyle:value.LineSpacingStyle,direction:value.DrawingDirection,style:wire(value.Style),
    text:wire(value.Value),background:wire(value.BackgroundFill),columns:wire(value.Columns),definedHeight:wire(value.DefinedHeight)};
  if (value instanceof api.Text) return {common,position:wire(value.Position),rotation:wire(value.Rotation),height:wire(value.Height),width:wire(value.Width),
    widthFactor:wire(value.WidthFactor),oblique:wire(value.ObliqueAngle),alignment:value.Alignment,backward:value.IsBackward,upsideDown:value.IsUpsideDown,style:wire(value.Style),text:value.Value};
  if (value instanceof api.Shape) return {common,name:value.Name,position:wire(value.Position),rotation:wire(value.Rotation),size:wire(value.Size),
    widthFactor:wire(value.WidthFactor),oblique:wire(value.ObliqueAngle),thickness:wire(value.Thickness),style:wire(value.Style)};
  if (value instanceof api.Mesh) return {common,vertexes:Array.from(value.Vertexes,wire),faces:Array.from(value.Faces,wire),edges:Array.from(value.Edges,wire),
    subdivision:value.SubdivisionLevel,blend:value.BlendCrease};
  if (value instanceof api.Point) return {common,position:wire(value.Position),rotation:wire(value.Rotation),thickness:wire(value.Thickness)};
  if (value instanceof api.Line) return {common,start:wire(value.StartPoint),end:wire(value.EndPoint),direction:wire(value.Direction),thickness:wire(value.Thickness)};
  if (value instanceof api.Ray || value instanceof api.XLine) return {common,origin:wire(value.Origin),direction:wire(value.Direction)};
  if (value instanceof api.Face3D || value instanceof api.Solid || value instanceof api.Trace) {
    const vertices = ['FirstVertex','SecondVertex','ThirdVertex','FourthVertex'].map(k=>wire(value[k]));
    return value instanceof api.Face3D ? {common,vertices,edgeFlags:value.EdgeFlags} : {common,vertices,elevation:wire(value.Elevation),thickness:wire(value.Thickness)};
  }
  throw new Error('Unmapped entity result: '+value.constructor.name);
}
