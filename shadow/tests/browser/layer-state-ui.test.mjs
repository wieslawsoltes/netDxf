import test from 'node:test';
import assert from 'node:assert/strict';
import {layerStateTools} from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/layer-state-ui.mjs';
import {installDom,byText,byLabel,gate,tick} from './ui-fixture.mjs';

function fixture(t,states=[{id:{value:'state:1'},name:'Normal',description:'Process',currentLayer:'0',paperSpace:false,layers:['0','Pipes']}]) {
  const root=installDom(t),read=gate(),answer=gate(),edits=[];
  const client={key:'drawing/1',busy:false,read:()=>read.promise};
  document.getElementById=id=>id==='dialog'?{close:()=>answer.resolve(true)}:null;
  const open=layerStateTools({client,edit:async(...args)=>edits.push(args),editable(){},dialog:(title,children)=>{root.replaceChildren(...children);return answer.promise;}});
  return {root,client,read,answer,edits,open,states};
}
test('layer state manager sends only checked restore properties with the opened receipt',async t=>{
  const f=fixture(t),pending=f.open();f.read.resolve({layerStates:f.states});await tick();
  await byText(f.root,'Clear all').click();byLabel(f.root,'Restore Color').checked=true;
  byLabel(f.root,'Restore current layer').checked=false;byLabel(f.root,'Turn off unlisted layers').checked=true;
  f.answer.resolve(true);await pending;
  assert.deepEqual(f.edits,[['layer-state',{state:{action:'restore',id:{value:'state:1'},fields:64,restoreCurrentLayer:false,turnOffUnlistedLayers:true}},{expectedKey:'drawing/1'}]]);
});
test('new state saves current layers without overwriting the selected state',async t=>{
  const f=fixture(t),pending=f.open();f.read.resolve({layerStates:f.states});await tick();await byText(f.root,'New').click();
  byLabel(f.root,'Layer state name').value='Review';byLabel(f.root,'Layer state description').value='Review layers';f.answer.resolve(true);await pending;
  assert.deepEqual(f.edits[0][1],{state:{action:'save',name:'Review',description:'Review layers'}});
});
test('state rename targets a stable source ID',async t=>{
  const f=fixture(t),pending=f.open();f.read.resolve({layerStates:f.states});await tick();
  byLabel(f.root,'Layer state name').value='Renamed';await byText(f.root,'Rename').click();await pending;
  assert.deepEqual(f.edits[0][1],{state:{action:'rename',id:{value:'state:1'},name:'Renamed'}});
});
test('changed drawing refuses a stale layer-state submission',async t=>{
  const f=fixture(t),pending=f.open();f.read.resolve({layerStates:f.states});await tick();
  f.client.key='drawing/2';f.answer.resolve(true);await assert.rejects(pending,/drawing changed/);assert.deepEqual(f.edits,[]);
});
test('late layer-state discovery cannot open a dialog in another drawing',async t=>{
  const f=fixture(t),pending=f.open();f.client.key='other';f.read.resolve({layerStates:f.states});await pending;
  assert.equal(f.root.children.length,0);assert.deepEqual(f.edits,[]);
});
