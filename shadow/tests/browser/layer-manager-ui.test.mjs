import test from 'node:test';
import assert from 'node:assert/strict';
import {layerManagerTools} from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/layer-manager-ui.mjs';
import {installDom,byText,byLabel,gate,tick,all} from './ui-fixture.mjs';

const layer=(id,name=id)=>({id:{value:id},name,description:'',visible:true,frozen:false,locked:false,plot:true,linetype:'Continuous',lineweight:'Default',color:'#FFFFFF',colorIndex:7,trueColor:false,transparency:0});
function fixture(t,{layers=[layer('0'),layer('A'),layer('B')],storage,read}={}) {
  const root=installDom(t),answer=gate(),edits=[],errors=[],saved=new Map();
  const client={key:'document/view/1',state:{documentId:'document'},busy:false,read:read||(async()=>({layers,layerStatus:layers.map(l=>({id:l.id.value,current:l.name==='0',uses:l.name==='A'?1:0})),layerIsolation:{active:false,valid:true}}))};
  let dialogs=0;
  document.getElementById=id=>id==='dialog'?{close:()=>answer.resolve(true)}:null;
  const api=layerManagerTools({client,editable(){},edit:async(...args)=>edits.push(args),editLayer:async(...args)=>edits.push(['editLayer',...args]),report:error=>errors.push(error.message),
    storage:storage||{getItem:key=>saved.get(key),setItem:(key,value)=>saved.set(key,value)},
    dialog:(title,children)=>{if(dialogs++)return Promise.resolve(false);root.replaceChildren(...children);return answer.promise;}});
  return {root,answer,edits,errors,saved,client,api};
}
async function include(f,name){const control=byLabel(f.root,`Include layer ${name}`);assert.ok(control);control.checked=true;await control.dispatch('change');}

test('property filters and groups only persist bounded local IDs and never edit native source',async t=>{
  const f=fixture(t),pending=f.api.open();await tick();
  byLabel(f.root,'Rule 1 Name pattern').value='A';await byLabel(f.root,'Rule 1 Name pattern').dispatch('input');
  assert.ok(byLabel(f.root,'Include layer A'));assert.equal(byLabel(f.root,'Include layer B'),undefined);
  byLabel(f.root,'Layer filter or group name').value='Process';await byText(f.root,'Save property filter').click();
  await include(f,'A');byLabel(f.root,'Layer filter or group name').value='Pipes';await byText(f.root,'Save checked as group').click();
  assert.equal(byLabel(f.root,'Layer filter').value,'g:Pipes');
  const saved=JSON.parse([...f.saved.values()][0]);assert.deepEqual(saved.groups[0].members,[{id:'A',name:'A'}]);assert.equal(saved.filters[0].rules[0].name,'A');
  f.answer.resolve(true);await pending;assert.deepEqual(f.edits,[]);assert.deepEqual(f.errors,[]);
});
test('checked isolation submits one native operation with opened identity and chosen mode',async t=>{
  const f=fixture(t),pending=f.api.open();await tick();await include(f,'A');await include(f,'B');
  byLabel(f.root,'Layer isolation mode').value='lock';await byText(f.root,'Isolate checked layers').click();await pending;
  assert.deepEqual(f.edits,[['layer-isolation',{isolation:{action:'isolate',mode:'lock',layers:[{value:'A'},{value:'B'}]}},{expectedKey:'document/view/1'}]]);
});
test('repeat group save replaces membership and group actions add/remove only checked stable IDs',async t=>{
  const f=fixture(t),pending=f.api.open();await tick();
  await include(f,'A');byLabel(f.root,'Layer filter or group name').value='Review';await byText(f.root,'Save checked as group').click();
  byLabel(f.root,'Layer filter').value='';await byLabel(f.root,'Layer filter').dispatch('change');
  await include(f,'B');byLabel(f.root,'Layer filter or group name').value='Review';await byText(f.root,'Save checked as group').click();
  let value=JSON.parse([...f.saved.values()][0]);assert.equal(value.groups.length,1);assert.deepEqual(value.groups[0].members.map(member=>member.id),['A','B']);
  await byText(f.root,'Clear checked layers').click();await include(f,'A');await byText(f.root,'Remove checked from group').click();
  value=JSON.parse([...f.saved.values()][0]);assert.deepEqual(value.groups[0].members.map(member=>member.id),['B']);
  await byText(f.root,'Add checked to group').click();value=JSON.parse([...f.saved.values()][0]);assert.deepEqual(value.groups[0].members.map(member=>member.id),['B','A']);
  f.answer.resolve(false);await pending;assert.deepEqual(f.edits,[]);assert.deepEqual(f.errors,[]);
});
test('selection commands omit explicit layers and guard busy state',async t=>{
  const f=fixture(t);await f.api.isolateSelection();await f.api.restore();
  assert.deepEqual(f.edits.map(edit=>edit[1]),[{isolation:{action:'isolate',mode:'off'}},{isolation:{action:'restore'}}]);
  f.client.busy=true;await assert.rejects(f.api.isolateSelection(),/Finish/);assert.equal(f.edits.length,2);
});
test('stale modal action and late resource result never write or open a different drawing',async t=>{
  const f=fixture(t),pending=f.api.open();await tick();await include(f,'A');f.client.key='other';
  await byText(f.root,'Isolate checked layers').click();assert.match(f.errors[0],/drawing changed/);f.answer.resolve(false);await pending;assert.deepEqual(f.edits,[]);
  const read=gate(),g=fixture(t,{read:()=>read.promise}),late=g.api.open();g.client.key='other';read.resolve({layers:[layer('X')]});await late;assert.equal(g.root.children.length,0);
});
test('paging retains checked identities outside the current page',async t=>{
  const f=fixture(t,{layers:Array.from({length:205},(_,i)=>layer(String(i)))}),pending=f.api.open();await tick();
  await include(f,'0');await byText(f.root,'Next layer page').click();await include(f,'100');
  await byText(f.root,'Next layer page').click();await include(f,'204');
  await byText(f.root,'Isolate checked layers').click();await pending;
  assert.deepEqual(f.edits[0][1].isolation.layers,[{value:'0'},{value:'100'},{value:'204'}]);
});
test('blocked storage does not pretend to save a filter or mutate drawing',async t=>{
  const f=fixture(t,{storage:{getItem(){return null;},setItem(){throw Error('full');}}}),pending=f.api.open();await tick();
  byLabel(f.root,'Layer filter or group name').value='Not saved';await byText(f.root,'Save property filter').click();
  assert.match(f.errors[0],/not be saved/);assert.equal(byLabel(f.root,'Layer filter').children.length,1);
  f.answer.resolve(false);await pending;assert.deepEqual(f.edits,[]);
});
test('invalid isolation metadata keeps property/filter access while refusing isolation',async t=>{
  const f=fixture(t,{read:async()=>({layers:[layer('A')],layerIsolation:{active:false,valid:false,notice:'Invalid receipt'}})}),pending=f.api.open();await tick();await include(f,'A');
  assert.equal(byText(f.root,'Isolate checked layers').disabled,true);assert.equal(byText(f.root,'Restore layer isolation').disabled,true);
  assert.equal(byText(f.root,'Edit layer properties').disabled,false);assert.ok(all(f.root).some(node=>node.textContent==='Invalid receipt'));
  f.answer.resolve(false);await pending;
});
