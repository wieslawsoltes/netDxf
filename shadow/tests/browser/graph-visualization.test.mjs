import test from 'node:test';
import assert from 'node:assert/strict';
import {graphLimits,graphIdentity,graphBounds,mergeGraphProjection,graphPresentation,graphStyle,graphLegend,graphSourceLinks,graphResultScene,hitGraph,drawGraph,graphSvg} from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/graph-visualization.mjs';
import {graphProjectionRequest} from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/graph-contracts.mjs';
const node=(id,x,y,component=0)=>({id,x,y,z:0,component,degree:1,deadEnd:true});
const edge=(id,a,b,component=0)=>({id,a:a.id,b:b.id,start:a,end:b,entity:'H:LEAF',rootEntity:'H:ROOT',occurrence:`root/leaf/${id}`,insertPath:['H:ROOT'],component,bridge:true});
const a=node(70,0,0),b=node(400,10,0),e=edge(900,a,b);
const page=(nodes=[a,b],edges=[e],options={})=>({graphKey:'same-build',layout:'Model',componentCount:1,cycleRank:0,nodes:{items:nodes,total:2,nextOffset:null},edges:{items:edges,total:1,nextOffset:null},...options});
test('projection pages merge sparse IDs and report exact partial coverage',()=>{
  const first=mergeGraphProjection(null,page([a],[],{nodes:{items:[a],total:2,nextOffset:1},edges:{items:[],total:1,nextOffset:0}}));
  assert.equal(first.partial,true);assert.deepEqual(graphBounds(first),{minX:0,minY:0,maxX:0,maxY:0,isEmpty:false});
  const complete=mergeGraphProjection(first,page([b],[e]));
  assert.deepEqual(complete.nodes.map(n=>n.id),[70,400]);assert.equal(complete.edges[0].id,900);assert.equal(complete.partial,false);
  assert.throws(()=>mergeGraphProjection(first,{...page(),graphKey:'new-build'}),/changed between pages/);
  assert.throws(()=>mergeGraphProjection(null,page([{...a,x:NaN}])),/coordinates/);
});
test('projection renderer caps retained graph objects and never labels an incomplete projection complete',()=>{
  const nodes=Array.from({length:graphLimits.nodes+2},(_,i)=>node(i,i,0));
  const result=mergeGraphProjection(null,page(nodes,[],{nodes:{items:nodes,total:nodes.length,nextOffset:null},edges:{items:[],total:0,nextOffset:null}}));
  assert.equal(result.nodes.length,graphLimits.nodes);assert.equal(result.partial,true);assert.equal(result.capped,true);
});
test('presentation colors expose components, topology features, selected item and traversal depth',()=>{
  const base=mergeGraphProjection(null,page()),scene=graphPresentation(base,null,{mode:'components'});
  assert.notEqual(graphStyle(a,'node',scene).color,graphStyle({...a,component:1},'node',scene).color);
  scene.mode='features';assert.equal(graphStyle(a,'node',scene).color,'#ff7d8a');assert.equal(graphStyle(e,'edge',scene).color,'#d99cff');
  assert.equal(graphLegend(scene).length,5);
  const result=graphResultScene({nodes:[{node:a,depth:0},{node:b,viaEdge:e,depth:1}]});
  const highlighted=graphPresentation(base,result,{mode:'result',selected:{kind:'node',id:b.id}});
  assert.equal(graphStyle(b,'node',highlighted).color,'#ffffff');assert.equal(highlighted.resultEdges.has(e.id),true);
  assert.equal(graphStyle({...e,id:44},'edge',highlighted).alpha,.22);
});
test('graph hit testing works in pixels with sparse endpoint geometry and obeys visibility',()=>{
  const scene=graphPresentation(mergeGraphProjection(null,page()),null),project=p=>({x:p.x*10,y:p.y*10});
  assert.equal(hitGraph(scene,{x:1,y:1},project).node.id,70);
  assert.equal(hitGraph(scene,{x:50,y:2},project).edge.id,900);
  assert.equal(hitGraph(scene,{x:50,y:2},project,{kind:'node'}),null);
  assert.equal(hitGraph({...scene,showEdges:false},{x:50,y:2},project),null);
  assert.equal(hitGraph({...scene,nodes:[]},{x:50,y:2},project).edge.id,900);
  assert.equal(hitGraph(scene,{x:50,y:20},project),null);
});
test('source linkage keeps exact occurrences and owning roots distinct from definition leaves',()=>{
  assert.deepEqual(graphSourceLinks({edges:[e,e,{...e,id:901,occurrence:'other/leaf',rootEntity:'H:OTHER'}]}),[
    {entity:'H:LEAF',occurrence:'root/leaf/900',rootEntity:'H:ROOT',insertPath:['H:ROOT']},
    {entity:'H:LEAF',occurrence:'other/leaf',rootEntity:'H:OTHER',insertPath:['H:ROOT']}]);
});
test('identity preserves topology through selection changes but fences revisions, layout and font changes',()=>{
  const state={serverId:'s',documentId:'d',workspaceId:'w',viewVersion:1,revision:2,layout:'Model',fontRevision:3,changeVersion:4};
  assert.equal(graphIdentity(state),graphIdentity({...state,changeVersion:5}));
  for(const key of ['serverId','documentId','workspaceId','viewVersion','revision','layout','fontRevision'])assert.notEqual(graphIdentity(state),graphIdentity({...state,[key]:'other'}));
});
test('projection queries carry bounded pages, filters, topology options and build guard',()=>{
  const q=graphProjectionRequest({graphKey:'build',projectionUnused:1,component:'2',rootEntity:' H:1 ',endpointTolerance:.2},{documentId:'d',workspaceId:'w',viewVersion:1,revision:2,layout:'Model'},{nodeOffset:2000,edgeOffset:4000});
  assert.equal(q.tool,'graph_visualization');assert.equal(q.arguments.nodeLimit,2000);assert.equal(q.arguments.edgeLimit,4000);assert.equal(q.arguments.rootEntity,'H:1');assert.equal(q.arguments.component,2);assert.equal(q.arguments.graphKey,'build');assert.equal(q.arguments.offset,undefined);
  assert.throws(()=>graphProjectionRequest({component:'-1'},{}),/component/);
});
test('SVG export escapes source metadata and clearly identifies partial XY projection',()=>{
  const scene=graphPresentation({...mergeGraphProjection(null,page()),partial:true},null);
  scene.edges[0]={...scene.edges[0],entity:'<script>evil</script>',occurrence:'" onload="evil'};
  const svg=graphSvg(scene,{title:'<Graph & result>'});
  assert.match(svg,/&lt;script&gt;/);assert.doesNotMatch(svg,/<script>| onload="/);assert.match(svg,/Partial topology/);assert.match(svg,/XY projection/);assert.match(svg,/&lt;Graph &amp; result&gt;/);
  assert.equal((graphSvg(scene,{maximumEdges:0}).match(/<path /g)||[]).length,0);
});
test('canvas rendering bounds visible labels and restores caller drawing state',()=>{
  let texts=0,saves=0,restores=0;
  const c={save(){saves++;},restore(){restores++;},fillRect(){},beginPath(){},moveTo(){},lineTo(){},stroke(){},arc(){},fill(){},strokeText(){},fillText(){texts++;}};
  const nodes=Array.from({length:1000},(_,i)=>node(i,i%10,0));
  drawGraph(c,graphPresentation({nodes,edges:[]},null,{labels:'nodes'}),p=>p,{w:100,h:100});
  assert.equal(texts,graphLimits.labels);assert.equal(saves,1);assert.equal(restores,1);
});
test('SVG retains requested labels, node opacity, and accurate geometry/label truncation notices',()=>{
  const scene=graphPresentation(mergeGraphProjection(null,page()),{nodes:[a],edges:[]},{mode:'result',labels:'all'});
  const svg=graphSvg(scene);assert.match(svg,/>E900<\/text>/);assert.match(svg,/>N70<\/text>/);assert.match(svg,/>N400<\/text>/);assert.match(svg,/<circle[^>]+opacity="0.22"/);assert.match(svg,/2 exported nodes, 1 exported edges, 3 labels/);
  const thumbnail=graphSvg(scene,{maximumNodes:1,maximumEdges:0});assert.match(thumbnail,/1 exported nodes, 0 exported edges/);assert.match(thumbnail,/Export geometry truncated/);assert.doesNotMatch(thumbnail,/>E900<\/text>/);
  const large=graphSvg(graphPresentation({nodes:Array.from({length:600},(_,i)=>node(i,i,0)),edges:[]},null,{labels:'nodes'}));
  assert.equal((large.match(/<text /g)||[]).length,graphLimits.labels);
});
