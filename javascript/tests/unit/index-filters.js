import test from 'node:test';
import assert from 'node:assert/strict';
import { DxfIdBuffer, DxfLayerIndex, DxfLayerIndexEntry, DxfLayerFilter, DxfSpatialIndex, DxfSpatialFilter,
  DxfDictionary, DxfPlaceholder, Point, Vector2, Vector3, Matrix4 } from '../../index.js';
import { ReferenceList } from '../../runtime/ReferenceList.js';
import { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, InvalidOperationException } from '../../runtime/Errors.js';
const entry=(name,buffer)=>new DxfLayerIndexEntry(name,buffer);
const xy=values=>Array.from(values,v=>[v.X,v.Y]);

test('IDBUFFER retains duplicate and null references, live order and independent clone shell',()=>{
  const b=new DxfIdBuffer(),p=new Point(),q=new Point();b.References.AddRange([p,null,p,q]);
  assert.deepEqual([...b.DatabaseReferences],[p,null,p,q]);assert.equal(b.References,b.DatabaseReferences);
  const c=b.CloneShell(),seen=[];b.CopyDatabaseReferencesTo(c,v=>{seen.push(v);return v===p?q:v;});
  assert.deepEqual(seen,[p,null,p,q]);assert.deepEqual([...c.References],[q,null,q,q]);
  c.References.Clear();assert.equal(b.References.Count,4);
});
test('checked Collection index guards precede validation and unsuccessful edits retain iterator version',()=>{
  const f=new DxfLayerFilter(['seed']),list=f.LayerNames,it=list.GetEnumerator();assert.equal(it.MoveNext(),true);
  for(const method of ['Insert','set_Item'])assert.throws(()=>list[method](-1,'bad\n'),{name:'ArgumentOutOfRangeException',ParamName:'index'});
  assert.throws(()=>list.set_Item(0,'bad\n'),{name:'ArgumentException',ParamName:'value'});
  assert.equal(it.MoveNext(),false);assert.equal(list.get_Item(0),'seed');
  const version=list.GetEnumerator();version.MoveNext();list.set_Item(0,'seed');assert.throws(()=>version.MoveNext(),InvalidOperationException);
});
test('stored names retain spelling, duplicates, whitespace and valid Unicode but reject unpaired code units',()=>{
  const input=['Alpha','Alpha','alpha','日本😀',' ','\t','a/b'],f=new DxfLayerFilter(input);input.length=0;
  assert.equal(f.LayerNames.Count,7);assert.deepEqual([...f.CloneShell().LayerNames],[...f.LayerNames]);
  for(const name of [null,'','a\0b','a\rb','a\nb','\ud800','\udc00','\ud800x']) {
    assert.throws(()=>f.LayerNames.Add(name),ArgumentException);
    assert.throws(()=>entry(name,new DxfIdBuffer()),ArgumentException);
  }
  assert.throws(()=>new DxfLayerFilter(null),ArgumentNullException);
});
test('index entries own distinct buffers, derive counts and expose immutable names',()=>{
  const i=new DxfLayerIndex(),a=new DxfIdBuffer(),b=new DxfIdBuffer(),e=entry('A',a);
  i.SetEntries([e,entry('A',b)]);assert.equal(a.Owner,i);assert.equal(b.Owner,i);a.References.AddRange([null,a,null]);assert.equal(e.Count,3);
  assert.throws(()=>{e.LayerName='B';},TypeError);assert.equal(i.Entries.set_Item,undefined);
  assert.deepEqual([...i.DeclaredOwnedObjects],[a,b]);assert.deepEqual([...i.DatabaseReferences],[a,b]);
});
test('replacement validates entire enumeration before releasing any existing owner',()=>{
  const i=new DxfLayerIndex(),a=new DxfIdBuffer(),b=new DxfIdBuffer();i.SetEntries([entry('A',a)]);const old=i.Entries;
  function* throwing(){yield entry('B',b);throw new Error('enumeration');}
  assert.throws(()=>i.SetEntries(throwing()),/enumeration/);assert.equal(a.Owner,i);assert.equal(b.Owner,null);
  assert.throws(()=>i.SetEntries([entry('B',b),entry('again',b)]),ArgumentException);assert.equal(b.Owner,null);
  i.SetEntries([entry('B',b)]);assert.equal(a.Owner,null);assert.equal(b.Owner,i);assert.equal(old.get_Item(0).Buffer,a);
  i.SetEntries([]);assert.equal(b.Owner,null);assert.equal(old.Count,1);
});
test('foreign ownership, erasure and cycles are rejected without partial adoption',()=>{
  const i=new DxfLayerIndex(),j=new DxfLayerIndex(),a=new DxfIdBuffer(),b=new DxfIdBuffer();j.SetEntries([entry('A',a)]);
  assert.throws(()=>i.SetEntries([entry('B',b),entry('A',a)]),ArgumentException);assert.equal(b.Owner,null);assert.equal(a.Owner,j);
  a.IsErased=true;assert.throws(()=>j.SetEntries([entry('A',a)]),InvalidOperationException);a.IsErased=false;
  i.Owner=b;assert.throws(()=>i.SetEntries([entry('cycle',b)]),ArgumentException);assert.equal(b.Owner,null);
});
test('registered-host adapter preserves the complete child set and invokes registration checks',()=>{
  // Supplemental structural-host test, not registered DxfDocument qualification.
  const i=new DxfLayerIndex(),a=new DxfIdBuffer(),b=new DxfIdBuffer();i.SetEntries([entry('A',a),entry('B',b)]);
  const seen=[],db={CheckRegistered(v){seen.push(v);}};i.Database=db;a.Database=db;b.Database=db;
  i.SetEntries([entry('renamed',b),entry('renamed',a)]);assert.deepEqual(seen,[i,b,i,a]);
  assert.throws(()=>i.SetEntries([entry('A',a)]),InvalidOperationException);assert.equal(i.Entries.Count,2);assert.equal(b.Owner,i);
  a.References.Add(b);assert.equal(seen.at(-1),b);
});
test('index cloning maps every child once and leaves ownership of originals intact',()=>{
  const i=new DxfLayerIndex(),a=new DxfIdBuffer(),b=new DxfIdBuffer();i.Timestamp=-0;i.SetEntries([entry('A',a)]);
  const copy=i.CloneShell();i.CopyDatabaseReferencesTo(copy,v=>{assert.equal(v,a);return b;});
  assert.equal(a.Owner,i);assert.equal(b.Owner,copy);assert.ok(Object.is(copy.Timestamp,-0));
  const errors=new ReferenceList();b.Owner=null;copy.ValidateDatabaseSchema(null,errors);assert.deepEqual([...errors],['LAYER_INDEX ownership is not reciprocal.']);
});
test('spatial finite values preserve signed zero, plane absence and nonnormalized normal magnitude',()=>{
  for(const Type of [DxfLayerIndex,DxfSpatialIndex]) {const i=new Type();i.Timestamp=-0;assert.ok(Object.is(i.CloneShell().Timestamp,-0));for(const v of [NaN,Infinity,-Infinity])assert.throws(()=>{i.Timestamp=v;},ArgumentOutOfRangeException);}
  const f=new DxfSpatialFilter();f.Normal=new Vector3(0,0,5);assert.equal(f.Normal.Z,5);f.Normal.Z=8;assert.equal(f.Normal.Z,5);
  f.FrontClippingDistance=-0;assert.ok(Object.is(f.CloneShell().FrontClippingDistance,-0));f.BackClippingDistance=-5;assert.equal(f.BackClippingDistance,-5);
  f.FrontClippingDistance=null;assert.equal(f.FrontClippingDistance,null);assert.throws(()=>{f.Normal=Vector3.Zero;},ArgumentException);
});
test('spatial boundary limits and iterable failures are atomic and snapshots copy vector values',()=>{
  const f=new DxfSpatialFilter(),before=f.Boundary,v=new Vector2(3,4);f.SetBoundary([v,new Vector2(5,6)]);v.X=99;
  const snapshot=f.Boundary;snapshot.get_Item(0).X=88;assert.deepEqual(xy(f.Boundary),[[3,4],[5,6]]);assert.deepEqual(xy(before),[[-1,-1],[1,1]]);
  function* bad(){yield new Vector2(7,8);throw new Error('failure');}assert.throws(()=>f.SetBoundary(bad()),/failure/);
  assert.throws(()=>f.SetBoundary([v,new Vector2(NaN,0)]),ArgumentOutOfRangeException);assert.deepEqual(xy(snapshot),[[3,4],[5,6]]);
  const max=Array.from({length:32767},()=>Vector2.Zero);f.SetBoundary(max);assert.equal(f.Boundary.Count,32767);
  assert.throws(()=>f.SetBoundary([...max,Vector2.UnitX]),ArgumentException);assert.equal(f.Boundary.Count,32767);
});
test('spatial matrices require finite affine values but retain singular affine matrices',()=>{
  const f=new DxfSpatialFilter(),m=Matrix4.Identity;m.M11=0;m.M22=0;f.InverseInsertTransform=m;m.M14=9;
  assert.equal(f.InverseInsertTransform.M14,0);f.InverseInsertTransform.M14=7;assert.equal(f.InverseInsertTransform.M14,0);
  const copy=f.CloneShell();assert.equal(copy.InverseInsertTransform.M11,0);
  for(const [key,value] of [['M41',1],['M44',0],['M12',NaN],['M24',Infinity]]) {const invalid=Matrix4.Identity;invalid[key]=value;assert.throws(()=>{f.ClipBoundaryTransform=invalid;},ArgumentException);}
  assert.equal(f.ClipBoundaryTransform.M44,1);
});
