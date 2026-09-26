import test from 'node:test';
import assert from 'node:assert/strict';
import * as api from '../../index.js';
import { createRetainedParent, retainedSnapshot } from '../../tools/retained-polyline-wire.mjs';
import { retainedPolylineCorpus } from '../../tools/retained-polyline-corpus.mjs';
import { RetainedHandleValue } from '../../runtime/RetainedPolylineRegistration.js';
const document=()=>new api.DxfDocument(api.DxfVersion.AutoCad2018);
function fixture(family='Polyline3D',options={}) {
  const doc=document(),parent=createRetainedParent({family,...options});doc.Entities.Add(parent);return {doc,parent};
}
for(const family of ['Polyline3D','PolygonMesh','PolyfaceMesh','Polyline2D']) {
  test(`${family} registers its actual retained child identities and resource references`,()=>{
    const {doc,parent}=fixture(family);
    for(const record of parent.StoredRecords){
      assert.equal(doc.GetObjectByHandle(record.Handle),record);assert.equal(record.Owner,parent);
      assert.equal(record.StoredOwner,parent);assert.equal(record.SourceDocument,doc);
      assert.equal(record.Linetype,doc.Linetypes.get_Item('StoredLine'));
    }
    const child=parent.VertexRecords.get_Item(0),uses=doc.Layers.GetReferences('Stored');
    assert.ok(Array.from(uses).some(r=>r.Reference===child&&r.Uses===1));
    assert.equal(doc.Layers.Remove('Stored'),false);
  });
  test(`${family} removal and same-document re-adoption preserve children and change parent handles`,()=>{
    const {doc,parent}=fixture(family,{blockOwner:true});const records=Array.from(parent.StoredRecords),handles=records.map(r=>r.Handle),old=parent.Handle;
    assert.equal(doc.Entities.Remove(parent),true);assert.equal(parent.Handle,null);
    for(let i=0;i<records.length;i++){assert.equal(records[i].Handle,handles[i]);assert.equal(records[i].Owner,parent);assert.equal(doc.GetObjectByHandle(handles[i]),null);}
    const block=doc.Blocks.Add(new api.Block('Destination'));block.Entities.Add(parent);
    assert.notEqual(parent.Handle,old);
    for(const record of records){assert.equal(record.StoredOwner,block.Record);assert.equal(doc.GetObjectByHandle(record.Handle),record);}
  });
  test(`${family} validates source profiles and bound-document ownership before adoption`,()=>{
    const {doc,parent}=fixture(family);assert.equal(doc.Entities.Remove(parent),true);
    const other=document(),before=other.DrawingVariables.HandleSeed;
    assert.throws(()=>other.Entities.Add(parent),{name:'NotSupportedException'});
    assert.equal(other.DrawingVariables.HandleSeed,before);assert.equal(Array.from(other.Entities.All).length,0);
    const fresh=createRetainedParent({family,version:13}),seed=doc.DrawingVariables.HandleSeed;
    assert.throws(()=>doc.Entities.Add(fresh),{name:'NotSupportedException'});
    assert.equal(doc.DrawingVariables.HandleSeed,seed);assert.equal(fresh.Handle,null);
  });
}
test('retained insertion creates one new identity with the current parent layer and no inherited metadata',()=>{
  const {doc,parent}=fixture(),before=doc.NumHandles,end=parent.EndSequenceRecord,records=Array.from(parent.VertexRecords);
  parent.Layer=new api.Layer('Now');const expected=doc.NumHandles;
  parent.InsertVertex(2,new api.Vector3(11,12,13));const inserted=parent.VertexRecords.get_Item(2);
  assert.equal(doc.NumHandles,expected+1n);assert.equal(inserted.IsAuthored,true);assert.equal(inserted.Layer,parent.Layer);
  assert.equal(inserted.UsesBlockRecordOwner,false);assert.equal(inserted.SourceVersion,end.SourceVersion);
  assert.equal(inserted.XData.Count,0);assert.equal(inserted.PersistentReactors.Count,0);assert.equal(inserted.ExtensionDictionary,null);
  assert.equal(parent.EndSequenceRecord,end);assert.deepEqual(Array.from(parent.VertexRecords).filter(r=>r!==inserted),records);
  assert.ok(doc.NumHandles>before);
});
test('retained insertion reserves metadata handles that are absent from AddedObjects',()=>{
  const {doc,parent}=fixture(),block=new api.Block('Attributes');block.AttributeDefinitions.Add(new api.AttributeDefinition('TAG'));
  const insert=new api.Insert(block);doc.Entities.Add(insert);const attribute=insert.Attributes.AttributeWithTag('TAG');
  assert.equal(doc.GetObjectByHandle(attribute.Handle),null);
  doc.NumHandles=BigInt('0x'+attribute.Handle);const occupied=new Set(Array.from(doc.RetainedMetadataObjects(),r=>r.Handle));
  parent.InsertVertex(0,api.Vector3.UnitX);const record=parent.VertexRecords.get_Item(0);
  assert.equal(occupied.has(record.Handle),false);assert.equal(attribute.Owner,insert);
});
test('retained vertex retirement preserves handles and does not retire sibling or SEQEND identities',()=>{
  const {doc,parent}=fixture(),record=parent.VertexRecords.get_Item(0),handle=record.Handle,end=parent.EndSequenceRecord;
  // Initialize the source's lazy removal database separately from the operation.
  void doc.Objects;const seed=doc.NumHandles;
  parent.MoveVertex(0,3);parent.RemoveVertexAt(3);
  assert.equal(record.Handle,handle);assert.equal(record.IsRemoved,true);assert.equal(record.Owner,null);
  assert.equal(doc.GetObjectByHandle(handle),null);assert.equal(parent.EndSequenceRecord,end);assert.equal(doc.NumHandles,seed);
  assert.throws(()=>record.Validate(doc,parent,false),{name:'InvalidOperationException'});
});
test('retained incoming XData rejection leaves geometry and handle allocation unchanged',()=>{
  const {doc,parent}=fixture(),record=parent.VertexRecords.get_Item(0),line=new api.Line();doc.Entities.Add(line);
  const data=new api.XData(new api.ApplicationRegistry('Incoming'));data.XDataRecord.Add(new api.XDataRecord(api.XDataCode.DatabaseHandle,'000'+record.Handle.toLowerCase()));line.XData.Add(data);
  const before=retainedSnapshot(parent),seed=doc.NumHandles;
  assert.throws(()=>parent.RemoveVertexAt(0),{name:'NotSupportedException'});assert.deepEqual(retainedSnapshot(parent),before);assert.equal(doc.NumHandles,seed);
  line.XData.Clear();parent.RemoveVertexAt(0);assert.equal(record.IsRemoved,true);
});
test('retained raw child-to-parent XData blocks a parent move but not unrelated geometry',()=>{
  const {doc,parent}=fixture(),record=parent.VertexRecords.get_Item(0),data=new api.XData(new api.ApplicationRegistry('ParentRef'));
  data.XDataRecord.Add(new api.XDataRecord(api.XDataCode.DatabaseHandle,parent.Handle));record.XData.Add(data);
  assert.equal(doc.Entities.Remove(parent),false);assert.notEqual(parent.Owner,null);
  record.XData.Clear();assert.equal(doc.Entities.Remove(parent),true);
});
test('retained private and owned payloads reject before mutation or lazy database allocation',()=>{
  const {doc,parent}=fixture(),record=parent.VertexRecords.get_Item(0);record.HasPrivateData=true;
  const seed=doc.NumHandles,shape=retainedSnapshot(parent);
  assert.throws(()=>parent.RemoveVertexAt(0),{name:'NotSupportedException'});assert.equal(doc.Entities.Remove(parent),false);
  assert.equal(doc.NumHandles,seed);assert.deepEqual(retainedSnapshot(parent),shape);
});
test('retained per-record and global tag limits leave topology unchanged',()=>{
  const {doc,parent}=fixture('Polyline3D',{count:300});const data=new api.XData(new api.ApplicationRegistry('TagBudget'));
  for(let i=0;i<3500;i++)data.XDataRecord.Add(new api.XDataRecord(api.XDataCode.String,'stored'));
  for(const record of parent.VertexRecords)record.XData.Add(data);
  const handles=Array.from(parent.VertexRecords,r=>r.Handle),seed=doc.NumHandles;
  assert.throws(()=>parent.InsertVertex(0,api.Vector3.Zero),{name:'NotSupportedException'});
  assert.deepEqual(Array.from(parent.VertexRecords,r=>r.Handle),handles);assert.equal(doc.NumHandles,seed);
  for(const record of parent.VertexRecords)record.XData.Clear();
  const first=parent.VertexRecords.get_Item(0);first.XDataStart=4097;
  assert.throws(()=>parent.InsertVertex(0,api.Vector3.Zero),{name:'NotSupportedException'});assert.equal(doc.NumHandles,seed);
});
test('retained insertion cannot overflow the signed handle allocator',()=>{
  const {doc,parent}=fixture();doc.NumHandles=9223372036854775807n;
  const count=parent.Vertexes.Count;assert.throws(()=>parent.InsertVertex(0,api.Vector3.Zero),{name:'InvalidOperationException'});
  assert.equal(parent.Vertexes.Count,count);assert.equal(doc.NumHandles,9223372036854775807n);
});
test('retained removal guards cover numeric handles beyond sixteen padded characters',()=>{
  assert.equal(RetainedHandleValue('00000000000000000000000Af'),175n);assert.equal(RetainedHandleValue('10000000000000000'),null);
  for(const value of [null,'','+1',' 1','1 ','0x1','GG'])assert.equal(RetainedHandleValue(value),null);
  const {doc,parent}=fixture(),record=parent.VertexRecords.get_Item(0);
  assert.equal(doc.RemovedPolylineHandle('0'.repeat(30)+record.Handle,new Set([record])),true);
});
test('retained corpus contains unique deterministic inputs and exact required coverage',()=>{
  const corpus=retainedPolylineCorpus();assert.deepEqual(corpus,retainedPolylineCorpus());assert.equal(corpus.length,181);
  assert.equal(new Set(corpus.map(p=>p.name)).size,181);assert.equal(corpus.reduce((n,p)=>n+p.request.steps.length,0),5419);
  assert.ok(corpus.every(p=>!Object.hasOwn(p,'expected')));
});
