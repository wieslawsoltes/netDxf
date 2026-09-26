// Observation only; no placement, transform, callback or cloning behavior lives here.
import {Leader,BoxedString,BoxedBoolean} from '../index.js';
export function leaderWire(value,wire){
  if(value instanceof BoxedString||value instanceof BoxedBoolean)return wire(value.Value);
  if(!(value instanceof Leader))return;
  const common={type:value.constructor.name,kind:value.Type,code:value.CodeName,handle:value.Handle,
    owner:value.Owner?.CodeName??null,color:wire(value.Color),layer:wire(value.Layer),linetype:wire(value.Linetype),lineweight:value.Lineweight,
    transparency:wire(value.Transparency),linetypeScale:wire(value.LinetypeScale),normal:wire(value.Normal),visible:value.IsVisible,
    colorName:value.ColorName,shadow:wire(value.ShadowMode),proxy:wire(value.ProxyGraphics),
    reactors:Array.from(value.Reactors,r=>r===null?null:{code:r.CodeName,handle:r.Handle}),xdata:Array.from(value.XData.Values,wire)};
  return {common,style:wire(value.Style),overrides:wire(value.StyleOverrides),arrow:value.ShowArrowhead,path:value.PathType,
    vertices:Array.from(value.Vertexes,wire),annotation:wire(value.Annotation),hookline:value.HasHookline,lineColor:wire(value.LineColor),elevation:wire(value.Elevation),offset:wire(value.Offset),direction:wire(value.Direction)};
}
