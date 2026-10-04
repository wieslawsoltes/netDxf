import test from 'node:test';
import assert from 'node:assert/strict';
import { EntityObject, EntityShadowMode, Point, Line, Ray, XLine, Face3D, Solid, Trace, Polyline2DVertex,
  Vector2, Vector3, Matrix3, Matrix4, Layer, Linetype, AciColor, Transparency, Lineweight,
  XData, ApplicationRegistry, XDataRecord, XDataCode } from '../../index.js';
import { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, InvalidOperationException, NotSupportedException } from '../../runtime/Errors.js';
const types=[Point,Line,Ray,XLine,Face3D,Solid,Trace];
const vector = v => [v.X,v.Y,...(v instanceof Vector3?[v.Z]:[])];

test('entity base is abstract and all concrete defaults are fresh',()=>{
  assert.throws(()=>new EntityObject(9,'LINE'),NotSupportedException);
  for(const Type of types){const a=new Type(),b=new Type();
    assert.equal(a.Owner,null);assert.equal(a.Handle,null);assert.equal(a.Color.IsByLayer,true);assert.equal(a.Linetype.IsByLayer,true);
    assert.equal(a.Lineweight,Lineweight.ByLayer);assert.equal(a.Transparency.IsByLayer,true);assert.equal(a.LinetypeScale,1);assert.equal(a.IsVisible,true);
    assert.deepEqual(vector(a.Normal),[0,0,1]);assert.notEqual(a.Layer,b.Layer);assert.notEqual(a.Linetype,b.Linetype);assert.notEqual(a.Color,b.Color);assert.notEqual(a.Transparency,b.Transparency);
    assert.equal(a.ColorName,null);assert.equal(a.ShadowMode,null);assert.equal(a.ProxyGraphics,null);assert.equal(a.Reactors.Count,0);
  }
});
test('entity null display setters leave existing references unchanged',()=>{
  for(const Type of types)for(const member of ['Color','Layer','Linetype','Transparency']){
    const e=new Type(),before=e[member];assert.throws(()=>{e[member]=null;},{name:'ArgumentNullException',ParamName:'value'});assert.equal(e[member],before);
  }
});
test('entity display events substitute references and run before assignment',()=>{
  for(const Type of types)for(const member of ['Layer','Linetype']){
    const e=new Type(),Factory=member==='Layer'?Layer:Linetype,old=e[member],proposed=new Factory('PROPOSED'),replacement=new Factory('REPLACEMENT');
    let count=0;e[member+'Changed'].Add((sender,args)=>{count++;assert.equal(sender,e);assert.equal(e[member],old);assert.equal(args.OldValue,old);assert.equal(args.NewValue,proposed);args.NewValue=replacement;});
    e[member]=proposed;assert.equal(e[member],replacement);assert.equal(count,1);
  }
});
test('throwing entity event callbacks preserve old values and callback null replacements match source',()=>{
  const e=new Line(),old=e.Layer,handler=()=>{throw new InvalidOperationException();};e.LayerChanged.Add(handler);
  assert.throws(()=>{e.Layer=new Layer('P');},InvalidOperationException);assert.equal(e.Layer,old);e.LayerChanged.Remove(handler);
  e.LayerChanged.Add((s,args)=>{args.NewValue=null;});e.Layer=new Layer('P');assert.equal(e.Layer,null);
});
test('linetype scale keeps source nonpositive-only validation without invented finite restriction',()=>{
  const e=new Line();for(const n of [-Infinity,-1,-0,0])assert.throws(()=>{e.LinetypeScale=n;},ArgumentOutOfRangeException);
  assert.equal(e.LinetypeScale,1);e.LinetypeScale=Infinity;assert.equal(e.Clone().LinetypeScale,Infinity);e.LinetypeScale=NaN;assert.ok(Number.isNaN(e.Clone().LinetypeScale));
});
test('normal values are copied and the pinned zero-normal NaN behavior is retained',()=>{
  const e=new Line(),n=new Vector3(0,3,4);e.Normal=n;n.X=99;assert.deepEqual(vector(e.Normal),[0,0.6000000000000001,0.8]);
  const out=e.Normal;out.X=88;assert.equal(e.Normal.X,0);e.Normal=Vector3.Zero;assert.ok(Vector3.IsNaN(e.Normal));
});
test('proxy graphics preserve null versus empty and isolate all caller and clone buffers',()=>{
  for(const Type of types){const e=new Type(),bytes=Uint8Array.of(1,2,255);e.ProxyGraphics=bytes;bytes[0]=99;assert.equal(e.ProxyGraphics[0],1);
    const out=e.ProxyGraphics;out[1]=88;assert.equal(e.ProxyGraphics[1],2);e.ColorName='';e.ShadowMode=EntityShadowMode.CastAndReceive;
    const copy=e.Clone();assert.equal(copy.ColorName,'');assert.equal(copy.ShadowMode,0);const changed=copy.ProxyGraphics;changed[0]=77;copy.ProxyGraphics=changed;assert.equal(e.ProxyGraphics[0],1);
    e.ProxyGraphics=new Uint8Array();assert.equal(e.Clone().ProxyGraphics.length,0);e.ClearProxyGraphics();assert.equal(e.ProxyGraphics,null);assert.equal(copy.ProxyGraphics[0],77);
  }
});
test('proxy maximum is enforced before mutation and accepts the exact inclusive limit',()=>{
  const e=new Line(),limit=EntityObject.MaximumProxyGraphicsBytes;assert.equal(limit,16*1024*1024);
  const bytes=new Uint8Array(limit);bytes[limit-1]=255;e.ProxyGraphics=bytes;assert.equal(e.ProxyGraphics[limit-1],255);
  assert.throws(()=>{e.ProxyGraphics=new Uint8Array(limit+1);},{name:'ArgumentOutOfRangeException',ParamName:'value'});assert.equal(e.ProxyGraphics.length,limit);
  assert.throws(()=>{e.ProxyGraphics=[1,2];},ArgumentException);assert.equal(e.ProxyGraphics.length,limit);e.ClearProxyGraphics();
});
test('shadow enum validation is atomic and common-data copy rejects a null destination',()=>{
  const e=new Line();e.ShadowMode=2;for(const bad of [-1,4,32767,0.5,NaN,Infinity,undefined,'0'])assert.throws(()=>{e.ShadowMode=bad;},ArgumentOutOfRangeException);
  assert.equal(e.ShadowMode,2);assert.throws(()=>e.CopyCommonDataTo(null),ArgumentNullException);e.ShadowMode=null;assert.equal(e.ShadowMode,null);
});
test('reactor read-only view is stable, live, independently stored and version checked',()=>{
  const e=new Line(),r=new Point(),view=e.Reactors;assert.equal(view,e.Reactors);assert.equal(view.Add,undefined);assert.equal(view.Remove,undefined);
  e.AddReactor(r);e.AddReactor(r);e.AddReactor(null);assert.equal(view.Count,3);assert.equal(view.get_Item(0),r);assert.equal(e.PersistentReactors.Count,0);
  const iterator=view.GetEnumerator();assert.equal(iterator.MoveNext(),true);e.RemoveReactor(r);assert.throws(()=>iterator.MoveNext(),InvalidOperationException);
  assert.deepEqual([...view],[r,null]);assert.equal(e.RemoveReactor(new Point()),false);assert.equal(e.Clone().Reactors.Count,0);
});
test('all primitive clones own display attributes, XData and geometry while remaining detached',()=>{
  for(const Type of types){const e=new Type(),data=new XData(new ApplicationRegistry('APP'));data.XDataRecord.Add(new XDataRecord(XDataCode.BinaryData,Uint8Array.of(3,4)));
    e.XData.Add(data);e.Handle='AB';e.Owner={CodeName:'BLOCK'};e.IsVisible=false;e.Layer.Description='source';e.AddReactor(new Point());
    const copy=e.Clone();assert.equal(copy.constructor,Type);assert.equal(copy.Owner,null);assert.equal(copy.Handle,null);assert.equal(copy.IsVisible,false);
    for(const member of ['Layer','Linetype','Color','Transparency'])assert.notEqual(copy[member],e[member]);
    assert.notEqual(copy.XData.get_Item('APP'),data);copy.XData.get_Item('APP').XDataRecord.Clear();assert.equal(data.XDataRecord.Count,1);
  }
});
test('line vector overloads and reverse have value semantics',()=>{
  const start=new Vector3(1,2,3),end=new Vector3(4,5,6),line=new Line(start,end);start.X=9;end.Y=9;
  assert.deepEqual(vector(line.StartPoint),[1,2,3]);line.StartPoint.X=8;assert.equal(line.StartPoint.X,1);assert.deepEqual(vector(line.Direction),[3,3,3]);
  line.Reverse();assert.deepEqual(vector(line.StartPoint),[4,5,6]);line.Reverse();assert.deepEqual(vector(line.StartPoint),[1,2,3]);
  const flat=new Line(new Vector2(1,2),new Vector2(3,4));assert.deepEqual(vector(flat.EndPoint),[3,4,0]);assert.throws(()=>new Line(Vector2.Zero,Vector3.Zero),ArgumentException);
});
test('point coordinate overloads and rotation normalization preserve source behavior',()=>{
  assert.deepEqual(vector(new Point(1,2,3).Position),[1,2,3]);assert.deepEqual(vector(new Point(new Vector2(2,3)).Position),[2,3,0]);
  const p=new Point();p.Rotation=-450;assert.equal(p.Rotation,270);p.Thickness=-2;assert.equal(p.Clone().Thickness,-2);
});
test('ray and xline normalize copied directions and retain zero-direction source behavior',()=>{
  for(const Type of [Ray,XLine]){const direction=new Vector3(0,0,2),e=new Type(new Vector3(1,2,3),direction);direction.X=8;
    assert.deepEqual(vector(e.Direction),[0,0,1]);e.Direction.Y=9;assert.equal(e.Direction.Y,0);e.Direction=Vector3.Zero;assert.ok(Vector3.IsNaN(e.Direction));
  }
});
test('three-vertex face constructors duplicate rather than alias the final coordinate value',()=>{
  for(const Type of [Face3D,Solid,Trace]){const p=new Vector2(3,4),e=new Type(Vector2.Zero,Vector2.UnitX,p);p.X=99;
    assert.equal(e.ThirdVertex.X,3);assert.equal(e.FourthVertex.X,3);e.ThirdVertex=Type===Face3D?new Vector3(7,8,9):new Vector2(7,8);assert.equal(e.FourthVertex.X,3);
  }
});
test('solid and trace keep pinned clone elevation omission and transform OCS elevation',()=>{
  for(const Type of [Solid,Trace]){const e=new Type(Vector2.Zero,Vector2.UnitX,Vector2.UnitY);e.Elevation=7;e.Thickness=2;
    const copy=e.Clone();assert.equal(copy.Elevation,0);assert.equal(copy.Thickness,2);
    e.TransformBy(Matrix3.Identity,new Vector3(3,4,5));assert.equal(e.Elevation,12);assert.deepEqual(vector(e.FirstVertex),[3,4]);assert.equal(e.Thickness,2);
  }
});
test('matrix4 transform selects affine top rows and leaves opaque metadata unchanged',()=>{
  const matrix=new Matrix4(2,0,0,3,0,-1,0,4,0,0,.5,5,9,8,7,6),e=new Line(Vector3.Zero,new Vector3(1,2,4));
  e.ProxyGraphics=Uint8Array.of(4,5,6);e.ColorName='Book';e.TransformBy(matrix);
  assert.deepEqual(vector(e.StartPoint),[3,4,5]);assert.deepEqual(vector(e.EndPoint),[5,2,7]);assert.deepEqual([...e.ProxyGraphics],[4,5,6]);assert.equal(e.ColorName,'Book');
});
test('singular transforms retain the prior normal and directed-entity direction',()=>{
  for(const Type of types){const e=new Type();e.TransformBy(Matrix3.Scale(0),new Vector3(3,4,5));assert.deepEqual(vector(e.Normal),[0,0,1]);
    if(e instanceof Ray||e instanceof XLine)assert.deepEqual(vector(e.Direction),[1,0,0]);}
});
test('base matrix4 adapter supports custom JavaScript subclasses implementing the two-argument overload',()=>{
  class Custom extends EntityObject {
    constructor(){super(9,'LINE');this.Point=Vector3.Zero;}
    TransformBy(...args){if(args.length===1)return super.TransformBy(args[0]);this.Point=Vector3.Add(Matrix3.Multiply(args[0],this.Point),args[1]);}
    Clone(){return this.$finishEntityClone(this.$copyEntityAttributes(new Custom()));}
  }
  const e=new Custom();e.TransformBy(new Matrix4(1,0,0,3,0,1,0,4,0,0,1,5,0,0,0,1));assert.deepEqual(vector(e.Point),[3,4,5]);
});
test('vertex width overrides distinguish omitted, explicit zero and signed zero',()=>{
  const v=new Polyline2DVertex(1,2,.5);assert.equal(v.StartWidthOverride,null);assert.equal(v.EndWidthOverride,null);assert.equal(v.VertexIdentifier,null);
  v.StartWidth=-0;v.EndWidth=0;v.VertexIdentifier=0;const copy=new Polyline2DVertex(v);assert.ok(Object.is(copy.StartWidthOverride,-0));assert.equal(copy.EndWidthOverride,0);assert.equal(copy.VertexIdentifier,0);
  copy.StartWidthOverride=null;copy.EndWidthOverride=null;assert.ok(Object.is(copy.StartWidth,0));assert.ok(Object.is(v.StartWidthOverride,-0));
});
test('vertex invalid widths leave raw values and presence flags unchanged',()=>{
  for(const key of ['StartWidth','EndWidth','StartWidthOverride','EndWidthOverride'])for(const value of [-1,-Number.MIN_VALUE,NaN,Infinity,-Infinity]){
    const v=new Polyline2DVertex();assert.throws(()=>{v[key]=value;},ArgumentOutOfRangeException);assert.equal(v.StartWidth,0);assert.equal(v.EndWidth,0);assert.equal(v.StartWidthOverride,null);assert.equal(v.EndWidthOverride,null);
  }
});
test('vertex copies retain identity and independent position values',()=>{
  const v=new Polyline2DVertex(new Vector2(3,4),-.25);v.VertexIdentifier=-2147483648;v.StartWidth=2.5;
  const q=v.Clone();q.Position=new Vector2(7,8);q.StartWidth=9;assert.equal(v.Position.X,3);assert.equal(v.StartWidth,2.5);assert.equal(q.VertexIdentifier,-2147483648);assert.equal(v.ToString(),'Polyline2DVertex: (3, 4)');
});
