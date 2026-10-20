import test from 'node:test';
import assert from 'node:assert/strict';
import {SelectionCycleModel, selectionCandidates, selectionCandidateLabel} from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/selection-cycling.mjs';
import {CadViewport} from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/viewport.mjs';
import {CommandSession} from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/core.mjs';

const candidate=(id='root')=>({entity:id,leafEntity:'leaf',occurrence:`${id}/leaf`,type:'netDxf.Entities.Insert',layer:'A',handle:'1A',
  bounds:{minX:0,minY:0,maxX:10,maxY:10},preview:[{points:[{x:0,y:0},{x:10,y:10}],closed:false}]});
const result=(ids=['a','b'])=>({candidates:ids.map(id=>candidate(id)),truncated:false});
const deferred=()=>{let resolve,reject;const promise=new Promise((yes,no)=>{resolve=yes;reject=no;});return {resolve,reject,promise};};
test('late overlap reads cannot revive cancelled, replaced or changed drawing/command scopes',async()=>{
  for(const change of ['cancel','scope','replace']){
    let scope='document/workspace/layout/revision/command/1';const calls=[];
    const model=new SelectionCycleModel({scope:()=>scope,load:()=>{const call=deferred();calls.push(call);return call.promise;}});
    const old=model.query({x:0,y:0},{open:true});
    if(change==='cancel')model.cancel();else if(change==='scope')scope+='new';else {
      const current=model.query({x:1,y:1},{open:true});calls[1].resolve(result(['new','other']));await current;
    }
    calls[0].resolve(result());assert.equal(await old,null);
    assert.equal(model.state?.result?.candidates[0].entity,change==='replace'?'new':undefined);
  }
});
test('cyclic keyboard index wraps, root source and exact pointer/modifiers survive without selection mutation',async()=>{
  let scope='s';const model=new SelectionCycleModel({scope:()=>scope,load:async()=>result()});
  const point={x:5,y:0,z:2};
  await model.query(point,{open:true,extend:true,remove:true,expectedKey:'s'});
  assert(model.state.active);model.advance(-1);assert.equal(model.state.index,1);model.advance();assert.equal(model.state.index,0);
  assert.equal(model.state.point,point);assert(model.state.extend&&model.state.remove);assert.equal(model.state.expectedKey,'s');
  scope='new';assert.equal(model.current(),null);
});
test('hover reads never acquire a selection and empty/single results never show a chooser',async()=>{
  for(const ids of [[],['a'],['a','b']]){
    const model=new SelectionCycleModel({scope:()=>1,load:async()=>result(ids)});
    await model.query({x:0,y:0},{open:false});assert.equal(model.state.active,false);
    await model.query({x:0,y:0},{open:true});assert.equal(model.state.active,ids.length>1);
  }
});
test('malformed, duplicate-root, excessive and nonfinite previews are rejected before use',()=>{
  assert.equal(selectionCandidates(result()).candidates.length,2);
  assert.throws(()=>selectionCandidates(result(['a','a'])));
  assert.throws(()=>selectionCandidates(result(Array.from({length:65},(_,i)=>String(i)))));
  const bad=result();bad.candidates[0].preview[0].points[0].x=Infinity;assert.throws(()=>selectionCandidates(bad));
  const large=result();large.candidates[0].preview[0].points=Array.from({length:1025},()=>({x:0,y:0}));assert.throws(()=>selectionCandidates(large));
  assert.equal(selectionCandidateLabel(candidate()),'Insert 1A — A');
});
function viewport(){
  const calls=[],errors=[];let opened;
  const view=Object.assign(Object.create(CadViewport.prototype),{client:{key:'scope',epoch:1,busy:0,state:{}},snapshot:null,command:null,
    camera:{x:0,y:0,scale:10},size:{w:200,h:100},settings:{selectionCycling:true},inputVersion:1,pointers:new Map(),invalidate(){},
    element:{classList:{remove(){}},hasPointerCapture:()=>false,getBoundingClientRect:()=>({left:0})},local:e=>({x:e.clientX,y:e.clientY}),
    hooks:{select:(...args)=>calls.push(['select',...args]),selectCandidate(){},box:(...args)=>calls.push(['box',...args]),pick:p=>calls.push(['pick',p]),error:e=>errors.push(e)},
    selectionCycle:{open:async(...args)=>{opened=args;}},snapped:async p=>p});
  const gesture=(extra={})=>{view.pointers.set(1,{});view.drag={id:1,inputVersion:1,key:'scope',epoch:1,command:view.command,
    commandVersion:view.command?.version,snapshot:null,pixel:{x:150,y:50},first:{x:5,y:0},extend:true,remove:false,...extra};};
  return {view,calls,errors,gesture,get opened(){return opened;}};
}
test('ordinary and active BREAK selections forward exact pick, extension/removal and scope to chooser',async()=>{
  for(const command of [null,new CommandSession('BREAK')]){
    const f=viewport();f.view.command=command;f.gesture({remove:true});
    await f.view.up({pointerId:1,clientX:150,clientY:50});
    assert.deepEqual(f.opened[0],{x:5,y:0,z:0});assert.equal(f.opened[2].expectedKey,'scope');
    assert(f.opened[2].extend&&f.opened[2].remove);assert.deepEqual(f.calls,[]);assert.deepEqual(f.errors,[]);
  }
});
test('point acquisition, graph acquisition, crossing selection and grips are not diverted to overlap chooser',async()=>{
  const point=viewport();point.view.command=new CommandSession('LINE');point.gesture();await point.view.up({pointerId:1,clientX:150,clientY:50});
  assert.equal(point.opened,undefined);assert.equal(point.calls[0][0],'pick');
  const crossing=viewport();crossing.gesture({moved:true});await crossing.view.up({pointerId:1,clientX:150,clientY:50});
  assert.equal(crossing.opened,undefined);assert.equal(crossing.calls[0][0],'box');
  const grip=viewport();grip.gesture({grip:{position:{x:5,y:0}}});await grip.view.up({pointerId:1,clientX:150,clientY:50});
  assert.equal(grip.opened,undefined);assert.equal(grip.calls[0][0],'select');
  const stretch=viewport();stretch.view.command={selecting:true,spec:{crossingSelection:true}};assert.equal(stretch.view.canCycleSelection(),false);
  const graph=viewport();graph.view.graphPick={};assert.equal(graph.view.canCycleSelection(),false);
});
test('changed gesture identity never starts a new candidate query',async()=>{
  const f=viewport();f.gesture();f.view.client.epoch++;await f.view.up({pointerId:1,clientX:150,clientY:50});
  assert.equal(f.opened,undefined);assert.equal(f.calls.length,0);assert.match(f.errors[0].message,/changed/);
});
