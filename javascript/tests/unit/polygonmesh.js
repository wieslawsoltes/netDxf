import test from 'node:test';
import assert from 'node:assert/strict';
import {PolygonMesh,PolygonMeshRecord,Vector3,Matrix3,Layer,Linetype,DxfTag,XData,XDataCode,XDataRecord,ApplicationRegistry} from '../../index.js';
import {ReferenceList} from '../../runtime/ReferenceList.js';
import {ArgumentException,ArgumentOutOfRangeException,InvalidOperationException,NotSupportedException} from '../../runtime/Errors.js';
const points=(u=4,v=4)=>Array.from({length:u*v},(_,i)=>new Vector3(i%u,Math.floor(i/u),((i*i%7)-3)*.125));
const mesh=(u=4,v=4)=>new PolygonMesh(u,v,points(u,v));
const xyz=v=>[v.X,v.Y,v.Z];
function retained(){const p=mesh(2,2),records=Array.from({length:4},()=>new PolygonMeshRecord('VERTEX',new ReferenceList([new DxfTag(0,'VERTEX')]))),end=new PolygonMeshRecord('SEQEND',new ReferenceList([new DxfTag(0,'SEQEND')]));p.SetStoredRecords(null,records,end);return {p,records,end};}
test('polygon mesh construction copies value arrays and preserves U-major indexing',()=>{
 const input=points(2,3),p=new PolygonMesh(2,3,input);input[3].X=99;p.GetVertex(1,1).Y=99;assert.deepEqual(xyz(p.GetVertex(1,1)),[1,1,-.125]);
 p.SetVertex(1,2,new Vector3(7,8,9));assert.deepEqual(xyz(p.Vertexes[5]),[7,8,9]);assert.equal(p.Vertexes.length,6);assert.throws(()=>{p.Vertexes.length=0;},NotSupportedException);
});
test('out-of-range grid indexes retain source fallback and no-op behavior',()=>{
 const p=mesh(2,2),original=xyz(p.Vertexes[0]);p.SetVertex(-1,1,new Vector3(9,9,9));assert.deepEqual(xyz(p.GetVertex(-1,1)),original);assert.deepEqual(xyz(p.GetVertex(2,0)),original);
});
test('sampling validates precision even for unsmoothed meshes and rejects nonfinite grids',()=>{
 const p=mesh();assert.throws(()=>p.MeshVertexes(2,3),{name:'ArgumentOutOfRangeException',ParamName:'precisionU'});assert.throws(()=>p.ToMesh(3,2),{name:'ArgumentOutOfRangeException',ParamName:'precisionV'});
 assert.equal(p.MeshVertexes(3,3).Count,16);p.Vertexes[0]=new Vector3(NaN,0,0);assert.throws(()=>p.MeshVertexes(),InvalidOperationException);assert.ok(Number.isNaN(p.Clone().Vertexes[0].X));
});
test('quadratic and cubic cardinality depends separately on closure along each axis',()=>{
 const q=mesh(2,2);q.SmoothType=5;assert.throws(()=>q.MeshVertexes(),InvalidOperationException);q.IsClosedInU=true;assert.throws(()=>q.MeshVertexes(),InvalidOperationException);q.IsClosedInV=true;assert.equal(q.MeshVertexes(4,5).Count,20);
 const c=mesh(3,3);c.SmoothType=6;assert.throws(()=>c.MeshVertexes(),InvalidOperationException);c.IsClosedInU=true;c.IsClosedInV=true;assert.equal(c.MeshVertexes(4,5).Count,20);
});
test('source-specific default density behavior differs among sampling conversion and explode',()=>{
 const p=mesh();p.SmoothType=6;const u=PolygonMesh.DefaultSurfU,v=PolygonMesh.DefaultSurfV;
 try{PolygonMesh.DefaultSurfU=0;PolygonMesh.DefaultSurfV=1;assert.equal(p.MeshVertexes().Count,9);assert.equal(p.Explode().Count,0);assert.throws(()=>p.ToMesh(),ArgumentOutOfRangeException);}
 finally{PolygonMesh.DefaultSurfU=u;PolygonMesh.DefaultSurfV=v;}
});
test('closed mesh seams preserve original face ordering and omitted double-closure corner',()=>{
 const p=mesh(3,3);p.IsClosedInU=true;p.IsClosedInV=true;const m=p.ToMesh();assert.equal(m.Faces.Count,8);assert.deepEqual(Array.from(m.Faces.get_Item(2)),[2,0,3,5]);assert.deepEqual(Array.from(m.Faces.get_Item(7)),[7,8,2,1]);assert.equal(p.Explode().Count,8);
});
test('mesh conversion returns generated geometry with default appearance rather than cloned metadata',()=>{
 const p=mesh();p.Layer=new Layer('SOURCE');p.IsVisible=false;p.ProxyGraphics=Uint8Array.of(1);p.ColorName='Book';
 const converted=p.ToMesh(),face=p.Explode().get_Item(0);for(const e of [converted,face]){assert.equal(e.Layer.Name,'0');assert.equal(e.IsVisible,true);assert.equal(e.ProxyGraphics,null);assert.equal(e.ColorName,null);}
});
test('polygon mesh clone isolates controls styles proxy and XData while retaining raw zero densities',()=>{
 const p=mesh();p.Layer=new Layer('SOURCE');p.ProxyGraphics=Uint8Array.of(1);p.Handle='AA';p.SmoothType=6;p.IsClosedInV=true;
 const data=new XData(new ApplicationRegistry('GRID'));data.XDataRecord.Add(new XDataRecord(XDataCode.String,'source'));p.XData.Add(data);
 const q=p.Clone();q.Vertexes[0].X=99;q.Layer.Name='COPY';q.XData.get_Item('GRID').XDataRecord.Clear();assert.equal(p.Vertexes[0].X,0);assert.equal(p.Layer.Name,'SOURCE');assert.equal(data.XDataRecord.Count,1);assert.equal(q.Handle,null);assert.equal(q.DensityU,0);assert.equal(q.SmoothType,6);assert.equal(q.IsClosedInV,true);
});
test('surface transform preserves caller array identity and source normal fallback',()=>{
 const p=mesh(),vertices=p.Vertexes,normal=p.Normal;p.TransformBy(Matrix3.Scale(0),new Vector3(1,2,3));assert.equal(p.Vertexes,vertices);assert.ok([...vertices].every(v=>v.Equals(new Vector3(1,2,3))));assert.deepEqual(xyz(p.Normal),xyz(normal));
});
test('retained clone keeps independently owned child records and resources',()=>{
 const {p,records,end}=retained();records[0].Resources.set(1,new Layer('CHILD'));const q=p.Clone();assert.notEqual(q.VertexRecords,p.VertexRecords);assert.notEqual(q.VertexRecords.get_Item(0),records[0]);assert.equal(q.VertexRecords.get_Item(0).Owner,q);assert.notEqual(q.VertexRecords.get_Item(0).Layer,records[0].Layer);assert.notEqual(q.EndSequenceRecord,end);assert.equal(q.EndSequenceRecord.Owner,q);
});
test('retained mesh cannot shallow-clone private external or invalid geometry state',()=>{
 const {p,records}=retained();records[0].HasPrivateData=true;assert.throws(()=>p.Clone(),NotSupportedException);records[0].HasPrivateData=false;p.SmoothType=5;assert.throws(()=>p.Clone(),NotSupportedException);p.SmoothType=0;p.Vertexes[0].Z=Infinity;assert.throws(()=>p.Clone(),InvalidOperationException);
});
test('retained record view stays stable while exposing source-bound record metadata',()=>{
 const {p,records}=retained(),view=p.VertexRecords;assert.equal(view,p.VertexRecords);assert.equal(view.Count,4);assert.equal(view.get_Item(0),records[0]);assert.equal(view.Add,undefined);const q=p.Clone();q.Vertexes[0].X=9;assert.equal(p.Vertexes[0].X,0);
});
test('retained validation refuses cross-document or mismatched registration before publication',()=>{
 const {p,records,end}=retained(),owner={Handle:'BLOCK'},objects=new Map();p.Handle='MESH';p.Owner=owner;objects.set(p.Handle,p);objects.set(owner.Handle,owner);[...records,end].forEach((r,i)=>{r.Handle='R'+i;objects.set(r.Handle,r);});
 const doc={DrawingVariables:{AcadVer:0},GetObjectByHandle:h=>objects.get(h)??null};p.BindStoredRecordDocument(doc);p.ValidateStoredRecords(doc,true);assert.throws(()=>p.ValidateStoredRecords({...doc},true),NotSupportedException);objects.delete(records[0].Handle);assert.throws(()=>p.ValidateStoredRecords(doc,true),InvalidOperationException);
});
test('mesh density validation rejects zero edits without changing loaded default values',()=>{
 const p=mesh();assert.equal(p.DensityU,0);assert.throws(()=>{p.DensityU=0;},ArgumentOutOfRangeException);assert.equal(p.DensityU,0);p.DensityU=201;assert.equal(p.Clone().DensityU,201);assert.throws(()=>{p.SmoothType=8;},ArgumentOutOfRangeException);assert.equal(p.SmoothType,0);
});
test('polygon mesh corpus preserves unique scenario names and complete requests',async()=>{
 const {polygonmeshCorpus}=await import('../../tools/polygonmesh-corpus.mjs');const c=polygonmeshCorpus();assert.equal(c.length,258);assert.equal(new Set(c.map(p=>p.name)).size,c.length);assert.equal(c.reduce((n,p)=>n+p.request.steps.length,0),1990);
});
