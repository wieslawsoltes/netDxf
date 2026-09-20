import test from 'node:test';
import assert from 'node:assert/strict';
import {Group,GroupEntityChangeEventArgs,Line,Vector3,XData,XDataRecord,XDataCode,ApplicationRegistry} from '../../index.js';
import {ArgumentException,NullReferenceException,InvalidOperationException} from '../../runtime/Errors.js';
import {jsGeometry} from '../../tools/foundations-wire.mjs';
const source=()=>new Line(Vector3.Zero,Vector3.UnitX);

test('group constructors preserve unnamed and source name-validation behavior',()=>{
  const unnamed=new Group();assert.equal(unnamed.Name,'');assert.equal(unnamed.IsUnnamed,true);
  assert.throws(()=>Group.CreateOverload('string',null),NullReferenceException);
  assert.throws(()=>new Group('A/B'),ArgumentException);assert.equal(new Group(' Name ').Name,'Name');
  const imported=Group.CreateOverload('string,bool','*A12',false);assert.equal(imported.IsUnnamed,true);
  assert.equal(imported.Clone().Name,'');imported.Name='Named';assert.equal(imported.IsUnnamed,false);
  assert.throws(()=>new Group(' '),ArgumentException);
});
test('group membership shares entity identity while each group holds its own reactor',()=>{
  const line=source(),a=new Group('A',[line]),b=new Group([line]);
  assert.equal(a.Entities.get_Item(0),line);assert.equal(line.Reactors.Count,2);assert.equal(line.Owner,null);
  a.Entities.Remove(line);assert.deepEqual([...line.Reactors],[b]);b.Entities.Clear();assert.equal(line.Reactors.Count,0);
  assert.equal(a.Owner,null);assert.equal(a.HasReferences(),false);assert.equal(a.GetReferences(),null);
});
test('group rejects null/duplicate membership before mutation and preserves insertion semantics',()=>{
  const a=source(),b=source(),g=new Group('G',[a]);
  assert.throws(()=>g.Entities.Add(null),ArgumentException);assert.throws(()=>g.Entities.Add(a),ArgumentException);
  assert.equal(g.Entities.Count,1);assert.equal(a.Reactors.Count,1);
  const events=[];g.EntityRemoved.Add((_,e)=>events.push(['remove',e.Item]));g.EntityAdded.Add((_,e)=>events.push(['add',e.Item]));
  g.Entities.Insert(0,b);assert.deepEqual([...g.Entities],[b,a]);assert.equal(a.Reactors.Count,0);assert.equal(b.Reactors.Count,1);
  assert.deepEqual(events,[['remove',a],['add',b]]);
});
test('group cancellation keeps candidates detached and exceptions retain source mutation order',()=>{
  const g=new Group(),line=source();const cancel=(_,e)=>{e.Cancel=true;};g.Entities.BeforeAddItem.Add(cancel);
  assert.throws(()=>g.Entities.Add(line),ArgumentException);assert.equal(line.Reactors.Count,0);assert.equal(g.Entities.Count,0);
  g.Entities.BeforeAddItem.Remove(cancel);g.EntityAdded.Add(()=>{throw new InvalidOperationException();});
  assert.throws(()=>g.Entities.Add(line),InvalidOperationException);assert.equal(g.Entities.Count,1);assert.deepEqual([...line.Reactors],[g]);
});
test('group clones independent entity/XData graphs without adopting original handles or owners',()=>{
  const line=source(),g=new Group('G',[line]);g.Handle='A';line.Handle='B';g.Description='Description';g.IsSelectable=false;
  const data=new XData(new ApplicationRegistry('GROUP'));data.XDataRecord.Add(new XDataRecord(XDataCode.String,'source'));g.XData.Add(data);
  const q=g.Clone('COPY'),copy=q.Entities.get_Item(0);assert.notEqual(copy,line);assert.equal(copy.Handle,null);assert.equal(q.Handle,null);
  assert.equal(q.Description,'Description');assert.equal(q.IsSelectable,false);assert.deepEqual([...copy.Reactors],[q]);assert.deepEqual([...line.Reactors],[g]);
  copy.EndPoint=new Vector3(9,8,7);q.XData.get_Item('GROUP').XDataRecord.Clear();assert.equal(line.EndPoint.X,1);assert.equal(data.XDataRecord.Count,1);
});
test('group entity cloning precedes replacement-name validation',()=>{
  class TracedLine extends Line { Clone(){calls++;return super.Clone();} }
  let calls=0;const g=new Group('G',[new TracedLine()]);assert.throws(()=>g.Clone('A/B'),ArgumentException);assert.equal(calls,1);
});
test('group rename failures preserve unnamed flag and successful inherited callbacks retain order',()=>{
  const g=new Group(),events=[];const fail=()=>{throw new InvalidOperationException();};g.NameChanged.Add(fail);
  assert.throws(()=>{g.Name='New';},InvalidOperationException);assert.equal(g.IsUnnamed,true);assert.equal(g.Name,'');
  g.NameChanged.Remove(fail);g.NameChanged.Add((_,e)=>events.push([g.IsUnnamed,e.OldValue,e.NewValue]));g.Name='New';
  assert.deepEqual(events,[[true,'','New']]);assert.equal(g.IsUnnamed,false);
});
test('group entity collections invalidate existing iterators after mutation',()=>{
  const g=new Group('G',[source()]),it=g.Entities.GetEnumerator();it.MoveNext();g.Entities.Add(source());assert.throws(()=>it.MoveNext(),InvalidOperationException);
});
test('group event arguments keep a readonly entity reference and permit null',()=>{
  const line=source(),args=new GroupEntityChangeEventArgs(line);assert.equal(args.Item,line);assert.throws(()=>{args.Item=null;},TypeError);
  assert.equal(new GroupEntityChangeEventArgs(null).Item,null);
});
test('reflection observers cannot fabricate Cancel on noncancellable group notifications',()=>{
  const result=jsGeometry({steps:[{kind:'new',type:'Objects.Group',args:[],id:'g'},
    {kind:'observe',target:'g',member:'EntityAdded',observer:'change',cancel:false},
    {kind:'get',target:'g',member:'Entities',id:'entities'},
    {kind:'call',target:'entities',member:'Add',args:[{new:'Entities.Line',args:[]}]},
    {kind:'snapshot',target:'g'}]});
  assert.deepEqual(result[3],{ok:false,error:'NullReferenceException',param:null});assert.equal(result[4].value.entities.length,1);
});
test('group comparison corpus preserves unique identities and event/error scenarios',async()=>{
  const {groupCorpus}=await import('../../tools/group-corpus.mjs');const corpus=groupCorpus();assert.deepEqual(corpus,groupCorpus());
  assert.equal(new Set(corpus.map(p=>p.name)).size,211);assert.equal(corpus.reduce((n,p)=>n+p.request.steps.length,0),2409);
});
