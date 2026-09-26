// Independent snapshots of the production attribute models; no expected geometry here.
import { Attribute, AttributeDefinition, AttributeDefinitionDictionary } from '../index.js';
export function attributeWire(v,wire){
  if(v instanceof AttributeDefinitionDictionary)return Array.from(v,p=>[wire(p.Key),wire(p.Value)]);
  if(!(v instanceof Attribute)&&!(v instanceof AttributeDefinition))return undefined;
  const common={type:v.constructor.name,code:v.CodeName,handle:v.Handle,owner:v.Owner?.CodeName??null,
    color:wire(v.Color),layer:wire(v.Layer),linetype:wire(v.Linetype),lineweight:v.Lineweight,transparency:wire(v.Transparency),
    scale:wire(v.LinetypeScale),normal:wire(v.Normal),visible:v.IsVisible,colorName:wire(v.ColorName),shadow:wire(v.ShadowMode),proxy:wire(v.ProxyGraphics),xdata:Array.from(v.XData.Values,wire)};
  const text={tag:wire(v.Tag),value:wire(v.Value),style:wire(v.Style),position:wire(v.Position),flags:v.Flags,
    height:wire(v.Height),width:wire(v.Width),widthFactor:wire(v.WidthFactor),oblique:wire(v.ObliqueAngle),rotation:wire(v.Rotation),
    alignment:v.Alignment,backward:v.IsBackward,upsideDown:v.IsUpsideDown};
  return v instanceof AttributeDefinition?{common,text,prompt:wire(v.Prompt)}:{common,text,definition:wire(v.Definition)};
}
