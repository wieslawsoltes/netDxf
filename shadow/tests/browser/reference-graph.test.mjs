import test from 'node:test';
import assert from 'node:assert/strict';
import {referenceRequest,referenceData,mergeReferenceGraph,referenceLayout,referenceSvg,hitReference} from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/reference-graph-model.mjs';
import {referenceGraphPalette} from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/reference-graph-ui.mjs';
import {byText,byLabel,gate,tick,installDom,all} from './ui-fixture.mjs';

const state={serverId:'server',documentId:'document',workspaceId:'live',viewVersion:1,revision:4};
const node=(id,depth=0)=>({id,type:'Fixture.Object',kind:'Object',depth,incoming:1,outgoing:1,exists:true});
const edge=(source,target,path='/Layer')=>({source,target,path});
const page=(items,total=items.length,offset=0,nextOffset=null)=>({items,total,offset,nextOffset});
const data=(overrides={})=>({referenceKey:'source-scope',direction:'outgoing',roots:['H:1'],nodeCount:2,edgeCount:1,objectsIndexed:12,
  nodes:page([node('H:1'),node('H:Layer',1)]),edges:page([edge('H:1','H:Layer')]),explorationComplete:true,...overrides});
test('reference requests bind source identity and keep topology coordinates/options out',()=>{
  assert.deepEqual(referenceRequest({},state).arguments,{documentId:'document',workspaceId:'live',viewVersion:1,revision:4,direction:'outgoing',nodeLimit:100,edgeLimit:200,depth:2,maximumNodes:100,maximumEdges:500});
  const args=referenceRequest({roots:'H:1, H:2 H:1',direction:'incoming',depth:0},state).arguments;
  assert.deepEqual(args.roots,['H:1','H:2']);assert.equal(args.direction,'incoming');assert.equal(args.depth,0);
  for(const values of [{depth:17},{maximumNodes:1001},{maximumEdges:0},{direction:'both'}])assert.throws(()=>referenceRequest(values,state));
  const scene=mergeReferenceGraph(null,data());assert.equal(referenceRequest({},state,scene).arguments.nodeOffset,2);
});
test('reference pages retain directed property paths and independently complete both streams',()=>{
  let scene=mergeReferenceGraph(null,data({nodes:page([node('H:1')],2,0,1),edges:page([edge('H:1','H:Layer')],2,0,1)}));
  assert.equal(scene.partial,true);
  scene=mergeReferenceGraph(scene,data({nodes:page([node('H:Layer',1)],2,1),edges:page([edge('H:1','H:Layer','/Style')],2,1)}));
  assert.equal(scene.partial,false);assert.equal(scene.edges.length,2);
  // Malformed cursors cannot skip an unreturned item.
  assert.throws(()=>mergeReferenceGraph(null,data({edges:page([],1,0,1)})));
});
test('reference page merging rejects stale scope, wrong identity and missing terminal data',()=>{
  const first=mergeReferenceGraph(null,data({nodes:page([node('H:1')],2,0,1),edges:page([edge('H:1','H:Layer')])}));
  const second=data({nodes:page([node('H:Layer',1)],2,1),edges:page([],1,1)});
  const scene=mergeReferenceGraph(first,second);assert.equal(scene.partial,false);assert.equal(scene.edges[0].source,'H:1');
  assert.throws(()=>mergeReferenceGraph(first,{...second,referenceKey:'other'}),/scope/);
  assert.throws(()=>mergeReferenceGraph(first,data({nodes:page([],2,1),edges:page([],1,1)})),/ended/);
  assert.throws(()=>referenceData({isError:false,result:{...state,revision:3,data:data()}},state),/changed/);
  assert.throws(()=>referenceData({isError:true,result:{error:{message:'Index budget exceeded'}}},state),/Index budget/);
});
test('directed diagrams escape labels, bound geometry and retain hit coordinates',()=>{
  const scene=mergeReferenceGraph(null,data({nodes:page([node('<root>'),{...node('H:Layer',1),type:'<script>bad</script>'}]),edges:page([edge('<root>','H:Layer','/a&b')])}));
  const svg=referenceSvg(scene,'<root>'),layout=referenceLayout(scene);
  assert.ok(svg.includes('marker-end="url(#arrow)"'));assert.ok(svg.includes('&lt;root&gt;'));assert.ok(!svg.includes('<script>'));
  assert.equal(hitReference(layout,layout.nodes[0].x+10,layout.nodes[0].y+10).id,'<root>');
  const large={...scene,nodes:Array.from({length:130},(_,i)=>node('N'+i,i%3)),edges:[]};
  assert.equal(referenceLayout(large).nodes.length,120);assert.match(referenceSvg(large),/Diagram display limit/);
});
function fixture(t) {
  const root=installDom(t),reads=[],errors=[],selected=[],downloads=[];
  const client={state:{...state},key:'current',data:{items:[{id:'H:1',type:'Line',layer:'0'}],selectedEntities:['H:1']},
    read(path,options){const pending=gate();reads.push({path,options,...pending});return pending.promise;}};
  const render=referenceGraphPalette({client,snapshot:()=>null,report:e=>errors.push(e),selectSources:(ids,key)=>selected.push({ids,key}),download:(...args)=>downloads.push(args)});
  const receipt=value=>({isError:false,result:{...client.state,data:value}});
  render(root);return {root,reads,errors,selected,downloads,client,render,receipt};
}
async function load(f,value=data()){
  const pending=byText(f.root,'Explore references').click();await tick();f.reads.at(-1).resolve(f.receipt(value));await pending;
}
test('selection roots explore without selecting; definition/resource selection stays unavailable',async t=>{
  const f=fixture(t),pending=byText(f.root,'Use drawing selection').click();await tick();
  assert.deepEqual(f.reads[0].options.body.arguments.roots,['H:1']);assert.deepEqual(f.selected,[]);
  f.reads[0].resolve(f.receipt(data()));await pending;
  await byText(f.root,'H:Layer · Object').click();assert.equal(byText(f.root,'Select source object').disabled,true);
  assert.match(f.root.textContent,/shared definition, resource/);
  await byText(f.root,'H:1 · Object').click();await byText(f.root,'Select source object').click();
  assert.deepEqual(f.selected,[{ids:['H:1'],key:'current'}]);
});
test('refocus follows incoming users and exact property links without editing geometry',async t=>{
  const f=fixture(t);await load(f);await byText(f.root,'H:Layer · Object').click();assert.match(f.root.textContent,/H:1 \/Layer → H:Layer/);
  const pending=byText(f.root,'Explore users').click();await tick();
  assert.equal(f.reads.at(-1).options.body.arguments.direction,'incoming');assert.deepEqual(f.reads.at(-1).options.body.arguments.roots,['H:Layer']);
  f.reads.at(-1).resolve(f.receipt(data({direction:'incoming',roots:['H:Layer']})));await pending;
  assert.deepEqual(f.selected,[]);assert.equal(f.client.state.revision,4);
});
test('input, hide, replacement and clear fence late reference responses',async t=>{
  for(const cancel of ['input','hide','drawing','clear']){
    const f=fixture(t),pending=byText(f.root,'Explore references').click();await tick();
    if(cancel==='input'){const input=byLabel(f.root,'Reference maximum depth');input.value='3';await input.dispatch('input');}
    if(cancel==='hide')f.render.hide();
    if(cancel==='drawing')f.client.key='other';
    if(cancel==='clear')await byText(f.root,'Clear reference view').click();
    f.reads[0].resolve(f.receipt(data()));await pending;assert.doesNotMatch(f.root.textContent,/12 indexed objects/);
  }
});
test('old source actions and exports refuse a changed drawing',async t=>{
  const f=fixture(t);await load(f);await byText(f.root,'H:1 · Object').click();f.client.key='changed';
  await byText(f.root,'Select source object').click();await byText(f.root,'Export reference JSON').click();
  assert.equal(f.errors.length,2);assert.deepEqual(f.selected,[]);assert.deepEqual(f.downloads,[]);
  const image=all(f.root).find(n=>n.tagName==='IMG');
  assert.doesNotThrow(()=>image.onclick({clientX:1,clientY:1}));assert.equal(f.errors.length,3);
});
test('adaptive continuation uses exact independent offsets and scope key',async t=>{
  const f=fixture(t);await load(f,data({nodes:page([node('H:1')],2,0,1)}));
  const pending=byText(f.root,'Load remaining references (bounded)').click();await tick();
  const args=f.reads.at(-1).options.body.arguments;assert.equal(args.referenceKey,'source-scope');assert.equal(args.nodeOffset,1);assert.equal(args.edgeOffset,1);
  f.reads.at(-1).resolve(f.receipt(data({nodes:page([node('H:Layer',1)],2,1),edges:page([],1,1)})));await pending;
  assert.equal(byText(f.root,'Load more references').disabled,true);assert.match(f.root.textContent,/2 \/ 2 objects/);
});
test('bulk reference loading stops at forty pages with honest resumable coverage',async t=>{
  const f=fixture(t),part=i=>data({nodes:page([node('N'+i)],50,i,i+1<50?i+1:null),edges:page([])});
  await load(f,part(0));
  const pending=byText(f.root,'Load remaining references (bounded)').click();
  for(let i=1;i<=40;i++){await tick();assert.equal(f.reads.at(-1).options.body.arguments.nodeOffset,i);f.reads.at(-1).resolve(f.receipt(part(i)));}
  await pending;
  assert.equal(f.reads.length,41);assert.match(f.root.textContent,/41 \/ 50 objects/);
  assert.match(f.root.textContent,/Partial pages loaded/);assert.equal(byText(f.root,'Load remaining references (bounded)').disabled,false);
});
test('clearing during bulk continuation discards every unpublished response',async t=>{
  const f=fixture(t),part=i=>data({nodes:page([node('N'+i)],5,i,i+1),edges:page([])});
  await load(f,part(0));const pending=byText(f.root,'Load remaining references (bounded)').click();await tick();
  f.reads.at(-1).resolve(f.receipt(part(1)));await tick();
  await byText(f.root,'Clear reference view').click();f.reads.at(-1).resolve(f.receipt(part(2)));await pending;
  assert.doesNotMatch(f.root.textContent,/indexed objects/);assert.equal(f.errors.length,0);
});
test('diagram uses revocable Blob images and exports honest bounded results',async t=>{
  const f=fixture(t),created=[],revoked=[],oldCreate=URL.createObjectURL,oldRevoke=URL.revokeObjectURL;
  URL.createObjectURL=blob=>{const url='blob:ref-'+created.length;created.push({url,blob});return url;};URL.revokeObjectURL=url=>revoked.push(url);
  t.after(()=>{URL.createObjectURL=oldCreate;URL.revokeObjectURL=oldRevoke;});
  await load(f);await byText(f.root,'H:1 · Object').click();await byText(f.root,'Export reference JSON').click();
  assert.equal(JSON.parse(f.downloads[0][1]).graph.referenceKey,'source-scope');f.render.hide();
  assert.equal(created.length,revoked.length);assert.equal(new Set(revoked).size,revoked.length);
  assert.ok(all(f.root).filter(n=>n.tagName==='IMG').every(n=>n.getAttribute('src').startsWith('blob:')));
});
