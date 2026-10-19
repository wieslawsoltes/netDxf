import { test } from 'node:test';
import assert from 'node:assert/strict';
import { graphRequest, graphData, graphOverlay } from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/graph-contracts.mjs';
const state = {documentId:'document',workspaceId:'branch',viewVersion:4,revision:7,layout:'Model'};
test('graph requests carry full view identity and bounded build/query options',()=>{
  const request=graphRequest('bridges',{limit:12,endpointTolerance:.1,respectElevation:false},state,24);
  assert.equal(request.tool,'graph_features');assert.equal(request.arguments.kind,'bridges');
  assert.equal(request.arguments.revision,7);assert.equal(request.arguments.workspaceId,'branch');assert.equal(request.arguments.offset,24);
  assert.equal(request.arguments.respectElevation,false);assert.equal(request.arguments.maximumSegments,100000);
  assert.equal(request.arguments.endpointTolerance,.1);
  assert.throws(()=>graphRequest('nodes',{limit:201},state));assert.throws(()=>graphRequest('path',{source:-1},state));
  assert.throws(()=>graphRequest('summary',{curveTolerance:0},state));assert.throws(()=>graphRequest('unknown',{},state));
});
test('graph filters and path/traversal operations emit only supported schema fields',()=>{
  assert.deepEqual(Object.keys(graphRequest('summary',{},state).arguments).filter(k=>['offset','limit','source','kind'].includes(k)),[]);
  assert.equal(graphRequest('nodes',{component:2,minimumDegree:3},state).arguments.minimumDegree,3);
  assert.equal(graphRequest('edges',{entity:' H:1 '},state).arguments.entity,'H:1');
  assert.equal(graphRequest('path',{source:3,target:7},state).arguments.target,7);
  assert.equal(graphRequest('traverse',{source:3,depth:0},state).arguments.depth,0);
  assert.equal(graphRequest('traverse',{},state).arguments.offset,undefined);
  assert.equal(graphRequest('nearest',{x:1,y:2},state).arguments.z,0);
  assert.equal(graphRequest('cycle',{cycle:3},state).arguments.cycle,3);
  assert.equal(graphRequest('cycles',{},state).tool,'graph_cycles');
  assert.equal(graphRequest('spanning_forest',{},state).tool,'graph_spanning_forest');
  assert.equal(graphRequest('edges',{edge:4,node:2,occurrence:' exact '},state).arguments.occurrence,'exact');
  assert.equal(graphRequest('edges',{edge:4,node:2},state).arguments.node,2);
  assert.equal(graphRequest('nodes',{node:7},state).arguments.node,7);
  assert.throws(()=>graphRequest('cycle',{cycle:-1},state));
});
test('graph receipts reject stale identities and propagate model-visible errors',()=>{
  const response={isError:false,result:{...state,data:{layout:'Model',nodeCount:5}}};
  assert.equal(graphData(response,state).nodeCount,5);
  for(const key of ['documentId','workspaceId','viewVersion','revision'])assert.throws(()=>graphData({...response,result:{...response.result,[key]:'stale'}},state));
  assert.throws(()=>graphData({isError:true,result:{error:{message:'Budget exceeded'}}},state),/Budget exceeded/);
  assert.throws(()=>graphData({isError:false,result:{truncated:true}},state),/Reduce the page size/);
  assert.equal(graphData(null,state),null);
});
test('page overlays retain sparse topology IDs and path edges without array-index confusion',()=>{
  const edge={id:17,a:400,b:900,entity:'H:1',start:{x:1,y:2},end:{x:3,y:4}};
  const overlay=graphOverlay({steps:{items:[{node:{id:400,x:1,y:2}},{node:{id:900,x:3,y:4},viaEdge:edge}]}});
  assert.equal(overlay.nodes.length,2);assert.deepEqual(overlay.nodes.map(n=>n.id),[400,900]);assert.deepEqual(overlay.edges,[edge]);
  assert.equal(graphOverlay({items:[edge]}).nodes.length,2);
  assert.equal(graphOverlay({node:{id:20,x:1,y:2}}).nodes[0].id,20);
});
