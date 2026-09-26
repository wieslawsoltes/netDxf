// Retained fixture adapter; no production mutation or expected-output logic.
import * as api from '../index.js';
import { DecodeTableText } from '../runtime/TablePayload.js';
import { BoxedScalar } from '../runtime/BoxedScalar.js';
import { doubleBits } from './wire.mjs';
import { tableStyleSnapshot } from './table-style-wire.mjs';
import { tableContentSnapshot } from './table-content-wire.mjs';
const real=value=>({double:doubleBits(value)}), point=v=>[real(v.X),real(v.Y),real(v.Z)],ref=value=>value==null?null:{type:value.constructor.name,handle:value.Handle,owner:value.Owner?.Handle??null};
const text=value=>({utf16:Array.from({length:value.length},(_,i)=>value.charCodeAt(i))});
export function storedTableSnapshot(value){
  if(value==null)return null;
  if(value instanceof api.StoredTable)return {kind:'table',common:ref(value),version:value.SourceVersion,normal:point(value.Normal),position:point(value.Position),grid:storedTableSnapshot(value.Grid),tags:Array.from(value.Payload,tableStyleSnapshot),references:Array.from(value.References,ref),backing:ref(value.BackingContent),typedBacking:ref(value.StoredBackingContent),agreement:value.BackingLiteralValuesAgree,resourceNames:Array.from(value.Payload,tag=>value.ChangedResourceName(tag))};
  if(value instanceof api.StoredTableGrid)return {kind:'grid',rows:value.RowCount,columns:value.ColumnCount,heights:Array.from(value.RowHeights,real),widths:Array.from(value.ColumnWidths,real),cells:Array.from(value.Cells,storedTableSnapshot)};
  if(value instanceof api.StoredTableCell)return {kind:'cell',type:value.StoredType,field:value.HasFieldReference,valueType:value.ValueType,flags:value.StoredFlags,hasValue:value.HasLiteralValue,value:value.LiteralValue===null?null:typeof value.LiteralValue==='string'?text(value.LiteralValue):value.ValueType===2?real(value.LiteralValue):value.LiteralValue,tags:Array.from(value.Tags,tableStyleSnapshot)};
  if(typeof value==='object'&&value[Symbol.iterator])return Array.from(value,storedTableSnapshot);
  return tableContentSnapshot(value);
}
export function storedTableLoad(step,document,read){
  const tags=step.tags.map(([code,input])=>{
    let value;
    if(input&&typeof input==='object'&&'handleOf'in input)value='0'.repeat(input.pad??0)+(input.lower?read({ref:input.handleOf}).Handle.toLowerCase():read({ref:input.handleOf}).Handle);
    else { value=read(input);if(input&&typeof input==='object'){const kind=['int','double','short','byte','long'].find(k=>Object.hasOwn(input,k));if(kind)value=new BoxedScalar({int:'Int32',double:'Double',short:'Int16',byte:'Byte',long:'Int64'}[kind],value);} }
    return new api.DxfTag(code,value);
  });
  const table=new api.StoredTable(document,tags,DecodeTableText);
  if(step.block)read(step.block).Entities.Add(table);else if(step.register!==false)document.Entities.Add(table);
  if(step.resolve!==false)table.Resolve();return table;
}
