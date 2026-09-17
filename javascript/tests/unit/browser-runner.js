// Harness tests, not independent .NET behavioral evidence. The full corpus uses the real C# oracle.
import test from 'node:test';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import fs from 'node:fs';
import { runBrowserCorpus, canonical } from '../../tools/browser-runner.mjs';
import { jsGeometry } from '../../tools/foundations-wire.mjs';
const native=JSON.parse(fs.readFileSync(new URL('../../native-port-manifest.json',import.meta.url)));
const hash=text=>createHash('sha256').update(text).digest('hex');
const request={steps:[{kind:'new',type:'Entities.Line',id:'p'}]};
const expected=hash(canonical(jsGeometry({...request,nativeManifest:native})));

test('browser runner compares every case after a mismatch and retains detailed model evidence',async()=>{
  const report=await runBrowserCorpus({cases:[{name:'wrong',input:request,expected:{models:'0'.repeat(64)}},{name:'correct',input:request,expected:{models:expected}}]},native,{hashCanonical:hash});
  assert.equal(report.completed,false);assert.equal(report.comparisons,2);assert.equal(report.failures.length,1);assert.equal(report.failures[0].name,'wrong');assert.ok(Array.isArray(report.failures[0].result));
});
test('browser runner rejects empty and malformed evidence rather than passing vacuously',async()=>{
  for(const corpus of [{cases:[]},{cases:[{name:'empty',input:request,expected:{}}]},{cases:[{name:'malformed',input:request,expected:{models:'bad'}}]}]){
    const report=await runBrowserCorpus(corpus,native,{hashCanonical:hash});assert.equal(report.completed,false);assert.ok(report.fatal);assert.equal(report.comparisons,0);
  }
});
test('browser runner records genuine matching digests and rejects broken digest adapters',async()=>{
  const corpus={cases:[{name:'line',input:request,expected:{models:expected}}]};
  const report=await runBrowserCorpus(corpus,native,{hashCanonical:hash});assert.equal(report.completed,true);assert.equal(report.comparisons,1);assert.deepEqual(report.categories,{models:1});
  const invalid=await runBrowserCorpus(corpus,native,{hashCanonical:()=>''});assert.equal(invalid.completed,false);assert.match(invalid.fatal,/Invalid computed digest/);
});
