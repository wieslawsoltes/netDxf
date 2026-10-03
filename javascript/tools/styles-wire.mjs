import * as api from '../index.js';
import { SetSupportFileSystem, SupportFileSystem } from '../runtime/SupportFileSystem.js';
import { base64ToBytes } from './wire.mjs';
const tableWire=(table,wire)=>({type:table.constructor.name,name:table.Name,code:table.CodeName,reserved:table.IsReserved,
  xdata:Array.from(table.XData.Values,data=>({name:data.ApplicationRegistry.Name,records:Array.from(data.XDataRecord,wire)}))});
export function styleWire(value,wire) {
  if(value instanceof api.TextStyleFontData)return {type:'TextStyleFontData',family:value.FamilyName,flags:value.Flags,style:value.FontStyle};
  if(value instanceof api.TextStyle)return {table:tableWire(value,wire),file:value.FontFile,bigFont:value.BigFont,family:value.FontFamilyName,fontStyle:value.FontStyle,
    fontData:wire(value.ExtendedFontData),height:wire(value.Height),width:wire(value.WidthFactor),oblique:wire(value.ObliqueAngle),flags:value.Flags,
    generation:value.TextGenerationFlags,lastHeight:wire(value.LastHeight),vertical:value.IsVertical,backward:value.IsBackward,upsideDown:value.IsUpsideDown};
  if(value instanceof api.ShapeStyle)return {table:tableWire(value,wire),file:value.File,size:wire(value.Size),width:wire(value.WidthFactor),oblique:wire(value.ObliqueAngle),flags:value.Flags,
    generation:value.TextGenerationFlags,lastHeight:wire(value.LastHeight)};
  if(value instanceof api.Linetype)return {table:tableWire(value,wire),description:value.Description,byLayer:value.IsByLayer,byBlock:value.IsByBlock,length:wire(value.Length()),segments:Array.from(value.Segments,wire)};
  if(value instanceof api.Layer)return {table:tableWire(value,wire),description:value.Description,color:wire(value.Color),linetype:wire(value.Linetype),lineweight:value.Lineweight,
    transparency:wire(value.Transparency),assigned:value.HasTransparencyAssignment,visible:value.IsVisible,frozen:value.IsFrozen,locked:value.IsLocked,plot:value.Plot};
  if(value instanceof api.LinetypeSegment) {
    const common={type:value.constructor.name,kind:value.Type,length:wire(value.Length)};
    if(value instanceof api.LinetypeTextSegment)return {common,text:value.Text,style:wire(value.Style),offset:wire(value.Offset),rotationType:value.RotationType,rotation:wire(value.Rotation),scale:wire(value.Scale)};
    if(value instanceof api.LinetypeShapeSegment)return {common,name:value.Name,style:wire(value.Style),offset:wire(value.Offset),rotationType:value.RotationType,rotation:wire(value.Rotation),scale:wire(value.Scale)};
    return {common};
  }
  if(value instanceof api.XDataDictionary)return {type:'XDataDictionary',entries:Array.from(value.Values,wire)};
  if(value instanceof api.XData)return {type:'XData',name:value.ApplicationRegistry.Name,records:Array.from(value.XDataRecord,wire)};
  if(value instanceof api.TableObject)return tableWire(value,wire);
  return undefined;
}
/** A synchronous in-memory input host, never an alternative SHX parser. */
export function shapeInput(step) {
  const bytes=base64ToBytes(step.bytes),file='oracle.shx';
  const previous=SetSupportFileSystem({ReadAllBytes:()=>bytes,Exists:()=>true,DirectorySeparators:SupportFileSystem.DirectorySeparators,InvalidPathChars:SupportFileSystem.InvalidPathChars});
  try {
    if(step.kind==='shape-names')return api.ShapeStyle.NamesFromFile(file);
    if(step.member==='StaticContainsShapeName')return api.ShapeStyle.ContainsShapeName(file,step.name);
    const style=new api.ShapeStyle('S',file);
    if(step.member==='ShapeNumber'||step.member==='ContainsShapeName')return style[step.member](step.name);
    if(step.member==='ShapeName')return style.ShapeName(step.number);
    return style.NamesFromShapeStyle();
  }finally { SetSupportFileSystem(previous); }
}
