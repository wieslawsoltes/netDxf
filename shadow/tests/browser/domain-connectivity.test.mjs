import test from 'node:test';
import assert from 'node:assert/strict';
import {connectivityRequest,mergeConnectivity,connectivityProof,connectivitySelection,connectivitySvg,connectivityCsv} from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/domain-connectivity-model.mjs';
import {domainConnectivityTools} from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/domain-connectivity-ui.mjs';
import {installDom,byText,byLabel,all,gate,tick} from './ui-fixture.mjs';
const state=()=>({documentId:'doc',workspaceId:'live',viewVersion:1,revision:0,layout:'Model'});
const node=(i,over={})=>({id:`key/p${i}`,key:'key',port:`p${i}`,entity:{value:`H:${i}`},module:'pid',class:'pipe',tag:`P-${i}`,kind:'process',position:{x:i,y:0,z:0},component:0,degree:1,open:false,passagesDeclared:true,...over});
const edge=(i,a=i,b=i+1)=>({id:`e${i}`,source:`key/p${a}`,target:`key/p${b}`,kind:'connection',direction:'both',reciprocal:true,span:0});
const pg=(items,total=items.length,offset=0)=>({items,total,offset,nextOffset:offset+items.length<total?offset+items.length:null});
const receipt=(over={})=>({graphKey:'g',...state(),module:'pid',operation:'network',complete:true,declarationsComplete:true,issuesComplete:true,objectsVisited:2,recordCount:2,limitations:[],options:{},nodes:pg([node(0),node(1)]),edges:pg([edge(0)]),issues:pg([]),components:pg([{id:0,nodeCount:2,edgeCount:1,entityCount:2,openPortCount:0}]),visits:pg([]),...over});
const values={operation:'network',direction:'both',maximumVisits:1000,maximumDepth:64,maximumObjects:10000,tolerance:.01};
test('network request ignores stale hidden path inputs; invalid bounds and modes reject',()=>{
 assert.equal(connectivityRequest('pid',{...values,source:'x'.repeat(700)},state()).source,undefined);
 for(const patch of [{maximumVisits:0},{maximumDepth:-1},{maximumObjects:100001},{tolerance:NaN},{operation:'eval'},{direction:'up'}])assert.throws(()=>connectivityRequest('pid',{...values,...patch},state()));
 assert.throws(()=>connectivityRequest('pid',{...values,operation:'path'},state()),/source/);
});
test('adaptive short pages retain exact independent cursors and finished streams',()=>{
 const a=mergeConnectivity(null,receipt({nodes:pg([node(0)],3),edges:pg([],0),components:pg([])}));
 const b=mergeConnectivity(a,receipt({nodes:pg([node(1)],3,1),edges:pg([],0),components:pg([])}));
 const c=mergeConnectivity(b,receipt({nodes:pg([node(2)],3,2),edges:pg([],0),components:pg([])}));
 assert.equal(c.partial,false);assert.equal(c.nodes.length,3);assert.equal(connectivityRequest('pid',values,state(),b).nodeOffset,2);
});
test('identity changes, missing pages, duplicate nodes and lying totals reject atomically',()=>{
 const a=mergeConnectivity(null,receipt({nodes:pg([node(0)],2)}));
 for(const over of [{graphKey:'other'},{revision:2},{module:'electrical'},{operation:'trace'},{options:{max:1}}])assert.throws(()=>mergeConnectivity(a,receipt({...over,nodes:pg([node(1)],2,1),edges:pg([],1,1),components:pg([],1,1)})),/identity/);
 assert.throws(()=>mergeConnectivity(a,receipt({nodes:pg([node(0)],2,1),edges:pg([],1,1),components:pg([],1,1)})),/Duplicate/);
 assert.throws(()=>mergeConnectivity(null,receipt({nodes:{...pg([node(0)],2),nextOffset:null}})),/Invalid/);
});
test('trace proof preserves predecessor links, discloses missing pages and rejects cycles',()=>{
 const scene=mergeConnectivity(null,receipt({operation:'trace',walk:{source:'key/p0'},visits:pg([{id:'key/p0',depth:0,predecessor:null,connection:null},{id:'key/p1',depth:1,predecessor:'key/p0',connection:'e0'}])}));
 assert.deepEqual(connectivityProof(scene,'key/p1'),{nodes:['key/p0','key/p1'],edges:['e0'],complete:true});
 assert.equal(connectivityProof({...scene,visits:scene.visits.slice(1)},'key/p1').complete,false);
 assert.throws(()=>connectivityProof({...scene,visits:[{id:'key/p1',depth:1,predecessor:'key/p1',connection:'e0'}]},'key/p1'),/depth|cycle|unrelated/);
});
test('source selection resolves only explicit active root rows and remains bounded',()=>{
 const scene=mergeConnectivity(null,receipt());assert.deepEqual(connectivitySelection(scene,['key/p0'],[{id:'H:0'}]).entities,['H:0']);
 assert.match(connectivitySelection(scene,['key/p1'],[{id:'H:0'}]).reason,/active scene/);
 assert.ok(connectivitySelection(scene,['unknown'],[]).reason);
});
test('SVG exports are finite, bounded and XML safe; CSV neutralizes formulas',()=>{
 const nodes=Array.from({length:500},(_,i)=>node(i,{tag:'<script>&"',component:i})),scene={...mergeConnectivity(null,receipt()),nodes,edges:[],visits:[]};
 const svg=connectivitySvg(scene);assert.match(svg,/180\/500 loaded ports/);assert.match(svg,/height="2140"/);assert.doesNotMatch(svg,/<script>/);assert.match(svg,/&lt;script&gt;/);
 const csv=connectivityCsv({...scene,nodes:[node(0,{tag:'=WEBSERVICE("bad")',position:{x:-5,y:0,z:0}})]});assert.match(csv,/'=WEBSERVICE/);assert.match(csv,/"-5"/);assert.doesNotMatch(csv,/"'-5"/);
});
function setup(t){
 const root=installDom(t),reads=[],errors=[],selections=[],exports=[],created=[],revoked=[];
 const create=URL.createObjectURL,revoke=URL.revokeObjectURL;URL.createObjectURL=()=>{const url=`blob:${created.length}`;created.push(url);return url;};URL.revokeObjectURL=url=>revoked.push(url);t.after(()=>{URL.createObjectURL=create;URL.revokeObjectURL=revoke;});
 const client={id:'drawing',key:'key',state:state(),data:{modules:[{id:'pid',enabled:true}],items:[{id:'H:0'},{id:'H:1'}],moduleConfigurationVersion:0},read(path,options){const p=gate();reads.push({path,...options,...p});return p.promise;}};
 const ui=domainConnectivityTools({client,report:e=>errors.push(e),selectEntities:async(...args)=>selections.push(args),download:(...args)=>exports.push(args)});ui.render(root,'pid');t.after(()=>ui.hide());return {root,client,ui,reads,errors,selections,exports,created,revoked};
}
async function load(f,data=receipt()){const action=byText(f.root,'Run connectivity analysis').click();await tick();f.reads.at(-1).resolve(data);await action;}
test('network load is read-only; inspect selects only explicitly and exports complete receipt',async t=>{
 const f=setup(t);await load(f);assert.equal(f.selections.length,0);await byText(f.root,'P-0 · p0').click();await byText(f.root,'Select port source').click();assert.deepEqual(f.selections[0],[['H:0'],null,false,'key']);
 await byText(f.root,'Export connectivity JSON').click();const json=JSON.parse(await f.exports[0][0].text());assert.equal(json.graphKey,'g');assert.equal(json.totals.nodes,2);assert.equal(json.partial,false);assert.deepEqual(f.errors,[]);
 assert.ok(f.created.length);f.ui.hide();assert.ok(f.created.every(url=>f.revoked.includes(url)));
});
test('stale results after drawing switch, hide, module disable or clear cannot resurrect',async t=>{
 for(const cancel of [f=>{f.client.key='new';},f=>f.ui.hide(),f=>{f.client.data.modules[0].enabled=false;},f=>byText(f.root,'Clear connectivity').click()]){
  const f=setup(t),pending=byText(f.root,'Run connectivity analysis').click();await tick();await cancel(f);f.reads[0].resolve(receipt());await pending;assert.equal(f.created.length,0);assert.equal(f.selections.length,0);assert.deepEqual(f.errors,[]);
 }
});
test('late failures from a superseded query do not replace current status',async t=>{
 const f=setup(t),old=byText(f.root,'Run connectivity analysis').click();await tick();const source=byLabel(f.root,'Connectivity source port');source.value='key/p0';await source.dispatch('input');const latest=byText(f.root,'Run connectivity analysis').click();await tick();f.reads[1].resolve(receipt());await latest;f.reads[0].reject(new Error('old'));await old;assert.deepEqual(f.errors,[]);assert.match(f.root.textContent,/All receipt pages loaded/);
});
test('bulk load follows short pages and stops after 40 with truthful partial export',async t=>{
 const f=setup(t);await load(f,receipt({nodes:pg([node(0)],45),edges:pg([]),components:pg([])}));
 const bulk=byText(f.root,'Load remaining connectivity').click();
 for(let i=1;i<=40;i++){await tick();assert.equal(f.reads[i].body.nodeOffset,i);assert.equal(f.reads[i].body.edgeOffset,0);assert.equal(f.reads[i].body.graphKey,'g');f.reads[i].resolve(receipt({nodes:pg([node(i)],45,i),edges:pg([]),components:pg([])}));}
 await bulk;assert.equal(f.reads.length,41);assert.match(f.root.textContent,/41\/45 ports/);assert.match(f.root.textContent,/Partial pages loaded/);
 await byText(f.root,'Export connectivity JSON').click();assert.equal(JSON.parse(await f.exports[0][0].text()).partial,true);
});
test('clear during bulk stops later continuation requests and revokes image blobs',async t=>{
 const f=setup(t);await load(f,receipt({nodes:pg([node(0)],3),edges:pg([]),components:pg([])}));const bulk=byText(f.root,'Load remaining connectivity').click();await tick();await byText(f.root,'Clear connectivity').click();f.reads[1].resolve(receipt({nodes:pg([node(1)],3,1),edges:pg([]),components:pg([])}));await bulk;assert.equal(f.reads.length,2);assert.match(f.root.textContent,/Connectivity cleared/);assert.ok(f.created.every(url=>f.revoked.includes(url)));
});
test('path endpoint actions preserve network until explicit run and show predecessor proof',async t=>{
 const f=setup(t);await load(f);await byText(f.root,'P-0 · p0').click();await byText(f.root,'Use as source port').click();await byText(f.root,'P-1 · p1').click();await byText(f.root,'Use as target port').click();const mode=byLabel(f.root,'Connectivity operation');mode.value='path';await mode.dispatch('change');
 const action=byText(f.root,'Run connectivity analysis').click();await tick();assert.equal(f.reads[1].body.source,'key/p0');assert.equal(f.reads[1].body.target,'key/p1');
 f.reads[1].resolve(receipt({operation:'path',walk:{source:'key/p0',target:'key/p1',status:'found',pathLength:1,complete:true},visits:pg([{id:'key/p0',depth:0,predecessor:null,connection:null},{id:'key/p1',depth:1,predecessor:'key/p0',connection:'e0'}])}));await action;await byText(f.root,'P-1 · p1').click();await byText(f.root,'Show predecessor proof').click();assert.match(f.root.textContent,/Proof: 1 links/);assert.equal(f.selections.length,0);assert.deepEqual(f.errors,[]);
});
test('diagram uses blob CSP source and module controls do not inject style or HTML attributes',async t=>{
 const f=setup(t);await load(f);const image=all(f.root).find(n=>n.tagName==='IMG');assert.match(image.getAttribute('src'),/^blob:/);assert.ok(all(f.root).every(n=>n.getAttribute('style')===null));
});
test('Unicode portable keys and ports retain their percent-encoded identifiers through requests',()=>{
 const id=encodeURIComponent('水'.repeat(128))+'/'+encodeURIComponent('端'.repeat(64));assert.ok(id.length>600&&id.length<=2048);
 assert.equal(connectivityRequest('pid',{...values,operation:'path',source:id,target:id},state()).source,id);
 assert.throws(()=>connectivityRequest('pid',{...values,operation:'trace',source:'x'.repeat(2049)},state()),/source/);
});
test('retained snapshots expose no current-drawing actions and fence pending current responses',async t=>{
 const root=installDom(t),reads=[],errors=[];let snapshot=null;
 const client={id:'d',key:'k',state:state(),data:{modules:[{id:'pid',enabled:true}],items:[],moduleConfigurationVersion:0},read(){const p=gate();reads.push(p);return p.promise;}};
 const ui=domainConnectivityTools({client,snapshot:()=>snapshot,report:e=>errors.push(e)});ui.render(root,'pid');
 const action=byText(root,'Run connectivity analysis').click();await tick();snapshot=0;reads[0].resolve(receipt());await action;assert.equal(byText(root,'Export connectivity JSON'),undefined);
 ui.render(root,'pid');assert.match(root.textContent,/Return to Current drawing/);assert.equal(byText(root,'Run connectivity analysis'),undefined);assert.deepEqual(errors,[]);ui.hide();
});
test('a fresh scoped query clears the previous proof overlay even when port IDs remain',async t=>{
 const f=setup(t);await load(f);const mode=byLabel(f.root,'Connectivity operation');mode.value='path';await mode.dispatch('change');
 for(const [label,value]of [['Connectivity source port','key/p0'],['Connectivity target port','key/p1']]){const input=byLabel(f.root,label);input.value=value;await input.dispatch('input');}
 const first=byText(f.root,'Run connectivity analysis').click();await tick();f.reads[1].resolve(receipt({operation:'path',walk:{source:'key/p0',status:'found',pathLength:1,complete:true},visits:pg([{id:'key/p0',depth:0,predecessor:null,connection:null},{id:'key/p1',depth:1,predecessor:'key/p0',connection:'e0'}])}));await first;
 await byText(f.root,'P-1 · p1').click();await byText(f.root,'Show predecessor proof').click();await byText(f.root,'Export connectivity SVG').click();assert.match(await f.exports[0][0].text(),/#dc2626/);
 mode.value='network';await mode.dispatch('change');const second=byText(f.root,'Run connectivity analysis').click();await tick();f.reads[2].resolve(receipt({graphKey:'new-query',operation:'network',walk:null,visits:pg([])}));await second;
 await byText(f.root,'Export connectivity SVG').click();assert.doesNotMatch(await f.exports[1][0].text(),/#dc2626/);assert.equal(byText(f.root,'Show predecessor proof'),undefined);
});

test('directed schematic links terminate at card borders and long links bend around intervening cards',()=>{
 const scene=mergeConnectivity(null,receipt({nodes:pg(Array.from({length:13},(_,i)=>node(i))),edges:pg([{...edge(0),direction:'forward'},edge(1,0,2),edge(2,0,12)]),components:pg([])}));
 const svg=connectivitySvg(scene);
 assert.match(svg,/d="M149 42 L214 42"[^>]*marker-end="url\(#arrow\)"/);
 assert.match(svg,/d="M86.5 59 C86.5 97 466.5 97 466.5 59"/);
 assert.match(svg,/d="M149 42 C179 42 179 182 149 182"/);
 assert.match(svg,/d="M86.5 59 C86.5 97 466.5 97 466.5 59" fill="none"/);
 assert.match(svg,/refX="10"/);
 assert.doesNotMatch(svg,/NaN|Infinity/);
});
test('schematic intrinsic width follows used columns and remains bounded for large networks',()=>{
 const small=connectivitySvg(mergeConnectivity(null,receipt()));assert.match(small,/width="404" height="120" viewBox="0 0 404 120"/);
 const four=connectivitySvg(mergeConnectivity(null,receipt({nodes:pg(Array.from({length:4},(_,i)=>node(i)))})));assert.match(four,/width="784" height="120"/);
 const large=connectivitySvg(mergeConnectivity(null,receipt({nodes:pg(Array.from({length:500},(_,i)=>node(i)))})));assert.match(large,/width="1164" height="2140"/);
});
