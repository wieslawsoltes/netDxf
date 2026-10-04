import test from 'node:test';
import assert from 'node:assert/strict';
import { UCS, Vector3, CoordinateSystem, XData, XDataRecord, XDataCode, ApplicationRegistry } from '../../index.js';
import { ArgumentException, InvalidOperationException, NotSupportedException, KeyNotFoundException } from '../../runtime/Errors.js';
import { UcsReferenceHost } from '../../runtime/UcsReferenceHost.js';
const xyz = v => [v.X,v.Y,v.Z];
test('UCS origins and axes are copied without re-normalizing clones',()=>{
 const x=new Vector3(0,2,0),y=new Vector3(-3,0,0),o=new Vector3(3,4,5),u=new UCS('U',o,x,y);
 x.X=99;y.Y=99;o.Z=99;u.Origin.X=88;u.XAxis.Y=88;
 assert.deepEqual(xyz(u.Origin),[3,4,5]);assert.deepEqual(xyz(u.XAxis),[0,1,0]);assert.deepEqual(xyz(u.YAxis),[-1,0,0]);
 const copy=u.Clone();assert.deepEqual(copy.GetTransformation(),u.GetTransformation());
 assert.throws(()=>u.SetAxis(Vector3.UnitX,Vector3.UnitX),ArgumentException);assert.deepEqual(xyz(u.XAxis),[0,1,0]);
});
test('orthographic map has stable live read-only identity and value copies',()=>{
 const u=new UCS('U'),map=u.OrthographicOrigins,p=new Vector3(1,2,3);u.SetOrthographicOrigin(1,p);p.X=99;
 map.get_Item(1).Y=99;assert.deepEqual(xyz(map.get_Item(1)),[1,2,3]);
 for(const action of [()=>map.Add(2,p),()=>map.Remove(1),()=>map.Clear(),()=>map.set_Item(1,p)])assert.throws(action,NotSupportedException);
 assert.equal(map,u.OrthographicOrigins);assert.equal(map.Count,1);assert.throws(()=>map.get_Item(6),KeyNotFoundException);
 const box={};assert.equal(u.TryGetOrthographicOrigin(6,box),false);assert.deepEqual(xyz(box.value),[0,0,0]);
});
test('orthographic map preserves free-slot reuse and .NET insertion invalidation',()=>{
 const u=new UCS('U');for(const key of [1,2,3,4])u.SetOrthographicOrigin(key,new Vector3(key,0,0));
 u.RemoveOrthographicOrigin(2);u.RemoveOrthographicOrigin(4);u.SetOrthographicOrigin(5,Vector3.Zero);u.SetOrthographicOrigin(6,Vector3.Zero);
 assert.deepEqual([...u.OrthographicOrigins.Keys],[1,6,3,5]);
 const iterator=u.OrthographicOrigins.GetEnumerator();assert.equal(iterator.MoveNext(),true);
 u.SetOrthographicOrigin(1,Vector3.UnitX);assert.equal(iterator.MoveNext(),true);
 u.RemoveOrthographicOrigin(3);assert.equal(iterator.MoveNext(),true);
 u.SetOrthographicOrigin(2,Vector3.UnitY);assert.throws(()=>iterator.MoveNext(),InvalidOperationException);
});
test('UCS base metadata preserves explicit absence and references through clone',()=>{
 const u=new UCS('U'),base=new UCS('B');u.SetLoadedOrthographicBase(1,null,true);
 assert.equal(u.BaseUcs,null);assert.equal(u.Clone().BaseUcsHandlePresent,true);
 u.SetOrthographicBase(2,base);const q=u.Clone('Q');assert.equal(q.BaseUcs,base);
 assert.throws(()=>u.SetOrthographicBase(0,base),ArgumentException);assert.equal(u.BaseUcs,base);assert.equal(u.OrthographicViewType,2);
 u.SetOrthographicBase(0);assert.equal(u.BaseUcsHandlePresent,false);assert.equal(u.BaseUcs,null);assert.equal(q.BaseUcs,base);
});
test('point transforms snapshot the UCS before enumerating user input',()=>{
 const u=new UCS('U');u.Origin=new Vector3(10,20,30);
 const input={*[Symbol.iterator](){yield Vector3.Zero;u.Origin=new Vector3(999,999,999);yield Vector3.UnitX;}};
 const result=u.Transform(input,CoordinateSystem.Object,CoordinateSystem.World);
 assert.deepEqual(result.map(xyz),[[10,20,30],[11,20,30]]);
});
test('UCS clone owns XData but shares an explicitly authored base reference',()=>{
 const u=new UCS('U'),data=new XData(new ApplicationRegistry('APP'));data.XDataRecord.Add(new XDataRecord(XDataCode.BinaryData,Uint8Array.of(1,2)));
 u.XData.Add(data);u.Handle='FF';u.SetOrthographicBase(1,u);const q=u.Clone('Q');
 assert.equal(q.Handle,null);assert.equal(q.Owner,null);assert.equal(q.BaseUcs,u);
 q.XData.get_Item('APP').XDataRecord.Clear();assert.equal(data.XDataRecord.Count,1);
});
test('UCS name rejection and event failure leave names unchanged',()=>{
 const u=new UCS('U');assert.throws(()=>{u.Name='   ';},ArgumentException);assert.equal(u.Name,'U');
 u.NameChanged.Add(()=>{throw new InvalidOperationException();});assert.throws(()=>{u.Name='Other';},InvalidOperationException);assert.equal(u.Name,'U');
});
test('internal UCS reference host validates before changing tracked references',()=>{
 // Structural internal-host contract test, not a registered DxfDocument conformance case.
 const trace=[],target=new UCS('T'),table={References:new Map([['T',{Add:v=>trace.push(['add',v]),Remove:v=>trace.push(['remove',v])}]])};
 const document={CodeName:'DOCUMENT',UCSs:table,GetObjectByHandle:h=>h==='A'?target:null};table.Owner=document;target.Owner=table;target.Handle='A';
 const owner=new UCS('U');owner.Owner={Owner:document};
 UcsReferenceHost.Replace(owner,null,target);assert.deepEqual(trace,[['add',owner]]);
 assert.throws(()=>UcsReferenceHost.Replace(owner,target,new UCS('foreign')),ArgumentException);assert.equal(trace.length,1);
 UcsReferenceHost.Replace(owner,target,null);assert.equal(trace[1][0],'remove');
});
