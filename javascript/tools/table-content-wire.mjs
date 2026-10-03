// Independent input/observation adapter; all behavior belongs to production modules.
import * as api from '../index.js';
import { BoxedScalar } from '../runtime/BoxedScalar.js';
import { DecodeTableText } from '../runtime/TablePayload.js';
import { doubleBits } from './wire.mjs';
import { tableStyleSnapshot } from './table-style-wire.mjs';
import { InvalidOperationException } from '../runtime/Errors.js';
const text=value=>({utf16:Array.from({length:value.length},(_,i)=>value.charCodeAt(i))});
const real=value=>({double:doubleBits(value)});
const ref=value=>value==null?null:{type:value.constructor.name,handle:value.Handle,owner:value.Owner?.Handle??null};
const scalar=(value,kind)=>value instanceof api.Vector3?[real(value.X),real(value.Y),real(value.Z)]:kind===2?real(value):typeof value==='string'?text(value):value;
export function tableContentSnapshot(value){
  if(value==null)return null;
  if(value instanceof api.DxfStoredTableContent)return {kind:'content',common:ref(value),version:value.SourceVersion,erased:value.IsErased,name:value.Name===null?null:text(value.Name),description:value.Description===null?null:text(value.Description),rows:value.RowCount,columns:value.ColumnCount,tags:Array.from(value.Payload,tableStyleSnapshot),subclasses:Array.from(value.Subclasses,tableContentSnapshot),values:Array.from(value.StoredValues,tableContentSnapshot),style:ref(value.TableStyle),references:Array.from(value.References,ref)};
  if(value instanceof api.DxfStoredTableContentSubclass)return {kind:'subclass',name:value.Name,tags:Array.from(value.Tags,tableStyleSnapshot)};
  if(value instanceof api.DxfStoredTableContentValue)return {kind:'value',type:value.Kind,value:scalar(value.Value,value.Kind),index:value.PayloadIndex,flags:value.StoredFormatFlags,unit:value.StoredUnitType,format:value.FormatString===null?null:text(value.FormatString),display:value.FormattedText===null?null:text(value.FormattedText),tags:Array.from(value.Tags,tableStyleSnapshot)};
  if(value instanceof api.DxfStoredTableContentValueEdit)return {kind:'edit',original:tableContentSnapshot(value.Original),value:scalar(value.Value,value.Original.Kind),display:value.FormattedText===null?null:text(value.FormattedText)};
  if(typeof value==='object'&&value[Symbol.iterator])return Array.from(value,tableContentSnapshot);
  return tableStyleSnapshot(value);
}
function tagValue(input,read){
  const value=read(input);
  if(input && typeof input==='object'){
    const kind=['int','double','short','byte','long'].find(key=>Object.hasOwn(input,key));
    if(kind)return new BoxedScalar({int:'Int32',double:'Double',short:'Int16',byte:'Byte',long:'Int64'}[kind],value);
  }
  return value;
}
export function tableContentLoad(step,document,read){
  const tags=step.tags.map(([code,value])=>new api.DxfTag(code,value&&typeof value==='object'&&'handleOf'in value?'0'.repeat(value.pad??0)+(value.lower?read({ref:value.handleOf}).Handle.toLowerCase():read({ref:value.handleOf}).Handle):tagValue(value,read)));
  const content=new api.DxfStoredTableContent(document,tags,DecodeTableText);
  if(step.register!==false){const parent=step.owner?read(step.owner):document.NamedObjects;
    if(parent instanceof api.DxfDictionary)parent.Add(step.name??'CONTENT_FIXTURE',content);
    else{content.Owner=parent;document.Objects.Register(content,false);}
    if(step.resolve!==false)content.Resolve(handle=>document.StoredTableHandleTarget(handle));
  }
  return content;
}
export function tableContentWith(step,target,read){
  let value=read(step.value);
  if(step.value&&typeof step.value==='object'){
    const kind=['int','double','short','byte','long'].find(k=>Object.hasOwn(step.value,k));
    if(kind)value=new BoxedScalar({int:'Int32',double:'Double',short:'Int16',byte:'Byte',long:'Int64'}[kind],value);
  }
  return target.WithValue(value,step.display===undefined?null:read(step.display));
}
export function tableContentCall(step,target,read,values){
  const members=step.members===null?null:step.members.map(read),count=step.repeat??members?.length??0,counters=[0,0,0,0,0];
  if(step.log)values.set(step.log,counters);
  const name=read(step.name),description=read(step.description),style=step.style?read(step.style):null;
  const invoke=source=>{target.ReplaceContent(name,description,style,source);return null;};
  function hook(stage){for(const action of step.hooks??[]){
    if(action.stage!==stage)continue;
    if(action.kind==='throw')throw new InvalidOperationException('Injected caller failure.');
    if(action.kind==='reenter'||action.kind==='catch-reenter'){try{invoke([]);}catch(error){if(action.kind==='reenter')throw error;counters[4]++;}}
    else{const subject=read(action.target);if(action.kind==='set')subject[action.member]=read(action.value);else subject[action.member](...(action.args??[]).map(read));}
  }}
  const source=members===null?null:{GetEnumerator(){counters[0]++;hook('get');if(step.nullEnumerator)return null;let at=-1;return {MoveNext(){counters[1]++;hook('move');return ++at<count;},get Current(){counters[2]++;hook('current');return members[at%members.length];},Dispose(){counters[3]++;hook('dispose');}};}};
  return invoke(source);
}
