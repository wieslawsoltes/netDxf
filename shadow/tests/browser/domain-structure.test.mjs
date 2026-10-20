import test from 'node:test';
import assert from 'node:assert/strict';
import {connectivityRequest,connectivityOffsets,connectivityStreams,mergeConnectivity,connectivitySvg,connectivitySelectionIndex,connectivitySelection} from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/domain-connectivity-model.mjs';
import {domainLoopProof,domainProofIndex,domainCondensationSvg,domainStructureCsv} from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/domain-structure-model.mjs';
import {domainConnectivityTools} from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/domain-connectivity-ui.mjs';
import {installDom,byText,byLabel,gate,tick} from './ui-fixture.mjs';
const pg=(items,total=items.length,offset=0)=>({items,total,offset,nextOffset:offset+items.length<total?offset+items.length:null});
const state={documentId:'doc',workspaceId:'live',viewVersion:1,revision:0,layout:'Model'};
const nodes=Array.from({length:4},(_,i)=>({id:'n'+i,key:'n'+i,port:'p'+i,entity:{value:'H:'+i},tag:'P'+i,class:'pipe',kind:'process',position:{x:i,y:0,z:0},component:0,degree:i===2?3:2,open:false,passagesDeclared:true}));
const edges=[[0,1],[1,2],[2,0],[2,3]].map(([a,b],i)=>({id:'e'+i,source:'n'+a,target:'n'+b,kind:'passage',direction:'forward',reciprocal:true,span:1}));
const steps=edges.slice(0,3).map((e,i)=>({id:'0:'+i,loop:0,step:i,source:e.source,target:e.target,connection:e.id}));
function receipt(over={}){
 return {graphKey:'g',...state,module:'pid',operation:'structure',complete:true,declarationsComplete:true,issuesComplete:true,objectsVisited:4,recordCount:4,limitations:[],options:{},
  nodes:pg(nodes),edges:pg(edges),issues:pg([]),components:pg([{id:0,nodeCount:4,edgeCount:4,entityCount:4,openPortCount:0}]),visits:pg([]),
  structure:{scopeComplete:true,loopSearchComplete:true,loopLimitReached:false,proofLimitReached:false,searchLimitReached:false,searchWork:5,options:{maximumLoops:20,maximumProofSteps:4000,maximumSearchWork:100000}},
  strongComponents:pg([{id:0,portCount:3,connectionCount:3,loopStatus:'found'},{id:1,portCount:1,connectionCount:0,loopStatus:'absent'}]),
  portStructures:pg(nodes.map((n,i)=>({id:n.id,strongComponent:i===3?1:0,articulationPoint:i===2}))),
  condensation:pg([{id:'0>1',source:0,target:1,connectionCount:1}]),criticalConnections:pg([{id:'e3',source:'n2',target:'n3'}]),loops:pg([{id:0,strongComponent:0,stepCount:3}]),loopSteps:pg(steps),...over};
}
const values={operation:'structure',direction:'both',maximumVisits:1000,maximumDepth:64,maximumObjects:10000,tolerance:.01};
test('structure request has bounded evidence options and ignores obsolete hidden endpoints',()=>{
 const request=connectivityRequest('pid',{...values,source:'x'.repeat(9000),target:'bad'},state);
 assert.equal(request.source,undefined);assert.deepEqual(request.structureOptions,{maximumLoops:20,maximumProofSteps:4000,maximumSearchWork:100000});
 for(const patch of [{maximumLoops:101},{maximumProofSteps:1},{maximumSearchWork:1000001}])assert.throws(()=>connectivityRequest('pid',{...values,...patch},state));
 assert.equal(connectivityOffsets.condensation,'condensationOffset');assert.equal(connectivityOffsets.loopSteps,'loopStepOffset');
});
test('eleven adaptive streams preserve exact cursors and immutable structure scope',()=>{
 const first=mergeConnectivity(null,receipt({loopSteps:pg(steps.slice(0,1),3)}));
 const next=receipt({loopSteps:pg(steps.slice(1),3,1)});
 for(const name of connectivityStreams.filter(n=>n!=='loopSteps'))next[name]=pg([],first.totals[name],first[name].length);
 const joined=mergeConnectivity(first,next);assert.equal(joined.partial,false);assert.equal(joined.loopSteps.length,3);
 assert.equal(connectivityRequest('pid',values,state,first).loopStepOffset,1);
 assert.throws(()=>mergeConnectivity(first,{...next,structure:{...next.structure,options:{...next.structure.options,maximumLoops:1}}}),/scope/);
 assert.throws(()=>mergeConnectivity(null,receipt({strongComponents:pg([{id:0,portCount:-1,connectionCount:0,loopStatus:'found'}])})),/component/);
});
test('loop proofs require complete closed distinct connections in declared direction',()=>{
 const scene=mergeConnectivity(null,receipt());assert.deepEqual(domainLoopProof(scene,0),{nodes:['n0','n1','n2','n0'],edges:['e0','e1','e2'],complete:true});
 assert.deepEqual(domainLoopProof(scene,0,domainProofIndex(scene)),domainLoopProof(scene,0));
 assert.throws(()=>domainLoopProof({...scene},0,domainProofIndex(scene)),/another receipt/);
 assert.equal(domainLoopProof({...scene,loopSteps:steps.slice(0,1)},0).complete,false);
 assert.equal(domainLoopProof({...scene,edges:edges.slice(0,2)},0).complete,false);
 assert.throws(()=>domainLoopProof({...scene,edges:edges.map(e=>({...e,direction:'reverse'}))},0),/direction/);
 assert.throws(()=>domainLoopProof({...scene,loopSteps:steps.map(s=>({...s,connection:'e0'}))},0),/chain/);
 assert.throws(()=>domainLoopProof({...scene,loopSteps:steps.map(s=>s.step===2?{...s,target:'n3'}:s)},0),/cycle|direction/);
});
test('condensation export caps dimensions and discloses omitted nodes, ranks, links and incomplete scope',()=>{
 const scene=mergeConnectivity(null,receipt());
 scene.strongComponents=Array.from({length:1000},(_,i)=>({id:i,portCount:1,connectionCount:0,loopStatus:'absent'}));
 scene.condensation=Array.from({length:999},(_,i)=>({id:i+'>'+(i+1),source:i,target:i+1,connectionCount:1}));
 scene.totals.strongComponents=1000;scene.totals.condensation=999;scene.module='<script>&"';scene.structure.scopeComplete=false;
 const svg=domainCondensationSvg(scene),match=svg.match(/width="(\d+)" height="(\d+)"/);
 assert.ok(Number(match[1])<1600&&Number(match[2])<7000);assert.match(svg,/120\/1000 groups and 119\/999 links/);
 assert.match(svg,/Scope incomplete/);assert.match(svg,/column seven/);assert.match(svg,/&lt;script&gt;/);assert.doesNotMatch(svg,/NaN|Infinity|<script>/);
});
test('structure exports retain coverage and proof while port SVG can highlight weak cuts',()=>{
 const scene=mergeConnectivity(null,receipt());assert.match(domainStructureCsv(scene,'loopSteps'),/scopeComplete/);assert.match(domainStructureCsv(scene,'loopSteps'),/"e2"/);
 assert.match(connectivitySvg(scene,{critical:true}),/#c25700/);assert.match(connectivitySvg(scene,{proof:domainLoopProof(scene,0)}),/#dc2626/);
 assert.match(connectivitySvg(scene,{strongComponent:1}),/1\/1 loaded ports/);
 assert.doesNotMatch(connectivitySvg(scene,{strongComponent:1}),/P0/);
});
function fixture(t){
 const root=installDom(t),reads=[],errors=[],selected=[],exports=[];
 const oldCreate=URL.createObjectURL,oldRevoke=URL.revokeObjectURL;let created=0,revoked=0;
 URL.createObjectURL=()=>`blob:${++created}`;URL.revokeObjectURL=()=>revoked++;t.after(()=>{URL.createObjectURL=oldCreate;URL.revokeObjectURL=oldRevoke;});
 const client={id:'d',key:'k',state:{...state},data:{modules:[{id:'pid',enabled:true}],items:nodes.map(n=>({id:n.entity.value})),moduleConfigurationVersion:0},read(path,options){const wait=gate();reads.push({...wait,path,...options});return wait.promise;}};
 const ui=domainConnectivityTools({client,selectEntities:async ids=>selected.push(ids),report:e=>errors.push(e),download:(blob,name)=>exports.push({blob,name})});
 ui.setOperation('structure');ui.render(root,'pid');t.after(()=>ui.hide());
 return {root,ui,client,reads,errors,selected,exports,blobs:()=>({created,revoked})};
}
async function load(f,data=receipt()){const pending=byText(f.root,'Run connectivity analysis').click();await tick();f.reads.at(-1).resolve(data);await pending;}
test('structure UI inspects exact proofs and explicit sources, exports CSV/SVG and updates pressed state',async t=>{
 const f=fixture(t);assert.equal(f.reads.length,0);assert.equal(byLabel(f.root,'Connectivity operation').value,'structure');
 await load(f);assert.equal(f.selected.length,0);assert.match(f.root.textContent,/not simulated failure/);
 await byText(f.root,'Show condensation').click();assert.equal(byText(f.root,'Show condensation').getAttribute('aria-pressed'),'true');
 await byText(f.root,'Show port structure').click();assert.equal(byText(f.root,'Show port structure').getAttribute('aria-pressed'),'true');
 await byText(f.root,'Inspect loop 0').click();assert.match(f.root.textContent,/Closed directed proof: 3 distinct connections/);
 await byText(f.root,'Select loop 0 sources').click();assert.deepEqual(f.selected,[['H:0','H:1','H:2']]);
 await byText(f.root,'Export loop proof CSV').click();assert.match(await f.exports[0].blob.text(),/"e2"/);
 await byText(f.root,'Export condensation SVG').click();assert.match(await f.exports[1].blob.text(),/condensation/);
 await byText(f.root,'Inspect condensation 0>1').click();assert.match(f.root.textContent,/1\/1 supporting declared connections/);
 assert.deepEqual(f.errors,[]);
});
test('partial loop pages and unavailable source roots cannot be presented as selectable proof',async t=>{
 const f=fixture(t);await load(f,receipt({loopSteps:pg(steps.slice(0,1),3)}));
 assert.equal(byText(f.root,'Inspect loop 0').disabled,true);assert.equal(byText(f.root,'Select loop 0 sources').disabled,true);
 const more=byText(f.root,'Load remaining connectivity').click();await tick();assert.equal(f.reads[1].body.loopStepOffset,1);assert.equal(f.reads[1].body.condensationOffset,1);
 const next=receipt({loopSteps:pg(steps.slice(1),3,1)});for(const name of connectivityStreams.filter(n=>n!=='loopSteps'))next[name]=pg([],next[name]?.total??0,next[name]?.total??0);
 f.reads[1].resolve(next);await more;assert.equal(byText(f.root,'Inspect loop 0').disabled,false);
 f.client.data.items=[];f.ui.render(f.root,'pid');assert.equal(byText(f.root,'Select loop 0 sources').disabled,true);assert.equal(f.selected.length,0);
});
test('setOperation fences pending requests, clears prior evidence and never auto-runs',async t=>{
 const f=fixture(t),pending=byText(f.root,'Run connectivity analysis').click();await tick();f.ui.setOperation('network');f.ui.render(f.root,'pid');
 f.reads[0].resolve(receipt());await pending;assert.equal(f.blobs().created,0);assert.equal(f.reads.length,1);assert.equal(byText(f.root,'Inspect loop 0'),undefined);
 assert.equal(byLabel(f.root,'Connectivity operation').value,'network');assert.throws(()=>f.ui.setOperation('eval'),/Unknown/);
});
test('fresh structural query clears loop overlay and hide cancels in-flight continuation',async t=>{
 const f=fixture(t);await load(f);await byText(f.root,'Inspect loop 0').click();await byText(f.root,'Export connectivity SVG').click();assert.match(await f.exports[0].blob.text(),/#dc2626/);
 await load(f,receipt({graphKey:'fresh',loopSteps:pg(steps.slice(0,1),3)}));await byText(f.root,'Export connectivity SVG').click();assert.doesNotMatch(await f.exports[1].blob.text(),/#dc2626/);
 const pending=byText(f.root,'Load remaining connectivity').click();await tick();f.ui.hide();f.reads.at(-1).resolve(receipt());await pending;
 assert.equal(f.blobs().created,f.blobs().revoked);assert.deepEqual(f.errors,[]);
});
test('structure port trace action restores the direction control immediately',async t=>{
 const f=fixture(t);await load(f);assert.equal(byLabel(f.root,'Connectivity direction').disabled,true);
 await byText(f.root,'P0 · p0').click();const pending=byText(f.root,'Trace from this port').click();await tick();
 assert.equal(byLabel(f.root,'Connectivity operation').value,'trace');assert.equal(byLabel(f.root,'Connectivity direction').disabled,false);
 assert.equal(f.reads[1].body.operation,'trace');f.ui.hide();f.reads[1].resolve(null);await pending;assert.deepEqual(f.errors,[]);
});
test('shared proof and selection indexes avoid per-witness scans of the whole graph',()=>{
 const scene=mergeConnectivity(null,receipt());let portScans=0,edgeScans=0;
 scene.nodes.map=function(...args){portScans++;return Array.prototype.map.apply(this,args);};
 scene.edges.map=function(...args){edgeScans++;return Array.prototype.map.apply(this,args);};
 const proofIndex=domainProofIndex(scene),rows=nodes.map(n=>({id:n.entity.value})),selectionIndex=connectivitySelectionIndex(scene,rows);
 for(let i=0;i<100;i++){const proof=domainLoopProof(scene,0,proofIndex);assert.equal(connectivitySelection(scene,proof.nodes,rows,selectionIndex).reason,null);}
 assert.equal(portScans,2);assert.equal(edgeScans,1);
 assert.throws(()=>connectivitySelection({...scene},['n0'],rows,selectionIndex),/another receipt/);
});
