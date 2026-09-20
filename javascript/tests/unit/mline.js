import test from 'node:test';
import assert from 'node:assert/strict';
import {MLine,MLineVertex,MLineStyle,MLineStyleElement,MLineStyleElementChangeEventArgs,ObservableCollection,Vector2,Vector3,Matrix3,Line,Arc,AciColor,Linetype,MLineStyleFlags,XData,XDataRecord,XDataCode,ApplicationRegistry} from '../../index.js';
import {ReferenceList} from '../../runtime/ReferenceList.js';
import {ArgumentException,ArgumentNullException,ArgumentOutOfRangeException,NullReferenceException,InvalidOperationException} from '../../runtime/Errors.js';
import {doubleBits,fromBits} from '../../tools/wire.mjs';
const vertices=()=>[new Vector2(0,0),new Vector2(4,0),new Vector2(4,3)];
const line=()=>new MLine(vertices());

test('default equality invokes non-reflexive reference Equals even on self',()=>{
  let calls=0;const item={Equals(){calls++;return false;}},c=new ObservableCollection();c.Add(item);
  assert.equal(c.Contains(item),false);assert.equal(c.IndexOf(item),-1);assert.equal(c.Remove(item),false);assert.ok(calls>=3);assert.equal(c.Count,1);
});
test('default equality handles null outside virtual Equals and preserves numeric NaN semantics',()=>{
  const c=new ObservableCollection(),item={Equals:()=>true};c.Add(item);assert.equal(c.Contains(null),false);c.Add(null);assert.equal(c.IndexOf(null),1);
  const numbers=new ObservableCollection();numbers.Add(NaN);assert.equal(numbers.Contains(NaN),true);assert.equal(numbers.Remove(NaN),true);
});
test('MLINE style element NaN/infinite offset membership follows source approximate equality',()=>{
  for(const n of [NaN,Infinity,-Infinity]){const e=new MLineStyleElement(n),s=new MLineStyle('S',[e]);assert.equal(e.Equals(e),false);assert.equal(s.Elements.Contains(e),false);assert.equal(s.Elements.Remove(e),false);assert.equal(s.Elements.Count,1);}
});
test('MLINE element CompareTo returns positive integer zero and descending numeric order',()=>{
  const e=new MLineStyleElement(-0);assert.ok(Object.is(e.CompareTo(new MLineStyleElement(0)),0));assert.equal(e.CompareTo(new MLineStyleElement(1)),1);
  assert.equal(new MLineStyleElement(NaN).CompareTo(new MLineStyleElement(1)),1);assert.throws(()=>e.CompareTo(null),ArgumentNullException);
});
test('MLINE element constructors share nullable resources but setters and cloning retain source guards',()=>{
  const color=AciColor.Red,lt=Linetype.Dashed,e=new MLineStyleElement(2,color,lt);assert.equal(e.Color,color);assert.equal(e.Linetype,lt);
  assert.notEqual(e.Clone().Color,color);assert.notEqual(e.Clone().Linetype,lt);
  const invalid=new MLineStyleElement(1,null,null);assert.equal(invalid.Color,null);assert.throws(()=>invalid.Clone(),NullReferenceException);assert.throws(()=>{invalid.Color=null;},ArgumentNullException);
});
test('MLINE style defaults are fresh sorted styles and allow source-valid NaN angles',()=>{
  const a=MLineStyle.Default,b=MLineStyle.Default;assert.notEqual(a,b);assert.equal(a.IsReserved,false);assert.deepEqual(Array.from(a.Elements,e=>e.Offset),[.5,-.5]);
  assert.throws(()=>new MLineStyle('S',[]),ArgumentOutOfRangeException);assert.throws(()=>new MLineStyle('S',[null]),ArgumentException);
  a.StartAngle=NaN;assert.ok(Number.isNaN(a.Clone().StartAngle));assert.throws(()=>{a.EndAngle=171;},ArgumentOutOfRangeException);assert.equal(a.EndAngle,90);
});
test('MLINE styles copy descriptions and preserve ordered deep-clone state',()=>{
  const s=new MLineStyle('S',[new MLineStyleElement(-2),new MLineStyleElement(2)],null);s.Description=null;assert.equal(s.Description,'');
  s.FillColor=AciColor.Red;s.Flags=64;s.StartAngle=45;const q=s.Clone('Q');assert.notEqual(q.FillColor,s.FillColor);assert.notEqual(q.Elements.get_Item(0),s.Elements.get_Item(0));assert.equal(q.Flags,64);assert.equal(q.StartAngle,45);
});
test('MLINE added notification occurs before subscribing to element linetype events',()=>{
  const s=new MLineStyle('S'),e=new MLineStyleElement(2),events=[];
  s.MLineStyleElementAdded.Add(()=>{events.push('added');e.Linetype=Linetype.Dashed;});s.MLineStyleElementLinetypeChanged.Add(()=>events.push('linetype'));
  s.Elements.Add(e);assert.deepEqual(events,['added']);e.Linetype=Linetype.Dot;assert.deepEqual(events,['added','linetype']);
});
test('MLINE notification failures leave source-defined partially applied subscriptions',()=>{
  const s=new MLineStyle('S'),e=new MLineStyleElement(2),events=[];s.MLineStyleElementAdded.Add(()=>{throw new InvalidOperationException();});s.MLineStyleElementLinetypeChanged.Add(()=>events.push('change'));
  assert.throws(()=>s.Elements.Add(e),InvalidOperationException);assert.equal(s.Elements.Count,3);e.Linetype=Linetype.Dot;assert.deepEqual(events,[]);
});
test('MLINE removal passes the equality probe to events rather than replacing it with the removed reference',()=>{
  const s=new MLineStyle('S'),original=s.Elements.get_Item(0),probe=new MLineStyleElement(.5),events=[];
  s.MLineStyleElementRemoved.Add((_,e)=>events.push(e.Item));s.Elements.Remove(probe);assert.equal(events[0],probe);
  s.MLineStyleElementLinetypeChanged.Add(()=>events.push('still-subscribed'));original.Linetype=Linetype.Dot;assert.equal(events[1],'still-subscribed');
});
test('MLINE styles support duplicate references with multicast subscription counts',()=>{
  const e=new MLineStyleElement(1),s=new MLineStyle('S',[e,e]);let calls=0;s.MLineStyleElementLinetypeChanged.Add(()=>calls++);e.Linetype=Linetype.Dot;assert.equal(calls,2);
  s.Elements.Remove(e);e.Linetype=Linetype.Dashed;assert.equal(calls,3);
});
test('MLINE style change events permit replacement and reject null assignments before callbacks',()=>{
  const m=line(),old=m.Style,replacement=new MLineStyle('Replacement');let calls=0;m.MLineStyleChanged.Add((_,e)=>{calls++;assert.equal(e.OldValue,old);e.NewValue=replacement;});
  assert.throws(()=>{m.Style=null;},ArgumentNullException);assert.equal(calls,0);m.Style=new MLineStyle('Proposed');assert.equal(m.Style,replacement);assert.equal(calls,1);
});
test('MLINE construction snapshots input points and retains C# single-point/empty behavior',()=>{
  const points=vertices(),m=new MLine(points);points[0].X=99;assert.equal(m.Vertexes.get_Item(0).Position.X,0);
  assert.throws(()=>new MLine([Vector2.Zero]),ArgumentOutOfRangeException);assert.equal(new MLine().Explode().Count,0);assert.throws(()=>new MLine().TransformBy(Matrix3.Identity,Vector3.Zero),ArgumentOutOfRangeException);
});
test('MLINE Update rebuilds vertex records without replacing the vertex list',()=>{
  const m=line(),list=m.Vertexes,old=list.get_Item(0),distance=old.Distances.get_Item(0).get_Item(0);m.Scale=3;assert.equal(old.Distances.get_Item(0).get_Item(0),distance);
  m.Update();assert.equal(m.Vertexes,list);assert.notEqual(list.get_Item(0),old);assert.equal(old.Distances.get_Item(0).get_Item(0),distance);
  assert.equal(list.get_Item(0).Distances.get_Item(0).get_Item(0),distance*3);
});
test('MLINE vertex vectors copy values while distance array/list references are live',()=>{
  const p=new Vector2(1,2),d=new ReferenceList([1,2]),distances=[d],v=new MLineVertex(p,Vector2.UnitX,Vector2.UnitY,distances);p.X=99;v.Position.Y=99;
  assert.deepEqual([v.Position.X,v.Position.Y],[1,2]);assert.equal(v.Distances,distances);d.Add(3);assert.equal(v.Distances[0].Count,3);
  const q=v.Clone();q.Distances[0].Clear();assert.equal(d.Count,3);assert.notEqual(q.Distances,distances);
});
test('MLINE break-distance lists split output segments without changing cached source data',()=>{
  const m=new MLine([Vector2.Zero,new Vector2(10,0)]),v=m.Vertexes.get_Item(0);v.Distances[0]=new ReferenceList([.5,1,2,4]);
  const output=m.Explode();assert.equal(output.Count,3);assert.equal(v.Distances[0].Count,4);
  assert.equal(output.get_Item(0).StartPoint.X,1);assert.equal(output.get_Item(0).EndPoint.X,2);assert.equal(output.get_Item(1).StartPoint.X,4);assert.equal(output.get_Item(1).EndPoint.X,10);
});
test('MLINE round caps split on resource differences and preserve line-versus-arc visibility contracts',()=>{
  const s=new MLineStyle('Caps');s.Flags=MLineStyleFlags.StartRoundCap|MLineStyleFlags.EndSquareCap;s.Elements.get_Item(0).Color=AciColor.Red;
  const m=new MLine([Vector2.Zero,new Vector2(10,0)],s,1);m.IsVisible=false;const output=Array.from(m.Explode());
  assert.equal(output.filter(v=>v instanceof Arc).length,2);assert.ok(output.filter(v=>v instanceof Arc).every(v=>v.IsVisible===false));assert.ok(output.filter(v=>v instanceof Line).every(v=>v.IsVisible===true));
});
test('MLINE explode retains the pinned elevation omission rather than inventing output translation',()=>{
  const m=new MLine([Vector2.Zero,new Vector2(10,0)]);m.Elevation=10;assert.ok(Array.from(m.Explode()).every(v=>v.StartPoint.Z===0&&v.EndPoint.Z===0));
});
test('MLINE transform replaces vertex records and scales every break distance',()=>{
  const m=line(),old=m.Vertexes.get_Item(0);old.Distances[0].Add(3);old.Distances[0].Add(4);m.TransformBy(Matrix3.Scale(2),new Vector3(7,8,9));
  assert.notEqual(m.Vertexes.get_Item(0),old);assert.equal(m.Scale,2);assert.equal(m.Elevation,9);assert.equal(m.Vertexes.get_Item(0).Distances[0].get_Item(2),6);assert.equal(old.Distances[0].get_Item(2),3);
});
test('MLINE clone retains common data but not handles or mutable resource aliases',()=>{
  const m=line();m.Handle='A';m.ColorName='Book';m.ProxyGraphics=Uint8Array.of(1,2);m.IsVisible=false;
  const data=new XData(new ApplicationRegistry('M'));data.XDataRecord.Add(new XDataRecord(XDataCode.String,'source'));m.XData.Add(data);const q=m.Clone();
  assert.equal(q.Handle,null);assert.equal(q.IsVisible,false);assert.equal(q.ColorName,'Book');assert.deepEqual([...q.ProxyGraphics],[1,2]);assert.notEqual(q.Style,m.Style);
  q.Vertexes.get_Item(0).Distances[0].Clear();q.XData.get_Item('M').XDataRecord.Clear();assert.equal(m.Vertexes.get_Item(0).Distances[0].Count,2);assert.equal(data.XDataRecord.Count,1);
});
test('MLINE scalar storage and style-element clones retain signed zero and NaN payloads',()=>{
  const m=new MLine();m.Scale=-0;m.Elevation=fromBits('FFF8000000001234');const q=m.Clone();assert.equal(doubleBits(q.Scale),'8000000000000000');assert.equal(doubleBits(q.Elevation),'FFF8000000001234');
  const e=new MLineStyleElement(fromBits('7FF8000000001234'));assert.equal(doubleBits(e.Clone().Offset),'7FF8000000001234');
});
test('MLINE event args expose readonly shared items',()=>{const e=new MLineStyleElement(1),args=new MLineStyleElementChangeEventArgs(e);assert.equal(args.Item,e);assert.throws(()=>{args.Item=null;},TypeError);});
test('MLINE corpus is deterministic and never supplies expected outputs',async()=>{
  const {mlineCorpus}=await import('../../tools/mline-corpus.mjs'),a=mlineCorpus();assert.deepEqual(a,mlineCorpus());assert.equal(new Set(a.map(x=>x.name)).size,a.length);assert.equal(a.length,789);assert.equal(a.reduce((n,p)=>n+p.request.steps.length,0),5123);assert.ok(a.every(x=>!('expected'in x)));
});
