import test from 'node:test';
import assert from 'node:assert/strict';
import {SelectionCycling} from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/selection-cycling-ui.mjs';
import {ribbonDom} from './ribbon-dom-fixture.mjs';
import {gate,tick} from './ui-fixture.mjs';
const result={candidates:['a','b'].map(entity=>({entity,leafEntity:'leaf',occurrence:`${entity}/leaf`,type:'Line',layer:'0',
  bounds:{minX:0,minY:0,maxX:10,maxY:0},preview:[],previewTruncated:true})),truncated:false};
function fixture(t,load=async()=>result){
  const dom=ribbonDom(t),oldAdd=globalThis.addEventListener,oldRemove=globalThis.removeEventListener;
  globalThis.addEventListener=(...args)=>dom.windowEvents.addEventListener(...args);
  globalThis.removeEventListener=(...args)=>dom.windowEvents.removeEventListener(...args);
  t.after(()=>{globalThis.addEventListener=oldAdd;globalThis.removeEventListener=oldRemove;});
  const picked=[],errors=[];let scope='scope',eligible=true,cancelled=0;
  const chooser=new SelectionCycling(dom.panel,{scope:()=>scope,load,choose:(...args)=>picked.push(args),error:e=>errors.push(e),invalidate(){},eligible:()=>eligible,cancelled:()=>cancelled++});
  dom.disposeWith(()=>chooser.dispose());
  const open=(context={})=>chooser.open({x:5,y:0},{x:880,y:110},{expectedKey:scope,extend:true,remove:false,...context});
  const key=(key,target=chooser.list,extra={})=>{
    let consumed=false;chooser.key({key,target,preventDefault(){consumed=true;},stopImmediatePropagation(){},...extra});return consumed;
  };
  return {...dom,chooser,picked,errors,open,key,setScope:value=>scope=value,setEligible:value=>eligible=value,get cancelled(){return cancelled;}};
}
test('list keyboard and hover highlight preserve selection until Enter, then commit exact root and restore viewport focus',async t=>{
  const f=fixture(t);await f.open();
  assert.equal(document.activeElement,f.chooser.list);assert.equal(f.chooser.preview.entity,'a');assert.equal(f.picked.length,0);
  assert(f.key('ArrowDown'));assert.equal(f.chooser.preview.entity,'b');assert.equal(f.picked.length,0);
  assert.equal(document.activeElement,f.chooser.list,'index change must not replace focused listbox');
  assert.equal(f.chooser.list.getAttribute('aria-activedescendant'),f.chooser.list.children[1].getAttribute('id'));
  f.chooser.list.children[0].emit('pointermove');assert.equal(f.chooser.preview.entity,'a');
  assert(f.key('Enter'));await tick();assert.equal(f.picked[0][0].entity,'a');assert.equal(f.picked[0][1].extend,true);
  assert.equal(document.activeElement,f.panel);assert.equal(f.chooser.active,false);
});
test('Escape and explicit close cancel without selecting; outside clicks retain clicked focus',async t=>{
  const f=fixture(t);await f.open();assert(f.key('Escape'));assert.equal(document.activeElement,f.panel);assert.equal(f.picked.length,0);
  assert.equal(f.cancelled,1,'Escape disarms a temporary selection cycle');
  await f.open();f.chooser.close.emit('click');assert.equal(f.chooser.active,false);assert.equal(document.activeElement,f.panel);
  await f.open();f.tab.focus();f.events.emit('pointerdown',{target:f.tab});assert.equal(f.chooser.active,false);
  assert.equal(document.activeElement,f.tab);assert.equal(f.picked.length,0);
});
test('pending responses are cancelled by outside focus and cannot select even a single candidate',async t=>{
  const wait=gate(),f=fixture(t,()=>wait.promise),loading=f.open();assert(f.chooser.active);
  f.tab.focus();f.events.emit('focusin',{target:f.tab});wait.resolve({...result,candidates:[result.candidates[0]]});await loading;
  assert.equal(f.picked.length,0);assert.equal(f.chooser.active,false);
});
test('temporary cycling hides list, keyboard cycles, and text inputs/modal controls retain their keys',async t=>{
  const f=fixture(t);await f.open({temporary:true});assert(f.chooser.list.hidden);
  f.panel.focus();assert(f.key(' ',f.panel,{shiftKey:true}));assert.equal(f.chooser.preview.entity,'b');
  const input=document.createElement('input');f.panel.append(input);
  // Fake DOM closest does not implement comma selectors: model the browser's matching editable ancestor.
  input.closest=()=>input;assert.equal(f.key('Escape',input),false);assert(f.chooser.active);
  f.setEligible(false);assert.equal(f.key('Enter'),false);await f.chooser.commit();assert.equal(f.picked.length,0);
});
test('stale scope removes candidate highlight and chooser without committing',async t=>{
  const f=fixture(t);await f.open();f.setScope('new layout');assert.equal(f.chooser.preview,null);assert.equal(f.chooser.active,false);
  await f.chooser.commit();assert.equal(f.picked.length,0);assert(f.chooser.host.hidden);
});
