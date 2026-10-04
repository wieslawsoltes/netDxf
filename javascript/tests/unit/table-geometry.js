import test from 'node:test';
import assert from 'node:assert/strict';
import * as api from '../../index.js';
import { tableGeometryPacket, tableGeometryCorpus } from '../../tools/table-geometry-corpus.mjs';
import { fromBits } from '../../tools/wire.mjs';
const {DxfStoredTableGeometry:G,DxfStoredTableGeometryCell:C,DxfStoredTableCellGeometry:V,Vector3,DxfDocument,DxfTag}=api;
const scalar = () => new V(new Vector3(-1,2,3),new Vector3(4,5,6),-7,8,9,-10,-2147483648);
const cell = (reference=null) => new C(-2147483648,-0,-20,reference,[scalar()]);
function packet(options) { return tableGeometryPacket(options).map(([code,value]) => new DxfTag(code,value?.double?fromBits(value.double):value?.int??value)); }
function fixture(options) { const doc=new DxfDocument(api.DxfVersion.AutoCad2018), geometry=new G(doc,packet(options));doc.NamedObjects.Add('geometry',geometry);geometry.Resolve(h=>doc.StoredTableHandleTarget(h));return {doc,geometry}; }
const state = (doc, geometry) => [geometry.Payload,geometry.Cells,doc.NumHandles,doc.AddedObjects.Count];

test('geometry exports the original-path classes and immutable value packets',async()=>{
  const module=await import('../../netDxf/Objects/DxfStoredTableGeometry.js');assert.equal(module.DxfStoredTableGeometry,G);
  const c=cell();for(const key of ['GeometryDataFlags','WidthWithGap','HeightWithGap','Geometry'])assert.throws(()=>{c[key]=null;},TypeError);
  assert.throws(()=>c.Geometry.Clear(),{name:'NotSupportedException'});
});
test('geometry vector inputs and every vector getter use independent value copies',()=>{
  const a=new Vector3(1,2,3),b=new Vector3(4,5,6),value=new V(a,b,1,2,3,4,5);a.X=99;b.Y=99;value.TopLeftDistance.Y=99;value.CenterDistance.Z=99;
  assert.deepEqual(value.TopLeftDistance.ToArray(),[1,2,3]);assert.deepEqual(value.CenterDistance.ToArray(),[4,5,6]);
});
test('geometry public value accepts finite signed dimensions and retains negative zero',()=>{
  const v=new V(new Vector3(-0,1,2),Vector3.Zero,-0,-1,Number.MAX_VALUE,-Number.MIN_VALUE,-2147483648);
  assert.ok(Object.is(v.TopLeftDistance.X,-0));assert.ok(Object.is(v.ContentWidth,-0));assert.equal(v.Height,-Number.MIN_VALUE);
});
test('geometry constructor consumes and disposes the source before exposing its copy',()=>{
  const log=[],source={GetEnumerator(){log.push('get');let i=0;return {MoveNext(){log.push('move');return i++===0;},get Current(){log.push('current');return scalar();},Dispose(){log.push('dispose');}};}};
  const c=new C(0,0,0,null,source);assert.equal(c.Geometry.Count,1);assert.deepEqual(log,['get','move','current','move','dispose']);
});
test('geometry constructor does not suppress a disposal failure',()=>{
  const error=new Error('dispose'),source={GetEnumerator(){return {MoveNext(){return false;},Dispose(){throw error;}};}};
  assert.throws(()=>new C(0,0,0,null,source),e=>e===error);
});
test('geometry parser rejects reordered counts trailing data and nonfinite packet values',()=>{
  const doc=new DxfDocument();for(const change of [tags=>tags.reverse(),tags=>tags.push(new DxfTag(1,'extra'))]){
    const tags=packet();change(tags);assert.throws(()=>new G(doc,tags),{name:'FormatException'});
  }
  assert.throws(()=>new DxfTag(40,Infinity),{name:'ArgumentOutOfRangeException'});
});
test('geometry parsed values retain independent counts rather than inferring grid addresses',()=>{
  const {geometry}=fixture({rows:123,columns:456,cells:2,count:0});assert.equal(geometry.Cells.Count,2);assert.equal(geometry.RowCount,123);assert.equal(geometry.ColumnCount,456);
});
test('geometry no-op retains payload and cells snapshots without allocating handles',()=>{
  const {doc,geometry}=fixture(),before=state(doc,geometry);geometry.ReplaceGeometry(2,3,[cell()]);assert.deepEqual(state(doc,geometry),before);
});
test('geometry exact zero-sign changes are edits rather than approximate no-ops',()=>{
  const {doc,geometry}=fixture(),before=geometry.Payload,c=new C(-2147483648,0,-20,null,[scalar()]);geometry.ReplaceGeometry(2,3,[c]);
  assert.notEqual(geometry.Payload,before);assert.ok(Object.is(geometry.Payload.get_Item(5).Value,0));assert.ok(Object.is(before.get_Item(5).Value,-0));
});
test('geometry changes preserve old snapshots and allow repeated cell identities',()=>{
  const {doc,geometry}=fixture(),old=geometry.Cells,oldTags=geometry.Payload,oldRefs=geometry.References,seed=doc.NumHandles,c=cell();
  geometry.ReplaceGeometry(777,888,[c,c]);assert.equal(old.Count,1);assert.equal(oldTags.Count,20);assert.equal(oldRefs.Count,0);assert.equal(doc.NumHandles,seed);assert.equal(geometry.Cells.get_Item(0),c);assert.equal(geometry.Cells.get_Item(1),c);
});
test('geometry rejected input finishes disposal and leaves source state unchanged',()=>{
  const {doc,geometry}=fixture(),before=state(doc,geometry);let disposed=0;
  const source={GetEnumerator(){return {MoveNext(){return true;},get Current(){return null;},Dispose(){disposed++;}};}};
  assert.throws(()=>geometry.ReplaceGeometry(0,0,source),{name:'ArgumentException',ParamName:'cells'});assert.equal(disposed,1);assert.deepEqual(state(doc,geometry),before);
});
for(const stage of ['get','move','current','dispose'])test('geometry caught reentry poisons outer edit during '+stage,()=>{
  const {doc,geometry}=fixture(),before=state(doc,geometry);let once=false,i=0;
  const hook=name=>{if(name===stage&&!once){once=true;assert.throws(()=>geometry.ReplaceGeometry(0,0,[]),{name:'InvalidOperationException'});}};
  const source={GetEnumerator(){hook('get');return {MoveNext(){hook('move');return i++===0;},get Current(){hook('current');return cell();},Dispose(){hook('dispose');}};}};
  assert.throws(()=>geometry.ReplaceGeometry(2,3,source),{name:'InvalidOperationException'});assert.deepEqual(state(doc,geometry),before);geometry.ReplaceGeometry(0,0,[]);assert.equal(geometry.Cells.Count,0);
});
test('geometry validation observes caller mutations after enumeration without rolling them back',()=>{
  const {doc,geometry}=fixture(),before=geometry.Payload;
  const source={GetEnumerator(){return {MoveNext(){return false;},Dispose(){doc.DrawingVariables.AcadVer=api.DxfVersion.AutoCad2013;}};}};
  assert.throws(()=>geometry.ReplaceGeometry(0,0,source),{name:'InvalidOperationException'});assert.equal(doc.DrawingVariables.AcadVer,api.DxfVersion.AutoCad2013);assert.equal(geometry.Payload,before);
});
test('geometry resolution preserves repeated exact target identities and lexical handle spelling',()=>{
  const {doc,geometry}=fixture();const target=doc.TextStyles.get_Item('Standard');const loaded=new G(doc,packet({reference:'00'+target.Handle.toLowerCase(),cells:2}));doc.NamedObjects.Add('second',loaded);loaded.Resolve(h=>doc.StoredTableHandleTarget(h));
  const before=loaded.References,old=loaded.Cells;loaded.ReplaceGeometry(7,8,old);assert.equal(loaded.Payload.get_Item(7).Value,'00'+target.Handle.toLowerCase());assert.deepEqual([...loaded.References],[target,target]);assert.deepEqual([...before],[target,target]);
});
test('geometry rejects foreign identity even when its name and handle match a local resource',()=>{
  const {doc,geometry}=fixture(),foreign=new DxfDocument(),before=state(doc,geometry);
  assert.throws(()=>geometry.ReplaceGeometry(1,1,[cell(foreign.TextStyles.get_Item('Standard'))]),{name:'ArgumentException',ParamName:'cells'});assert.deepEqual(state(doc,geometry),before);
});
test('geometry references guard owner-held INSERT attributes until explicit release',()=>{
  const {doc,geometry}=fixture(),block=new api.Block('B');block.AttributeDefinitions.Add(new api.AttributeDefinition('TAG'));const insert=new api.Insert(block);doc.Entities.Add(insert);const attribute=insert.Attributes.get_Item(0);
  assert.equal(doc.GetObjectByHandle(attribute.Handle),null);geometry.ReplaceGeometry(1,1,[cell(attribute)]);assert.equal(doc.Entities.Remove(insert),false);geometry.ReplaceGeometry(0,0,[]);assert.equal(doc.Entities.Remove(insert),true);
});
test('geometry references guard nonregistry layout viewports until explicit release',()=>{
  const {doc,geometry}=fixture(),layout=doc.Layouts.Add(new api.Layout('Sheet'));assert.equal(doc.GetObjectByHandle(layout.Viewport.Handle),null);
  geometry.ReplaceGeometry(1,1,[cell(layout.Viewport)]);assert.equal(doc.Layouts.Remove(layout),false);geometry.ReplaceGeometry(0,0,[]);assert.equal(doc.Layouts.Remove(layout),true);
});
test('geometry references release old STYLE dependencies while preserving previous snapshots',()=>{
  const {doc,geometry}=fixture(),a=doc.TextStyles.Add(new api.TextStyle('A','txt.shx')),b=doc.TextStyles.Add(new api.TextStyle('B','txt.shx'));
  geometry.ReplaceGeometry(1,1,[cell(a)]);const old=geometry.References;geometry.ReplaceGeometry(2,1,[cell(b),cell(b)]);
  assert.equal(doc.TextStyles.Remove(a),true);assert.equal(doc.TextStyles.Remove(b),false);assert.deepEqual([...old],[a]);assert.equal(doc.TextStyles.GetReferences(b).get_Item(0).Uses,2);
});
test('geometry generic clone and erasure remain explicitly rejected',()=>{
  const {doc,geometry}=fixture();assert.throws(()=>doc.Objects.CloneObject(geometry,doc.NamedObjects,'copy'),{name:'NotSupportedException'});assert.throws(()=>doc.Objects.EraseOwnedTree(geometry),{name:'NotSupportedException'});
});
test('geometry exact packet limit includes distinct reactors and binary-XData chunks',()=>{
  const {doc,geometry}=fixture();geometry.PersistentReactors.Add(doc.NamedObjects);geometry.PersistentReactors.Add(doc.NamedObjects);
  const data=new api.XData(new api.ApplicationRegistry('META'));data.XDataRecord.Add(new api.XDataRecord(api.XDataCode.BinaryData,new Uint8Array(127)));data.XDataRecord.Add(new api.XDataRecord(api.XDataCode.BinaryData,new Uint8Array(1)));geometry.XData.Add(data);
  // 4 + 209713 * 5 = 1,048,569 packet tags; +2 identity/owner +3 reactors
  // +1 APPID +2 binary chunks = 1,048,577: one tag over the record limit.
  let i=0;const empty=new C(0,0,0,null,[]),source={GetEnumerator(){return{MoveNext(){return i++<209713;},get Current(){return empty;},Dispose(){}};}};
  const before=geometry.Payload;assert.throws(()=>geometry.ReplaceGeometry(0,0,source),{name:'ArgumentException',ParamName:'cells'});assert.equal(geometry.Payload,before);
});
test('geometry corpus includes stable identities and exact input totals without expected values',()=>{
  const corpus=tableGeometryCorpus();assert.deepEqual(corpus,tableGeometryCorpus());assert.equal(corpus.length,270);assert.equal(new Set(corpus.map(p=>p.name)).size,270);assert.equal(corpus.reduce((n,p)=>n+p.request.steps.length,0),3966);assert.ok(corpus.every(p=>!Object.hasOwn(p,'expected')));
});
