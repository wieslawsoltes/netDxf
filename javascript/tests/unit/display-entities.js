import test from 'node:test';
import assert from 'node:assert/strict';
import { Text, Shape, Mesh, MeshEdge, Vector2, Vector3, Matrix3, Matrix4, TextStyle, ShapeStyle, TextAlignment, XData, XDataRecord, XDataCode, ApplicationRegistry } from '../../index.js';
import { ValueList } from '../../runtime/ValueList.js';
import { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, InvalidOperationException, NullReferenceException } from '../../runtime/Errors.js';
const shape=()=>new Shape('ZIG',new ShapeStyle('S','shapes.shx'));
const mesh=()=>new Mesh([Vector3.Zero,Vector3.UnitX,Vector3.UnitY],[[0,1,2]],[new MeshEdge(0,1)]);
const v3=v=>[v.X,v.Y,v.Z];
test('Text constructor overloads copy positions and inherit only initial font metrics',()=>{
  const position=new Vector2(2,3),style=new TextStyle('S','font.shx');style.WidthFactor=1.25;style.ObliqueAngle=-12;
  const text=new Text(null,position,2,style);position.X=99;
  assert.deepEqual(v3(text.Position),[2,3,0]);assert.equal(text.Value,null);assert.equal(text.WidthFactor,1.25);assert.equal(text.ObliqueAngle,-12);
  style.WidthFactor=2;assert.equal(text.WidthFactor,1.25);text.Style=TextStyle.Default;assert.equal(text.WidthFactor,1.25);
  assert.throws(()=>new Text('T',Vector3.Zero),ArgumentException);
});
test('Text constructor and setters preserve exact parameter names and failed-edit state',()=>{
  assert.throws(()=>new Text('T',Vector3.Zero,0),{name:'ArgumentOutOfRangeException',ParamName:'height',ActualValue:'T'});
  assert.throws(()=>new Text('T',Vector3.Zero,1,null),{name:'ArgumentNullException',ParamName:'style'});
  const t=new Text();for(const [key,value] of [['Height',0],['Width',0],['WidthFactor',.001],['ObliqueAngle',90]]){
    const before=t[key];assert.throws(()=>{t[key]=value;},ArgumentOutOfRangeException);assert.equal(t[key],before);
  }
  t.Height=NaN;assert.ok(Number.isNaN(t.Clone().Height));
});
test('style events fire before assignment, accept replacements, and retain old values on throw',()=>{
  for(const object of [new Text(),shape()]){
    const event=object instanceof Text?'TextStyleChanged':'StyleChanged',Ctor=object instanceof Text?TextStyle:ShapeStyle;
    const old=object.Style,proposed=new Ctor('P','font.shx'),replacement=new Ctor('R','font.shx');
    const handler=(sender,e)=>{assert.equal(sender,object);assert.equal(object.Style,old);assert.equal(e.OldValue,old);assert.equal(e.NewValue,proposed);e.NewValue=replacement;};
    object[event].Add(handler);object.Style=proposed;assert.equal(object.Style,replacement);object[event].Remove(handler);
    object[event].Add(()=>{throw new InvalidOperationException();});assert.throws(()=>{object.Style=proposed;},InvalidOperationException);assert.equal(object.Style,replacement);
    assert.throws(()=>{object.Style=null;},ArgumentNullException);
  }
});
test('Text reflection honors the explicit mirror setting and aligned/fit exceptions',()=>{
  const old=Text.DefaultMirrText;
  try{
    Text.DefaultMirrText=false;const plain=new Text();plain.TransformBy(Matrix3.Scale(-1,1,1),Vector3.Zero);
    assert.equal(plain.Alignment,TextAlignment.BaselineRight);assert.equal(plain.IsBackward,false);
    Text.DefaultMirrText=true;const mirrored=new Text();mirrored.TransformBy(Matrix3.Scale(-1,1,1),Vector3.Zero);assert.equal(mirrored.IsBackward,true);
    for(const alignment of [TextAlignment.Fit,TextAlignment.Aligned]){const t=new Text();t.Alignment=alignment;t.TransformBy(Matrix3.Scale(-1,1,1),Vector3.Zero);assert.equal(t.Alignment,alignment);assert.equal(t.IsBackward,true);}
  }finally{Text.DefaultMirrText=old;}
});
test('Text transforms use the existing owner drawing variable rather than detached default',()=>{
  const old=Text.DefaultMirrText;try{Text.DefaultMirrText=true;const t=new Text();t.Owner={Record:{Owner:{Owner:{DrawingVariables:{MirrText:false}}}}};
    t.TransformBy(Matrix3.Scale(-1,1,1),Vector3.Zero);assert.equal(t.IsBackward,false);assert.equal(t.Alignment,TextAlignment.BaselineRight);
  }finally{Text.DefaultMirrText=old;}
});
test('Text clone preserves authored metrics, both mirror flags and isolated font metadata',()=>{
  const t=new Text('Value',Vector3.Zero,1,new TextStyle('AUTHORED','font.shx'));Object.assign(t,{Height:2,Width:4,WidthFactor:.75,ObliqueAngle:-12,Rotation:37,Alignment:TextAlignment.TopRight,IsBackward:true,IsUpsideDown:true});
  const q=t.Clone();for(const key of ['Height','Width','WidthFactor','ObliqueAngle','Rotation','Alignment','IsBackward','IsUpsideDown','Value'])assert.equal(q[key],t[key]);
  assert.notEqual(q.Style,t.Style);q.Style.Name='OTHER';assert.equal(t.Style.Name,'AUTHORED');
});
test('Shape keeps raw constructor rotation and the pinned clone width-factor omission',()=>{
  const s=new Shape('Z',new ShapeStyle('S','s.shx'),Vector3.Zero,1,-450);s.WidthFactor=-2;
  assert.equal(s.Rotation,-450);const q=s.Clone();assert.equal(q.Rotation,270);assert.equal(q.WidthFactor,1);assert.equal(s.WidthFactor,-2);
  s.ObliqueAngle=-12;assert.equal(s.ObliqueAngle,348);assert.throws(()=>{s.WidthFactor=1e-13;},ArgumentOutOfRangeException);
});
test('Shape reflection and zero-scale transforms retain metadata and valid scalar guards',()=>{
  const s=shape();s.Thickness=-3;s.ProxyGraphics=Uint8Array.of(7);s.TransformBy(Matrix3.Scale(-1,1,1),Vector3.Zero);
  assert.ok(s.WidthFactor<0);assert.equal(s.Thickness,-3);assert.deepEqual(s.ProxyGraphics,Uint8Array.of(7));
  const zero=shape();zero.TransformBy(Matrix3.Scale(0),Vector3.Zero);assert.ok(zero.Size>0);assert.notEqual(zero.WidthFactor,0);
});
test('Mesh constructor owns collections but shares face and edge references as in C#',()=>{
  const v=new Vector3(1,2,3),face=[0,0,0],edge=new MeshEdge(0,0),vs=[v],fs=[face],es=[edge];const m=new Mesh(vs,fs,es);
  vs.length=0;fs.length=0;es.length=0;v.X=99;assert.equal(m.Vertexes.Count,1);assert.equal(m.Vertexes.get_Item(0).X,1);
  assert.equal(m.Faces.get_Item(0),face);assert.equal(m.Edges.get_Item(0),edge);face[0]=42;edge.Crease=5;assert.equal(m.Faces.get_Item(0)[0],42);assert.equal(m.Edges.get_Item(0).Crease,5);
});
test('Mesh clone deep-copies faces and edges and preserves blend/subdivision metadata',()=>{
  const m=mesh();m.BlendCrease=true;m.SubdivisionLevel=255;const q=m.Clone();
  q.Faces.get_Item(0)[0]=2;q.Edges.get_Item(0).Crease=5;q.Vertexes.set_Item(1,new Vector3(9,8,7));
  assert.equal(m.Faces.get_Item(0)[0],0);assert.equal(m.Edges.get_Item(0).Crease,0);assert.equal(m.Vertexes.get_Item(1).X,1);
  assert.equal(q.BlendCrease,true);assert.equal(q.SubdivisionLevel,255);
});
test('Mesh metadata validates the JavaScript byte domain and null collections fail precisely',()=>{
  assert.throws(()=>new Mesh(null,null),{name:'ArgumentNullException',ParamName:'vertexes'});assert.throws(()=>new Mesh([],null),{name:'ArgumentNullException',ParamName:'faces'});
  const m=mesh();for(const n of [-1,256,.5,NaN,undefined])assert.throws(()=>{m.SubdivisionLevel=n;},ArgumentOutOfRangeException);
  assert.equal(m.SubdivisionLevel,0);for(const member of ['Faces','Edges']){const bad=mesh();bad[member].Add(null);assert.throws(()=>bad.Clone(),NullReferenceException);}
});
test('Mesh TransformBy preserves topology and accepts the inherited Matrix4 adapter',()=>{
  const m=mesh(),face=m.Faces.get_Item(0),edge=m.Edges.get_Item(0);m.TransformBy(new Matrix4(2,0,0,3,0,2,0,4,0,0,2,5,9,8,7,6));
  assert.deepEqual(v3(m.Vertexes.get_Item(1)),[5,4,5]);assert.equal(m.Faces.get_Item(0),face);assert.equal(m.Edges.get_Item(0),edge);
});
test('value lists copy on every read/write path and retain enumerator invalidation',()=>{
  const v=new Vector3(1,2,3),list=new ValueList([v]);v.X=99;list.get_Item(0).X=88;assert.equal(list.get_Item(0).X,1);
  list.Add(v);v.Y=99;assert.equal(list.get_Item(1).Y,2);const items=list.ToArray();items[0].Z=99;assert.equal(list.get_Item(0).Z,3);
  for(const item of list)item.X=77;assert.equal(list.get_Item(0).X,1);const e=list.GetEnumerator();assert.equal(e.MoveNext(),true);e.Current.X=99;assert.equal(e.Current.X,1);
  list.set_Item(0,Vector3.Zero);assert.throws(()=>e.MoveNext(),InvalidOperationException);
  const copied=[null,null];list.CopyTo(copied);copied[0].Y=88;assert.equal(list.get_Item(0).Y,0);
});
test('new entity clones retain proxy graphics and independently cloned binary XData',()=>{
  for(const p of [new Text(),shape(),mesh()]){
    const data=new XData(new ApplicationRegistry('APP'));data.XDataRecord.Add(new XDataRecord(XDataCode.BinaryData,Uint8Array.of(1,2,3)));p.XData.Add(data);
    p.ProxyGraphics=Uint8Array.of(4,5);p.ColorName='BOOK';p.ShadowMode=2;const q=p.Clone();q.XData.get_Item('APP').XDataRecord.get_Item(0).Value[0]=9;
    assert.equal(p.XData.get_Item('APP').XDataRecord.get_Item(0).Value[0],1);assert.deepEqual(q.ProxyGraphics,Uint8Array.of(4,5));assert.equal(q.ColorName,'BOOK');assert.equal(q.ShadowMode,2);
  }
});
