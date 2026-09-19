import test from 'node:test';
import assert from 'node:assert/strict';
import { Vector2, Vector3, Vector4, Matrix3, Matrix4, BezierCurveCubic, BoundingRectangle, AciColor, Transparency, CloneValue } from '../../geometry.js';
import { ObservableCollection } from '../../netDxf/Collections/ObservableCollection.js';
import { EventHook } from '../../runtime/EventHook.js';
import { InvalidOperationException, ArgumentException } from '../../runtime/Errors.js';

test('geometry value copies isolate normalizations and cached matrix state',()=>{
  const a = new Vector3(3,4,0), b = Vector3.Normalize(a);
  assert.deepEqual(a.ToArray(),[3,4,0]); assert.equal(a.IsNormalized,false); assert.equal(b.IsNormalized,true);
  const clone=CloneValue(a);clone.X=9;assert.equal(a.X,3);
  const m=Matrix3.Identity,n=CloneValue(m);n.M11=2;
  assert.equal(m.IsIdentity,true);assert.equal(n.IsIdentity,false);
  assert.equal(Matrix3.Identity.M11,1);
});
test('geometry source-specific zero normalization is not silently unified',()=>{
  assert.deepEqual(Vector2.Normalize(Vector2.Zero).ToArray(),[0,0]);
  assert.ok(Vector3.Normalize(Vector3.Zero).ToArray().every(Number.isNaN));
});
test('AciColor ambiguous byte and normalized-double constructors are explicit',()=>{
  const bytes=AciColor.CreateOverload('byte,byte,byte',1,0,0);
  const linear=AciColor.CreateOverload('double,double,double',1,0,0);
  assert.equal(bytes.R,1);assert.equal(linear.R,255);
  const copy=linear.Clone();copy.Index=2;assert.equal(linear.R,255);
});
test('geometry bounds and Bezier points preserve source struct ownership',()=>{
  const p=new Vector3(1,2,3), curve=new BezierCurveCubic(p,new Vector3(2,4,3),new Vector3(3,4,3),new Vector3(4,2,3));
  p.X=99;assert.equal(curve.CalculatePoint(0).X,1);
  const q=curve.ControlPoints[0];q.X=42;
  // ControlPoints is the source mutable Vector3 array: element-field edits are retained.
  assert.equal(curve.CalculatePoint(0).X,42);
  const bounds=new BoundingRectangle(new Vector2(-1,-2),new Vector2(3,4));
  const min=bounds.Min;min.X=90;assert.equal(bounds.Min.X,-1);
});
test('events snapshot multicast handlers and remove the last duplicate subscription',()=>{
  const e=new EventHook(), log=[];const last=()=>log.push('last');
  const first=()=>{log.push('first');e.Remove(last);};e.Add(first);e.Add(last);e.Add(last);
  e.Invoke(null,null);assert.deepEqual(log,['first','last','last']);log.length=0;
  e.Invoke(null,null);assert.deepEqual(log,['first','last']);
});
test('observable collection replacement event order and cancellation',()=>{
  const c=new ObservableCollection();c.Add(1);const events=[];
  for(const n of ['BeforeRemoveItem','BeforeAddItem','AddItem','RemoveItem'])c[n].Add((_,e)=>events.push(n+':'+e.Item));
  c.set_Item(0,2);assert.deepEqual(events,['BeforeRemoveItem:1','BeforeAddItem:2','AddItem:2','RemoveItem:1']);
  c.BeforeAddItem.Add((_,e)=>{e.Cancel=true;});c.set_Item(0,3);assert.deepEqual(c.ToArray(),[2]);
  assert.throws(()=>c.Add(3),ArgumentException);
});
test('observable enumeration is fail-fast, including self AddRange',()=>{
  const c=new ObservableCollection();c.AddRange([1,2,3]);const e=c.GetEnumerator();assert.equal(e.MoveNext(),true);
  c.Reverse();assert.throws(()=>e.MoveNext(),InvalidOperationException);
  assert.throws(()=>c.AddRange(c),InvalidOperationException);assert.deepEqual(c.ToArray(),[3,2,1,3]);
});
test('observable Clear retains cancelled removals and matches event-driven semantics',()=>{
  const c=new ObservableCollection();c.AddRange([1,2,3]);c.BeforeRemoveItem.Add((_,e)=>{if(e.Item===2)e.Cancel=true;});
  c.Clear();assert.deepEqual(c.ToArray(),[2]);
});
test('observable value elements are copied on add, get and event access',()=>{
  const c=new ObservableCollection(),a=new Vector3(1,2,3);c.Add(a);a.X=9;
  c.get_Item(0).X=6;assert.equal(c.get_Item(0).X,1);
  c.AddItem.Add((_,e)=>{e.Item.X=20;});c.Add(new Vector3(2,3,4));assert.equal(c.get_Item(1).X,2);
});
test('observable introsort handles long random and reverse integer sequences',()=>{
  let seed=37;const values=Array.from({length:2048},()=>{seed=(Math.imul(seed,1664525)+1013904223)>>>0;return seed%79;});
  const c=new ObservableCollection();c.AddRange(values);c.Sort();assert.deepEqual(c.ToArray(),values.slice().sort((a,b)=>a-b));
  c.Sort((a,b)=>b-a);assert.deepEqual(c.ToArray(),values.slice().sort((a,b)=>b-a));
});
