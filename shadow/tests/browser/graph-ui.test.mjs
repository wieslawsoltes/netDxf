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

const projection=(overrides={})=>({graphKey:'build-a',nodeCount:2,edgeCount:1,componentCount:1,cycleRank:0,
  nodes:{items:[{id:10,x:0,y:0,degree:1,component:0,deadEnd:true},{id:90,x:10,y:0,degree:1,component:0,deadEnd:true}],total:2,nextOffset:null},
  edges:{items:[{id:30,a:10,b:90,start:{x:0,y:0},end:{x:10,y:0},length:10,entity:'H:LEAF',rootEntity:'H:ROOT',occurrence:'insert/leaf',component:0,bridge:true}],total:1,nextOffset:null},...overrides});
async function loadProjection(f,data=projection()){
  const pending=byText(f.root,'Load topology').click();await tick();f.reads.at(-1).resolve(f.receipt(data));await pending;
}
test('topology projection exposes counts, colors, complete coverage and paged accumulation',async t=>{
  const f=fixture(t),first=projection({nodes:{items:[{id:10,x:0,y:0,component:0}],total:2,nextOffset:1},edges:{items:[],total:1,nextOffset:0}});
  await loadProjection(f,first);assert.match(f.root.textContent,/Partial topology/);assert.equal(f.reads[0].options.body.tool,'graph_visualization');
  const more=byText(f.root,'Load more topology').click();await tick();assert.equal(f.reads[1].options.body.arguments.graphKey,'build-a');assert.equal(f.reads[1].options.body.arguments.nodeOffset,1);
  f.reads[1].resolve(f.receipt(projection()));await more;
  assert.equal(f.viewport.graph.edges.length,1);assert.equal(f.viewport.graph.nodes.length,2);assert.match(f.root.textContent,/Complete filtered topology/);assert.equal(byText(f.root,'Load more topology').disabled,true);
  const mode=byLabel(f.root,'Graph coloring');mode.value='features';await mode.dispatch('change');assert.equal(f.viewport.graph.mode,'features');assert.match(f.root.textContent,/Bridge \/ articulation/);
});
test('changed topology parameters fence pending projection and all analysis build IDs',async t=>{
  const f=fixture(t),pending=byText(f.root,'Load topology').click();await tick();
  const tolerance=byLabel(f.root,'Endpoint tolerance');tolerance.value='.2';await tolerance.dispatch('input');
  f.reads[0].resolve(f.receipt(projection()));await pending;assert.equal(f.viewport.graph,null);assert.doesNotMatch(f.root.textContent,/Complete filtered topology/);
  await loadProjection(f);
  const query=byText(f.root,'Run analysis').click();await tick();assert.equal(f.reads.at(-1).options.body.arguments.graphKey,'build-a');f.reads.at(-1).resolve(f.receipt({nodeCount:2,graphKey:'build-a'}));await query;
});
test('a different graph build cannot silently merge into the loaded projection',async t=>{
  const f=fixture(t);await loadProjection(f,projection({nodes:{items:[],total:2,nextOffset:0}}));
  const more=byText(f.root,'Load more topology').click();await tick();f.reads.at(-1).resolve(f.receipt(projection({graphKey:'other-build'})));await more;
  assert.equal(f.viewport.graph.nodes.length,0);assert.match(f.errors.at(-1).message,/changed between pages/);
});
test('full result loading follows offsets with one build guard and retains all loaded geometry',async t=>{
  const f=fixture(t);f.render.setOperation('edges');
  let request=byText(f.root,'Run analysis').click();await tick();
  const edge=id=>({id,a:id,b:id+1,start:{x:id,y:0},end:{x:id+1,y:0},entity:'H:'+id,length:1});
  f.reads[0].resolve(f.receipt({graphKey:'result-build',items:[edge(10)],total:2,nextOffset:1}));await request;
  request=byText(f.root,'Load full result (bounded)').click();await tick();f.reads[1].resolve(f.receipt({graphKey:'result-build',items:[edge(10)],total:2,nextOffset:1}));await tick();
  assert.equal(f.reads[2].options.body.arguments.offset,1);assert.equal(f.reads[2].options.body.arguments.graphKey,'result-build');
  f.reads[2].resolve(f.receipt({graphKey:'result-build',items:[edge(20)],total:2,nextOffset:null}));await request;
  await byText(f.root,'Highlight this result').click();assert.deepEqual(f.viewport.graph.edges.map(e=>e.id),[10,20]);assert.match(f.root.textContent,/Loaded across pages/);assert.doesNotMatch(f.root.textContent,/Partial result/);
});
test('full result paging rejects repeated cursors and drawing changes without publishing partial stale data',async t=>{
  const f=fixture(t);f.render.setOperation('nodes');
  let pending=byText(f.root,'Run analysis').click();await tick();f.reads[0].resolve(f.receipt({graphKey:'build-a',items:[{id:1,x:0,y:0}],total:3,nextOffset:1}));await pending;
  pending=byText(f.root,'Load full result (bounded)').click();await tick();f.reads[1].resolve(f.receipt({graphKey:'build-a',items:[{id:1,x:0,y:0}],total:3,nextOffset:1}));await tick();f.reads[2].resolve(f.receipt({graphKey:'build-a',items:[{id:2,x:1,y:0}],total:3,nextOffset:1}));await pending;
  assert.match(f.errors[0].message,/invalid page cursor/);
  pending=byText(f.root,'Run analysis').click();await tick();f.client.key='new';f.reads.at(-1).resolve(f.receipt({items:[{id:99,x:0,y:0}],total:1,nextOffset:null}));await pending;assert.doesNotMatch(f.root.textContent,/Node 99/);
});
test('visual endpoint picking is explicit, cancels independently and never accepts a stale drawing',async t=>{
  const f=fixture(t);let picks=0,lastPick;f.viewport.beginGraphPick=value=>{picks++;lastPick=value;};
  f.render.setOperation('path');assert.equal(picks,0);await loadProjection(f);
  await byText(f.root,'Pick start node').click();assert.equal(lastPick.kind,'node');lastPick.onPick({kind:'node',node:{id:90,x:10,y:0}});
  assert.equal(byLabel(f.root,'Start node').value,'90');assert.match(f.root.textContent,/Start node: 90/);
  await byText(f.root,'Pick end node').click();f.client.key='different';assert.throws(()=>lastPick.onPick({kind:'node',node:{id:10,x:0,y:0}}),/drawing changed/);assert.equal(byLabel(f.root,'End node').value,'1');
});
test('source selection passes owning root and exact occurrence under a captured drawing receipt',async t=>{
  const root=installDom(t),selected=[],errors=[],state={documentId:'d',workspaceId:'w',viewVersion:1,revision:0,layout:'Model'};
  const client={state,key:'a',async read(){return {result:{...state,data:{...projection(),layout:'Model'}}};}},viewport={camera:{},invalidate(){}};
  const render=graphPalette({client,viewport,snapshot:()=>null,report:e=>errors.push(e),selectSources:async(sources,key)=>selected.push({sources,key})});render(root);
  await byText(root,'Load topology').click();let pick;viewport.beginGraphPick=value=>{pick=value;};await byText(root,'Inspect in drawing').click();pick.onPick({kind:'edge',id:30,edge:projection().edges.items[0]});
  await byText(root,'Select source in drawing').click();assert.equal(selected[0].key,'a');assert.equal(selected[0].sources[0].rootEntity,'H:ROOT');assert.equal(selected[0].sources[0].occurrence,'insert/leaf');
  client.key='new';await byText(root,'Select source in drawing').click();assert.equal(selected.length,1);assert.match(errors.at(-1).message,/drawing changed/);
});
test('bounded full results preserve the first unconsumed offset when slicing the last provider page',async t=>{
  const root=installDom(t),requests=[],errors=[],state={documentId:'d',workspaceId:'w',viewVersion:1,revision:0,layout:'Model'};
  const client={state,key:'a',async read(path,{body}){const offset=body.arguments.offset||0,limit=body.arguments.limit;requests.push(offset);return {result:{...state,data:{layout:'Model',graphKey:'build-a',items:Array.from({length:Math.min(limit,4100-offset)},(_,i)=>({id:offset+i,x:offset+i,y:0})),total:4100,nextOffset:offset+limit<4100?offset+limit:null}}};}};
  const render=graphPalette({client,viewport:{camera:{},invalidate(){}},snapshot:()=>null,report:e=>errors.push(e)});render(root);render.setOperation('nodes');
  const limit=byLabel(root,'Page / visit limit');limit.value='199';await limit.dispatch('input');
  await byText(root,'Run analysis').click();await byText(root,'Load full result (bounded)').click();
  assert.deepEqual(errors,[]);assert.match(root.textContent,/4100 matches · showing 1–4000/);assert.equal(byText(root,'Next').disabled,false);
  await byText(root,'Next').click();assert.equal(requests.at(-1),4000);assert.match(root.textContent,/showing 4001–4100/);
});
test('hiding the workspace cancels graph picking and fences an in-flight projection',async t=>{
  const f=fixture(t);let cancels=0;f.viewport.cancelGraphPick=()=>cancels++;
  const pending=byText(f.root,'Load topology').click();await tick();f.render.hide();assert.equal(cancels,1);f.reads[0].resolve(f.receipt(projection()));await pending;
  assert.equal(f.viewport.graph,undefined);assert.doesNotMatch(f.root.textContent,/Complete filtered topology/);
});
test('analysis-only continuation reuses its discovered build identity and topology refresh supersedes older analysis',async t=>{
  const f=fixture(t);f.render.setOperation('nodes');let pending=byText(f.root,'Run analysis').click();await tick();
  f.reads[0].resolve(f.receipt({graphKey:'analysis-build',items:[{id:1,x:0,y:0}],total:2,nextOffset:1}));await pending;
  pending=byText(f.root,'Next').click();await tick();assert.equal(f.reads[1].options.body.arguments.graphKey,'analysis-build');
  const load=byText(f.root,'Load topology').click();await tick();f.reads[2].resolve(f.receipt(projection()));await load;
  f.reads[1].resolve(f.receipt({graphKey:'analysis-build',items:[{id:88,x:0,y:0}],total:2,nextOffset:null}));await pending;
  assert.doesNotMatch(f.root.textContent,/Node 88/);assert.equal(f.viewport.graph.graphKey,'build-a');
});
test('clear overlay fences pending loads and stays cleared across palette rendering until explicitly shown',async t=>{
  const f=fixture(t);const pending=byText(f.root,'Load topology').click();await tick();await byText(f.root,'Clear overlay').click();f.reads[0].resolve(f.receipt(projection()));await pending;assert.equal(f.viewport.graph,null);
  await loadProjection(f);await byText(f.root,'Clear overlay').click();f.render(f.root);assert.equal(f.viewport.graph,null);await byText(f.root,'Show overlay').click();assert.equal(f.viewport.graph.edges.length,1);
});
test('public graph clear fences loading and saved analytical overlays across hide/reopen',async t=>{
  const f=fixture(t);const load=byText(f.root,'Load topology').click();await tick();f.render.clearOverlay();f.reads[0].resolve(f.receipt(projection()));await load;assert.equal(f.viewport.graph,null);
  await loadProjection(f);f.render.setOperation('edges');const pending=byText(f.root,'Run analysis').click();await tick();f.reads.at(-1).resolve(f.receipt({graphKey:'build-a',items:projection().edges.items,total:1,nextOffset:null}));await pending;assert.equal(f.viewport.graph.highlight,true);
  f.render.clearOverlay();assert.equal(f.viewport.graph,null);f.render.hide();f.render(f.root);assert.equal(f.viewport.graph,null);await byText(f.root,'Show overlay').click();assert.equal(f.viewport.graph.edges.length,1);
});
