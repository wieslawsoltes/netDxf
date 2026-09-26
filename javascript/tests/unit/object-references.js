import test from 'node:test';
import assert from 'node:assert/strict';
import { DxfObject, DxfObjectReference, DxfObjectReferences, Layer, Line, VPort } from '../../index.js';
import { ArgumentNullException, NullReferenceException, InvalidOperationException } from '../../runtime/Errors.js';
import { objectReferenceCorpus } from '../../tools/object-reference-corpus.mjs';
const snapshot=refs=>Array.from(refs.ToList(),entry=>[entry.Reference,entry.Uses]);

test('reference accounting standalone export is the package class and starts empty', async()=>{
  const { DxfObjectReferences: Standalone }=await import('../../netDxf/Collections/DxfObjectReferences.js');
  assert.equal(Standalone,DxfObjectReferences);const refs=new Standalone();
  assert.equal(refs.IsEmpty(),true);assert.equal(refs.ToList().Count,0);
});
test('repeated references increment and decrement, deleting only at exactly zero',()=>{
  const refs=new DxfObjectReferences(),item=new Line();refs.Add(item);refs.Add(item);
  assert.deepEqual(snapshot(refs),[[item,2]]);assert.equal(refs.Remove(item),true);
  assert.deepEqual(snapshot(refs),[[item,1]]);assert.equal(refs.Remove(item),true);
  assert.equal(refs.IsEmpty(),true);assert.equal(refs.Remove(item),false);
});
test('reference identity mode retains equal named table objects independently',()=>{
  const a=new Layer('Shared'),b=new Layer('Shared'),byValue=new DxfObjectReferences(),byIdentity=new DxfObjectReferences(true);
  for(const refs of [byValue,byIdentity]){refs.Add(a);refs.Add(b);}
  assert.deepEqual(snapshot(byValue),[[a,2]]);assert.deepEqual(snapshot(byIdentity),[[a,1],[b,1]]);
});
test('default reference accounting preserves VPORT identity semantics',()=>{
  const a=new VPort('View'),b=new VPort('View'),refs=new DxfObjectReferences();refs.Add(a);refs.Add(b);refs.Add(a);
  assert.deepEqual(snapshot(refs),[[a,2],[b,1]]);
});
test('zero and negative bulk counts are retained, not normalized or discarded',()=>{
  const refs=new DxfObjectReferences(),a=new Line(),b=new Line();
  refs.Add([new DxfObjectReference(a,0),new DxfObjectReference(b,-2)]);
  assert.equal(refs.IsEmpty(),false);assert.deepEqual(snapshot(refs),[[a,0],[b,-2]]);
  refs.Remove(a);refs.Add(b);assert.deepEqual(snapshot(refs),[[a,-1],[b,-1]]);
  refs.Add(b);assert.deepEqual(snapshot(refs),[[a,-1],[b,0]]);
});
test('reference counters preserve unchecked signed Int32 wrapping',()=>{
  const refs=new DxfObjectReferences(),item=new Line();refs.Add([new DxfObjectReference(item,2147483647)]);
  refs.Add(item);assert.equal(refs.ToList().get_Item(0).Uses,-2147483648);
  refs.Remove(item);assert.equal(refs.ToList().get_Item(0).Uses,2147483647);
  refs.Add([new DxfObjectReference(item,2147483647)]);assert.equal(refs.ToList().get_Item(0).Uses,-2);
});
test('bulk input nulls retain earlier changes and stop before later entries',()=>{
  const refs=new DxfObjectReferences(),a=new Line(),b=new Line();
  assert.throws(()=>refs.Add([new DxfObjectReference(a,2),null,new DxfObjectReference(b,3)]),NullReferenceException);
  assert.deepEqual(snapshot(refs),[[a,2]]);
  assert.throws(()=>refs.Add([new DxfObjectReference(null,1)]),{name:'ArgumentNullException',ParamName:'key'});
  assert.deepEqual(snapshot(refs),[[a,2]]);
});
test('erased null overloads preserve distinct single-reference and enumerable failures',()=>{
  const refs=new DxfObjectReferences();assert.throws(()=>refs.Add(null),ArgumentNullException);
  assert.throws(()=>refs.Add(null,true),NullReferenceException);assert.throws(()=>refs.Remove(null),ArgumentNullException);
  assert.equal(refs.IsEmpty(),true);
});
test('ToList returns independent lists and immutable entries but retains referenced object identity',()=>{
  const refs=new DxfObjectReferences(),item=new Line();refs.Add(item);
  const first=refs.ToList(),second=refs.ToList();assert.notEqual(first,second);assert.notEqual(first.get_Item(0),second.get_Item(0));
  assert.equal(first.get_Item(0).Reference,item);refs.Add(item);assert.equal(first.get_Item(0).Uses,1);
  first.Clear();assert.equal(refs.ToList().get_Item(0).Uses,2);assert.equal(second.Count,1);
  assert.throws(()=>{second.get_Item(0).Uses=9;},TypeError);
});
test('default lookup respects a changed source hash while explicit identity survives rename',()=>{
  for(const identity of [false,true]){const refs=new DxfObjectReferences(identity),item=new Layer('Original');refs.Add(item);item.Name='Renamed';
    assert.equal(refs.Remove(item),identity);assert.equal(refs.IsEmpty(),identity);}
});
test('reference snapshots retain dictionary freed-slot order',()=>{
  const refs=new DxfObjectReferences(),items=Array.from({length:5},()=>new Line());
  for(const item of items.slice(0,3))refs.Add(item);refs.Remove(items[1]);refs.Remove(items[0]);refs.Add(items[3]);refs.Add(items[4]);
  assert.deepEqual(snapshot(refs).map(([item])=>item),[items[3],items[4],items[2]]);
});
test('bulk enumeration is lazy and does not roll back successfully visited inputs',()=>{
  const refs=new DxfObjectReferences(),item=new Line();
  function* entries(){yield new DxfObjectReference(item,3);throw new InvalidOperationException('enumeration failed');}
  assert.throws(()=>refs.Add(entries()),InvalidOperationException);assert.deepEqual(snapshot(refs),[[item,3]]);
});
test('explicit identity comparer never invokes user Equals or GetHashCode',()=>{
  class Hostile extends DxfObject{constructor(){super('HOSTILE');}Equals(){throw new Error('equals');}GetHashCode(){throw new Error('hash');}}
  const refs=new DxfObjectReferences(true),a=new Hostile(),b=new Hostile();refs.Add(a);refs.Add(a);refs.Add(b);
  assert.deepEqual(snapshot(refs),[[a,2],[b,1]]);assert.equal(refs.Remove(a),true);
});
test('reference accounting input corpus is deterministic with all expected scenarios retained',()=>{
  const cases=objectReferenceCorpus();assert.deepEqual(cases,objectReferenceCorpus());
  assert.equal(cases.length,122);assert.equal(new Set(cases.map(item=>item.name)).size,122);
  assert.equal(cases.reduce((n,item)=>n+item.request.steps.length,0),20428);assert.ok(cases.every(item=>!Object.hasOwn(item,'expected')));
});
