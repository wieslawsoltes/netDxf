import {mleaderWire} from './mleader-wire.mjs';
// Observation only: no geometry or style algorithms are implemented here.
import * as api from '../index.js';
import {BoxedScalar} from '../runtime/BoxedScalar.js';
import {dimensionSchema} from './dimension-schema.mjs';
const valueType=v=>v==null?null:v instanceof BoxedScalar||v instanceof api.HeaderEnum||v instanceof api.BoxedChar?v.Type:({number:'Double',boolean:'Boolean',string:'String',bigint:'Int64'}[typeof v]??v.constructor.name);
export function dimensionWire(value,wire){
  const mleader=mleaderWire(value,wire);if(mleader!==undefined)return mleader;
  if(value instanceof api.BoxedChar)return {charCode:value.Value.charCodeAt(0)};
  if(value instanceof api.DimensionStyleOverride)return {type:'DimensionStyleOverride',kind:value.Type,valueType:valueType(value.Value),value:wire(value.Value)};
  if(value instanceof api.DimensionStyleOverrideChangeEventArgs)return {type:'DimensionStyleOverrideChangeEventArgs',item:wire(value.Item)};
  if(value instanceof api.DimensionStyleOverrideDictionaryEventArgs)return {type:'DimensionStyleOverrideDictionaryEventArgs',item:wire(value.Item),cancel:value.Cancel};
  if(value instanceof api.DimensionStyleOverrideDictionary)return {type:'DimensionStyleOverrideDictionary',items:Array.from(value,wire)};
  for(const [name,properties] of Object.entries(dimensionSchema))if(value instanceof api[name]){
    const fields={};for(const [key,type]of properties)fields[key]=type==='char'?{charCode:value[key].charCodeAt(0)}:wire(value[key]);
    if(value instanceof api.DimensionStyle)return {type:name,fields,name:value.Name,code:value.CodeName,handle:value.Handle,owner:value.Owner?.CodeName??null,reserved:value.IsReserved,xdata:Array.from(value.XData.Values,wire)};
    return {type:name,fields};
  }
}
