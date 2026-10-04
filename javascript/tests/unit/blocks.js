import test from 'node:test';
import assert from 'node:assert/strict';
import { Block } from '../../netDxf/Blocks/Block.js';
import { BlockRecord } from '../../netDxf/Blocks/BlockRecord.js';
import { Line, Circle, Hatch, HatchBoundaryPath, HatchPattern, AttributeDefinition, Vector3, XData, XDataRecord, XDataCode, ApplicationRegistry, MText, MTextColumns } from '../../index.js';
import { ArgumentException, InvalidOperationException } from '../../runtime/Errors.js';
const line=()=>new Line(Vector3.Zero,Vector3.UnitX);

test('block owns distinct record/end objects and value-copied origins',()=>{
  const b=new Block(' B ');assert.equal(b.Name,'B');assert.equal(b.Record.Name,' B ');assert.equal(b.End.Owner,b);assert.equal(b.Record.Owner,null);
  const p=new Vector3(-0,2,3);b.Origin=p;p.Y=99;b.Origin.Z=99;assert.equal(b.Origin.Y,2);assert.equal(b.Origin.Z,3);assert.ok(Object.is(b.Origin.X,-0));
});
test('block admission preserves exclusive entity and attribute owners',()=>{
 const e=line(),a=new AttributeDefinition('TAG'),b=new Block('B',[e],[a]),other=new Block('OTHER');
 assert.equal(e.Owner,b);assert.equal(a.Owner,b);assert.equal(b.Flags&2,2);
 assert.throws(()=>b.Entities.Add(e),ArgumentException);assert.throws(()=>other.Entities.Add(e),ArgumentException);assert.throws(()=>other.AttributeDefinitions.Add(a),ArgumentException);
 assert.equal(b.Entities.Remove(e),true);assert.equal(e.Owner,null);other.Entities.Add(e);assert.equal(e.Owner,other);
 b.AttributeDefinitions.Clear();assert.equal(a.Owner,null);assert.equal(b.Flags&2,0);
});
test('block events observe pre-owner state and retain source post-mutation failures',()=>{
 const b=new Block('B'),e=line(),events=[];b.EntityAdded.Add((_,args)=>{events.push(args.Item.Owner);throw new InvalidOperationException();});
 assert.throws(()=>b.Entities.Add(e),InvalidOperationException);assert.deepEqual(events,[null]);assert.equal(b.Entities.Count,1);assert.equal(e.Owner,null);
});
test('block collection Insert retains the source nonstandard removal notification',()=>{
 const a=line(),c=line(),b=new Block('B',[a]);b.Entities.Insert(0,c);
 assert.equal(b.Entities.Count,2);assert.equal(b.Entities.get_Item(1),a);assert.equal(a.Owner,null);assert.equal(c.Owner,b);
});
test('block adoption attaches associative hatch sources and validates foreign owners first',()=>{
 const circle=new Circle(Vector3.Zero,2),path=new HatchBoundaryPath([circle]),h=new Hatch(HatchPattern.Solid,[path],true),b=new Block('B');
 b.Entities.Add(h);assert.equal(circle.Owner,b);assert.equal(h.Owner,b);assert.deepEqual([...b.Entities],[h,circle]);assert.equal(b.Entities.Remove(circle),false);
 h.UnLinkBoundary();assert.equal(b.Entities.Remove(circle),true);assert.equal(circle.Owner,null);
 const foreign=new Block('Foreign',[circle]),incoming=new Hatch(HatchPattern.Solid,[new HatchBoundaryPath([circle])],true);
 assert.throws(()=>b.Entities.Add(incoming),ArgumentException);assert.equal(incoming.Owner,null);assert.equal(circle.Owner,foreign);assert.equal(b.Entities.Count,1);
});
test('block cloning owns independent entities attributes and all three XData scopes',()=>{
 const b=new Block('B',[line()],[new AttributeDefinition('TAG')]);
 for(const object of [b,b.Record,b.End]){const d=new XData(new ApplicationRegistry('APP'));d.XDataRecord.Add(new XDataRecord(XDataCode.String,'payload'));object.XData.Add(d);}
 b.AssignHandle(10n);const q=b.Clone('Copy');
 assert.equal(q.Handle,null);assert.equal(q.Record.Handle,null);assert.equal(q.End.Handle,null);assert.equal(q.Entities.get_Item(0).Owner,q);assert.equal(q.AttributeDefinitions.get_Item('TAG').Owner,q);
 for(const [source,copy] of [[b,q],[b.Record,q.Record],[b.End,q.End]]){copy.XData.get_Item('APP').XDataRecord.Clear();assert.equal(source.XData.get_Item('APP').XDataRecord.Count,1);}
});
test('block handle assignment skips ordinary entities and orders record end attributes block',()=>{
 const b=new Block('B',[line()],[new AttributeDefinition('TAG')]);assert.equal(b.AssignHandle(16n),20n);
 assert.deepEqual([b.Record.Handle,b.End.Handle,b.AttributeDefinitions.get_Item('TAG').Handle,b.Handle],['10','11','12','13']);assert.equal(b.Entities.get_Item(0).Handle,null);
});
test('block clones deliberately reset source record units switches and xref path',()=>{
 const before=BlockRecord.DefaultUnits;try{
 BlockRecord.DefaultUnits=4;const b=new Block('X','drawing.dwg',true);b.Record.Units=6;b.Record.AllowExploding=false;b.Record.ScaleUniformly=true;
 const q=b.Clone('C');assert.equal(q.Record.Units,4);assert.equal(q.Record.AllowExploding,true);assert.equal(q.Record.ScaleUniformly,false);assert.equal(q.IsXRef,true);assert.equal(q.XrefFile,'');
 }finally{BlockRecord.DefaultUnits=before;}
});
test('block rename retains case-only record names and anonymous internal-use state',()=>{
 const b=new Block('B');b.Name='b';assert.equal(b.Name,'B');assert.equal(b.Record.Name,'b');
 const internal=Block.CreateOverload('string,System.Collections.Generic.IEnumerable<netDxf.Entities.EntityObject>,System.Collections.Generic.IEnumerable<netDxf.Entities.AttributeDefinition>,bool','*U1',null,null,false);
 internal.Flags=1;internal.Name='Named';assert.equal(internal.Flags&1,0);assert.equal(internal.IsForInternalUseOnly,true);assert.throws(()=>{internal.Name='Again';},ArgumentException);
 assert.throws(()=>{Block.ModelSpace.Name='Renamed';},ArgumentException);
});
test('block clone relinks column entities without retaining source object references',()=>{
 const text=new MText('abcd');text.Columns=new MTextColumns();text.Columns.Count=2;text.Columns.Width=2;text.Columns.Gutter=1;
 const columns=text.ConvertToLinkedColumns(['ab','cd']),b=new Block('B',columns),q=b.Clone('Q');
 const first=q.Entities.get_Item(0),linked=first.Columns.LinkedColumns.get_Item(0);
 assert.equal(linked.Owner,q);assert.ok(q.Entities.Contains(linked));assert.notEqual(linked,columns.get_Item(1));
});
