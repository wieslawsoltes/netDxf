import fs from 'node:fs';
import path from 'node:path';
import { isDeepStrictEqual } from 'node:util';
import { OracleClient } from './OracleClient.mjs';
import { javascriptRoot, sourceRoot, baseline, configuration, computeSourceFingerprint } from './dotnet.mjs';
import { runtimeFingerprint, verificationFingerprint, sha256 } from './evidence.mjs';
import { filesystem } from './filesystem-wire.mjs';
import { AtomicRawSource, AtomicRawVersions } from '../tests/netDxf.Conformance/AtomicSaveTests.js';
const proof={runtimeFingerprint:runtimeFingerprint(),verificationFingerprint:verificationFingerprint()},oracle=new OracleClient();
const inventory=JSON.parse(fs.readFileSync(path.join(javascriptRoot,'artifacts/inventory/source-inventory.json')));
const stats={sourceFixtures:0,comparisons:0,successfulSaves:0,matchingRejections:0,byteComparisons:0,failures:0};
const results=[],failures=[];let completed=false,fatal=null;
async function check(name,request){
  const expected=await oracle.request({op:'filesystem',...request});
  if(!expected.ok)throw new Error('Filesystem oracle setup failed: '+expected.error);
  const actual=filesystem(request),passed=isDeepStrictEqual(actual,expected.value);
  stats.comparisons++;if(expected.value.operation.ok)stats.successfulSaves++;else if(passed)stats.matchingRejections++;
  if(expected.value.bytes!==null)stats.byteComparisons++;
  results.push({name,passed,error:expected.value.operation.error,inputSha256:request.bytes?sha256(Buffer.from(request.bytes,'base64')):null});
  if(!passed){stats.failures++;failures.push({name,request,expected:expected.value,actual});console.error('Filesystem mismatch:',name,expected.value.operation,actual.operation);}
}
try{
  if(computeSourceFingerprint()!==baseline.sourceFingerprint||inventory.sourceFingerprint!==baseline.sourceFingerprint||inventory.fixtures.length!==baseline.counts.dxfFixtures)
    throw new Error('Filesystem differential requires the complete pinned fixture corpus.');
  for(const fixture of inventory.fixtures){
    const bytes=fs.readFileSync(path.join(sourceRoot,fixture.path));
    if(bytes.length!==fixture.length||sha256(bytes)!==fixture.sha256)throw new Error('Changed fixture: '+fixture.path);
    for(const binary of [false,true])for(const existing of [false,true])
      await check(`${fixture.path}/${binary}/${existing}`,{mode:'raw',bytes:bytes.toString('base64'),binary,existing});
    stats.sourceFixtures++;
  }
  for(const version of AtomicRawVersions)for(const binary of [false,true])for(const existing of [false,true]){
    const bytes=Buffer.from(AtomicRawSource(version,binary).ToBytes()).toString('base64');
    await check(`raw-default/${version}/${binary}/${existing}`,{mode:'raw',bytes,existing});
    await check(`raw-budget/${version}/${binary}/${existing}`,{mode:'raw',bytes,existing,maximumBytes:16});
    await check(`raw-cancel/${version}/${binary}/${existing}`,{mode:'raw',bytes,existing,cancel:true});
    const invalid=Buffer.from(AtomicRawSource(version,binary,true).ToBytes()).toString('base64');
    await check(`raw-invalid/${version}/${binary}/${existing}`,{mode:'raw',bytes:invalid,binary:!binary,existing});
  }
  for(const existing of [false,true]){
    for(const failure of [-1,0,1,2,3])await check(`staged/${existing}/${failure}`,{mode:'staged',existing,failure});
    for(const pathCase of ['null','empty','nul','directory','missing-parent','readonly','symlink','dangling'])
      for(const cancel of [false,true])await check(`path/${existing}/${pathCase}/${cancel}`,{mode:'path',existing,pathCase,cancel});
  }
  completed=true;
}catch(error){fatal=error.stack;}
finally{
  try{await oracle.close();}catch(error){fatal??=error.stack;completed=false;}
  if(runtimeFingerprint()!==proof.runtimeFingerprint||verificationFingerprint()!==proof.verificationFingerprint){completed=false;fatal='Source changed during filesystem qualification.';}
  const out=path.join(javascriptRoot,'artifacts/filesystem-differential',configuration);fs.mkdirSync(out,{recursive:true});
  fs.writeFileSync(path.join(out,'results.json'),JSON.stringify({...proof,sourceRef:baseline.ref,configuration,platform:process.platform,completed,fatal,stats,results,failures},null,2)+'\n');
}
console.log('Filesystem differential:',JSON.stringify(stats));
if(!completed||fatal||stats.failures)throw new Error(fatal||'Filesystem differential failed.');
