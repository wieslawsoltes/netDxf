import test from 'node:test';
import assert from 'node:assert/strict';
import {scheduleRequest,mergeSchedule,scheduleField,scheduleSelection,scheduleCsv} from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/domain-schedule-model.mjs';
import {domainScheduleTools} from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/domain-schedule-ui.mjs';
import {byText,byLabel,gate,tick,installDom} from './ui-fixture.mjs';
const options={query:'',class:'',groupBy:'class',sortBy:'tag',descending:false,maximumObjects:10000};
const row=i=>({entity:{value:`H:${i}`},type:'Line',class:'pipe',key:`p${i}`,tag:`Item ${i}`,properties:{service:'Water'},ports:[],references:[],segmentLength:5});
const page=(items,total=items.length,offset=0)=>({items,total,offset,nextOffset:offset+items.length<total?offset+items.length:null});
const receipt=(rows=[row(0)],extra={})=>({scheduleKey:'receipt',documentId:'doc',workspaceId:'live',viewVersion:1,revision:0,layout:'Model',module:'pid',drawingUnits:'Millimeters',options,complete:true,scanComplete:true,objectsVisited:rows.length,recordsScanned:rows.length,invalidRecords:0,limitations:[],rows:page(rows),groups:page([]),issues:page([]),...extra});

test('schedule pages preserve independent exact cursors and refuse changed evidence',()=>{
  const first=receipt([row(0)],{rows:page([row(0)],2),recordsScanned:2});
  const scene=mergeSchedule(null,first);
  assert.equal(scene.partial,true);assert.equal(scene.next.rows,1);
  const next={...first,rows:page([row(1)],2,1)};
  const done=mergeSchedule(scene,next);assert.equal(done.partial,false);assert.equal(done.rows.length,2);
  assert.equal(scheduleRequest('pid',options,{revision:0},scene).rowOffset,1);
  for(const edit of [{drawingUnits:'Meters'},{viewVersion:2},{options:{...options,query:'other'}},{objectsVisited:10},{limitations:['partial']},{rows:page([row(0)],2,1)},{rows:page([row(1)],2,0)},{rows:{...next.rows,nextOffset:2}}])
    assert.throws(()=>mergeSchedule(scene,{...next,...edit}),/Schedule|schedule/);
});
test('schedule fields match backend precedence and casing, CSV guards formulas and reports coverage',()=>{
  const source={...row(0),tag:'=2+3',symbolId:'sym',properties:{class:'override',Service:'Water'}};
  assert.equal(scheduleField(source,'Class'),'pipe');assert.equal(scheduleField(source,'symbolid'),'sym');assert.equal(scheduleField(source,'SERVICE'),'Water');assert.equal(scheduleField(source,'Entity'),'H:0');
  const scene=mergeSchedule(null,receipt([source],{rows:page([source],2)})),csv=scheduleCsv(scene);
  assert.match(csv,/'=2\+3/);assert.match(csv,/"pagesComplete"/);assert.match(csv,/"false","1","2"/);
  assert.throws(()=>scheduleCsv(scene,'unexpected'));
  assert.deepEqual(scheduleSelection([source],[{id:'H:0'}]).entities,['H:0']);assert.ok(scheduleSelection([source],[]).reason);
  for(const values of [{maximumObjects:0},{maximumObjects:100001},{groupBy:'bad\nfield'},{sortBy:''},{query:'x'.repeat(257)}])assert.throws(()=>scheduleRequest('pid',values,{revision:0}));
});
function fixture(t){
  const root=installDom(t),reads=[],errors=[],selections=[],edits=[],downloads=[];
  const client={id:'doc',state:{documentId:'doc',workspaceId:'live',revision:0,viewVersion:1,layout:'Model'},key:'view1',data:{moduleConfigurationVersion:0,modules:[{id:'pid',enabled:true}],items:[{id:'H:0'}]},read(path,request){const pending=gate();reads.push({path,request,...pending});return pending.promise;}};
  const ui=domainScheduleTools({client,selectEntities:(...args)=>selections.push(args),editRecord:(...args)=>edits.push(args),report:e=>errors.push(e),download:(...args)=>downloads.push(args)});
  ui.render(root,'pid');return {root,reads,errors,selections,edits,downloads,client,ui};
}
test('schedule input invalidates pending results and suppresses superseded failures',async t=>{
  const f=fixture(t);const old=byText(f.root,'Refresh data').click();await tick();
  const search=byLabel(f.root,'Search discipline schedule');search.value='current';await search.dispatch('input');
  const newer=byText(f.root,'Refresh data').click();await tick();f.reads[1].resolve(receipt([{...row(0),tag:'Current item'}],{options:{...options,query:'current'}}));await newer;
  f.reads[0].reject(new Error('obsolete error'));await old;
  assert.match(f.root.textContent,/Current item/);assert.deepEqual(f.errors,[]);
  f.ui.render(f.root,'pid');assert.match(f.root.textContent,/Current item/);assert.equal(byLabel(f.root,'Search discipline schedule').value,'current');
});
test('schedule continuation sends original query and each stream offset; source actions stay guarded',async t=>{
  const f=fixture(t),first=receipt([row(0)],{rows:page([row(0)],2),recordsScanned:2});
  const loading=byText(f.root,'Refresh data').click();await tick();f.reads[0].resolve(first);await loading;
  const more=byText(f.root,'Load remaining schedule').click();await tick();assert.equal(f.reads[1].path,'modules/schedule');
  assert.equal(f.reads[1].request.body.rowOffset,1);assert.equal(f.reads[1].request.body.groupOffset,0);assert.equal(f.reads[1].request.body.scheduleKey,'receipt');
  f.reads[1].resolve({...first,rows:page([row(1)],2,1)});await more;
  await byLabel(f.root,'Inspect schedule object Item 0').click();await byText(f.root,'Select schedule object').click();
  assert.deepEqual(f.selections[0],[['H:0'],null,false,'view1']);await byText(f.root,'Edit schedule object data').click();assert.equal(f.edits[0][1].entity.value,'H:0');
  await byLabel(f.root,'Inspect schedule object Item 1').click();assert.equal(byText(f.root,'Select schedule object').disabled,true);
  f.client.key='different';await byText(f.root,'Edit schedule object data').click();assert.equal(f.edits.length,1);assert.match(f.errors[0].message,/drawing or module changed/);
});
test('schedule refresh resets an unavailable display page and unload hides stale work',async t=>{
  const f=fixture(t),rows=Array.from({length:51},(_,i)=>row(i));
  let loading=byText(f.root,'Refresh data').click();await tick();f.reads[0].resolve(receipt(rows));await loading;
  await byText(f.root,'Next records').click();assert.match(f.root.textContent,/Item 50/);
  loading=byText(f.root,'Refresh data').click();await tick();f.reads[1].resolve(receipt([row(0)]));await loading;
  assert.equal(byText(f.root,'Previous records').disabled,true);assert.match(f.root.textContent,/Item 0/);
  loading=byText(f.root,'Refresh data').click();await tick();f.ui.hide();f.reads[2].resolve(receipt([{...row(0),tag:'Obsolete'}]));await loading;
  assert.doesNotMatch(f.root.textContent,/Obsolete/);assert.deepEqual(f.errors,[]);
});
