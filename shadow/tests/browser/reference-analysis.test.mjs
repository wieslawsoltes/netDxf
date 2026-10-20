import test from 'node:test';
import assert from 'node:assert/strict';
import {referenceAnalysisRequest,mergeReferenceAnalysis,referenceImpactProof,referenceAnalysisView} from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/reference-analysis-model.mjs';
import {referenceSvg,referenceLayout} from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/reference-graph-model.mjs';
import {referenceGraphPalette} from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/reference-graph-ui.mjs';
import {byText,byLabel,gate,tick,installDom,all} from './ui-fixture.mjs';
const state={serverId:'server',documentId:'document',workspaceId:'live',viewVersion:1,revision:4};
const page=(items,total=items.length,offset=0,nextOffset=null)=>({items,total,offset,nextOffset});
const node=(id,depth=0,extra={})=>({id,type:'Fixture.Object',kind:'Object',depth,incoming:1,outgoing:1,exists:true,...extra});
const edge=(source,target,path='/Layer')=>({source,target,path});
const nodes=[node('A',0,{component:0,cyclic:true}),node('B',1,{component:0,cyclic:true}),node('Layer',1,{component:1,cyclic:false})];
const edges=[edge('A','B','/Owner'),edge('B','A','/Owner'),edge('A','Layer')];
const groups=[{id:0,nodeCount:2,internalEdgeCount:2,cyclic:true},{id:1,nodeCount:1,internalEdgeCount:0,cyclic:false}];
const links=[{source:0,target:1,referenceCount:1}];
const data=(extra={})=>({referenceKey:'components-scope',analysis:'components',direction:'outgoing',roots:['A'],objectsIndexed:12,explorationComplete:true,
  nodes:page(nodes),edges:page(edges),results:page(groups),links:page(links),...extra});
const impact=()=>data({referenceKey:'impact-scope',analysis:'impact',direction:'incoming',roots:['Layer'],
  nodes:page([node('Layer',0,{predecessor:null,via:null}),node('A',1,{predecessor:'Layer',via:edge('A','Layer')}),node('B',2,{predecessor:'A',via:edge('B','A','/Owner')})]),
  edges:page([edge('A','Layer'),edge('B','A','/Owner')]),results:page([]),links:page([])});
const path=(status='found')=>data({analysis:'path',status,referenceKey:'path-scope',visitedNodes:3,examinedEdges:3,results:page([]),links:page([]),
  nodes:page(status==='found'?[node('A'),node('Layer',1)]:[]),edges:page(status==='found'?[edge('A','Layer')]:[]),explorationComplete:status!=='incomplete',depthLimitReached:status==='incomplete'});

test('analysis requests bind exact mode, endpoints, identity and all four adaptive cursors',()=>{
  const result=referenceAnalysisRequest({operation:'path',source:'A',target:'Layer',roots:'irrelevant'},state);
  assert.equal(result.tool,'graph_reference_path');assert.equal(result.arguments.source,'A');assert.equal(result.arguments.target,'Layer');assert.equal(result.arguments.roots,undefined);
  assert.equal(referenceAnalysisRequest({operation:'impact',direction:'outgoing'},state).arguments.direction,'incoming');
  for(const values of [{operation:'unknown'},{operation:'path',source:'A B',target:'Layer'},{operation:'path',source:'A'}])assert.throws(()=>referenceAnalysisRequest(values,state));
  const scene=mergeReferenceAnalysis(null,data({nodes:page([nodes[0]],3,0,1),edges:page([edges[0]],3,0,1),results:page([groups[0]],2,0,1)}));
  const args=referenceAnalysisRequest({operation:'components'},state,scene).arguments;
  assert.equal(args.nodeOffset,1);assert.equal(args.edgeOffset,1);assert.equal(args.resultOffset,1);assert.equal(args.linkOffset,1);assert.equal(args.referenceKey,'components-scope');
});
test('four streams merge without losing remaining summaries or links',()=>{
  let scene=mergeReferenceAnalysis(null,data({nodes:page([nodes[0]],3,0,1),edges:page([edges[0]],3,0,1),results:page([groups[0]],2,0,1)}));
  scene=mergeReferenceAnalysis(scene,data({nodes:page(nodes.slice(1),3,1),edges:page(edges.slice(1),3,1),results:page([groups[1]],2,1),links:page([],1,1)}));
  assert.equal(scene.partial,false);assert.deepEqual(scene.results,groups);assert.deepEqual(scene.links,links);
  const partial=mergeReferenceAnalysis(null,data({results:page([groups[0]],2,0,1)}));assert.equal(partial.partial,true);
  const final=mergeReferenceAnalysis(partial,data({nodes:page([],3,3),edges:page([],3,3),results:page([groups[1]],2,1),links:page([],1,1)}));assert.equal(final.partial,false);
});
test('analysis pages reject identity/mode changes, skipped rows and malformed components',()=>{
  const first=mergeReferenceAnalysis(null,data({results:page([groups[0]],2,0,1)}));
  assert.throws(()=>mergeReferenceAnalysis(first,data({analysis:'impact'})),/changed/);
  assert.throws(()=>mergeReferenceAnalysis(first,data({referenceKey:'changed'})),/scope/);
  assert.throws(()=>mergeReferenceAnalysis(first,data({results:page([],2,2)})),/page/);
  assert.throws(()=>mergeReferenceAnalysis(null,data({results:page([groups[0],groups[0]])})),/Duplicate/);
  assert.throws(()=>mergeReferenceAnalysis(null,data({links:page([{source:0,target:0,referenceCount:1}])})),/condensation/);
  assert.throws(()=>mergeReferenceAnalysis(null,path('unknown')),/status/);
});
test('impact proof follows actual edge direction and reports missing pages',()=>{
  const scene=mergeReferenceAnalysis(null,impact()),proof=referenceImpactProof(scene,'B');
  assert.equal(proof.complete,true);assert.deepEqual(proof.nodes,['B','A','Layer']);assert.deepEqual(proof.edges,[edge('B','A','/Owner'),edge('A','Layer')]);
  const partial=referenceImpactProof({...scene,nodes:scene.nodes.filter(n=>n.id!=='A')},'B');assert.equal(partial.complete,false);assert.match(partial.reason,/more object pages/);
  assert.throws(()=>referenceImpactProof({...scene,nodes:[node('A',1,{predecessor:'A',via:edge('A','A')})]},'A'),/depth/);
  assert.throws(()=>referenceImpactProof({...scene,nodes:[node('A',0,{predecessor:null,via:edge('A','Layer')})]},'A'),/root/);
});
test('component condensation is directed, colored and exported with truthful partial scope',()=>{
  const scene=mergeReferenceAnalysis(null,data({explorationComplete:false,depthLimitReached:true}));
  const visual=referenceAnalysisView(scene,{condensed:true,component:0});
  assert.equal(visual.nodes[0].depth,0);assert.equal(visual.nodes[1].depth,1);assert.equal(visual.edges[0].path,'1 captured references');
  const svg=referenceSvg(visual);assert.match(svg,/Condensation DAG/);assert.match(svg,/cannot|do not establish/);assert.match(svg,/#ffd166/);assert.match(svg,/2 objects/);
  const objectSvg=referenceSvg(referenceAnalysisView(scene,{component:0}));assert.match(objectSvg,/#ffd166/);assert.match(objectSvg,/\/Owner/);
  assert.throws(()=>referenceAnalysisView({...scene,links:[...scene.links,{source:1,target:0,referenceCount:1}]},{condensed:true}),/cycle/);
});
test('path SVG distinguishes proved path, unreachable search and incomplete search',()=>{
  for(const status of ['found','unreachable','incomplete']){
    const svg=referenceSvg(referenceAnalysisView(mergeReferenceAnalysis(null,path(status))));assert.match(svg,new RegExp(`Dependency path status: ${status}`));assert.doesNotMatch(svg,/Exploration complete/);
  }
});
function fixture(t){
  const root=installDom(t),reads=[],errors=[],downloads=[],selected=[];
  const client={state:{...state},key:'current',data:{items:[{id:'A',type:'Line'},{id:'B',type:'Line'}],selectedEntities:['A','B'],layers:[{id:{value:'Layer'},name:'0'}]},read(path,options){const pending=gate();reads.push({path,options,...pending});return pending.promise;}};
  const render=referenceGraphPalette({client,snapshot:()=>null,report:e=>errors.push(e),download:(...args)=>downloads.push(args),selectSources:(ids,key)=>selected.push({ids,key})});
  render(root);return {root,reads,errors,downloads,selected,render,client,receipt:value=>({isError:false,result:{...client.state,data:value}})};
}
async function load(f,operation,value){f.render.setOperation(operation);const labels={components:'Analyze cyclic groups',impact:'Explore reference impact',path:'Find dependency path'};if(operation==='path'){for(const [key,val] of [['source','A'],['target','Layer']]){const input=byLabel(f.root,`Reference path ${key}`);input.value=val;await input.dispatch('input');}}const pending=byText(f.root,labels[operation]).click();await tick();f.reads.at(-1).resolve(f.receipt(value));await pending;}
test('component UI exposes members, DAG links and full analysis JSON without selecting geometry',async t=>{
  const f=fixture(t);await load(f,'components',data());assert.equal(f.reads[0].options.body.tool,'graph_reference_components');
  await byText(f.root,'Group 0 · 2 objects · cyclic').click();assert.match(f.root.textContent,/Strongly connected group 0/);assert.match(f.root.textContent,/Group 0 → Group 1 · 1 references/);
  const toggle=byLabel(f.root,'Reference diagram view');toggle.value='condensed';await toggle.dispatch('change');
  await byText(f.root,'Export reference SVG').click();assert.match(f.downloads.at(-1)[1],/Condensation DAG/);
  await byText(f.root,'Export reference JSON').click();const exported=JSON.parse(f.downloads.at(-1)[1]);assert.deepEqual(exported.graph.results,groups);assert.deepEqual(exported.graph.links,links);assert.equal(exported.graph.nodes[0].component,0);assert.deepEqual(f.selected,[]);
});
test('impact UI gives proof text and refuses resource source selection',async t=>{
  const f=fixture(t);await load(f,'impact',impact());assert.equal(byLabel(f.root,'Reference direction').disabled,true);
  await byText(f.root,'B · Object').click();assert.match(f.root.textContent,/Why this object is affected/);assert.match(f.root.textContent,/B \/Owner → A/);assert.match(f.root.textContent,/A \/Layer → Layer/);
  await byText(f.root,'Select source object').click();assert.deepEqual(f.selected,[{ids:['B'],key:'current'}]);
  await byText(f.root,'Layer · Object').click();assert.equal(byText(f.root,'Select source object').disabled,true);
  await byText(f.root,'Export reference JSON').click();assert.equal(JSON.parse(f.downloads[0][1]).graph.nodes[1].predecessor,'Layer');
});
test('path mode can use inspector endpoints and does not claim full graph completeness',async t=>{
  const f=fixture(t);await load(f,'components',data());await byText(f.root,'A · Object').click();await byText(f.root,'Use as path start').click();await byText(f.root,'Layer · Object').click();await byText(f.root,'Use as path end').click();
  f.render.setOperation('path');assert.equal(byLabel(f.root,'Reference path source').value,'A');assert.equal(byLabel(f.root,'Reference path target').value,'Layer');
  const pending=byText(f.root,'Find dependency path').click();await tick();f.reads.at(-1).resolve(f.receipt(path()));await pending;
  assert.match(f.root.textContent,/Shortest dependency path found: 1 reference hops/);assert.doesNotMatch(f.root.textContent,/Complete reachable source graph/);assert.deepEqual(f.selected,[]);
});
test('incomplete path status stays distinct from unreachable in visible output',async t=>{
  const f=fixture(t);await load(f,'path',path('incomplete'));assert.match(f.root.textContent,/does not prove the target is unreachable/);assert.doesNotMatch(f.root.textContent,/No dependency path exists/);
  await load(f,'path',path('unreachable'));assert.match(f.root.textContent,/No dependency path exists/);
});
test('changing analysis, inputs, hiding or clearing fences pending responses',async t=>{
  for(const cancel of ['mode','input','hide','clear','identity']){
    const f=fixture(t);f.render.setOperation('components');const pending=byText(f.root,'Analyze cyclic groups').click();await tick();
    if(cancel==='mode')f.render.setOperation('impact');if(cancel==='input'){const input=byLabel(f.root,'Reference maximum depth');input.value='3';await input.dispatch('input');}if(cancel==='hide')f.render.hide();if(cancel==='clear')f.render.clear();if(cancel==='identity')f.client.key='new';
    f.reads.at(-1).resolve(f.receipt(data()));await pending;assert.doesNotMatch(f.root.textContent,/12 indexed objects/);
  }
});
test('analysis continuation follows results and links after geometry is complete',async t=>{
  const f=fixture(t);await load(f,'components',data({results:page([groups[0]],2,0,1)}));
  const pending=byText(f.root,'Load remaining references (bounded)').click();await tick();const args=f.reads.at(-1).options.body.arguments;
  assert.equal(args.resultOffset,1);assert.equal(args.linkOffset,1);assert.equal(args.nodeOffset,3);assert.equal(args.edgeOffset,3);
  f.reads.at(-1).resolve(f.receipt(data({nodes:page([],3,3),edges:page([],3,3),results:page([groups[1]],2,1),links:page([],1,1)})));await pending;
  assert.equal(byText(f.root,'Load more references').disabled,true);assert.match(f.root.textContent,/2 \/ 2 groups/);
});
test('stale component, impact and export actions have no side effects',async t=>{
  const f=fixture(t);await load(f,'impact',impact());await byText(f.root,'A · Object').click();f.client.key='other';
  await byText(f.root,'Select source object').click();await byText(f.root,'Use as path end').click();await byText(f.root,'Export reference JSON').click();assert.equal(f.errors.length,3);assert.equal(f.selected.length,0);assert.equal(f.downloads.length,0);
  assert.ok(all(f.root).filter(n=>n.tagName==='IMG').every(n=>n.getAttribute('src').startsWith('blob:')));
});

test('long condensation ranks retain exact metadata but bound SVG display columns',()=>{
  const count=1000,scene=mergeReferenceAnalysis(null,data({nodes:page([]),edges:page([]),results:page(Array.from({length:count},(_,id)=>({id,nodeCount:1,internalEdgeCount:0,cyclic:false}))),links:page(Array.from({length:count-1},(_,id)=>({source:id,target:id+1,referenceCount:1})))}));
  const view=referenceAnalysisView(scene,{condensed:true,component:999}),layout=referenceLayout(view);
  assert.equal(view.nodes.at(-1).rank,999);assert.equal(layout.nodes[0].id,'component:999');assert.equal(layout.nodes[0].depth,999);assert.equal(layout.nodes[0].column,16);assert.ok(layout.width<=4258);assert.equal(layout.rankCompressed,true);
  assert.match(referenceSvg(view),/Display columns are capped at 17/);assert.match(referenceSvg(view),/rank 999/);
});
test('component aria-pressed follows group picks and resets on object inspection',async t=>{
  const f=fixture(t);await load(f,'components',data());const first=byText(f.root,'Group 0 · 2 objects · cyclic'),second=byText(f.root,'Group 1 · 1 objects · acyclic');
  await first.click();assert.equal(first.getAttribute('aria-pressed'),'true');assert.equal(second.getAttribute('aria-pressed'),'false');
  await second.click();assert.equal(first.getAttribute('aria-pressed'),'false');assert.equal(second.getAttribute('aria-pressed'),'true');
  await byText(f.root,'A · Object').click();assert.equal(second.getAttribute('aria-pressed'),'false');
});

test('hidden stale explore roots never block explicit dependency path endpoints',()=>{
  for(const roots of [Array.from({length:201},(_,i)=>'N'+i).join(' '),'x'.repeat(513)]){
    const request=referenceAnalysisRequest({operation:'path',source:'A',target:'Layer',roots},state);
    assert.equal(request.arguments.source,'A');assert.equal(request.arguments.target,'Layer');assert.equal(request.arguments.roots,undefined);
  }
});
