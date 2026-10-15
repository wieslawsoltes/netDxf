import { test } from 'node:test';
import assert from 'node:assert/strict';
import { number, parsePoint, point, distance, screen, world, zoom, constrain, polygonArea, CommandSession, commandName, commands } from '../../samples/netDxf.Shadow.Studio/wwwroot/cad/core.mjs';
import { CadClient, ApiError } from '../../samples/netDxf.Shadow.Studio/wwwroot/cad/client.mjs';

const near = (actual, expected) => assert(Math.abs(actual - expected) < 1e-8, `${actual} != ${expected}`);
for (const [source,value] of [['2+3*4',14],['(2+3)*4',20],['-2*-3',6],['1e3/4',250],['pi*2',Math.PI*2],['.5 + .25',.75]])
  test(`precision arithmetic: ${source}`,()=>near(number(source),value));
for (const source of ['', '1/0','1e99','Infinity','NaN','window.alert(1)','2**4','1 +','('.repeat(30)+'1'+')'.repeat(30)])
  test(`unsafe arithmetic refused: ${source}`,()=>assert.throws(()=>number(source)));
test('absolute, relative, polar and Z coordinate parsing',()=>{
  assert.deepEqual(parsePoint('10,20,3'),point(10,20,3));
  assert.deepEqual(parsePoint('@-5,4',point(10,20,3)),point(5,24,3));
  const p=parsePoint('@10<90',point(5,5));near(p.x,5);near(p.y,15);
  assert.deepEqual(parsePoint('#2,3',point(9,9)),point(2,3));
  assert.deepEqual(parsePoint('5',point(),point(3,4)),point(3,4));
});
test('coordinates reject missing directions and non-finite values',()=>{
  for(const text of ['1,','1,2,3,4','1e13,0','@1<2<3','@','bad,0'])assert.throws(()=>parsePoint(text));
  assert.throws(()=>parsePoint('4',point(),point()));
});
test('camera round trip and pointer-anchored zoom',()=>{
  const camera={x:132,y:-9,scale:2.3},size={w:1537,h:847},p=point(15,-7),pixel=point(320,180);
  const result=world(screen(p,camera,size),camera,size);near(result.x,p.x);near(result.y,p.y);
  const before=world(pixel,camera,size),after=world(pixel,zoom(camera,size,pixel,3),size);near(distance(before,after),0);
});
test('ortho, grid and polar constraints',()=>{
  const p=constrain(point(13,6),point(),{ortho:true});near(p.x,13);near(p.y,0);
  assert.deepEqual(constrain(point(13,6),null,{snap:true,gridStep:10}),point(10,10));
  const polar=constrain(point(10,9),point(),{polar:true});near(polar.x,polar.y);
});
test('polygon area is orientation independent',()=>{const p=[point(),point(10,0),point(10,10),point(0,10)];near(polygonArea(p),100);near(polygonArea(p.reverse()),100);});
test('all command aliases resolve without ambiguous names',()=>{
  const seen=new Set(Object.keys(commands));for(const [name,s] of Object.entries(commands))for(const alias of s.aliases){assert(!seen.has(alias));seen.add(alias);assert.equal(commandName(alias.toLowerCase()),name);}
});
test('point chains remain detached until explicit completion',()=>{
  const s=new CommandSession('L');s.input('0,0');s.input('@10,0');assert(!s.complete);s.input('U');assert.equal(s.command.points.length,1);assert.throws(()=>s.input(''));
  s.input('10,0');s.input('10,10');s.input('C');assert(s.complete);assert(s.command.closed);assert.equal(s.command.points.length,3);
});
test('spline requires four controls and never invents closed periodic geometry',()=>{
  const s=new CommandSession('SPL');for(const p of ['0,0','10,10','20,10'])s.input(p);assert.throws(()=>s.input(''));s.input('30,0');assert.throws(()=>s.input('C'));s.input('');assert(s.complete);
});
test('circle radius accepts a snapped point, polygon preserves picked orientation',()=>{
  const s=new CommandSession('C');s.pick(point(5,5));s.pick(point(8,9));assert.equal(s.command.value,5);assert(s.complete);
  const p=new CommandSession('POL');p.input('6');p.pick(point());p.pick(point(0,10));near(p.command.rotation,90);
});
test('invalid scalar does not consume a command prompt',()=>{
  const s=new CommandSession('C');s.input('0,0');assert.throws(()=>s.input('-1'));assert.equal(s.index,1);s.input('3');assert(s.complete);
});
test('ordered two-object modifiers require exactly two objects',()=>{
  const s=new CommandSession('TR');assert(s.selecting);assert.throws(()=>s.acceptSelection(1));assert.throws(()=>s.acceptSelection(3));s.acceptSelection(2);s.pick(point());assert(s.complete);
});
test('text input is data, not executable source',()=>{
  const s=new CommandSession('T');s.input('1,2');s.input('3');s.input('0');s.input('<script>alert(1)</script>');assert.equal(s.command.text,'<script>alert(1)</script>');assert(s.complete);
});
test('arrays enforce integral cardinalities before any transaction',()=>{
  const s=new CommandSession('AR','0',1);assert.throws(()=>s.input('2.5'));s.input('3');s.input('2');s.input('0,0');s.input('20,30');assert(s.complete);assert.equal(s.command.count,3);assert.equal(s.command.value,2);
});
const state=(revision=0,extra={})=>({state:{serverId:'server',documentId:'doc',workspaceId:'live',viewVersion:0,changeVersion:revision,revision,...extra}});
const response=data=>new Response(JSON.stringify(data),{headers:{'Content-Type':'application/json'}});
const deferred=()=>{let resolve;const promise=new Promise(r=>resolve=r);return{promise,resolve};};
test('writes serialize and obtain the next revision without replay',async()=>{
  let revision=0;const requests=[];
  const c=new CadClient(async(path,options)=>{if(path.endsWith('/state'))return response(state(revision));const body=JSON.parse(options.body);requests.push(body);assert.equal(body.revision,revision);return response(state(++revision));});
  await c.activate('a');await Promise.all([c.mutate('command',{command:{name:'LINE'}}),c.mutate('command',{command:{name:'CIRCLE'}})]);
  assert.deepEqual(requests.map(x=>x.revision),[0,1]);assert.equal(c.state.revision,2);assert.equal(c.busy,0);
});
test('a delayed activation cannot replace another drawing',async()=>{
  const a=deferred(),c=new CadClient(path=>path.includes('/a/')?a.promise:Promise.resolve(response(state(0,{documentId:'b'}))));
  const pending=c.activate('a');await c.activate('b');a.resolve(response(state()));await pending;assert.equal(c.id,'b');assert.equal(c.state.documentId,'b');
});
test('a delayed refresh cannot overwrite a mutation receipt',async()=>{
  const read=deferred();let reads=0;const c=new CadClient(async(path)=>path.endsWith('/state')?(++reads===1?response(state()):read.promise):response(state(1)));
  await c.activate('a');const pending=c.refresh();await c.mutate('command',{});read.resolve(response(state()));await pending;assert.equal(c.state.revision,1);
});
test('HTTP conflicts are not retried and do not poison the next command',async()=>{
  let calls=0;const c=new CadClient(async path=>path.endsWith('/state')?response(state()):++calls===1?new Response('{"error":"stale"}',{status:409}):response(state(1)));
  await c.activate('a');await assert.rejects(c.mutate('command',{}),e=>e instanceof ApiError&&e.status===409);assert.equal(calls,1);await c.mutate('command',{});assert.equal(c.state.revision,1);
});
test('switching documents while a write is in progress is refused',async()=>{
  const pending=deferred();const c=new CadClient(async path=>path.endsWith('/state')?response(state()):pending.promise);await c.activate('a');const write=c.mutate('command',{});await assert.rejects(c.activate('b'));pending.resolve(response(state(1)));await write;assert.equal(c.id,'a');
});
test('queued edits cannot cross a workspace transition',async()=>{
  const c=new CadClient(async path=>response(state(0,path.endsWith('/state')?{}:{workspaceId:'branch',viewVersion:1,changeVersion:1})));await c.activate('a');
  const transition=c.mutate('workspace',{id:'branch'}),edit=c.mutate('command',{});await transition;await assert.rejects(edit,e=>e.status===409);
});
test('receipts include all document/workspace headers',async()=>{
  let options;const c=new CadClient(async(path,o)=>{options=o;return response(state());});await c.activate('a');await c.mutate('select',{mode:'all'});
  assert.deepEqual(options.headers,{'X-Shadow-Studio':'1','X-Shadow-Document':'doc','X-Shadow-Workspace':'live','X-Shadow-View':'0','Content-Type':'application/json'});
});
test('stale read-only responses are dropped after a selection or document change',async()=>{
  const pending=deferred(),c=new CadClient(async path=>path.endsWith('/snap')?pending.promise:response(state()));await c.activate('a');const snap=c.read('snap',{body:{}});await c.activate('b');pending.resolve(response({position:point()}));assert.equal(await snap,null);
});
test('loading a document does not launch competing refreshes',async()=>{
  const pending=deferred();let calls=0;const c=new CadClient(()=>{calls++;return pending.promise;});const load=c.activate('a');await c.refresh();assert.equal(calls,1);pending.resolve(response(state()));await load;
});
test('final grip snap preserves source-selection exclusion after pointer release',async()=>{
  const { CadViewport }=await import('../../samples/netDxf.Shadow.Studio/wwwroot/cad/viewport.mjs');
  let body; const view={settings:{osnap:true,snapModes:23},client:{state:state().state,key:'a',busy:0,read:async(path,options)=>{body=options.body;return null;}},snapshot:null,camera:{scale:2},snapGeneration:0,invalidate(){}};
  await CadViewport.prototype.snapped.call(view,point(10,20),point(),true,true);
  assert.equal(body.excludeSelection,true); assert.equal(body.tolerance,6);
});
