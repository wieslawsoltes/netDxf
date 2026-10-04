// Observation-only snapshots. Expected results come from the independent C# assembly.
import { MLine,MLineVertex,MLineStyle,MLineStyleElement,MLineStyleElementChangeEventArgs } from '../index.js';
export function mlineValueWire(value,wire){
  if(value instanceof MLineStyleElementChangeEventArgs)return {type:'MLineStyleElementChangeEventArgs',item:wire(value.Item)};
  if(value instanceof MLineStyleElement)return {type:'MLineStyleElement',offset:wire(value.Offset),color:wire(value.Color),linetype:wire(value.Linetype)};
  if(value instanceof MLineStyle)return {type:'MLineStyle',name:value.Name,code:value.CodeName,handle:value.Handle,owner:value.Owner?.CodeName??null,reserved:value.IsReserved,
    flags:value.Flags,description:wire(value.Description),fill:wire(value.FillColor),start:wire(value.StartAngle),end:wire(value.EndAngle),elements:Array.from(value.Elements,wire),xdata:Array.from(value.XData.Values,wire)};
  if(value instanceof MLineVertex)return {type:'MLineVertex',position:wire(value.Position),direction:wire(value.Direction),miter:wire(value.Miter),distances:wire(value.Distances)};
  return undefined;
}
export function mlineEntityWire(value,common,wire){
  if(!(value instanceof MLine))return undefined;
  return {common,style:wire(value.Style),scale:wire(value.Scale),elevation:wire(value.Elevation),justification:value.Justification,closed:value.IsClosed,start:value.NoStartCaps,end:value.NoEndCaps,vertices:Array.from(value.Vertexes,wire)};
}
