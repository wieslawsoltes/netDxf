import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtemp,cp,rm} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import {join} from 'node:path';
import {pathToFileURL} from 'node:url';
import {byText,byLabel,gate,tick,installDom} from './ui-fixture.mjs';

// Exercise the same composed asset layout that native and WASM hosts publish.
const assets=await mkdtemp(join(tmpdir(),'netdxf-external-ui-'));
await cp(new URL('../../src/netDxf.Shadow.Studio.Web/wwwroot/agents',import.meta.url),join(assets,'agents'),{recursive:true});
await cp(new URL('../../src/netDxf.Shadow.Markdown/wwwroot',import.meta.url),join(assets,'markdown'),{recursive:true});
const {mountExternalAgents}=await import(pathToFileURL(join(assets,'agents/external-agents.mjs')));
test.after(()=>rm(assets,{recursive:true,force:true}));
const reply=value=>new Response(JSON.stringify(value),{headers:{'content-type':'application/json'}});
function fixture(t,override=()=>undefined,services={}){
  const root=installDom(t),original=globalThis.fetch,calls=[],errors=[],bindings=[];let document='first';
  globalThis.fetch=async(url,options)=>{
    const path=new URL(url).pathname,body=options.body?JSON.parse(options.body):null;calls.push({path,body});
    const custom=override(path,body,calls);if(custom!==undefined)return reply(await custom);
    if(path==='/agents/profiles')return reply({profiles:[{id:'codex',enabled:true}],workspace:'/fixture'});
    if(path==='/agents/start')return reply({protocol:'codex',initialization:{}});
    if(path==='/agents/call'){
      if(body.method==='account/read')return reply({account:null});
      if(body.method==='model/list')return reply({data:[{id:'fixture',model:'fixture',isDefault:true}]});
      if(body.method==='thread/start')return reply({thread:{id:'session'}});
      if(body.method==='turn/start')return reply({turn:{id:'turn'}});
      if(body.method==='turn/steer')return reply({turnId:'turn'});
      return reply({});
    }
    if(path.startsWith('/agents/events'))return reply({events:[],pending:[],connected:true});
    if(path==='/agents/stop')return reply({});
    assert.fail(path);
  };
  const ui=mountExternalAgents(root,{...services,connectDrawing:async()=>bindings.push(document),disconnectDrawing(){},getDocument:()=>document,report:error=>errors.push(error)});
  byLabel(root,'Coding agent companion access token').value='fixture-token';
  t.after(()=>{ui.dispose();globalThis.fetch=original;});
  return {root,ui,calls,errors,bindings,setDocument(value){document=value;},connect:()=>byText(root,'Connect local agent').click()};
}
test('sending reserves the turn before async session creation and ignores repeated Enter',async t=>{
  const session=gate();const f=fixture(t,(_path,body)=>body?.method==='thread/start'?session.promise:undefined);await f.connect();
  const input=byLabel(f.root,'Message to external coding agent');input.value='Draw a line';const sending=byText(f.root,'Send').click();await tick();
  assert.equal(byText(f.root,'Send').disabled,true);await input.dispatch('keydown',{key:'Enter'});await tick();
  assert.equal(f.calls.filter(c=>c.body?.method==='thread/start').length,1);
  session.resolve({thread:{id:'session'}});await sending;
  assert.equal(f.calls.filter(c=>c.body?.method==='turn/start').length,1);assert.equal(f.ui.running,true);
});
test('normal drawing receipts preserve an external turn; switching documents cancels and disables sending',async t=>{
  const f=fixture(t);await f.connect();byLabel(f.root,'Message to external coding agent').value='Draw a line';await byText(f.root,'Send').click();
  f.ui.documentChanged();await tick();assert.equal(f.ui.running,true);assert.equal(f.calls.filter(c=>c.body?.method==='turn/interrupt').length,0);
  f.setDocument('second');f.ui.documentChanged();await tick();assert.equal(f.ui.running,false);assert.equal(f.calls.filter(c=>c.body?.method==='turn/interrupt').length,1);assert.equal(byText(f.root,'Send').disabled,true);
  assert.match(f.root.textContent,/Reconnect the local agent/);
});
test('an obsolete connect response cannot start a process or bind a newer drawing',async t=>{
  const profiles=gate();let first=true;const f=fixture(t,path=>path==='/agents/profiles'&&first?(first=false,profiles.promise):undefined);
  const old=f.connect();await tick();f.setDocument('second');f.ui.documentChanged();await f.connect();
  profiles.resolve({profiles:[{id:'codex',enabled:true}],workspace:'/old'});await old;
  assert.equal(f.calls.filter(c=>c.path==='/agents/start').length,1);assert.deepEqual(f.bindings,['second']);assert.equal(byText(f.root,'Send').disabled,false);assert.deepEqual(f.errors,[]);
});
test('an old turn response after switching drawings cannot overwrite reconnect status',async t=>{
  const turn=gate();const f=fixture(t,(_path,body)=>body?.method==='turn/start'?turn.promise:undefined);await f.connect();byLabel(f.root,'Message to external coding agent').value='Draw';const sending=byText(f.root,'Send').click();await tick();
  f.setDocument('second');f.ui.documentChanged();turn.resolve({turn:{id:'obsolete'}});await sending;
  assert.match(f.root.textContent,/Reconnect the local agent/);assert.equal(byText(f.root,'Send').disabled,true);assert.deepEqual(f.errors,[]);
});

test('disconnect notification fences a pending session creation before it can start a turn',async t=>{
  const events=gate(),session=gate();const f=fixture(t,(path,body)=>path.startsWith('/agents/events')?events.promise:body?.method==='thread/start'?session.promise:undefined);
  await f.connect();byLabel(f.root,'Message to external coding agent').value='Draw';const sending=byText(f.root,'Send').click();await tick();
  events.resolve({events:[],pending:[],connected:false,status:'disconnected'});await tick();session.resolve({thread:{id:'obsolete'}});await sending;
  assert.equal(f.calls.filter(c=>c.body?.method==='turn/start'||c.body?.method==='session/prompt').length,0);assert.equal(f.ui.running,false);assert.equal(byText(f.root,'Send').disabled,true);assert.match(f.root.textContent,/local agent disconnected/);
});

test('steering waits for a real Codex turn ID, retains typed input and never starts a replacement turn',async t=>{
  const turn=gate();const f=fixture(t,(_path,body)=>body?.method==='turn/start'?turn.promise:undefined);await f.connect();
  const input=byLabel(f.root,'Message to external coding agent');input.value='Draw a line';const sending=byText(f.root,'Send').click();await tick();
  assert.equal(byText(f.root,'Steer active turn').disabled,true);
  turn.resolve({turn:{id:'turn'}});await sending;
  input.value='Use layer B';await byText(f.root,'Steer active turn').click();
  const steer=f.calls.find(c=>c.body?.method==='turn/steer').body;
  assert.deepEqual(steer.parameters,{threadId:'session',expectedTurnId:'turn',input:[{type:'text',text:'Use layer B'}]});
  assert.equal(input.value,'');assert.equal(f.ui.running,true);assert.equal(f.calls.filter(c=>c.body?.method==='turn/start').length,1);
});

test('failed steering keeps the draft and current turn available without replaying input',async t=>{
  const f=fixture(t,(_path,body)=>body?.method==='turn/steer'?Promise.reject(new Error('Expected turn is stale')):undefined);await f.connect();
  const input=byLabel(f.root,'Message to external coding agent');input.value='Draw';await byText(f.root,'Send').click();
  input.value='Keep this draft';await byText(f.root,'Steer active turn').click();
  assert.equal(input.value,'Keep this draft');assert.equal(f.ui.running,true);
  assert.equal(f.calls.filter(c=>c.body?.method==='turn/steer').length,1);assert.equal(f.calls.filter(c=>c.body?.method==='turn/start').length,1);
  assert.match(f.root.textContent,/Expected turn is stale/);
});

test('compaction acknowledgement keeps the UI busy until the actual turn completion',async t=>{
  const events=gate();let first=true;
  const f=fixture(t,path=>path.startsWith('/agents/events')&&first?(first=false,events.promise):undefined);await f.connect();
  assert.equal(byText(f.root,'Compact context').disabled,true);await byText(f.root,'New session').click();
  await byText(f.root,'Compact context').click();assert.equal(f.ui.running,true);assert.equal(byText(f.root,'Send').disabled,true);
  assert.equal(byText(f.root,'Steer active turn').disabled,true);assert.equal(byText(f.root,'Compact context').disabled,true);
  events.resolve({connected:true,pending:[],events:[
    {sequence:1,kind:'notification',method:'turn/started',data:{threadId:'session',turn:{id:'compact',status:'inProgress'}}},
    {sequence:2,kind:'notification',method:'item/completed',data:{threadId:'session',item:{id:'compact-item',type:'contextCompaction'}}},
    {sequence:3,kind:'notification',method:'turn/completed',data:{threadId:'session',turn:{id:'compact',status:'completed'}}}
  ]});await tick();
  assert.equal(f.ui.running,false);assert.equal(byText(f.root,'Send').disabled,false);assert.match(f.root.textContent,/Context compaction completed/);
  assert.equal(f.calls.filter(c=>c.body?.method==='thread/compact/start').length,1);assert.equal(f.calls.filter(c=>c.body?.method==='turn/start').length,0);
});

test('steering rechecks the active turn after asynchronous drawing-reference validation',async t=>{
  const events=gate(),validation=gate();let first=true,validations=0;
  const ref={documentId:'00000000-0000-0000-0000-000000000001',workspaceId:'live',revision:1,viewVersion:1,objectId:'entity-1',handle:'A1',type:'netDxf.Entities.Line',layout:'Model',occurrenceKey:'source/entity-1',insertPath:[]};
  const f=fixture(t,path=>path.startsWith('/agents/events')&&first?(first=false,events.promise):undefined,
    {drawingReferences:{capture:async()=>({references:[ref]}),validate:()=>++validations===1?Promise.resolve({canSend:true,items:[{reference:ref,current:ref,status:'current'}]}):validation.promise}});await f.connect();
  const input=byLabel(f.root,'Message to external coding agent');input.value='Draw';await byText(f.root,'Send').click();
  await byText(f.root,'Attach selected objects').click();input.value='Use selected object';const steering=byText(f.root,'Steer active turn').click();await tick();
  events.resolve({connected:true,pending:[],events:[{sequence:1,kind:'notification',method:'turn/completed',data:{threadId:'session',turn:{id:'turn',status:'completed'}}}]});await tick();
  validation.resolve({canSend:true,items:[{reference:ref,current:ref,status:'current'}]});await steering;
  assert.equal(f.calls.filter(c=>c.body?.method==='turn/steer').length,0);assert.equal(input.value,'Use selected object');assert.equal(f.ui.running,false);
});
