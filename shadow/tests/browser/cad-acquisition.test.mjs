import { test } from 'node:test';
import assert from 'node:assert/strict';
import { CommandSession, point } from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/core.mjs';
import { CadClient } from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/client.mjs';
import { CadViewport } from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/viewport.mjs';

function deferred() { let resolve, reject; const promise = new Promise((a,b) => { resolve=a; reject=b; }); return {promise,resolve,reject}; }
const data = () => ({ state: { serverId:'server', documentId:'doc', workspaceId:'live', viewVersion:0, changeVersion:0, revision:0 } });
const response = value => new Response(JSON.stringify(value), { headers:{'Content-Type':'application/json'} });
function viewport(read = async () => null) {
  return Object.assign(Object.create(CadViewport.prototype), {
    settings:{osnap:true,snapModes:23}, client:{state:data().state,key:'a',epoch:0,busy:0,read},
    snapshot:null, camera:{x:0,y:0,scale:2}, size:{w:200,h:200}, inputVersion:1, snapGeneration:0,
    command:null, pointers:new Map([[1,point()]]), invalidate(){}, local:e=>point(e.clientX,e.clientY),
    element:{classList:{remove(){}},hasPointerCapture:()=>false,getBoundingClientRect:()=>({left:0})}, hooks:{}
  });
}
const release = {pointerId:1,clientX:120,clientY:90};
function gesture(view, extra = {}) {
  view.drag = {id:1,inputVersion:view.inputVersion,key:view.client.key,epoch:view.client.epoch,
    command:view.command,commandVersion:view.command?.version,snapshot:view.snapshot,
    pixel:point(110,100),first:point(5,0),...extra};
}

test('command versions fence point, undo, close and selection state changes', () => {
  const line = new CommandSession('L'); assert.equal(line.version,0);
  line.input('0,0'); line.input('10,0'); assert.equal(line.version,2);
  line.input('U'); assert.equal(line.version,3); assert.equal(line.index,1);
  line.input('10,0'); line.input('10,10'); line.input('C'); assert.equal(line.version,6);
  const move = new CommandSession('M'); move.acceptSelection(1); assert.equal(move.version,1);
});
test('invalid input never advances the command version', () => {
  const circle = new CommandSession('C'); circle.input('0,0'); const before = circle.version;
  assert.throws(() => circle.input('-10')); assert.equal(circle.version,before);
  assert.throws(() => circle.pick(point(0,0,NaN))); assert.equal(circle.version,before);
});
test('a rejected polygon radius does not alter detached rotation', () => {
  const polygon = new CommandSession('POL'); polygon.input('6'); polygon.pick(point());
  const before = JSON.stringify(polygon.command); assert.throws(() => polygon.pick(point()));
  assert.equal(JSON.stringify(polygon.command), before);
});
test('all non-finite coordinate axes are refused rather than defaulted to zero', () => {
  for(const axis of ['x','y','z']) for(const value of [NaN,Infinity,-Infinity]) {
    const command=new CommandSession('PO'); assert.throws(()=>command.pick({...point(),[axis]:value}));
    assert.equal(command.version,0); assert.deepEqual(command.command.points,[]);
  }
});
test('read receipt is invalid after reactivation of the same document and key', async () => {
  const delayed=deferred(), client=new CadClient(async path=>path.endsWith('/snap')?delayed.promise:response(data()));
  await client.activate('a'); const snap=client.read('snap',{body:{}}); await client.activate('a');
  delayed.resolve(response({position:point(3,4)})); assert.equal(await snap,null);
});
test('queued writes fence read responses before the native revision changes', async () => {
  const delayed=deferred(), write=deferred();
  const client=new CadClient(async path=>path.endsWith('/snap')?delayed.promise:path.endsWith('/state')?response(data()):write.promise);
  await client.activate('a'); const snap=client.read('snap',{body:{}}); const editing=client.mutate('command',{});
  delayed.resolve(response({position:point(3,4)})); assert.equal(await snap,null);
  write.resolve(response(data())); await editing;
});
test('hover acquisition cannot cancel a final snapped pick', async () => {
  const final=deferred(), hover=deferred(), requests=[];
  const view=viewport(async (_,options)=>{requests.push(options);return requests.length===1?final.promise:hover.promise;});
  const picked=view.snapped(point(10,20),point(),true), hovering=view.snapped(point(12,20),point());
  assert.equal(requests[0].signal.aborted,false);
  final.resolve({position:point(9,19)}); assert.deepEqual(await picked,point(9,19));
  hover.resolve(null); await hovering;
});
test('aborted final snaps are not converted into unsnapped edits', async () => {
  const view=viewport(async ()=>{throw new DOMException('Cancelled','AbortError');});
  await assert.rejects(view.snapped(point(10,20),point(),true), {name:'AbortError'});
});
test('final snaps reject a changed read generation', async () => {
  const delayed=deferred(), view=viewport(()=>delayed.promise);
  const picked=view.snapped(point(10,20),point(),true); view.client.epoch++;
  delayed.resolve({position:point(9,19)}); await assert.rejects(picked,/changed/);
});
test('cancelling input fences transports which ignore AbortSignal', async () => {
  const delayed=deferred(), view=viewport(()=>delayed.promise);
  const picked=view.snapped(point(10,20),point(),true); view.cancelInput();
  delayed.resolve({position:point(9,19)}); await assert.rejects(picked,/changed/);
  assert.equal(view.pointers.size,0); assert.equal(view.snapHit,null);
});
test('a drag begun in another drawing cannot select in the current drawing', async () => {
  const view=viewport(), errors=[]; let calls=0;
  view.hooks={select:()=>calls++,error:e=>errors.push(e)}; gesture(view); view.client.key='b';
  await view.up(release); assert.equal(calls,0); assert.match(errors[0].message,/changed/);
});
test('command advancement while snapping cannot consume the next prompt', async () => {
  const delayed=deferred(), view=viewport(()=>delayed.promise), errors=[]; let calls=0;
  view.command=new CommandSession('L'); view.hooks={pick:()=>calls++,error:e=>errors.push(e)}; gesture(view);
  const up=view.up(release); view.command.input('40,50'); delayed.resolve({position:point(9,19)}); await up;
  assert.equal(calls,0); assert.equal(view.command.command.points.length,1); assert.match(errors[0].message,/changed/);
});
test('Escape invalidates a pending grip even when both command references are null', async () => {
  const delayed=deferred(), view=viewport(()=>delayed.promise), errors=[]; let calls=0;
  view.hooks={grip:()=>calls++,error:e=>errors.push(e)}; gesture(view,{grip:{position:point()},moved:true});
  const up=view.up(release); view.cancelInput(); delayed.resolve(null); await up;
  assert.equal(calls,0); assert.equal(errors.length,1);
});
test('entering a retained snapshot during acquisition prevents a live edit', async () => {
  const delayed=deferred(), view=viewport(()=>delayed.promise); let calls=0;
  view.command=new CommandSession('L'); view.hooks={pick:()=>calls++,error(){}}; gesture(view);
  const up=view.up(release); view.snapshot=0; delayed.resolve(null); await up; assert.equal(calls,0);
});
test('a valid source grip is committed once using the final snap', async () => {
  const target=point(15,16), requests=[], calls=[], view=viewport(async (_,options)=>{requests.push(options.body);return {position:target};});
  const grip={id:'center',entity:'source',position:point()}; view.hooks={grip:(g,p)=>calls.push([g,p])}; gesture(view,{grip,moved:true});
  await view.up(release); assert.deepEqual(calls,[[grip,target]]); assert.equal(requests[0].excludeSelection,true);
});
test('a valid crossing gesture uses its original source receipt', async () => {
  const view=viewport(); let args;
  view.hooks={box:(...values)=>args=values}; gesture(view,{moved:true,extend:true,remove:false});
  await view.up({...release,clientX:90}); assert.equal(args[2],true); assert.equal(args[3],true); assert.equal(args[4],false);
});
test('selection windows add to the command selection without requiring Ctrl',async()=>{
  const view=viewport();view.command=new CommandSession('MOVE');let args;
  view.hooks={box:(...values)=>args=values};gesture(view,{moved:true,extend:false,remove:false});
  await view.up(release);assert.equal(args[3],true);assert.equal(args[4],false);
});
test('object snap queries the raw pointer so exact geometry can override Ortho',async()=>{
  let request;const view=viewport(async(_,options)=>{request=options.body;return {position:point(10,4)};});
  view.settings.ortho=true;
  assert.deepEqual(await view.snapped(point(10,4),point(),true),point(10,4));assert.deepEqual(request.point,point(10,4));
  view.client.read=async()=>null;assert.deepEqual(await view.snapped(point(10,4),point(),true),point(10,0));
});
test('STRETCH drag acquires both raw window corners without running object snaps',async()=>{
  let reads=0,args;const view=viewport(async()=>{reads++;return {position:point(999,999)};});
  view.command=new CommandSession('STRETCH');view.settings.ortho=true;view.settings.snap=true;
  view.hooks={window:(...values)=>args=values};gesture(view,{moved:true});
  await view.up(release);assert.equal(reads,0);assert.deepEqual(args,[point(5,0),point(10,5)]);
});
test('STRETCH two-click window passes raw corners to the command',async()=>{
  const view=viewport();view.command=new CommandSession('STRETCH');view.settings.snap=true;
  view.hooks={pick:p=>view.command.pick(p)};gesture(view);await view.up(release);
  assert.equal(view.command.index,1);assert.deepEqual(view.command.command.points,[point(10,5)]);
  gesture(view);await view.up({...release,clientX:110,clientY:100});
  assert(view.command.selecting);assert.deepEqual(view.command.command.points,[point(10,5),point(5,0)]);
});


test('background refresh remains suspended through pointer capture and final acquisition', async () => {
  const delayed=deferred(), view=viewport(()=>delayed.promise);
  assert.equal(view.inputActive,true);
  view.pointers.clear(); assert.equal(view.inputActive,false);
  const pending=view.snapped(point(10,20),point(),true);
  assert.equal(view.inputActive,true);
  delayed.resolve({position:point(9,19)}); await pending;
  assert.equal(view.inputActive,false);
});
test('cancelled final acquisition no longer blocks idle background refresh', async () => {
  const delayed=deferred(), view=viewport(()=>delayed.promise);
  view.pointers.clear(); const pending=view.snapped(point(10,20),point(),true);
  assert.equal(view.inputActive,true); view.cancelInput(); assert.equal(view.inputActive,false);
  delayed.resolve(null); await assert.rejects(pending,/changed/);
});
