import test from 'node:test';
import assert from 'node:assert/strict';
import {graphPalette} from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/graph-ui.mjs';
import {byText,byLabel,gate,tick,installDom} from './ui-fixture.mjs';

function fixture(t){
  const root=installDom(t),reads=[],errors=[];
  const state={documentId:'drawing',workspaceId:'live',viewVersion:1,revision:0,layout:'Model'};
  const client={state,key:'current',read(path,options){const pending=gate();reads.push({path,options,...pending});return pending.promise;}};
  const viewport={camera:{},invalidate(){},changed(){}};
  const render=graphPalette({client,viewport,snapshot:()=>null,report:e=>errors.push(e)});render(root);
  const receipt=data=>({isError:false,result:{...state,data:{...data,layout:'Model'}}});
  return {root,reads,errors,client,viewport,render,receipt};
}
test('changing graph parameters rejects a pending result and clears cached data',async t=>{
  const f=fixture(t),old=byText(f.root,'Run analysis').click();await tick();
  const tolerance=byLabel(f.root,'Endpoint tolerance');tolerance.value='.2';await tolerance.dispatch('input');
  const current=byText(f.root,'Run analysis').click();await tick();
  assert.equal(f.reads[1].options.body.arguments.endpointTolerance,.2);
  f.reads[1].resolve(f.receipt({nodeCount:222}));await current;f.reads[0].resolve(f.receipt({nodeCount:111}));await old;
  assert.match(f.root.textContent,/222/);assert.doesNotMatch(f.root.textContent,/111/);
  tolerance.value='.3';await tolerance.dispatch('input');f.render(f.root);assert.doesNotMatch(f.root.textContent,/222/);
});
test('superseded graph errors cannot replace current results or reenable an active request',async t=>{
  const f=fixture(t),old=byText(f.root,'Run analysis').click();await tick();
  const tolerance=byLabel(f.root,'Endpoint tolerance');tolerance.value='.2';await tolerance.dispatch('input');
  const current=byText(f.root,'Run analysis').click();await tick();
  f.reads[0].reject(new Error('obsolete'));await old;
  assert.equal(byText(f.root,'Run analysis').disabled,true);assert.deepEqual(f.errors,[]);
  f.reads[1].resolve(f.receipt({nodeCount:2}));await current;assert.equal(byText(f.root,'Run analysis').disabled,false);
});
test('cycle cards drill into source-linked ordered geometry and highlight sparse graph IDs',async t=>{
  const f=fixture(t),operation=byLabel(f.root,'Graph analysis operation');operation.value='cycles';await operation.dispatch('change');
  let pending=byText(f.root,'Run analysis').click();await tick();
  f.reads[0].resolve(f.receipt({items:[{id:3,closingEdge:42,edgeCount:1}],total:4,nextOffset:null}));await pending;
  await byText(f.root,'Inspect cycle').click();assert.equal(byLabel(f.root,'Cycle ID').value,'3');
  pending=byText(f.root,'Run analysis').click();await tick();assert.equal(f.reads[1].options.body.tool,'graph_cycle');
  const node={id:12,x:1,y:2,degree:2},edge={id:42,a:12,b:12,length:5,start:node,end:node,entity:'H:9'};
  f.reads[1].resolve(f.receipt({cycle:3,length:5,edgeCount:1,steps:{items:[{node,viaEdge:edge}],total:2,nextOffset:null}}));await pending;
  await byText(f.root,'Highlight this result').click();assert.deepEqual(f.viewport.graph.edges,[edge]);assert.equal(f.viewport.graph.nodes[0].id,12);
});
test('minimum forest results render paged edge geometry',async t=>{
  const f=fixture(t),operation=byLabel(f.root,'Graph analysis operation');operation.value='spanning_forest';await operation.dispatch('change');
  const pending=byText(f.root,'Run analysis').click();await tick();
  f.reads[0].resolve(f.receipt({length:12,componentCount:2,edgeCount:3,steps:{items:[{id:9,a:1,b:2,length:4,entity:'H:4',start:{x:0,y:0},end:{x:4,y:0}}],total:3,nextOffset:1}}));await pending;
  assert.match(f.root.textContent,/Minimum forest length 12/);assert.equal(byText(f.root,'Next').disabled,false);
});
test('choosing a path endpoint invalidates the previous result and overlay',async t=>{
  const f=fixture(t),operation=byLabel(f.root,'Graph analysis operation');operation.value='path';await operation.dispatch('change');
  const pending=byText(f.root,'Run analysis').click();await tick();
  f.reads[0].resolve(f.receipt({reachable:true,length:5,edgeCount:1,steps:{items:[{node:{id:17,x:2,y:3}}],total:1,nextOffset:null}}));await pending;
  await byText(f.root,'Highlight this result').click();assert.ok(f.viewport.graph);
  await byText(f.root,'Start').click();
  assert.equal(byLabel(f.root,'Start node').value,'17');assert.equal(f.viewport.graph,null);
  assert.match(f.root.textContent,/Inputs changed/);assert.doesNotMatch(f.root.textContent,/Path length 5/);
  const next=byText(f.root,'Run analysis').click();await tick();assert.equal(f.reads[1].options.body.arguments.source,17);
  f.reads[1].resolve(f.receipt({reachable:false,steps:{items:[],total:0,nextOffset:null}}));await next;
});
test('graph result actions refuse a superseded drawing even before its palette rerenders',async t=>{
  const f=fixture(t),operation=byLabel(f.root,'Graph analysis operation');operation.value='nodes';await operation.dispatch('change');
  const pending=byText(f.root,'Run analysis').click();await tick();
  f.reads[0].resolve(f.receipt({items:[{id:17,x:2,y:3}],total:1,nextOffset:null}));await pending;
  f.client.key='different-revision';
  await byText(f.root,'Highlight this result').click();await byText(f.root,'Focus').click();
  assert.equal(f.viewport.graph,undefined);assert.deepEqual(f.viewport.camera,{});
  assert.equal(f.errors.length,2);assert.ok(f.errors.every(e=>/drawing changed/.test(e.message)));
});
