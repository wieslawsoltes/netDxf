import test from 'node:test';
import assert from 'node:assert/strict';
import {ModelOracleSession} from '../../tools/ModelOracleSession.mjs';
const request={steps:[{kind:'snapshot',target:'v'}]};
test('model oracle restart records a native failure and does not discard later independent scenarios',async()=>{
  let clients=0,closed=0;const session=new ModelOracleSession(()=>{
    const index=clients++;return {async request(){if(index===0)throw new Error('native assertion');return [{ok:true,value:7}];},async close(){closed++;}};
  });
  const first=await session.observe(request);assert.equal(first.ok,false);assert.equal(first.failure.request,request);assert.match(first.failure.message,/native assertion/);
  const next=await session.observe(request);assert.deepEqual(next,{ok:true,value:[{ok:true,value:7}]});await session.close();assert.equal(clients,2);assert.equal(closed,2);
});
test('model oracle rejects malformed, short, fatal and non-model responses instead of making golden outputs',async()=>{
  for(const response of [null,[],{fatal:'source fault'},[{value:1}],[{ok:true},{ok:true}]]){
    let closed=0;const s=new ModelOracleSession(()=>({async request(){return response;},async close(){closed++;}}));
    const result=await s.observe(request);assert.equal(result.ok,false);assert.match(result.failure.message,/malformed model response/);assert.equal(closed,1);await s.close();
  }
});
test('model oracle retains shutdown errors and ordinary model exception results distinctly',async()=>{
  let count=0;const s=new ModelOracleSession(()=>({async request(){if(count++===0)throw new Error('exit');return [{ok:false,error:'ArgumentException',param:null}];},async close(){throw new Error('cleanup failure');}}));
  const first=await s.observe(request);assert.match(first.failure.closeError,/cleanup failure/);
  assert.deepEqual(await s.observe(request),{ok:true,value:[{ok:false,error:'ArgumentException',param:null}]});await assert.rejects(()=>s.close(),/cleanup failure/);
});

test('browser evidence cannot pass when the native observation failed, even with an equal digest',async()=>{
  const {runBrowserCorpus,canonical}=await import('../../tools/browser-runner.mjs');
  const {jsGeometry}=await import('../../tools/foundations-wire.mjs');
  const {createHash}=await import('node:crypto');
  const input={steps:[{kind:'new',type:'Entities.Line',args:[],id:'v'}]};
  const hash=text=>createHash('sha256').update(text).digest('hex');
  const corpus={cases:[{name:'native-failure',input,expected:{models:hash(canonical(jsGeometry(input)))},sourceOracleFailure:{message:'native assertion'}},
    {name:'later-scenario',input,expected:{models:hash(canonical(jsGeometry(input)))}}]};
  const result=await runBrowserCorpus(corpus,{files:[]},{hashCanonical:hash});
  assert.equal(result.comparisons,2);assert.equal(result.failures.length,0);assert.equal(result.sourceOracleFailures.length,1);assert.equal(result.completed,false);
});
