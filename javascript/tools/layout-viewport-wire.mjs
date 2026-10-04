// Test-only observation; expected results come from the unchanged C# assembly.
import { Layout, Viewport, EntityChangeEventArgs } from '../index.js';
const owner=value=>value==null?null:{code:value.CodeName,handle:value.Handle};
export function layoutViewportValueWire(value,wire){
  if(value instanceof EntityChangeEventArgs)return {type:'EntityChangeEventArgs',item:wire(value.Item)};
  if(!(value instanceof Layout))return undefined;
  const fields={};for(const key of ['Name','TabOrder','IsPaperSpace','IsReserved','MinLimit','MaxLimit','BasePoint','MinExtents','MaxExtents','Elevation','UcsOrigin','UcsXAxis','UcsYAxis'])fields[key]=wire(value[key]);
  return {type:'Layout',code:value.CodeName,handle:value.Handle,owner:owner(value.Owner),fields,plot:wire(value.PlotSettings),viewport:wire(value.Viewport),block:wire(value.AssociatedBlock),xdata:Array.from(value.XData.Values,wire)};
}
export function viewportEntityWire(value,common,wire){
  if(!(value instanceof Viewport))return undefined;
  const fields={};for(const key of ['Center','Width','Height','Stacking','Id','ViewCenter','SnapBase','SnapSpacing','GridSpacing','ViewDirection','ViewTarget',
    'LensLength','FrontClipPlane','BackClipPlane','ViewHeight','SnapAngle','TwistAngle','CircleZoomPercent','Status','UcsOrigin','UcsXAxis','UcsYAxis','Elevation','SunHandlePresent'])fields[key]=wire(value[key]);
  return {common,fields,boundary:wire(value.ClippingBoundary),frozen:Array.from(value.FrozenLayers,wire),sun:owner(value.Sun)};
}
