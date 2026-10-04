import test from 'node:test';
import assert from 'node:assert/strict';
import * as api from '../../index.js';
import { Copy, DotNetNaN } from '../../runtime/GeometryRuntime.js';
import { doubleBits } from '../../tools/wire.mjs';
import { ArgumentException,ArgumentNullException,ArgumentOutOfRangeException,InvalidOperationException,NotSupportedException } from '../../runtime/Errors.js';
const types=['Point','Line','Ray','XLine','Face3D','Solid','Trace'];
const bytes=n=>Uint8Array.from({length:n},(_,i)=>(37*i+19)&255);
for(const type of types){
  test(`${type} default appearance, type and clone state`,()=>{
    const a=new api[type](),b=new api[type]();assert.equal(a.Type,api.EntityType[type]);assert.equal(a.ToString(),type);
    assert.equal(a.Layer.Name,'0');assert.equal(a.Linetype.Name,'ByLayer');assert.equal(a.Color.IsByLayer,true);assert.equal(a.Transparency.IsByLayer,true);
    assert.equal(a.LinetypeScale,1);assert.equal(a.IsVisible,true);assert.deepEqual(a.Normal,api.Vector3.UnitZ);
    assert.notEqual(a.Layer,b.Layer);assert.notEqual(a.Linetype,b.Linetype);assert.notEqual(a.Color,b.Color);
    a.ColorName='';a.ShadowMode=api.EntityShadowMode.Ignore;a.ProxyGraphics=bytes(129);a.IsVisible=false;
    const copy=a.Clone();assert.ok(copy instanceof api[type]);assert.equal(copy.ColorName,'');assert.equal(copy.ShadowMode,3);assert.deepEqual(copy.ProxyGraphics,a.ProxyGraphics);
    assert.equal(copy.IsVisible,false);assert.notEqual(copy.Layer,a.Layer);assert.notEqual(copy.Linetype,a.Linetype);assert.notEqual(copy.Color,a.Color);assert.notEqual(copy.Transparency,a.Transparency);
    assert.equal(copy.Handle,null);assert.equal(copy.Owner,null);
  });
  test(`${type} proxy bytes have copy-in/copy-out semantics and survive transforms`,()=>{
    const a=new api[type](),source=bytes(129);a.ProxyGraphics=source;source[0]^=255;assert.equal(a.ProxyGraphics[0],19);
    const output=a.ProxyGraphics;output[0]=200;assert.equal(a.ProxyGraphics[0],19);
    a.TransformBy(api.Matrix3.Scale(2),new api.Vector3(3,4,5));assert.deepEqual(a.ProxyGraphics,bytes(129));
    const clone=a.Clone();clone.ClearProxyGraphics();assert.equal(clone.ProxyGraphics,null);assert.deepEqual(a.ProxyGraphics,bytes(129));
    a.ProxyGraphics=new Uint8Array();assert.ok(a.ProxyGraphics instanceof Uint8Array);assert.equal(a.ProxyGraphics.length,0);a.ClearProxyGraphics();assert.equal(a.ProxyGraphics,null);
  });
}
test('entity abstract base and Matrix4 delegation preserve virtual dispatch',()=>{
  assert.throws(()=>new api.EntityObject(api.EntityType.Line,'LINE'),NotSupportedException);
  class Custom extends api.EntityObject{
    constructor(){super(api.EntityType.Line,'LINE');this.last=null;}
    TransformBy(matrix,translation){if(matrix instanceof api.Matrix4)return super.TransformBy(matrix);this.last=[matrix,translation];}
  }
  const entity=new Custom();entity.TransformBy(new api.Matrix4(1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16));
  assert.equal(entity.last[0].M33,11);assert.deepEqual(entity.last[1],new api.Vector3(4,8,12));
});
test('proxy graphics maximum, invalid input and shadow rejection do not corrupt prior state',()=>{
  const a=new api.Line();a.ProxyGraphics=bytes(3);a.ShadowMode=2;
  assert.throws(()=>{a.ProxyGraphics=new Uint8Array(api.EntityObject.MaximumProxyGraphicsBytes+1);},ArgumentOutOfRangeException);
  assert.deepEqual(a.ProxyGraphics,bytes(3));assert.throws(()=>{a.ProxyGraphics=[1,2,3];},ArgumentException);
  for(const value of [-1,4,NaN,Infinity,.5]){assert.throws(()=>{a.ShadowMode=value;},ArgumentOutOfRangeException);assert.equal(a.ShadowMode,2);}
  const maximum=new Uint8Array(api.EntityObject.MaximumProxyGraphicsBytes);maximum[maximum.length-1]=91;a.ProxyGraphics=maximum;
  assert.equal(a.ProxyGraphics.length,maximum.length);assert.equal(a.ProxyGraphics.at(-1),91);assert.notEqual(a.ProxyGraphics,maximum);
  a.CopyCommonDataTo(new api.Line());assert.throws(()=>a.CopyCommonDataTo(null),ArgumentNullException);
});
test('reactor view is live and distinct from persistent database reactors; clones start detached',()=>{
  const a=new api.Line(),registry=new api.ApplicationRegistry('R'),view=a.Reactors;assert.ok(Object.isFrozen(view));assert.equal(view.Add,undefined);
  a.AddReactor(registry);a.AddReactor(registry);assert.equal(view.Count,2);assert.equal(view.get_Item(0),registry);assert.equal(a.PersistentReactors.Count,0);
  const iterator=view.GetEnumerator();assert.equal(iterator.MoveNext(),true);a.RemoveReactor(registry);assert.throws(()=>iterator.MoveNext(),InvalidOperationException);
  assert.equal(view.Count,1);assert.equal(a.Clone().Reactors.Count,0);assert.equal(a.RemoveReactor(registry),true);assert.equal(a.RemoveReactor(registry),false);
});
test('entity events preserve replacement, exception rollback and clone isolation',()=>{
  const a=new api.Line(),replacement=new api.Layer('REPLACEMENT'),trace=[];
  const handler=(sender,e)=>{assert.equal(sender,a);trace.push([e.OldValue.Name,e.NewValue.Name]);e.NewValue=replacement;};
  a.LayerChanged.Add(handler);a.Layer=new api.Layer('PROPOSED');assert.equal(a.Layer,replacement);assert.deepEqual(trace,[['0','PROPOSED']]);
  const before=a.Layer,fail=()=>{throw new InvalidOperationException('fail');};a.LayerChanged.Add(fail);
  assert.throws(()=>{a.Layer=new api.Layer('FAIL');},InvalidOperationException);assert.equal(a.Layer,before);
  a.LayerChanged.Remove(fail);a.LayerChanged.Remove(handler);const clone=a.Clone();clone.Layer=new api.Layer('CLONED');assert.equal(a.Layer.Name,'REPLACEMENT');
  for(const property of ['Layer','Linetype','Color','Transparency']){const before=a[property];assert.throws(()=>{a[property]=null;},ArgumentNullException);assert.equal(a[property],before);}
});
test('line endpoints copy values, reverse preserves fields, Matrix4 uses column-vector translation',()=>{
  const start=new api.Vector3(1,2,3),end=new api.Vector3(4,5,6),line=new api.Line(start,end);start.X=99;end.X=88;line.StartPoint.X=77;
  assert.deepEqual(line.StartPoint,new api.Vector3(1,2,3));assert.deepEqual(line.Direction,new api.Vector3(3,3,3));line.Thickness=5;line.Reverse();line.Reverse();assert.equal(line.Thickness,5);
  line.TransformBy(new api.Matrix4(2,0,0,3,0,2,0,4,0,0,2,5,7,8,9,2));assert.deepEqual(line.EndPoint,new api.Vector3(11,14,17));
});
test('ray and construction-line directions normalize with pinned zero behavior',()=>{
  for(const Type of [api.Ray,api.XLine]){
    const item=new Type(api.Vector3.Zero,new api.Vector3(3,4,0));assert.deepEqual(item.Direction,api.Vector3.Normalize(new api.Vector3(3,4,0)));
    item.TransformBy(api.Matrix3.Zero,new api.Vector3(1,2,3));assert.deepEqual(item.Origin,new api.Vector3(1,2,3));assert.ok(item.Direction.IsNormalized);
    // The pinned Normalize returns NaN for zero, so the later IsZero guard does not throw.
    item.Direction=api.Vector3.Zero;assert.equal(api.Vector3.IsNaN(item.Direction),true);assert.equal(doubleBits(item.Direction.X),doubleBits(DotNetNaN));
  }
});
test('triangular faces duplicate the third vertex independently and retain edge flags',()=>{
  const third=new api.Vector3(5,6,7),face=new api.Face3D(new api.Vector3(1,2,3),new api.Vector3(3,4,5),third);third.X=99;
  assert.deepEqual(face.ThirdVertex,face.FourthVertex);face.ThirdVertex=new api.Vector3(8,9,10);assert.equal(face.FourthVertex.X,5);
  face.EdgeFlags=15;assert.equal(face.Clone().EdgeFlags,15);face.TransformBy(api.Matrix3.Identity,new api.Vector3(1,2,3));assert.equal(face.FourthVertex.Z,10);
});
test('solid and trace retain source-specific clone and planar transform behavior',()=>{
  for(const Type of [api.Solid,api.Trace]){
    const item=new Type(api.Vector2.Zero,api.Vector2.UnitX,api.Vector2.UnitY);assert.equal(item.SecondVertex.IsNormalized,false);
    item.Elevation=17;item.Thickness=4;assert.equal(item.Clone().Elevation,0);assert.equal(item.Clone().Thickness,4);
    item.TransformBy(api.Matrix3.Identity,new api.Vector3(1,2,3));assert.deepEqual(item.FirstVertex,new api.Vector2(1,2));assert.equal(item.Elevation,20);
  }
});
test('point rotation normalization and orthogonal transform retain exact observable results',()=>{
  const point=new api.Point(1,2,3);point.Rotation=-450;assert.equal(point.Rotation,270);point.Rotation=0;
  point.TransformBy(new api.Matrix3(0,-1,0,1,0,0,0,0,1),new api.Vector3(3,4,5));assert.deepEqual(point.Position,new api.Vector3(1,5,8));assert.equal(point.Rotation,90);
});
