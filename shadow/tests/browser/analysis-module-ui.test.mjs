import test from 'node:test';
import assert from 'node:assert/strict';
import {moduleTools} from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/module-ui.mjs';
import {analysisTools} from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/analysis-ui.mjs';
import {all,byText,byLabel,gate,tick,installDom} from './ui-fixture.mjs';

const descriptor={id:'pid',name:'P&ID',commands:[],standards:[]};
const state=()=>({documentId:'document',workspaceId:'live',revision:0,viewVersion:1,layout:'Model'});
const symbol=name=>({name,version:'1.0',category:'Equipment',units:'Millimeters',basePoint:{x:0,y:0,z:0},attributes:[],ports:[]});
const page=name=>({total:1,items:[symbol(name)]});
const analysis=(code,count=0)=>({records:Array.from({length:count},(_,i)=>({entity:{value:`H:${i}`},tag:`Item ${i}`,class:'Equipment',properties:{},ports:[]})),issues:[{code,message:code,entities:[]}],complete:true});
async function modules(t,section='symbols'){
  const root=installDom(t),reads=[],errors=[];
  const client={id:'drawing',state:state(),key:'current',data:{moduleConfigurationVersion:0,modules:[{id:'pid',enabled:true}],selectedEntities:[]},read(path,options){if(path==='modules')return Promise.resolve({modules:[{enabled:true,descriptor}]});const pending=gate();reads.push({path,options,...pending});return pending.promise;}};
  const ui=moduleTools({client,edit:async()=>{},dialog:async()=>false,report:error=>errors.push(error),editable(){},place(){},showPalette(){},changed(){},viewport:{camera:{},changed(){}}});
  await ui.sync();ui.open('pid',section);ui.render(root);return {root,reads,errors,client,ui};
}
test('newer symbol search wins even when an earlier search succeeds later',async t=>{
  const f=await modules(t);const search=byLabel(f.root,'Search symbol library');search.value='pump';const pending=byText(f.root,'Search').click();await tick();
  assert.equal(f.reads[1].options.body.query,'pump');f.reads[1].resolve(page('Current pump'));await pending;
  f.reads[0].resolve(page('Obsolete tank'));await tick();
  assert.match(f.root.textContent,/Current pump/);assert.doesNotMatch(f.root.textContent,/Obsolete tank/);assert.deepEqual(f.errors,[]);
  f.ui.render(f.root);assert.match(f.root.textContent,/Current pump/);assert.equal(f.reads.length,2);
});
test('superseded module failures do not report an error over the current results',async t=>{
  const f=await modules(t);const pending=byText(f.root,'Search').click();await tick();f.reads[1].resolve(page('Pump'));await pending;
  f.reads[0].reject(new Error('Obsolete failure'));await tick();assert.deepEqual(f.errors,[]);assert.match(f.root.textContent,/Pump/);
});
test('analysis keeps the latest tolerance and response across a palette redraw',async t=>{
  const f=await modules(t,'validation');const input=byLabel(f.root,'Connection tolerance');input.value='.02';const old=byText(f.root,'Run validation').click();await tick();input.value='.03';const latest=byText(f.root,'Run validation').click();await tick();
  f.reads[1].resolve(analysis('current.rule'));await latest;f.reads[0].resolve(analysis('old.rule'));await old;
  assert.match(f.root.textContent,/current.rule/);assert.doesNotMatch(f.root.textContent,/old.rule/);
  f.ui.render(f.root);assert.equal(byLabel(f.root,'Connection tolerance').value,'.03');assert.match(f.root.textContent,/current.rule/);
});
test('refreshing shorter module data returns a later page to the available records',async t=>{
  const f=await modules(t,'data');const initial=byText(f.root,'Refresh data').click();await tick();f.reads[0].resolve(analysis('ok',51));await initial;
  await byText(f.root,'Next records').click();assert.match(f.root.textContent,/Item 50/);
  const refreshed=byText(f.root,'Refresh data').click();await tick();f.reads[1].resolve(analysis('ok',1));await refreshed;
  assert.match(f.root.textContent,/Item 0/);assert.equal(byText(f.root,'Previous records').disabled,true);
});
test('saved module preferences cannot continue into a newly activated drawing',async t=>{
  installDom(t);globalThis.localStorage.getItem=()=>JSON.stringify({pid:false,piping:false});const writes=[];
  const client={id:'first',state:{documentId:'first'},key:'first-view',data:{moduleConfigurationVersion:0},read:async()=>client.id==='first'?{modules:[descriptor,{...descriptor,id:'piping'}].map(descriptor=>({descriptor,enabled:true}))}:null};
  const ui=moduleTools({client,changed(){},report(){},edit:async(...args)=>{writes.push(args);client.id='second';client.state={documentId:'second'};client.key='second-view';}});
  await ui.sync();await tick();assert.equal(writes.length,1);assert.equal(writes[0][2].expectedKey,'first-view');
});
const proposal=id=>({...state(),previewId:id,current:true,description:id,summary:{added:0,changed:1,removed:0,details:[]},beforeHealth:{complete:true,issues:[]},afterHealth:{complete:true,issues:[]},validation:'Preview validated'});
function previews(t){
  const root=installDom(t),lists=[],frames=[],edits=[],errors=[],urls=[];
  const create=URL.createObjectURL,revoke=URL.revokeObjectURL;URL.createObjectURL=()=>{const url=`blob:fixture-${urls.length}`;urls.push(url);return url;};URL.revokeObjectURL=()=>{};t.after(()=>{URL.createObjectURL=create;URL.revokeObjectURL=revoke;});
  const client={state:state(),key:'receipt-1',read(path){const pending=gate();(path==='edit-previews'?lists:frames).push({path,...pending});return pending.promise;}};
  const ui=analysisTools({client,editable(){},edit:async(...args)=>{edits.push(args);return {};},report:error=>errors.push(error),viewport:{camera:{},changed(){}}});ui.previews(root);t.after(()=>ui.destroy());return {root,client,lists,frames,edits,errors,urls,ui};
}
test('a superseded preview load stops before adding images, controls or later proposals',async t=>{
  const f=previews(t);f.lists[0].resolve([proposal('old A'),proposal('old B')]);await tick();assert.equal(f.frames.length,1);
  const x=byLabel(f.root,'Preview move X');x.value='1';await x.dispatch('input');const creating=byText(f.root,'Create move preview').click();await tick();f.lists[1].resolve([proposal('current')]);await tick();
  f.frames[1].resolve(new Blob(['before'],{type:'image/svg+xml'}));await tick();f.frames[2].resolve(new Blob(['after'],{type:'image/svg+xml'}));await creating;
  f.frames[0].resolve(new Blob(['obsolete'],{type:'image/svg+xml'}));await tick();
  assert.equal(f.frames.length,3);assert.equal(f.urls.length,2);assert.doesNotMatch(f.root.textContent,/old A|old B/);assert.equal(all(f.root).filter(n=>n.tagName==='IMG').length,2);assert.equal(all(f.root).filter(n=>n.tagName==='BUTTON'&&n.textContent==='Apply as one edit').length,1);
});
test('preview Apply remains bound to the receipt that displayed its before/after images',async t=>{
  const f=previews(t);f.lists[0].resolve([proposal('change')]);await tick();f.frames[0].resolve(new Blob(['before']));await tick();f.frames[1].resolve(new Blob(['after']));await tick();
  f.client.key='receipt-2';const applying=byText(f.root,'Apply as one edit').click();await tick();assert.equal(f.edits[0][2].expectedKey,'receipt-1');f.lists[1].resolve([]);await applying;
});
