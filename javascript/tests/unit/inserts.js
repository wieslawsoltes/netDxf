import test from 'node:test';
import assert from 'node:assert/strict';
import {Block,Insert,Line,Circle,Ellipse,Text,Vector3,Matrix3,AttributeDefinition,Attribute,MathHelper} from '../../index.js';
import {ReferenceList} from '../../runtime/ReferenceList.js';
import {ArgumentException,ArgumentOutOfRangeException,InvalidOperationException,NotSupportedException} from '../../runtime/Errors.js';
const make=()=>{const a=new AttributeDefinition('TAG');a.Position=new Vector3(2,3,0);a.Value='default';const b=new Block('B',[new Line(Vector3.Zero,Vector3.UnitX)],[a]);b.Origin=new Vector3(1,2,0);return new Insert(b,new Vector3(10,20,0));};

test('INSERT construction copies placement but snapshots attributes with shared definitions',()=>{
 const i=make(),a=i.Attributes.get_Item(0),d=i.Block.AttributeDefinitions.get_Item('TAG');
 assert.equal(a.Owner,i);assert.equal(a.Definition,d);assert.deepEqual([a.Position.X,a.Position.Y,a.Position.Z],[11,21,0]);
 d.Value='changed';assert.equal(a.Value,'default');i.Position.X=99;assert.equal(i.Position.X,10);
});
test('INSERT synchronization retains values and existing same-tag definitions',()=>{
 const i=make(),old=i.Attributes,attribute=old.get_Item(0),definition=attribute.Definition;
 attribute.Value='edited';i.Block.AttributeDefinitions.Remove('TAG');const replacement=new AttributeDefinition('TAG');replacement.Position=new Vector3(99,99,0);i.Block.AttributeDefinitions.Add(replacement);i.Sync();
 assert.notEqual(i.Attributes,old);assert.equal(i.Attributes.get_Item(0),attribute);assert.equal(attribute.Definition,definition);assert.equal(attribute.Value,'edited');assert.equal(attribute.Position.X,11);
});
test('INSERT synchronization releases removed identity and owns new attributes before events',()=>{
 const i=make(),old=i.Attributes.get_Item(0),events=[];old.Handle='AB';i.Block.AttributeDefinitions.Remove('TAG');i.Block.AttributeDefinitions.Add(new AttributeDefinition('NEW'));
 i.AttributeRemoved.Add((_,e)=>events.push([e.Item.Owner,e.Item.Handle]));i.AttributeAdded.Add((_,e)=>events.push([e.Item.Owner,i.Attributes.Count]));i.Sync();
 assert.deepEqual(events,[[i,'AB'],[i,1]]);assert.equal(old.Owner,null);assert.equal(old.Handle,null);assert.equal(i.Attributes.get_Item(0).Owner,i);
});
test('INSERT failed synchronization observer keeps source-defined partial state',()=>{
 const i=make(),old=i.Attributes.get_Item(0),collection=i.Attributes;i.Block.AttributeDefinitions.Remove('TAG');i.AttributeRemoved.Add(()=>{throw new InvalidOperationException();});
 assert.throws(()=>i.Sync(),InvalidOperationException);assert.equal(i.Attributes,collection);assert.equal(old.Owner,i);
});
test('INSERT array enumeration is repeatable deferred and snapshots grid dimensions on first advance',()=>{
 const i=make();i.Block.AttributeDefinitions.Clear();i.Sync();i.ColumnCount=2;i.ColumnSpacing=4;
 const enumerable=i.ExplodeEnumerable();i.ColumnCount=3;const iterator=enumerable[Symbol.iterator](),first=iterator.next();assert.equal(first.done,false);
 i.ColumnCount=1;i.ColumnSpacing=99;const rest=Array.from(iterator);assert.equal(rest.length,2);assert.equal(rest[1].StartPoint.X-first.value.StartPoint.X,8);
 assert.equal(Array.from(enumerable).length,1);
});
test('billion-cell INSERT grid supports indexed and lazy access without allocation of all cells',()=>{
 const b=new Block('Large',[new Line(Vector3.Zero,Vector3.UnitX)]),i=new Insert(b);i.ColumnCount=32767;i.RowCount=32767;i.ColumnSpacing=3;i.RowSpacing=2;
 assert.equal(i.InstanceCount,1073676289);assert.equal(i.ExplodeCell(32766,32766).Count,1);const it=i.ExplodeEnumerable()[Symbol.iterator]();assert.equal(it.next().done,false);it.return();
 assert.equal(i.GetGridPosition(32766,32766).X,98298);
});
test('INSERT spacing is independent of scale and retained as signed-zero binary64',()=>{
 const i=make();i.ColumnCount=2;i.ColumnSpacing=4;i.Scale=new Vector3(100,-50,7);assert.equal(i.GetGridPosition(0,1).X-i.Position.X,4);
 i.RowSpacing=-0;assert.ok(Object.is(i.Clone().RowSpacing,-0));for(const value of [NaN,Infinity,-Infinity])assert.throws(()=>{i.RowSpacing=value;},ArgumentOutOfRangeException);assert.ok(Object.is(i.RowSpacing,-0));
});
test('INSERT array shear rejection retains position scale spacing and attribute positions',()=>{
 const i=make();i.ColumnCount=2;i.ColumnSpacing=3;const before=i.Clone();assert.throws(()=>i.TransformBy(new Matrix3(1,.5,0,0,1,0,0,0,1),Vector3.UnitX),NotSupportedException);
 assert.deepEqual(i.Position,before.Position);assert.deepEqual(i.Scale,before.Scale);assert.equal(i.ColumnSpacing,3);assert.deepEqual(i.Attributes.get_Item(0).Position,before.Attributes.get_Item(0).Position);
});
test('nonuniform INSERT converts circles and attributes without sharing cells',()=>{
 const i=make();i.Block.Entities.Add(new Circle(Vector3.Zero,2));i.Scale=new Vector3(2,3,1);i.ColumnCount=2;i.ColumnSpacing=5;i.TransformAttributes();
 const values=Array.from(i.Explode());assert.equal(values.length,6);assert.ok(values[1] instanceof Ellipse);assert.ok(values[2] instanceof Text);assert.equal(values[2].Value,'default');assert.equal(values[2].Owner,null);
 values[0].StartPoint=Vector3.UnitZ;assert.notDeepEqual(values[3].StartPoint,values[0].StartPoint);assert.notDeepEqual(i.Block.Entities.get_Item(0).StartPoint,values[0].StartPoint);
});
test('INSERT handle allocation visits attributes before the INSERT, not the block definition',()=>{
 const i=make();assert.equal(i.AssignHandle(32n),34n);assert.equal(i.Attributes.get_Item(0).Handle,'20');assert.equal(i.Handle,'21');assert.equal(i.Block.Handle,null);assert.equal(i.Block.Record.Handle,null);
});
test('INSERT internal constructor retains existing ownership rejection and readonly attribute storage',()=>{
 const a=new Attribute(new AttributeDefinition('T')),first=Insert.CreateOverload('System.Collections.Generic.List<netDxf.Entities.Attribute>',new ReferenceList([a]));
 assert.equal(a.Owner,first);assert.throws(()=>Insert.CreateOverload('System.Collections.Generic.List<netDxf.Entities.Attribute>',new ReferenceList([a])),ArgumentException);
 assert.equal(first.Attributes.Count,1);assert.equal(typeof first.Attributes.Add,'undefined');
});
test('block/INSERT differential retains all deterministic requests without expected outputs',async()=>{
 const {blockCorpus}=await import('../../tools/block-corpus.mjs'),{insertCorpus}=await import('../../tools/insert-corpus.mjs');const all=[...blockCorpus(),...insertCorpus()];
 assert.deepEqual(all,[...blockCorpus(),...insertCorpus()]);assert.equal(all.length,369);assert.equal(new Set(all.map(p=>p.name)).size,369);assert.equal(all.reduce((n,p)=>n+p.request.steps.length,0),5327);assert.ok(all.every(p=>!('expected'in p)));
});
