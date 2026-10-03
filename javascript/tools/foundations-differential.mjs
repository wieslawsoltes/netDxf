import fs from 'node:fs';
import path from 'node:path';
import { isDeepStrictEqual } from 'node:util';
import { OracleClient } from './OracleClient.mjs';
import { oracleRoot, javascriptRoot, baseline, configuration } from './dotnet.mjs';
import { runtimeFingerprint, verificationFingerprint } from './evidence.mjs';
import { valueCorpus } from './value-corpus.mjs';
import { geometryCorpus } from './geometry-corpus.mjs';
import { jsGeometry } from './foundations-wire.mjs';

const nativeManifest=JSON.parse(fs.readFileSync(path.join(javascriptRoot,'native-port-manifest.json')));
const oracle = new OracleClient({args:[path.join(oracleRoot,'GeometryOracle.dll')]});
const output=path.join(javascriptRoot,'artifacts','foundations-differential',configuration);fs.mkdirSync(output,{recursive:true});
const proof={runtimeFingerprint:runtimeFingerprint(),verificationFingerprint:verificationFingerprint()};
const report={...proof,sourceRef:baseline.ref,configuration,completed:false,fatal:null,stats:{scenarios:0,operations:0,emittedByteComparisons:0,failures:0},categories:{},failures:[]};
try {
  report.environment=await oracle.request({op:'environment'});
  for(const probe of [...geometryCorpus(),...valueCorpus()]) {
    const expected=await oracle.request(probe.request),actual=jsGeometry({...probe.request,nativeManifest});
    if(!Array.isArray(expected) || expected.length!==probe.request.steps.length)throw new Error('Incomplete or invalid oracle response.');
    report.stats.scenarios++;report.stats.operations+=expected.length;
    for(let i=0;i<expected.length;i++)if(probe.request.steps[i].kind==='emit'&&expected[i].ok&&actual[i]?.ok&&isDeepStrictEqual(expected[i],actual[i]))report.stats.emittedByteComparisons+=2;
    const category=report.categories[probe.category]??={scenarios:0,operations:0,failures:0};category.scenarios++;category.operations+=expected.length;
    if(!isDeepStrictEqual(expected,actual)) {
      const differences=expected.flatMap((value,i)=>isDeepStrictEqual(value,actual[i])?[]:[{step:i,operation:probe.request.steps[i],expected:value,actual:actual[i]}]);
      report.stats.failures+=differences.length;category.failures+=differences.length;
      const failure={name:probe.name,category:probe.category,request:probe.request,differences};report.failures.push(failure);
      if(report.failures.length<=8)console.error('Geometry mismatch',JSON.stringify(failure));
    }
  }
  report.completed=true;
} catch(error) {report.fatal=error.stack;throw error;}
finally {
  try{await oracle.close();}catch(error){report.fatal??=error.stack;report.completed=false;}
  if(runtimeFingerprint()!==proof.runtimeFingerprint || verificationFingerprint()!==proof.verificationFingerprint){report.completed=false;report.fatal='Executable source changed during verification.';}
  fs.writeFileSync(path.join(output,'results.json'),JSON.stringify(report,null,2)+'\n');
}
console.log(JSON.stringify({stats:report.stats,categories:report.categories}));
if(!report.completed || report.fatal || report.stats.failures)process.exitCode=1;
