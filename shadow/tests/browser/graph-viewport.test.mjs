import test from 'node:test';
import assert from 'node:assert/strict';
import {CadViewport} from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/viewport.mjs';
import {graphIdentity,graphPresentation} from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/graph-visualization.mjs';
function fixture(){
  const state={documentId:'d',workspaceId:'w',viewVersion:1,revision:0,layout:'Model'},calls=[],errors=[];
  const view=Object.assign(Object.create(CadViewport.prototype),{client:{state,key:'a',epoch:1,busy:0},snapshot:null,command:null,camera:{x:0,y:0,scale:10},size:{w:200,h:100},inputVersion:1,snapGeneration:1,pointers:new Map(),invalidate(){},changed(){},
    element:{focus(){},hasPointerCapture:()=>false,classList:{remove(){}}},hooks:{select:()=>calls.push('select'),box:()=>calls.push('box'),pick:()=>calls.push('drawing-point'),error:e=>errors.push(e)},local:e=>({x:e.clientX,y:e.clientY})});
  view.graph=graphPresentation({nodes:[{id:7,x:0,y:0},{id:80,x:10,y:0}],edges:[{id:3,a:7,b:80,start:{x:0,y:0},end:{x:10,y:0}}]},null);view.graphKey=graphIdentity(state);
  const gesture=()=>{view.pointers.set(1,{});view.drag={id:1,inputVersion:view.inputVersion,key:view.client.key,epoch:view.client.epoch,command:view.command,commandVersion:view.command?.version,snapshot:view.snapshot,graphPick:view.graphPick,pixel:{x:100,y:50},first:{x:0,y:0}};};
  return {view,calls,errors,gesture};
}
test('graph node and edge clicks are consumed once without invoking CAD selection or snap',async()=>{
  const f=fixture(),hits=[];f.view.beginGraphPick({onPick:hit=>hits.push(hit)});f.gesture();await f.view.up({pointerId:1,clientX:100,clientY:50});
  assert.equal(hits[0].node.id,7);assert.equal(f.view.graphPick,null);assert.deepEqual(f.calls,[]);
  f.view.beginGraphPick({kind:'edge',onPick:hit=>hits.push(hit)});f.gesture();await f.view.up({pointerId:1,clientX:150,clientY:50});assert.equal(hits[1].edge.id,3);assert.deepEqual(f.errors,[]);
});
test('graph misses and drags keep acquisition active and never fall back to CAD selection',async()=>{
  const f=fixture();let hits=0;f.view.beginGraphPick({kind:'node',onPick:()=>hits++});f.gesture();await f.view.up({pointerId:1,clientX:150,clientY:10});
  assert.equal(hits,0);assert.ok(f.view.graphPick);assert.match(f.errors[0].message,/No visible graph item/);
  f.gesture();f.view.drag.moved=true;await f.view.up({pointerId:1,clientX:100,clientY:50});assert.equal(hits,0);assert.deepEqual(f.calls,[]);
});
test('Escape and changed drawing fence graph picks without clearing CAD selection',async()=>{
  const f=fixture();let cancellations=0,hits=0;f.view.beginGraphPick({onPick:()=>hits++,onCancel:()=>cancellations++});f.gesture();assert.equal(f.view.cancelGraphPick(),true);assert.equal(f.view.cancelGraphPick(),false);
  await f.view.up({pointerId:1,clientX:100,clientY:50});assert.equal(hits,0);assert.equal(cancellations,1);assert.deepEqual(f.calls,[]);
  f.view.beginGraphPick({onPick:()=>hits++});f.gesture();f.view.client.state={...f.view.client.state,revision:1};f.view.client.key='new';await f.view.up({pointerId:1,clientX:100,clientY:50});assert.equal(hits,0);assert.equal(f.view.graphCurrent(),false);
});
test('retained views and active drawing commands reject graph acquisition',()=>{
  const f=fixture();f.view.snapshot=0;assert.throws(()=>f.view.beginGraphPick(),/current topology/);f.view.snapshot=null;f.view.command={};assert.throws(()=>f.view.beginGraphPick(),/drawing command/);
});
