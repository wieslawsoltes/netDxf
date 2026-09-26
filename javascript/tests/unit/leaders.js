import test from 'node:test';
import assert from 'node:assert/strict';
import {Leader,MText,Text,Insert,Tolerance,ToleranceEntry,Block,Line,Circle,Vector2,Vector3,Matrix3,Matrix4,
  DimensionStyle,DimensionStyleOverride as Override,DimensionStyleOverrideType as O,BoxedString,BoxedBoolean,
  AciColor,MTextAttachmentPoint} from '../../index.js';
import {BoxedScalar} from '../../runtime/BoxedScalar.js';
import {ArgumentException,ArgumentNullException,ArgumentOutOfRangeException,InvalidOperationException,Exception} from '../../runtime/Errors.js';
import {doubleBits,fromBits} from '../../tools/wire.mjs';
const points=()=>[Vector2.Zero,new Vector2(4,3)];
const fresh=()=>new Leader(points());

test('LEADER snapshots input points and enforces source constructor guard order',()=>{
 const p=points(),l=new Leader(p);p[1].X=99;l.Vertexes.get_Item(1).Y=88;assert.deepEqual(l.Hook.ToArray(),[4,3]);
 assert.throws(()=>new Leader(null,null),{name:'ArgumentNullException',ParamName:'vertexes'});
 assert.throws(()=>new Leader([],null),{name:'ArgumentOutOfRangeException',ParamName:'vertexes'});
 assert.throws(()=>new Leader(points(),null),{name:'ArgumentNullException',ParamName:'style'});
});
test('LEADER text block and tolerance constructors build real annotated entities',()=>{
 const text=new Leader('Note',points());assert.ok(text.Annotation instanceof MText);assert.equal(text.Annotation.Value,'Note');assert.equal(text.HasHookline,true);assert.equal(text.Vertexes.Count,3);assert.equal(text.Annotation.Reactors.get_Item(0),text);
 const block=new Block('B'),insert=new Leader(block,points());assert.ok(insert.Annotation instanceof Insert);assert.equal(insert.Annotation.Block,block);assert.deepEqual(insert.Annotation.Position.ToArray(),[4,3,0]);
 const e=new ToleranceEntry(),t=new Leader(e,points());assert.ok(t.Annotation instanceof Tolerance);assert.equal(t.Annotation.Entry1,e);assert.equal(t.Annotation.Style,t.Style);
});
test('LEADER hookline toggling edits only the penultimate point and rechecks cardinality',()=>{
 const l=fresh();l.HasHookline=true;assert.equal(l.Vertexes.Count,3);assert.deepEqual(l.Hook.ToArray(),[4,3]);assert.deepEqual(l.Vertexes.get_Item(1).ToArray(),[4-l.Style.ArrowSize,3]);
 l.HasHookline=true;assert.equal(l.Vertexes.Count,3);l.HasHookline=false;assert.equal(l.Vertexes.Count,2);
 l.Vertexes.Clear();assert.throws(()=>{l.HasHookline=false;},Exception);assert.throws(()=>l.Update(true),Exception);assert.throws(()=>l.Clone(),ArgumentOutOfRangeException);
});
test('LEADER supports Text MText Insert and Tolerance annotations and rejects other entities atomically',()=>{
 const l=fresh(),text=new Text();l.Annotation=text;
 for(const unsupported of [new Line(),new Circle()]){assert.throws(()=>{l.Annotation=unsupported;},ArgumentException);assert.equal(l.Annotation,text);assert.equal(text.Reactors.get_Item(0),l);}
 for(const supported of [new MText(),new Insert(new Block('B')),new Tolerance()]){l.Annotation=supported;assert.equal(l.Annotation,supported);assert.equal(supported.Reactors.get_Item(0),l);}
 assert.equal(text.Reactors.Count,0);
});
test('LEADER annotation notifications observe source-ordered reactor changes and old identity',()=>{
 const l=fresh(),a=new Text(),b=new MText(),events=[];l.Annotation=a;
 l.AnnotationRemoved.Add((_,e)=>{events.push('removed');assert.equal(e.Item,a);assert.equal(l.Annotation,a);assert.equal(a.Reactors.Count,0);});
 l.AnnotationAdded.Add((_,e)=>{events.push('added');assert.equal(e.Item,b);assert.equal(l.Annotation,a);assert.equal(b.Reactors.get_Item(0),l);});
 l.Annotation=b;assert.deepEqual(events,['removed','added']);assert.equal(l.Annotation,b);l.Annotation=b;assert.equal(events.length,2);
});
test('throwing LEADER annotation callbacks retain partially applied source state',()=>{
 const l=fresh(),a=new Text(),b=new MText();l.Annotation=a;l.AnnotationAdded.Add(()=>{throw new InvalidOperationException();});
 assert.throws(()=>{l.Annotation=b;},InvalidOperationException);assert.equal(l.Annotation,a);assert.equal(a.Reactors.Count,0);assert.equal(b.Reactors.get_Item(0),l);
});
test('LEADER Update chooses whether annotation placement or the hook is authoritative',()=>{
 const l=fresh(),a=new MText();l.Annotation=a;l.Offset=new Vector2(2,3);l.Elevation=4;l.Update(true);
 assert.deepEqual(a.Position.ToArray(),[6.09,6,4]);assert.equal(a.Height,l.Style.TextHeight);assert.equal(a.AttachmentPoint,MTextAttachmentPoint.TopLeft);
 a.Position=new Vector3(10,12,99);l.Update(false);assert.deepEqual(l.Hook.ToArray(),[7.91,9]);assert.equal(a.Position.Z,99);
});
test('LEADER style overrides drive hook size text height gap and color without changing style defaults',()=>{
 const l=new Leader('Text',points()),style=l.Style;const height=style.TextHeight;l.StyleOverrides.Add(new Override(O.TextHeight,3));l.StyleOverrides.Add(new Override(O.DimScaleOverall,2));l.StyleOverrides.Add(new Override(O.ArrowSize,5));l.StyleOverrides.Add(new Override(O.TextOffset,-2));l.StyleOverrides.Add(new Override(O.TextColor,AciColor.Red));l.Update(true);
 assert.equal(l.Annotation.Height,6);assert.equal(style.TextHeight,height);assert.equal(l.Annotation.Color.Index,1);assert.deepEqual(l.Vertexes.get_Item(l.Vertexes.Count-2).ToArray(),[-6,3]);assert.deepEqual(l.Annotation.Position.ToArray(),[0,3,0]);
});
test('LEADER explicit boxed override values preserve shared identities and replacement cancellation',()=>{
 for(const [type,shared]of [[O.ArrowSize,new BoxedScalar('Double',2)],[O.DimLine1Off,new BoxedBoolean(true)],[O.DimPrefix,new BoxedString('shared')]]){
  const l=fresh(),a=new Override(type,shared),events=[];l.StyleOverrides.Add(a);l.DimensionStyleOverrideAdded.Add(()=>events.push('added'));l.StyleOverrides.set_Item(type,new Override(type,shared));assert.equal(l.StyleOverrides.get_Item(type),a);assert.deepEqual(events,[]);
 }
 const l=fresh(),old=new Override(O.ArrowSize,2);l.StyleOverrides.Add(old);const next=new Override(O.ArrowSize,2);l.StyleOverrides.set_Item(O.ArrowSize,next);assert.equal(l.StyleOverrides.get_Item(O.ArrowSize),next);
});
test('LEADER forwarded override callbacks run in the same replacement order and preserve failure mutation',()=>{
 const l=fresh(),a=new Override(O.ArrowSize,2),b=new Override(O.ArrowSize,3),events=[];l.StyleOverrides.Add(a);
 l.DimensionStyleOverrideAdded.Add((_,e)=>events.push(['add',e.Item]));l.DimensionStyleOverrideRemoved.Add((_,e)=>events.push(['remove',e.Item]));l.StyleOverrides.set_Item(O.ArrowSize,b);assert.deepEqual(events,[['add',b],['remove',a]]);
 l.DimensionStyleOverrideAdded.Add(()=>{throw new InvalidOperationException();});const c=new Override(O.ArrowSize,4);assert.throws(()=>l.StyleOverrides.set_Item(O.ArrowSize,c),InvalidOperationException);assert.equal(l.StyleOverrides.get_Item(O.ArrowSize),c);
});
test('LEADER style callback replacement and null validation preserve the old value until notification returns',()=>{
 const l=fresh(),old=l.Style,replacement=new DimensionStyle('Replacement');let count=0;
 l.LeaderStyleChanged.Add((_,e)=>{count++;assert.equal(l.Style,old);e.NewValue=replacement;});assert.throws(()=>{l.Style=null;},ArgumentNullException);l.Style=new DimensionStyle('Proposed');assert.equal(l.Style,replacement);assert.equal(count,1);
});
test('LEADER vertex lists expose value copies and fail-fast enumerators with value-type defaults',()=>{
 const l=fresh(),list=l.Vertexes,it=list.GetEnumerator();assert.deepEqual(it.Current.ToArray(),[0,0]);it.MoveNext();it.Current.X=99;assert.equal(list.get_Item(0).X,0);list.Add(new Vector2(8,9));assert.throws(()=>it.MoveNext(),InvalidOperationException);
 const empty=fresh().Vertexes;empty.Clear();const e=empty.GetEnumerator();assert.deepEqual(e.Current.ToArray(),[0,0]);assert.equal(e.MoveNext(),false);assert.deepEqual(e.Current.ToArray(),[0,0]);
});
test('LEADER transforms copy geometry and annotation while leaving its cached direction unchanged',()=>{
 const l=new Leader('Text',points());l.Offset=new Vector2(1,2);l.Elevation=5;l.Direction=new Vector2(3,4);const dir=l.Direction;
 l.TransformBy(Matrix3.Scale(2),new Vector3(7,8,9));assert.deepEqual(l.Hook.ToArray(),[15,14]);assert.equal(l.Elevation,19);assert.deepEqual(l.Offset.ToArray(),[2,4]);assert.deepEqual(l.Direction.ToArray(),dir.ToArray());
 l.TransformBy(Matrix4.Identity);assert.deepEqual(l.Hook.ToArray(),[15,14]);
});
test('LEADER clone retains source LineColor alias and deliberately resets the Direction cache',()=>{
 const l=new Leader('Source',points());l.Direction=new Vector2(3,4);l.LineColor=AciColor.Red;l.Handle='A';l.ProxyGraphics=Uint8Array.of(1,2);l.ColorName='Book';
 const q=l.Clone();assert.equal(q.Handle,null);assert.equal(q.LineColor,l.LineColor);assert.notEqual(q.Color,l.Color);assert.notEqual(q.Style,l.Style);assert.notEqual(q.Annotation,l.Annotation);assert.equal(q.Annotation.Reactors.get_Item(0),q);assert.deepEqual(q.Direction.ToArray(),[1,0]);assert.deepEqual([...q.ProxyGraphics],[1,2]);assert.equal(q.ColorName,'Book');
 q.LineColor.Index=3;assert.equal(l.LineColor.Index,3);q.Vertexes.Clear();assert.equal(l.Vertexes.Count,3);
});
test('LEADER elevation clones retain exact signed zero and quiet NaN payloads',()=>{
 for(const bits of ['8000000000000000','7FF8000000001234','FFF8000000005678']){const l=fresh();l.Elevation=fromBits(bits);assert.equal(doubleBits(l.Clone().Elevation),bits);}
});
test('block ownership admits leader annotations and reactors prevent removing live references',()=>{
 const l=new Leader('Note',points()),b=new Block('B'),a=l.Annotation;b.Entities.Add(l);assert.equal(l.Owner,b);assert.equal(a.Owner,b);assert.equal(b.Entities.Remove(a),false);
 l.Annotation=null;assert.equal(b.Entities.Remove(a),true);assert.equal(a.Owner,null);
});
test('LEADER comparison corpus keeps every input unique deterministic and free of golden outputs',async()=>{
 const {leaderCorpus}=await import('../../tools/leader-corpus.mjs');const c=leaderCorpus();assert.deepEqual(c,leaderCorpus());assert.equal(c.length,639);assert.equal(c.reduce((n,p)=>n+p.request.steps.length,0),7221);assert.equal(new Set(c.map(p=>p.name)).size,c.length);assert.ok(c.every(p=>!Object.hasOwn(p,'expected')));
});
