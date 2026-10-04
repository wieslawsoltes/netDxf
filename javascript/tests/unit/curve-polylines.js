import test from 'node:test';
import assert from 'node:assert/strict';
import {Circle,Arc,Polyline2D,Polyline2DVertex,Polyline2DRecord,PolylineSmoothType,Vector2,Vector3,Matrix3,DxfTag,Layer,Linetype,XData,ApplicationRegistry,XDataRecord,XDataCode} from '../../index.js';
import {ReferenceList} from '../../runtime/ReferenceList.js';
import {NurbsEvaluator} from '../../runtime/NurbsEvaluator.js';
import {PeriodicSplineData} from '../../netDxf/Entities/PeriodicSplineData.js';
import {ArgumentException,ArgumentNullException,ArgumentOutOfRangeException,InvalidOperationException,NotSupportedException} from '../../runtime/Errors.js';
import {doubleBits} from '../../tools/wire.mjs';
const xy=v=>[v.X,v.Y];
function retained(){
 const p=new Polyline2D([new Polyline2DVertex(0,0,.25),new Polyline2DVertex(2,1,0),new Polyline2DVertex(3,0,-.25)],true);
 const records=Array.from(p.Vertexes,(v,i)=>{const r=new Polyline2DRecord('VERTEX',new ReferenceList([new DxfTag(0,'VERTEX')]));r.StoredVertex=v;return r;});
 const end=new Polyline2DRecord('SEQEND',new ReferenceList([new DxfTag(0,'SEQEND')]));p.SetStoredRecords(null,records,end);p.StoredHeaderTags=new ReferenceList([new DxfTag(0,'POLYLINE')]);p.StoredNormal=Vector3.UnitZ;return p;
}
test('curve center values are copied and polygon sampling stays center-relative',()=>{
 for(const Type of [Circle,Arc]){const c=new Vector3(9,8,7),e=Type===Circle?new Type(c,2):new Type(c,2,0,90);c.X=100;e.Center.Y=100;assert.equal(e.Center.X,9);assert.equal(e.Center.Y,8);
  const points=e.PolygonalVertexes(5);assert.deepEqual(xy(points.get_Item(0)),[2,0]);points.get_Item(0).X=88;assert.equal(points.get_Item(0).X,2);}
});
test('circle conversion closes sampled coordinates while arc conversion remains open',()=>{
 const circle=new Circle(new Vector3(9,8,7),2),arc=new Arc(new Vector3(9,8,7),2,0,90);
 for(const e of [circle,arc]){e.Thickness=3;e.IsVisible=false;e.ProxyGraphics=Uint8Array.of(1,2);e.Layer=new Layer('CURVE');e.ColorName='Book';
  const p=e.ToPolyline2D(7);assert.equal(p.Vertexes.Count,7);assert.equal(p.IsClosed,e===circle);assert.deepEqual(xy(p.Vertexes.get_Item(0).Position),[11,8]);assert.equal(p.Elevation,7);assert.equal(p.Thickness,3);
  assert.equal(p.IsVisible,true);assert.equal(p.ProxyGraphics,null);assert.equal(p.ColorName,null);assert.notEqual(p.Layer,e.Layer);assert.equal(p.Layer.Name,'CURVE');}
});
test('curve setters retain original nonpositive-only radius guards and exact signed bits',()=>{
 for(const e of [new Circle(),new Arc()]){for(const n of [0,-0,-1,-Infinity])assert.throws(()=>{e.Radius=n;},ArgumentOutOfRangeException);assert.equal(e.Radius,1);
  e.Radius=Infinity;assert.equal(e.Clone().Radius,Infinity);e.Radius=NaN;assert.ok(Number.isNaN(e.Clone().Radius));e.Thickness=-0;assert.ok(Object.is(e.Clone().Thickness,-0));}
});
test('circle and arc precision failures are explicit and matrix transforms keep thickness',()=>{
 for(const e of [new Circle(),new Arc()]){for(const n of [-1,0,1])assert.throws(()=>e.PolygonalVertexes(n),ArgumentOutOfRangeException);
  e.Thickness=2;e.TransformBy(Matrix3.Scale(3),new Vector3(1,2,3));assert.equal(e.Radius,3);assert.equal(e.Thickness,2);assert.equal(e.Center.Z,3);}
});
test('curve clones retain XData and common data with independent geometry and resources',()=>{
 for(const e of [new Circle(),new Arc()]){e.Handle='A';e.ProxyGraphics=Uint8Array.of(9);e.IsVisible=false;e.ColorName='B';const xd=new XData(new ApplicationRegistry('CURVE'));xd.XDataRecord.Add(new XDataRecord(XDataCode.String,'x'));e.XData.Add(xd);
  const q=e.Clone();q.XData.get_Item('CURVE').XDataRecord.Clear();q.Center=new Vector3(1,2,3);assert.equal(xd.XDataRecord.Count,1);assert.equal(e.Center.X,0);assert.equal(q.Handle,null);assert.equal(q.IsVisible,false);assert.equal(q.ColorName,'B');assert.deepEqual([...q.ProxyGraphics],[9]);}
});
test('polyline construction copies collection membership but retains vertex references',()=>{
 const v=new Polyline2DVertex(1,2),a=[v],p=new Polyline2D(a);a.length=0;assert.equal(p.Vertexes.Count,1);assert.equal(p.Vertexes.get_Item(0),v);v.Bulge=.5;assert.equal(p.Vertexes.get_Item(0).Bulge,.5);
 const c=new Vector2(1,2),q=new Polyline2D([c]);c.X=99;assert.equal(q.Vertexes.get_Item(0).Position.X,1);assert.throws(()=>new Polyline2D(null),ArgumentNullException);
});
test('polyline reversal preserves vertex identity and rotates directed outgoing attributes',()=>{
 const a=new Polyline2DVertex(0,0,.25),b=new Polyline2DVertex(1,0,-.5),c=new Polyline2DVertex(1,1,1),p=new Polyline2D([a,b,c]);
 a.StartWidthOverride=null;a.EndWidthOverride=0;b.StartWidth=2;b.EndWidth=3;c.StartWidth=4;c.EndWidth=5;const id=p.Vertexes;
 p.Reverse();assert.equal(p.Vertexes,id);assert.equal(p.Vertexes.get_Item(0),c);assert.equal(c.Bulge,.5);assert.equal(c.StartWidthOverride,3);assert.equal(c.EndWidthOverride,2);
 p.Reverse();assert.equal(p.Vertexes.get_Item(0),a);assert.equal(a.Bulge,.25);assert.equal(a.StartWidthOverride,null);assert.equal(a.EndWidthOverride,0);assert.equal(b.StartWidth,2);
});
test('wide polyline nonuniform transform rejection is atomic',()=>{
 const p=new Polyline2D([new Polyline2DVertex(0,0,.25),new Polyline2DVertex(2,3,0)]);p.ConstantWidth=2;p.Elevation=4;const before=p.Vertexes.get_Item(0).Position;
 assert.throws(()=>p.TransformBy(Matrix3.Scale(2,3,1),Vector3.UnitX),NotSupportedException);assert.deepEqual(xy(p.Vertexes.get_Item(0).Position),xy(before));assert.equal(p.ConstantWidth,2);assert.equal(p.Elevation,4);
 p.TransformBy(Matrix3.Scale(2,2,7),Vector3.UnitX);assert.equal(p.ConstantWidth,4);assert.equal(p.Elevation,28);
});
test('width precedence distinguishes omitted zero and positive overrides',()=>{
 const v=new Polyline2DVertex(0,0),p=new Polyline2D([v]);p.LegacyDefaultStartWidth=3;assert.equal(p.GetEffectiveStartWidth(0),3);v.StartWidthOverride=0;assert.equal(p.GetEffectiveStartWidth(0),0);
 p.ConstantWidth=2;assert.equal(p.GetEffectiveStartWidth(0),2);assert.equal(v.StartWidthOverride,0);p.ConstantWidth=0;assert.equal(p.GetEffectiveStartWidth(0),0);p.SetConstantWidth(7);assert.equal(p.ConstantWidth,null);assert.equal(v.StartWidth,7);assert.equal(v.EndWidth,7);
});
test('ReferenceList Reverse retains the list object and invalidates active iterators',()=>{
 const l=new ReferenceList([1,2,3,4]),i=l.GetEnumerator();i.MoveNext();l.Reverse(1,2);assert.deepEqual(l.ToArray(),[1,3,2,4]);assert.throws(()=>i.MoveNext(),InvalidOperationException);
 assert.throws(()=>l.Reverse(-1,1),ArgumentOutOfRangeException);assert.throws(()=>l.Reverse(0,-1),ArgumentOutOfRangeException);assert.throws(()=>l.Reverse(3,2),ArgumentException);assert.deepEqual(l.ToArray(),[1,3,2,4]);
});
test('smooth polylines use the same periodic evaluator and preserve the authored controls',()=>{
 const p=new Polyline2D([Vector2.Zero,Vector2.UnitX,new Vector2(2,2),new Vector2(0,3)],true);p.SmoothType=PolylineSmoothType.Cubic;
 const before=p.Vertexes.get_Item(0);const points=p.PolygonalVertexes(12);assert.equal(points.Count,12);assert.equal(p.Vertexes.get_Item(0),before);assert.equal(p.Explode().Count,32);assert.equal(p.CodeName,'POLYLINE');p.SmoothType=0;assert.equal(p.CodeName,'LWPOLYLINE');
});
test('NURBS extreme positive weights and cancelling coordinates remain finite without mutating inputs',()=>{
 const controls=[new Vector3(1e308,0,0),new Vector3(-1e308,1,0),new Vector3(1e308,0,1),new Vector3(-1e308,1,1)],weights=[1e-150,1e150,1e-150,1e150],copy=weights.slice();
 const points=NurbsEvaluator(controls,weights,null,3,false,true,9);assert.equal(points.Count,9);assert.ok([...points].every(p=>[p.X,p.Y,p.Z].every(Number.isFinite)));assert.deepEqual(weights,copy);assert.equal(controls[0].X,1e308);
});
test('periodic representation guards reject invalid compact layout and nonrepresentable ranges',()=>{
 const p=[Vector3.Zero,Vector3.UnitX,Vector3.UnitY,new Vector3(1,1,1)];
 assert.throws(()=>NurbsEvaluator(p,[Number.MIN_VALUE,1e308,1,1],null,3,false,true,5),NotSupportedException);
 assert.throws(()=>NurbsEvaluator(p,[1,1,0,1],null,3,false,true,5),NotSupportedException);assert.throws(()=>NurbsEvaluator(p,null,null,11,false,true,5),NotSupportedException);
 assert.equal(PeriodicSplineData.Same(0,-0),false);assert.equal(PeriodicSplineData.Same(1,1),true);
});
test('retained polyline clones rebind independent record data to independently cloned vertices',()=>{
 const p=retained(),q=p.Clone();assert.equal(q.CodeName,'POLYLINE');assert.notEqual(q.VertexRecords,p.VertexRecords);assert.notEqual(q.EndSequenceRecord,p.EndSequenceRecord);
 assert.equal(q.VertexRecords.get_Item(0).Vertex,q.Vertexes.get_Item(0));assert.equal(q.VertexRecords.get_Item(0).Owner,q);assert.notEqual(q.VertexRecords.get_Item(0).Vertex,p.Vertexes.get_Item(0));
 q.Vertexes.get_Item(0).Position=new Vector2(9,9);assert.equal(p.Vertexes.get_Item(0).Position.X,0);
});
test('retained reversal moves record associations and swaps legacy default widths',()=>{
 const p=retained(),view=p.VertexRecords,first=view.get_Item(0),last=view.get_Item(2);p.LegacyDefaultStartWidth=2;p.LegacyDefaultEndWidth=3;p.Reverse();
 assert.equal(p.VertexRecords,view);assert.equal(view.get_Item(0),last);assert.equal(view.get_Item(0).Vertex,p.Vertexes.get_Item(0));assert.equal(p.LegacyDefaultStartWidth,3);assert.equal(p.LegacyDefaultEndWidth,2);p.Reverse();assert.equal(view.get_Item(0),first);
});
test('retained record representation guards prevent lossy smoothing and constant-width rewrites',()=>{
 const p=retained();assert.throws(()=>{p.ConstantWidth=0;},NotSupportedException);assert.throws(()=>{p.SmoothType=5;},NotSupportedException);assert.equal(p.ConstantWidth,null);assert.equal(p.SmoothType,0);
 p.HasPrivateHeader=true;assert.throws(()=>p.Clone(),NotSupportedException);p.HasPrivateHeader=false;p.Vertexes.set_Item(0,new Polyline2DVertex(0,0));assert.throws(()=>p.Reverse(),NotSupportedException);
});
test('retained transforms stage positions before finite checks and scale thickness separately',()=>{
 const p=retained();p.Thickness=2;p.Elevation=3;const before=xy(p.Vertexes.get_Item(0).Position);
 assert.throws(()=>p.TransformBy(Matrix3.Scale(0),Vector3.Zero),NotSupportedException);assert.deepEqual(xy(p.Vertexes.get_Item(0).Position),before);assert.equal(p.Thickness,2);
 p.TransformBy(Matrix3.Scale(2,2,3),Vector3.UnitX);assert.equal(p.Thickness,6);assert.equal(p.Elevation,9);
});
test('retained geometry projection emits optional raw widths and IDs without materializing omissions',()=>{
 const p=retained(),v=p.Vertexes.get_Item(0),r=p.VertexRecords.get_Item(0);v.StartWidthOverride=0;v.VertexIdentifier=0;const tags=r.GeometryTags();assert.equal(tags.get(40).Value,0);assert.equal(tags.get(91).Value,0);assert.equal(tags.has(41),false);assert.equal(p.EndSequenceRecord.GeometryTags().size,0);
});
test('retained resource cloning isolates table models and refuses unknown resource graphs',()=>{
 const p=retained(),r=p.VertexRecords.get_Item(0),layer=new Layer('L');r.Resources.set(3,layer);const q=p.Clone();assert.notEqual(q.VertexRecords.get_Item(0).Layer,layer);assert.equal(q.VertexRecords.get_Item(0).Layer.Name,'L');
 r.Resources.set(4,new Circle());assert.throws(()=>p.Clone(),NotSupportedException);
});
test('normal polyline cloning separates vertex optional fields and mutable resources',()=>{
 const v=new Polyline2DVertex(1,2,.5),p=new Polyline2D([v]);v.StartWidth=-0;v.VertexIdentifier=0;p.Layer=new Layer('SOURCE');p.Linetype=Linetype.Dashed;p.ProxyGraphics=Uint8Array.of(1);
 const q=p.Clone();assert.notEqual(q.Vertexes.get_Item(0),v);assert.ok(Object.is(q.Vertexes.get_Item(0).StartWidthOverride,-0));q.Vertexes.get_Item(0).StartWidth=9;assert.ok(Object.is(v.StartWidthOverride,-0));assert.notEqual(q.Layer,p.Layer);assert.notEqual(q.Linetype,p.Linetype);
});

test('new curve and NURBS corpora retain distinct scenario identities including signed zero',async()=>{
 const {curveCorpus}=await import('../../tools/curve-corpus.mjs');const {nurbsCorpus}=await import('../../tools/nurbs-corpus.mjs');
 for(const items of [curveCorpus(),nurbsCorpus()])assert.equal(new Set(items.map(p=>p.name)).size,items.length);
});
