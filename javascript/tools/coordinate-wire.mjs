import { UCS, View, ViewUcs, VPort } from '../index.js';
import { ReadOnlyValueMap } from '../runtime/ReadOnlyValueMap.js';
const VIEW_FIELDS=["Target", "ViewDirection", "ViewCenter", "Height", "Width", "Rotation", "ViewMode", "LensLength", "FrontClippingPlane", "BackClippingPlane", "Flags", "RenderMode", "IsCameraPlottable", "IsPaperSpace"];
const VPORT_FIELDS=["ViewCenter", "SnapBasePoint", "SnapSpacing", "GridSpacing", "ViewDirection", "ViewTarget", "ViewHeight", "ViewAspectRatio", "ShowGrid", "SnapMode", "LowerLeftCorner", "UpperRightCorner", "Flags", "LensLength", "FrontClippingPlane", "BackClippingPlane", "SnapRotation", "ViewTwist", "ViewMode", "CircleSides", "FastZoom", "UcsIcon", "SnapStyle", "SnapIsopair", "RenderMode", "UcsPerViewport", "UcsOrigin", "UcsXAxis", "UcsYAxis", "UcsOrthographicType", "UcsElevation"];
const reference = item => item == null ? null : { name:item.Name, code:item.CodeName, handle:item.Handle };
export function coordinateWire(value,wire) {
  if (value instanceof ReadOnlyValueMap) return { origins:Array.from(value,pair=>({key:pair.Key,point:wire(pair.Value)})) };
  if (value instanceof UCS) return {type:'UCS',name:value.Name,code:value.CodeName,handle:value.Handle,
    origin:wire(value.Origin),x:wire(value.XAxis),y:wire(value.YAxis),z:wire(value.ZAxis),elevation:wire(value.Elevation),flags:value.Flags,
    orthographicType:value.OrthographicViewType,base:reference(value.BaseUcs),basePresent:value.BaseUcsHandlePresent,
    origins:wire(value.OrthographicOrigins),xdata:Array.from(value.XData.Values,wire)};
  if (value instanceof ViewUcs) return {type:'ViewUcs',origin:wire(value.Origin),x:wire(value.XAxis),y:wire(value.YAxis),elevation:wire(value.Elevation),
    kind:wire(value.OrthographicType),named:reference(value.NamedUcs),base:reference(value.BaseUcs),view:reference(value.View)};
  if (value instanceof View || value instanceof VPort) {
    const fields = value instanceof View ? VIEW_FIELDS : VPORT_FIELDS;
    const common = {name:value.Name,code:value.CodeName,handle:value.Handle,reserved:value.IsReserved,xdata:Array.from(value.XData.Values,wire)};
    const state = Object.fromEntries(fields.map(name=>[name,wire(value[name])]));
    const sun = {value:wire(value.Sun),present:value.SunHandlePresent};
    return value instanceof View ? {type:'View',common,state,sun,ucs:wire(value.Ucs),liveSection:value.LiveSection == null ? null : {code:value.LiveSection.CodeName,handle:value.LiveSection.Handle},livePresent:value.HasStoredLiveSection}
      : {type:'VPort',common,state,sun,named:reference(value.NamedUcs),base:reference(value.BaseUcs)};
  }
  return undefined;
}
