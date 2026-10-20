import test from 'node:test';
import assert from 'node:assert/strict';
import {savedLayerPatch,savedLayerValues,editSavedLayers} from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/layer-state-editor.mjs';
import {importLayerState} from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/layer-state-transfer-ui.mjs';
import {layerStateTools} from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/layer-state-ui.mjs';
import {installDom,byText,byLabel,gate,tick} from './ui-fixture.mjs';
const entry={name:'Pipes',flags:0x4008,color:'#FFFFFF',colorIndex:7,trueColor:false,linetype:'Continuous',lineweight:'Default',transparency:0,storedAlphaValue:0};
const resources={layers:[{name:'0'},{name:'Pipes'},{name:'Added'}],linetypes:[{status:{name:'Continuous'}}]};
function fixture(t) {
  const root=installDom(t),answers=[],edits=[],reads=[];const client={key:'drawing/1',busy:false,state:{revision:4},read:async(path,options)=>{reads.push([path,options]);return null;}};
  const dialog=(title,children)=>{const answer=gate();answers.push({title,...answer});root.replaceChildren(...children);return answer.promise;};
  document.getElementById=()=>({close:()=>answers.at(-1).resolve(true)});
  return {root,client,answers,edits,reads,dialog,edit:async(...args)=>edits.push(args),editable(){},key:client.key,resources};
}
test('saved property patches preserve unedited native flags and imported alpha',()=>{
  const values=savedLayerValues(entry);assert.equal(savedLayerPatch(entry,values),null);
  assert.deepEqual(savedLayerPatch(entry,{...values,locked:true}),{name:'Pipes',locked:true});
  assert.deepEqual(savedLayerPatch(entry,{...values,colorMode:'RGB',color:'#123456'}),{name:'Pipes',color:'#123456'});
  assert.throws(()=>savedLayerPatch(entry,{...values,transparency:'100'}),/between/);
});
test('saved mask initializes checkboxes and saves separately from restore',async t=>{
  const f=fixture(t);f.client.read=async()=>({layerStates:[{id:{value:'s:1'},name:'Only color',description:'',currentLayer:'0',layers:['0'],fields:64}]});
  const pending=layerStateTools(f)();await tick();
  assert.equal(byLabel(f.root,'Restore Color').checked,true);assert.equal(byLabel(f.root,'Restore On / Off').checked,false);
  byLabel(f.root,'Restore Locked / Unlocked').checked=true;await byText(f.root,'Save restore mask').click();await pending;
  assert.deepEqual(f.edits,[['layer-state',{state:{action:'options',id:{value:'s:1'},fields:68}},{expectedKey:'drawing/1'}]]);
});
test('saved entry form submits changed values and add/remove membership in one transaction',async t=>{
  const f=fixture(t);f.client.read=async()=>({state:{currentLayer:'0'},entries:[entry,{...entry,name:'0'}]});
  const pending=editSavedLayers({...f,state:{id:{value:'s:1'}}});await tick();
  assert.equal(byLabel(f.root,'Drawing layer to add').parentNode.tagName,'LABEL');
  assert.equal(byLabel(f.root,'Pending saved additions').parentNode.hidden,true);
  byLabel(f.root,'Saved Locked').checked=true;byLabel(f.root,'Saved transparency').value='20';
  const list=byLabel(f.root,'Saved layer entries');list.value='0';await list.dispatch('change');await byText(f.root,'Remove saved layer').click();
  byLabel(f.root,'Drawing layer to add').value='Added';await byText(f.root,'Add drawing layer').click();
  assert.equal(byLabel(f.root,'Pending saved additions').parentNode.hidden,false);
  f.answers[0].resolve(true);await pending;
  assert.deepEqual(f.edits,[['layer-state',{state:{action:'edit',id:{value:'s:1'},entries:[{name:'Pipes',locked:true,transparency:20}],addLayers:['Added'],removeLayers:['0']}},{expectedKey:'drawing/1'}]]);
});
test('late saved entry details cannot mount into another drawing',async t=>{
  const f=fixture(t),read=gate();f.client.read=()=>read.promise;const pending=editSavedLayers({...f,state:{id:{value:'s:1'}}});
  f.client.key='other';read.resolve({state:{currentLayer:'0'},entries:[entry]});await pending;assert.equal(f.answers.length,0);assert.equal(f.edits.length,0);
});
test('LAS import requires explicit mapping preview before committing the returned token',async t=>{
  const f=fixture(t);
  f.client.read=async(path,options)=>{f.reads.push([path,structuredClone(options)]);return f.reads.length===1?
    {name:'Incoming',layerCount:1,currentLayer:'Missing',conflicts:[{kind:'layer',source:'Missing',message:'Missing layer'}],notice:'No implicit restore'}:
    {name:'Incoming',layerCount:1,currentLayer:'Pipes',conflicts:[],notice:'Ready',token:'token-1'};};
  const pending=importLayerState(f);await tick();
  byLabel(f.root,'LAS file').files=[{size:20,text:async()=>'fixture LAS'}];f.answers[0].resolve(true);await tick();
  assert.equal(f.edits.length,0);byLabel(f.root,'Map layer Missing').value='Pipes';f.answers[1].resolve(true);await tick();
  assert.deepEqual(f.reads[1][1].body.options.layers,[{source:'Missing',target:'Pipes'}]);assert.equal(f.edits.length,0);
  f.answers[2].resolve(true);await pending;
  assert.deepEqual(f.edits,[['layer-state/import',{token:'token-1'},{expectedKey:'drawing/1'}]]);
});
test('LAS parse/download async completions cannot cross document receipts',async t=>{
  const f=fixture(t),contents=gate();const pending=importLayerState(f);await tick();
  byLabel(f.root,'LAS file').files=[{size:20,text:()=>contents.promise}];f.answers[0].resolve(true);await tick();
  f.client.key='other';contents.resolve('fixture LAS');await assert.rejects(pending,/drawing changed/);assert.equal(f.reads.length,0);assert.equal(f.edits.length,0);
});
test('changing LAS preview name requires a new validated token',async t=>{
  const f=fixture(t);f.client.read=async(path,options)=>{f.reads.push(structuredClone(options));return {name:options.body.options.name||'Incoming',layerCount:1,currentLayer:'0',token:`t${f.reads.length}`,conflicts:[]};};
  const pending=importLayerState(f);await tick();byLabel(f.root,'LAS file').files=[{size:1,text:async()=>'x'}];f.answers[0].resolve(true);await tick();
  byLabel(f.root,'Preview layer state name').value='Renamed';f.answers[1].resolve(true);await tick();assert.equal(f.edits.length,0);
  assert.equal(f.reads[1].body.options.name,'Renamed');f.answers[2].resolve(true);await pending;assert.equal(f.edits[0][1].token,'t2');
});
