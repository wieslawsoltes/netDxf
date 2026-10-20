import test from 'node:test';
import assert from 'node:assert/strict';
import {connectivityRequest,mergeConnectivity,connectivityStreams,connectivitySvg} from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/domain-connectivity-model.mjs';
import {isolationExclusions,isolationProof,isolationProofIndex,isolationCsv} from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/domain-isolation-model.mjs';
import {domainConnectivityTools} from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/domain-connectivity-ui.mjs';
import {installDom,byText,byLabel,gate,tick} from './ui-fixture.mjs';
const pg=(items,total=items.length,offset=0)=>({items,total,offset,nextOffset:offset+items.length<total?offset+items.length:null});
const state={documentId:'d',workspaceId:'live',viewVersion:1,revision:0,layout:'Model'};
const nodes=Array.from({length:4},(_,i)=>({id:'n'+i,key:'n'+i,port:'p'+i,entity:{value:'H:'+i},tag:'N'+i,class:'pipe',kind:'process',position:{x:i,y:0,z:0},component:0,degree:2,open:false,passagesDeclared:true}));
const edges=[[0,1],[1,3],[0,2],[2,3]].map(([a,b],i)=>({id:'e'+i,source:'n'+a,target:'n'+b,kind:'passage',direction:'forward',reciprocal:true,span:1}));
const before=[{id:'n0',depth:0,predecessor:null,connection:null},{id:'n1',depth:1,predecessor:'n0',connection:'e0'},{id:'n2',depth:1,predecessor:'n0',connection:'e2'},{id:'n3',depth:2,predecessor:'n1',connection:'e1'}];
const after=[before[0],before[2],{id:'n3',depth:2,predecessor:'n2',connection:'e3'}];
const summary={complete:true,scopeComplete:true,visitLimitReached:false,depthLimitReached:false,edgeLimitReached:false,examinedEdges:8,visitedCount:4,targetStatus:'reachable',targetPathLength:2};
function receipt(over={}) {
 return {graphKey:'g',...state,module:'pid',operation:'isolation',complete:true,declarationsComplete:true,issuesComplete:true,recordCount:4,limitations:[],options:{},
  nodes:pg(nodes),edges:pg(edges),issues:pg([]),components:pg([{id:0,nodeCount:4,edgeCount:4,entityCount:4,openPortCount:0}]),visits:pg([]),
  isolation:{source:'n0',target:'n3',direction:'forward',scopeComplete:true,options:{maximumVisits:10000,maximumDepth:256,maximumExaminedEdges:200000},before:summary,after:{...summary,visitedCount:3}},
  assessments:pg(nodes.map(n=>({id:n.id,before:'reachable',after:n.id==='n1'?'unreachable':'reachable',status:n.id==='n1'?'lost':'reachable'}))),
  beforeVisits:pg(before),afterVisits:pg(after),exclusions:pg([{id:'connection:e0',kind:'connection',value:'e0'}]),...over};
}
const values={operation:'isolation',source:'n0',target:'n3',direction:'forward',maximumVisits:10000,maximumDepth:256,maximumObjects:10000,tolerance:.01,excludedConnections:' e0 \ne0',excludedPorts:''};
test('isolation requests normalize exact bounded IDs, reject source removal and permit no target',()=>{
 const request=connectivityRequest('pid',values,state);assert.deepEqual(request.excludedConnections,['e0']);assert.deepEqual(request.excludedPorts,[]);
 assert.deepEqual(request.isolationOptions,{maximumVisits:10000,maximumDepth:256,maximumExaminedEdges:200000});
 assert.equal(connectivityRequest('pid',{...values,target:''},state).target,undefined);
 assert.throws(()=>connectivityRequest('pid',{...values,excludedPorts:'n0'},state),/source/);
 assert.throws(()=>connectivityRequest('pid',{...values,maximumExaminedEdges:400001},state),/examination/);
 assert.throws(()=>isolationExclusions(Array.from({length:2001},(_,i)=>'e'+i).join('\n')),/2000/);
 assert.throws(()=>isolationExclusions(Array.from({length:100},(_,i)=>i+'x'.repeat(1300)).join('\n')),/128000/);
 assert.throws(()=>isolationExclusions('x'.repeat(4129)),/length/);
 const unicode='passage:'+encodeURIComponent('界'.repeat(128))+'/p|'+encodeURIComponent('界'.repeat(128))+'/q:both';
 assert.deepEqual(isolationExclusions(unicode).excludedConnections,[unicode]);
});
test('fifteen streams follow actual offsets and reject mixed scenario scope or malformed rows',()=>{
 const first=mergeConnectivity(null,receipt({beforeVisits:pg(before.slice(0,1),4),afterVisits:pg(after.slice(0,1),3),exclusions:pg([{id:'connection:e0',kind:'connection',value:'e0'}],2)}));
 const next=receipt({beforeVisits:pg(before.slice(1),4,1),afterVisits:pg(after.slice(1),3,1),exclusions:pg([{id:'connection:e1',kind:'connection',value:'e1'}],2,1)});
 for(const name of connectivityStreams.filter(n=>!['beforeVisits','afterVisits','exclusions'].includes(n)))next[name]=pg([],first.totals[name],first[name].length);
 const joined=mergeConnectivity(first,next);assert.equal(joined.partial,false);
 assert.equal(connectivityRequest('pid',values,state,first).beforeVisitOffset,1);assert.equal(connectivityRequest('pid',values,state,first).exclusionOffset,1);
 assert.throws(()=>mergeConnectivity(first,{...next,isolation:{...next.isolation,source:'n1'}}),/scope/);
 assert.throws(()=>mergeConnectivity(null,receipt({assessments:pg([{id:'n0',before:'maybe',after:'reachable',status:'reachable'}])})),/assessment/);
 assert.throws(()=>mergeConnectivity(null,receipt({exclusions:pg([{id:'wrong',kind:'connection',value:'e0'}])})),/exclusion/);
});
test('before/after proofs require loaded exact directed chains and complete assumption pages',()=>{
 const scene=mergeConnectivity(null,receipt()),index=isolationProofIndex(scene);
 assert.deepEqual(isolationProof(scene,'n3',false,index),{nodes:['n0','n1','n3'],edges:['e0','e1'],complete:true});
 assert.deepEqual(isolationProof(scene,'n3',true,index),{nodes:['n0','n2','n3'],edges:['e2','e3'],complete:true});
 assert.equal(isolationProof({...scene,afterVisits:after.slice(1)},'n3').complete,false);
 assert.equal(isolationProof({...scene,edges:edges.slice(0,3)},'n3').complete,false);
 assert.equal(isolationProof({...scene,exclusions:[]},'n3').complete,false);
 assert.throws(()=>isolationProof({...scene},'n3',true,index),/another receipt/);
 assert.throws(()=>isolationProof({...scene,afterVisits:before},'n3'),/excluded/);
 assert.throws(()=>isolationProof({...scene,edges:edges.map(e=>({...e,direction:'reverse'}))},'n3'),/direction/);
 assert.throws(()=>isolationProof({...scene,afterVisits:after.map(v=>v.id==='n0'?{...v,depth:1}:v)},'n3'),/depth|root/);
});
test('scenario CSV/SVG exports disclose scope, assumptions, partial pages and exact evidence',()=>{
 const scene=mergeConnectivity(null,receipt());scene.isolation.scopeComplete=false;scene.partial=true;scene.module='=HYPERLINK("bad")';
 const csv=isolationCsv(scene);assert.match(csv,/beforeComplete/);assert.match(csv,/"lost"/);assert.match(csv,/"'=HYPERLINK/);assert.match(csv,/"false"/);
 const svg=connectivitySvg(scene,{proof:isolationProof(scene,'n3')});
 assert.match(svg,/#ffe3e3/);assert.match(svg,/#ddf7e8/);assert.match(svg,/excluded assumption/);assert.match(svg,/not valve states/);assert.match(svg,/#dc2626/);
 assert.match(isolationCsv(scene,'afterVisits'),/"e3"/);
});
function fixture(t) {
 const root=installDom(t),reads=[],errors=[],selected=[],exports=[];let created=0,revoked=0;
 const oldCreate=URL.createObjectURL,oldRevoke=URL.revokeObjectURL;
 URL.createObjectURL=()=>`blob:${++created}`;URL.revokeObjectURL=()=>revoked++;t.after(()=>{URL.createObjectURL=oldCreate;URL.revokeObjectURL=oldRevoke;});
 const client={id:'d',key:'k',state:{...state},data:{modules:[{id:'pid',enabled:true}],items:nodes.map(n=>({id:n.entity.value})),moduleConfigurationVersion:0},read(path,options){const pending=gate();reads.push({...pending,path,...options});return pending.promise;}};
 const ui=domainConnectivityTools({client,selectEntities:async(ids,_position,_extend,key)=>selected.push({ids,key}),report:e=>errors.push(e),download:(blob,name)=>exports.push({blob,name})});
 ui.setOperation('isolation');ui.render(root,'pid');t.after(()=>ui.hide());
 for(const [label,value]of [['Connectivity source port','n0'],['Connectivity target port','n3'],['Excluded connection IDs','e0']]){const input=byLabel(root,label);input.value=value;input.dispatch('input');}
 return {root,ui,client,reads,errors,selected,exports,blobs:()=>({created,revoked})};
}
async function load(f,data=receipt()){const pending=byText(f.root,'Run connectivity analysis').click();await tick();f.reads.at(-1).resolve(data);await pending;}
test('isolation UI compares paths without selecting, shows exact proof, exports and explicitly selects sources',async t=>{
 const f=fixture(t);assert.equal(f.reads.length,0);await load(f);assert.deepEqual(f.reads[0].body.excludedConnections,['e0']);assert.equal(f.selected.length,0);
 assert.match(f.root.textContent,/lost · before reachable → after unreachable/);assert.match(f.root.textContent,/does not close a valve/);
 await byText(f.root,'Inspect scenario target path').click();assert.match(f.root.textContent,/n0 → n2/);
 await byText(f.root,'Select isolation proof sources').click();assert.deepEqual(f.selected,[{ids:['H:0','H:2','H:3'],key:'k'}]);
 await byText(f.root,'Export scenario predecessors CSV').click();assert.match(await f.exports[0].blob.text(),/"e3"/);
 await byText(f.root,'Export reachability CSV').click();assert.match(await f.exports[1].blob.text(),/"lost"/);
 assert.deepEqual(f.errors,[]);
});
test('partial proof pages disable inspection; continuation keeps original exclusions despite edited form',async t=>{
 const f=fixture(t);await load(f,receipt({afterVisits:pg(after.slice(0,1),3)}));assert.equal(byText(f.root,'Inspect scenario target path').disabled,true);
 const input=byLabel(f.root,'Excluded connection IDs');input.value='e2';input.dispatch('input');
 const more=byText(f.root,'Load remaining connectivity').click();await tick();assert.deepEqual(f.reads[1].body.excludedConnections,['e0']);assert.equal(f.reads[1].body.afterVisitOffset,1);
 const data=receipt({afterVisits:pg(after.slice(1),3,1)});for(const name of connectivityStreams.filter(n=>n!=='afterVisits'))data[name]=pg([],data[name]?.total??0,data[name]?.total??0);
 f.reads[1].resolve(data);await more;assert.equal(byText(f.root,'Inspect scenario target path').disabled,false);
 assert.deepEqual(f.errors,[]);
});
test('inspection can explicitly add a port or declared link assumption without running a scenario',async t=>{
 const f=fixture(t);await load(f);await byText(f.root,'N1 · p1').click();await byText(f.root,'Exclude this port').click();assert.equal(byLabel(f.root,'Excluded port IDs').value,'n1');
 await byText(f.root,'Exclude connection e1').click();assert.equal(byLabel(f.root,'Excluded connection IDs').value,'e0\ne1');assert.equal(f.reads.length,1);assert.equal(f.selected.length,0);
 await byText(f.root,'Clear exclusion assumptions').click();assert.equal(byLabel(f.root,'Excluded port IDs').value,'');assert.equal(byLabel(f.root,'Excluded connection IDs').value,'');
});
test('fresh isolation response clears old proof; unavailable sources and stale documents cannot select',async t=>{
 const f=fixture(t);await load(f);await byText(f.root,'Inspect scenario target path').click();await byText(f.root,'Export connectivity SVG').click();assert.match(await f.exports[0].blob.text(),/#dc2626/);
 await load(f,receipt({graphKey:'g2'}));await byText(f.root,'Export connectivity SVG').click();assert.doesNotMatch(await f.exports[1].blob.text(),/#dc2626/);
 f.client.data.items=[];f.ui.render(f.root,'pid');assert.equal(byText(f.root,'Select comparison source n1').disabled,true);
 await byText(f.root,'Inspect scenario target path').click();assert.equal(byText(f.root,'Select isolation proof sources').disabled,true);
 f.client.key='changed';await byText(f.root,'Export connectivity JSON').click();assert.match(f.errors.at(-1).message,/changed/);assert.equal(f.selected.length,0);
});
test('clear and hide fence pending isolation queries and revoke blobs',async t=>{
 const f=fixture(t);await load(f);const pending=byText(f.root,'Run connectivity analysis').click();await tick();
 await byText(f.root,'Clear connectivity').click();f.reads[1].resolve(receipt());await pending;
 assert.equal(byText(f.root,'Inspect scenario target path'),undefined);assert.equal(f.blobs().created,f.blobs().revoked);assert.deepEqual(f.errors,[]);
});
