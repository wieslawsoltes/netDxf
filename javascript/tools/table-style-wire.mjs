// Test-only retained-constructor and enumerable adapters. All edits use production APIs.
import * as api from '../index.js';
import { DecodeTableText } from '../runtime/TablePayload.js';
import { doubleBits } from './wire.mjs';
import { InvalidOperationException } from '../runtime/Errors.js';
const ref=item=>item===null?null:{type:item.constructor.name,handle:item.Handle,owner:item.Owner?.Handle??null};
const text=value=>({utf16:Array.from({length:value.length},(_,i)=>value.charCodeAt(i))});
const real=value=>({double:doubleBits(value)});
export function tableStyleSnapshot(value) {
  if(value==null)return null;
  if(value instanceof api.DxfTableStyle)return {kind:'style',common:ref(value),version:value.SourceVersion,erased:value.IsErased,
    tags:Array.from(value.Tags,tableStyleSnapshot),header:tableStyleSnapshot(value.Header),rows:Array.from(value.Rows,tableStyleSnapshot),
    references:Array.from(value.References,ref),map:ref(value.CellStyleMap),typedMap:ref(value.StoredCellStyleMap)};
  if(value instanceof api.DxfTableStyleHeader)return {kind:'header',description:text(value.Description),flow:value.FlowDirection,flags:value.StoredFlags,
    horizontal:real(value.HorizontalCellMargin),vertical:real(value.VerticalCellMargin),title:value.SuppressTitle,heading:value.SuppressColumnHeading,version:value.StoredVersion};
  if(value instanceof api.DxfTableStyleRow)return {kind:'row',name:text(value.StoredTextStyleName),style:ref(value.TextStyle),values:tableStyleSnapshot(value.Values),borders:tableStyleSnapshot(value.Borders),data:tableStyleSnapshot(value.DataTypes),tags:Array.from(value.Tags,tableStyleSnapshot)};
  if(value instanceof api.DxfTableStyleRowValues)return {kind:'scalars',height:real(value.TextHeight),alignment:value.CellAlignment,color:value.StoredTextColor,fill:value.StoredFillColor,background:value.BackgroundColorEnabled};
  if(value instanceof api.DxfTableStyleBorderValues)return {kind:'border',lineweight:value.StoredLineweight,visible:value.IsVisible,color:value.StoredColor};
  if(value instanceof api.DxfTableStyleRowBorders)return {kind:'borders',values:Array.from(value.Values,tableStyleSnapshot)};
  if(value instanceof api.DxfTableStyleRowDataTypes)return {kind:'data',data:value.StoredDataType,unit:value.StoredUnitType};
  if(value instanceof api.DxfTableStyleRowEdit)return {kind:'edit',original:tableStyleSnapshot(value.Original),values:tableStyleSnapshot(value.Values),borders:tableStyleSnapshot(value.Borders),data:tableStyleSnapshot(value.DataTypes),style:ref(value.TextStyle)};
  if(value instanceof api.DxfStoredCellStyleMap)return {kind:'map',common:ref(value),version:value.SourceVersion,erased:value.IsErased,payload:Array.from(value.Payload,tableStyleSnapshot),entries:Array.from(value.Entries,tableStyleSnapshot),references:Array.from(value.References,ref)};
  if(value instanceof api.DxfStoredCellStyleMapEntry)return {kind:'entry',id:value.Id,type:value.StoredType,name:text(value.Name),format:Array.from(value.FormatPayload,tableStyleSnapshot)};
  if(value instanceof api.DxfTag)return {code:value.Code,value:value.ValueType===api.DxfTagValueType.Double?real(value.Value):value.ValueType===api.DxfTagValueType.BinaryData?Array.from(value.Value):typeof value.Value==='string'?text(value.Value):value.Value};
  if(value instanceof api.DxfObject)return ref(value);
  if(typeof value==='object'&&value[Symbol.iterator])return Array.from(value,tableStyleSnapshot);
  return value;
}
export function tableStyleLoad(step,document,read) {
  const tags=step.tags.map(row=>new api.DxfTag(row[0],row[1]&&typeof row[1]==='object'&&'handleOf'in row[1]?(('0'.repeat(row[1].pad??0))+(row[1].lower?read({ref:row[1].handleOf}).Handle.toLowerCase():read({ref:row[1].handleOf}).Handle)):read(row[1]))),Type=step.kind==='map'?api.DxfStoredCellStyleMap:api.DxfTableStyle;
  const value=new Type(document,tags,DecodeTableText);
  if(step.register!==false) {
    const parent=step.owner?read(step.owner):document.NamedObjects;parent.Add(step.name??'TABLE_FIXTURE',value);
    if(step.resolve!==false)value.Resolve(handle=>document.StoredTableHandleTarget(handle),DecodeTableText);
  }
  return value;
}
export function tableStyleCall(step,target,read,values) {
  const members=step.members===null?null:step.members.map(read),count=step.repeat??members?.length??0,counters=[0,0,0,0,0];
  if(step.log)values.set(step.log,counters);
  const header=step.header?read(step.header):null;
  function invoke(source){if(step.action==='style'){target.ReplaceStyle(header,source);return null;}if(step.action==='map'){target.ReplaceEntryNames(source);return null;}return new api.DxfTableStyleRowBorders(source);}
  function hook(stage) {
    for(const action of step.hooks??[]) {
      if(action.stage!==stage)continue;
      if(action.kind==='throw')throw new InvalidOperationException('Injected caller failure.');
      if(action.kind==='reenter'||action.kind==='catch-reenter') {try{invoke([]);}catch(error){if(action.kind==='reenter')throw error;counters[4]++;}}
      else {const subject=read(action.target);if(action.kind==='set')subject[action.member]=read(action.value);else subject[action.member](...(action.args??[]).map(read));}
    }
  }
  const source=members===null?null:{GetEnumerator(){counters[0]++;hook('get');if(step.nullEnumerator)return null;let at=-1;return{
    MoveNext(){counters[1]++;hook('move');return ++at<count;},get Current(){counters[2]++;hook('current');return members[at%members.length];},Dispose(){counters[3]++;hook('dispose');}
  };}};
  return invoke(source);
}
