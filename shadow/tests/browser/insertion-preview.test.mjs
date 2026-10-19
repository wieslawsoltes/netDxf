import test from 'node:test';
import assert from 'node:assert/strict';
import {InsertionPreview} from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/insertion-preview.mjs';

const data={svg:'<svg/>',imageBounds:{minX:-5,minY:-10,maxX:15,maxY:20}};
const session=()=>({name:'INSERT',version:0,command:{name:'INSERT',block:'Pump',points:[],scale:{x:2,y:3,z:1},rotation:30,basePoint:{x:3,y:4,z:0}}});
function fixture(read=async()=>data,decode=async()=>({close(){}})) {
  const requests=[],errors=[];let invalidations=0;
  const client={key:'drawing:revision',busy:0,state:{revision:3},data:{blocks:[{id:'pump-id',name:'Pump'}]},read:(...args)=>{requests.push(args);return read(...args);}};
  const preview=new InsertionPreview(client,{decode,error:error=>errors.push(error),invalidate:()=>invalidations++});
  return {client,preview,requests,errors,invalidations:()=>invalidations};
}
test('cursor motion reuses one isolated insertion render and anchors scaled graphics in world coordinates',async()=>{
  const f=fixture(),s=session();f.preview.sync(s);await f.preview.pending;
  const draws=[],context={save(){},restore(){},drawImage(...args){draws.push(args);}};
  for(let i=0;i<30;i++){f.preview.sync(s);f.preview.draw(context,{x:0,y:0,scale:2},{w:100,h:80},{x:i,y:7},{});}
  assert.equal(f.requests.length,1);assert.equal(f.invalidations(),1);
  assert.equal(f.requests[0][0],'insert-preview');
  const expected={...s.command,points:[{x:0,y:0,z:0}],reference:{value:'pump-id'}};delete expected.block;
  assert.deepEqual(f.requests[0][1].body.insert.command,expected);
  assert.deepEqual(draws.at(-1).slice(1),[98,-14,40,60]);
  s.command.points=[{x:20,y:10,z:0}];s.version++;f.preview.sync(s);assert.equal(f.requests.length,1);
  f.preview.draw(context,{x:0,y:0,scale:2},{w:100,h:80},{x:999,y:999},null);
  assert.deepEqual(draws.at(-1).slice(1),[80,-20,40,60]);
});
test('changed options and repeated insertion receipts replace the frame without reusing stale geometry',async()=>{
  let closed=0;const f=fixture(undefined,async()=>({close(){closed++;}})),s=session();
  f.preview.sync(s);await f.preview.pending;
  s.command.rotation=90;s.version++;f.preview.sync(s);await f.preview.pending;
  assert.equal(closed,1);assert.equal(f.requests.length,2);
  f.client.key='drawing:next';f.preview.sync(s);await f.preview.pending;
  assert.equal(closed,2);assert.equal(f.requests.length,3);
  f.preview.sync(null);assert.equal(closed,3);assert.equal(f.preview.frame,null);
});
test('cancelled and superseded preview decodes cannot replace current graphics',async()=>{
  let resolve,closed=0;const pending=new Promise(r=>resolve=r),f=fixture(undefined,()=>pending),s=session();
  f.preview.sync(s);const work=f.preview.pending;await Promise.resolve();await Promise.resolve();
  f.preview.sync(null);assert.equal(f.requests[0][1].signal.aborted,true);
  resolve({close(){closed++;}});await work;
  assert.equal(closed,1);assert.equal(f.preview.frame,null);assert.equal(f.invalidations(),0);
});
test('obsolete failures stay silent and malformed current bounds fail before image decoding',async()=>{
  let reject,decoded=0;const f=fixture(()=>new Promise((_,r)=>reject=r));
  f.preview.sync(session());const work=f.preview.pending;f.client.key='another drawing';reject(new Error('old failure'));await work;
  assert.equal(f.errors.length,0);
  const invalid=fixture(async()=>({...data,imageBounds:{minX:0,minY:0,maxX:Infinity,maxY:10}}),async()=>{decoded++;});
  invalid.preview.sync(session());await invalid.preview.pending;
  assert.equal(decoded,0);assert.match(invalid.errors[0].message,/invalid image bounds/);
});
