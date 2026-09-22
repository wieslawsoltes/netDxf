import {unitFormatWire} from './unit-format-wire.mjs';
import {dimensionWire} from './dimension-wire.mjs';
import { headerWire } from './header-wire.mjs';
import { layoutViewportValueWire } from './layout-viewport-wire.mjs';
import { blockWire } from './block-wire.mjs';
import { mlineValueWire } from './mline-wire.mjs';
import { groupWire } from './group-wire.mjs';
import { outputSettingsWire } from './output-settings-wire.mjs';
import { hatchBoundaryWire } from './hatch-entity-wire.mjs';
import { surfaceWire } from './surface-wire.mjs';
import { coordinateWire } from './coordinate-wire.mjs';
import { databaseModelWire } from './database-model-wire.mjs';
// Shared Node/browser operation interpreter; production implementations provide all behavior.
import * as api from '../index.js';
import { utf16Wire } from './mtext-wire.mjs';
import { entityWire } from './entities-wire.mjs';
import { styleWire } from './styles-wire.mjs';
import { Copy } from '../runtime/GeometryRuntime.js';

import { doubleBits } from './wire.mjs';

function wire(value) {
  if (value == null) return null;
  if (typeof value === 'number') return { double: doubleBits(value) };
  if (typeof value === 'bigint') return { long: value.toString() };
  if (typeof value === 'string') return utf16Wire(value);
  if (typeof value === 'boolean') return value;
  if(typeof value.MoveNext==='function'&&'Current' in value)return {type:'Enumerator'};
  if(Object.hasOwn(value,'Key')&&Object.hasOwn(value,'Value'))return [wire(value.Key),wire(value.Value)];
  if (value instanceof api.DxfObjectReference) return {type:'DxfObjectReference',reference:wire(value.Reference),uses:wire(value.Uses)};
  if (value instanceof api.DxfObjectReferences) return {type:'DxfObjectReferences',empty:value.IsEmpty(),references:wire(value.ToList())};
  const unitFormat=unitFormatWire(value,wire);if(unitFormat!==undefined)return unitFormat;
  const dimension=dimensionWire(value,wire);if(dimension!==undefined)return dimension;
  const header=headerWire(value,wire);if(header!==undefined)return header;
  const type = value.constructor.name;
  if (/^Vector[234]$/.test(type)) return { type, values: [...'XYZW'.slice(0, Number(type.at(-1)))].map(key => doubleBits(value[key])), normalized: value.IsNormalized };
  if (/^Matrix[234]$/.test(type)) {
    const n = Number(type.at(-1)), values = [];
    for (let r=1;r<=n;r++) for (let c=1;c<=n;c++) values.push(doubleBits(value[`M${r}${c}`]));
    return { type, values, identity: Copy(value).IsIdentity };
  }
  if (type === 'Tuple') return Object.keys(value).map(key=>wire(value[key]));
  if (value instanceof api.BezierCurve) return {type,degree:value.Degree,points:value.ControlPoints.map(wire)};
  if (value instanceof api.BoundingRectangle) return {type,min:wire(value.Min),max:wire(value.Max),center:wire(value.Center),radius:wire(value.Radius),width:wire(value.Width),height:wire(value.Height)};
  if (value instanceof api.ClippingBoundary) return {type,kind:value.Type,vertices:value.Vertexes.map(wire)};
  if (value instanceof api.AciColor) return {type:'AciColor',r:value.R,g:value.G,b:value.B,index:value.Index,trueColor:value.UseTrueColor,byLayer:value.IsByLayer,byBlock:value.IsByBlock};
  if (value instanceof api.HatchPatternLineDefinition) return {type,angle:wire(value.Angle),origin:wire(value.Origin),delta:wire(value.Delta),dashes:Array.from(value.DashPattern,wire)};
  if (value instanceof api.HatchPattern) {
    const pattern={type,name:value.Name,description:value.Description,style:value.Style,fill:value.Fill,kind:value.Type,isDouble:value.IsDouble,
      origin:wire(value.Origin),angle:wire(value.Angle),scale:wire(value.Scale),lines:Array.from(value.LineDefinitions,wire)};
    if (!(value instanceof api.HatchGradientPattern)) return {pattern};
    return {pattern,gradientType:value.GradientType,color1:wire(value.Color1),color2:wire(value.Color2),single:value.SingleColor,
      tint:wire(value.Tint),shift:wire(value.Shift),centered:value.Centered,aci1:wire(value.Color1AciIndex),aci2:wire(value.Color2AciIndex),
      auto1:value.IsColor1AciIndexAutomatic,auto2:value.IsColor2AciIndexAutomatic};
  }
  if (value instanceof api.XDataRecord) return {type:'XDataRecord',code:value.Code,value:wire(value.Value)};
  if (value instanceof api.DxfClass) return {type:'DxfClass',name:value.Name,cpp:value.CppClassName,application:value.ApplicationName,flags:value.ProxyFlags,count:value.InstanceCount,wasProxy:value.WasProxy,entity:value.IsEntity};
  if (value instanceof api.Color) return {type:'Color',argb:value.ToArgb(),name:value.Name,known:value.IsKnownColor,named: value.IsNamedColor,empty:value.IsEmpty};
  if (value instanceof api.Transparency) return {type,value:value.Value,stored:value.StoredAlphaValue,byLayer:value.IsByLayer,byBlock:value.IsByBlock};
  const layoutViewport=layoutViewportValueWire(value,wire);if(layoutViewport!==undefined)return layoutViewport;
  const block=blockWire(value,wire);if(block!==undefined)return block;
  const group=groupWire(value,wire); if(group!==undefined)return group;
  const output=outputSettingsWire(value,wire); if(output!==undefined)return output;
  const mline=mlineValueWire(value,wire);if(mline!==undefined)return mline;
  const boundary=hatchBoundaryWire(value,wire); if(boundary!==undefined)return boundary;
  const surface=surfaceWire(value,wire); if(surface!==undefined)return surface;
  const model=databaseModelWire(value,wire); if(model!==undefined)return model;
  const coordinate=coordinateWire(value,wire); if(coordinate!==undefined)return coordinate;
  const entity=entityWire(value,wire); if(entity!==undefined)return entity;
  const style=styleWire(value,wire); if(style!==undefined)return style;
  if(value instanceof Map)return Array.from(value,([key,item])=>[wire(key),wire(item)]);
  if(value instanceof api.DxfTag)return {code:value.Code,value:wire(value.Value)};
  if (typeof value[Symbol.iterator] === 'function') return Array.from(value, wire);
  throw new Error('Unmapped geometry result: ' + type);
}

export { wire };
