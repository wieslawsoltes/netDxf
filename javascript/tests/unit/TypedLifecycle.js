import test from 'node:test';
import assert from 'node:assert/strict';
import { ApplicationRegistry, XData, XDataRecord, XDataCode, XDataDictionary, DxfObject, TableObject, TableObjectChangedEventArgs, Vector3 } from '../../index.js';
import { StringDictionary } from '../../runtime/StringDictionary.js';
import { ReferenceList } from '../../runtime/ReferenceList.js';
import { InvalidOperationException } from '../../runtime/Errors.js';
const data=r=>{const x=new XData(r);x.XDataRecord.Add(new XDataRecord(XDataCode.BinaryData,Uint8Array.of(1,2)));return x;};
test('typed lifecycle / clone walks a 12000-registry cycle iteratively',()=>{
  const first=new ApplicationRegistry('R0');let previous=first;
  for(let i=1;i<12000;i++){const next=new ApplicationRegistry('R'+i);previous.XData.Add(data(next));previous=next;}
  previous.XData.Add(data(first));const copy=first.Clone('COPY');let node=copy;
  for(let i=1;i<12000;i++)node=node.XData.get_Item('R'+i).ApplicationRegistry;
  assert.equal(node.XData.get_Item('COPY').ApplicationRegistry,copy);assert.equal(previous.XData.get_Item('R0').ApplicationRegistry,first);
});
test('typed lifecycle / stored clones bypass public overrides, public Clone dispatches',()=>{
  let calls=0;class Registry extends ApplicationRegistry {Clone(...args){if(args.length===0)calls++;return super.Clone(...args);}}
  const x=data(new Registry('A')),a=new XDataDictionary(),b=new XDataDictionary();a.Add(x);b.Add(x);assert.equal(calls,0);x.Clone();assert.equal(calls,1);assert.notEqual(a.get_Item('A'),b.get_Item('A'));
});
test('typed lifecycle / readonly dictionary views are live and preserve freed-slot order',()=>{
  const d=new StringDictionary(),keys=d.Keys;d.Add('A',1);d.Add('B',2);d.Add('C',3);d.Remove('B');d.Add('D',4);assert.deepEqual(Array.from(keys),['A','D','C']);assert.equal(keys.Count,3);assert.throws(()=>keys.Clear());
});
test('typed lifecycle / reference list self-add is finite and enumerators are invalidated',()=>{
  const d=new ReferenceList([1,2]);d.AddRange(d);assert.deepEqual(d.ToArray(),[1,2,1,2]);const e=d.GetEnumerator();d.set_Item(0,3);assert.throws(()=>e.MoveNext(),InvalidOperationException);
});
test('typed lifecycle / callbacks see old name; mutating event NewValue is ignored by source',()=>{
  const r=new ApplicationRegistry('OLD');r.NameChanged.Add((sender,e)=>{assert.equal(sender.Name,'OLD');e.NewValue='IGNORED';});r.Name='NEW';assert.equal(r.Name,'NEW');
});
test('typed lifecycle / internal handle assignment preserves signed Int64 rollover',()=>{
  class Item extends DxfObject{}const obj=new Item('CARRIER');assert.equal(obj.AssignHandle(9223372036854775807n),-9223372036854775808n);assert.equal(obj.Handle,'7FFFFFFFFFFFFFFF');obj.AssignHandle(-1n);assert.equal(obj.Handle,'FFFFFFFFFFFFFFFF');
});
test('typed lifecycle / event generic struct payloads are copied',()=>{
  const point=new Vector3(1,2,3),e=new TableObjectChangedEventArgs(point,point);point.X=99;const read=e.NewValue;read.X=12;assert.equal(e.NewValue.X,1);assert.equal(e.OldValue.X,1);
});
test('typed lifecycle / string validation retains source constructor-versus-setter differences',()=>{
  assert.equal(TableObject.IsValidName(' '),true);assert.throws(()=>new ApplicationRegistry(' '));const r=new ApplicationRegistry(' A ');assert.equal(r.Name,'A');r.Name=' NEW ';assert.equal(r.Name,' NEW ');assert.equal(new ApplicationRegistry(' ACAD ').IsReserved,false);
});
