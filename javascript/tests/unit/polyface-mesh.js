import test from 'node:test';
import assert from 'node:assert/strict';
import { PolyfaceMesh, PolyfaceMeshFace, PolyfaceMeshRecord, Point, Line, Face3D, Layer, Linetype, Vector3, Matrix3, DxfTag, AciColor, XData, XDataRecord, XDataCode, ApplicationRegistry, DxfDictionary } from '../../index.js';
import { ReferenceList } from '../../runtime/ReferenceList.js';
import { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, InvalidOperationException, NotSupportedException } from '../../runtime/Errors.js';
const pts=()=>[new Vector3(1,2,3),new Vector3(4,5,6),new Vector3(7,8,9),new Vector3(10,11,12)];
const mesh=faces=>new PolyfaceMesh(pts(),faces??[[1,2,-3]]);
function retained() {
  const p=mesh(),coordinates=pts().map(()=>new PolyfaceMeshRecord('VERTEX',new ReferenceList([new DxfTag(0,'VERTEX')])));
  const face=new PolyfaceMeshRecord('VERTEX',new ReferenceList([new DxfTag(0,'VERTEX')]));face.IsFaceRecord=true;face.StoredFace=p.Faces.get_Item(0);face.FaceIndex=0;
  const end=new PolyfaceMeshRecord('SEQEND',new ReferenceList([new DxfTag(0,'SEQEND')]));
  p.SetStoredRecords(null,[...coordinates,face,end]);p.StoredHeaderTags=new ReferenceList([new DxfTag(0,'POLYLINE')]);return p;
}
test('polyface explosions preserve active topology signs and ignore inactive padding',()=>{
  const p=mesh([[1,0,-32768,32767],[1,-2,0,32767],[1,-2,-3,0],[-1,2,-3,4]]);const result=p.Explode().ToArray();
  assert.ok(result[0] instanceof Point);assert.ok(result[1] instanceof Line);assert.ok(result[2] instanceof Face3D);
  assert.equal(result[2].EdgeFlags,14);assert.equal(result[3].EdgeFlags,5);assert.equal(result[2].FourthVertex.X,7);
});
test('polyface explosion clones appearance without inventing copied entity metadata',()=>{
  const face=new PolyfaceMeshFace([1,2,3]);const p=mesh([face]);p.IsVisible=false;p.Color=AciColor.Blue;p.ProxyGraphics=Uint8Array.of(1);
  let result=p.Explode().get_Item(0);assert.equal(result.IsVisible,true);assert.equal(result.ProxyGraphics,null);assert.notEqual(result.Color,p.Color);assert.equal(result.Color.Index,p.Color.Index);
  face.Layer=new Layer('FACE');face.Color=AciColor.Red;result=p.Explode().get_Item(0);assert.equal(result.Layer.Name,'FACE');assert.notEqual(result.Layer,face.Layer);assert.equal(result.Color.Index,1);
});
test('polyface arrays copy coordinates and retain face references without resizing',()=>{
  const vertices=pts(),faces=[new PolyfaceMeshFace([1,2,3])],p=new PolyfaceMesh(vertices,faces);vertices[0].X=99;faces.push(new PolyfaceMeshFace([1]));
  assert.equal(p.Vertexes[0].X,1);assert.equal(p.Faces.Count,1);assert.equal(p.Faces.get_Item(0),faces[0]);
  p.Vertexes[0].X=7;assert.equal(p.Explode().get_Item(0).FirstVertex.X,7);assert.equal(p.Faces.Add,undefined);
  const iterator=p.Faces.GetEnumerator();assert.throws(()=>iterator.Current,InvalidOperationException);assert.equal(iterator.MoveNext(),true);assert.equal(iterator.Current,faces[0]);assert.equal(iterator.MoveNext(),false);
});
test('explicit raw-index enumerable constructor preserves repeated source enumeration',()=>{
  let yielded=0;const faces={*[Symbol.iterator](){for(const f of [[1],[1,2]]){yielded++;yield f;}}};
  const p=PolyfaceMesh.CreateOverload('System.Collections.Generic.IEnumerable<netDxf.Vector3>,System.Collections.Generic.IEnumerable<short[]>',pts(),faces);
  assert.equal(p.Faces.Count,2);assert.equal(yielded,5);
  assert.throws(()=>PolyfaceMesh.CreateOverload('System.Collections.Generic.IEnumerable<netDxf.Vector3>,System.Collections.Generic.IEnumerable<short[]>',pts(),[null]),ArgumentNullException);
});
test('mesh validates mutations on explode and clone and rejects invalid normals',()=>{
  const p=mesh();p.Faces.get_Item(0).VertexIndexes[0]=0;assert.throws(()=>p.Explode(),ArgumentException);assert.throws(()=>p.Clone(),ArgumentException);
  for(const normal of [Vector3.Zero,new Vector3(Infinity,0,1),new Vector3(NaN,1,0)]){const q=mesh();q.Normal=normal;assert.throws(()=>q.Clone(),InvalidOperationException);}
});
test('duplicate face subscriptions are independent across cloned meshes',()=>{
  const face=new PolyfaceMeshFace([1,2,3]),p=mesh([face,face]);let events=0;p.PolyfaceMeshFaceLayerChanged.Add(()=>events++);
  face.Layer=new Layer('SOURCE');assert.equal(events,2);const q=p.Clone();q.Faces.get_Item(0).Layer=new Layer('CLONE');assert.equal(events,2);
  assert.notEqual(q.Faces.get_Item(0),q.Faces.get_Item(1));assert.equal(face.Layer.Name,'SOURCE');
});
test('transform and clone preserve independent coordinates and common data',()=>{
  const p=mesh();p.ProxyGraphics=Uint8Array.of(9);const q=p.Clone();q.TransformBy(Matrix3.Scale(2),new Vector3(1,2,3));
  assert.equal(p.Vertexes[0].X,1);assert.equal(q.Vertexes[0].X,3);assert.deepEqual([...q.ProxyGraphics],[9]);assert.equal(q.Owner,null);
});
test('retained polyface clones keep record order and rebind face identity',()=>{
  const p=retained();p.DeclaredVertexCount=-12;p.DeclaredFaceCount=123;const q=p.Clone();
  assert.equal(q.RecordSequence.Count,6);assert.equal(q.VertexRecords.Count,4);assert.equal(q.FaceRecords.Count,1);
  assert.equal(q.FaceRecords.get_Item(0).Face,q.Faces.get_Item(0));assert.notEqual(q.FaceRecords.get_Item(0).Face,p.Faces.get_Item(0));
  assert.equal(q.EndSequenceRecord.Owner,q);assert.equal(q.EndSequenceRecord.Handle,null);assert.equal(q.DeclaredVertexCount,-12);assert.equal(q.DeclaredFaceCount,123);
});
test('retained polyface cloning refuses private, owned, external and stale geometry',()=>{
  const actions=[p=>{p.HasPrivateHeader=true;},p=>{p.ExtensionDictionary=new DxfDictionary();},p=>{p.PersistentReactors.Add(new Point());},p=>{p.EndSequenceRecord.HasPrivateData=true;},p=>{
    const x=new XData(new ApplicationRegistry('APP'));x.XDataRecord.Add(new XDataRecord(XDataCode.DatabaseHandle,'AB'));p.XData.Add(x);
  }];
  for(const edit of actions){const p=retained();edit(p);assert.throws(()=>p.Clone(),NotSupportedException);}
  const p=retained();p.Vertexes[0].X=Infinity;assert.throws(()=>p.Clone(),InvalidOperationException);
  const q=retained();q.FaceRecords.get_Item(0).StoredFace=new PolyfaceMeshFace([1]);assert.throws(()=>q.Clone(),NotSupportedException);
});
test('retained record validation enforces version, ownership, registration and resource identity',()=>{
  // This isolates the internal guard with a supplied registry, not a typed-document port.
  const p=retained(),record=p.EndSequenceRecord,registry=new Map(),doc={DrawingVariables:{AcadVer:18},GetObjectByHandle:h=>registry.get(h)??null};record.SourceVersion=18;
  record.Validate(doc,p,false);assert.throws(()=>record.Validate({...doc,DrawingVariables:{AcadVer:17}},p,false),NotSupportedException);
  assert.throws(()=>record.Validate(doc,mesh(),false),InvalidOperationException);record.Handle='A';registry.set('A',record);p.Handle='B';registry.set('B',p);record.Validate(doc,p,true);
  record.Resources.set(0,new Layer('RESOURCE'));assert.throws(()=>record.Validate(doc,p,true),InvalidOperationException);
});
test('stored record tag accounting tracks face appearance and metadata replacement',()=>{
  const r=new PolyfaceMeshRecord('VERTEX',new ReferenceList([new DxfTag(0,'VERTEX')]));r.StoredFace=new PolyfaceMeshFace([1,2,3]);assert.equal(r.TopologyTagCount(),1);
  r.Face.Layer=new Layer('FACE');r.Face.Color=AciColor.FromTrueColor(0x123456);assert.equal(r.TopologyTagCount(),4);
  r.ExtensionDictionary=new DxfDictionary();r.PersistentReactors.Add(new Point());assert.equal(r.TopologyTagCount(),10);assert.equal(r.CanClone(),false);
  const x=new XData(new ApplicationRegistry('APP'));x.XDataRecord.Add(new XDataRecord(XDataCode.Int16,4));r.XData.Add(x);assert.equal(r.TopologyTagCount(),12);
});
test('opaque reference enumeration excludes identity, owner, resources and metadata groups',()=>{
  const r=new PolyfaceMeshRecord('VERTEX',new ReferenceList([new DxfTag(0,'VERTEX'),new DxfTag(5,'A'),new DxfTag(330,'B'),new DxfTag(340,'C'),new DxfTag(102,'{ACAD_REACTORS'),new DxfTag(330,'D'),new DxfTag(102,'}'),new DxfTag(350,'E'),new DxfTag(1005,'F')]));
  r.IdentityIndex=1;r.OwnerIndex=2;r.MetadataGroups.set(4,6);r.Resources.set(3,new Layer('L'));
  assert.deepEqual(Array.from(r.OpaqueHandleTags,t=>t.Value),['E']);
});
