import '../node-entry.js';
import fs from 'node:fs';
import path from 'node:path';
import { isDeepStrictEqual } from 'node:util';
import { OracleClient } from './OracleClient.mjs';
import { oracleRoot, javascriptRoot, sourceRoot, baseline, configuration } from './dotnet.mjs';
import { runtimeFingerprint, verificationFingerprint, sha256 } from './evidence.mjs';
import { entityCorpus } from './entity-corpus.mjs';
import { jsGeometry } from './foundations-wire.mjs';
const corpus=entityCorpus();
const nativeManifest=JSON.parse(fs.readFileSync(path.join(javascriptRoot,'native-port-manifest.json')));
const proof={runtimeFingerprint:runtimeFingerprint(),verificationFingerprint:verificationFingerprint()};
const report={...proof,sourceRef:baseline.ref,configuration,completed:false,fatal:null,
  stats:{scenarios:0,operations:0,textComparisons:0,failures:0},categories:{},failures:[]};
const output=path.join(javascriptRoot,'artifacts/entity-differential',configuration);fs.mkdirSync(output,{recursive:true});
const oracle=new OracleClient({args:[path.join(oracleRoot,'GeometryOracle.dll')]});
try {
  report.environment=await oracle.request({op:'environment'});
  for(const probe of corpus) {
    const expected=await oracle.request(probe.request),actual=jsGeometry({...probe.request,nativeManifest});
    if(!Array.isArray(expected)||expected.length!==probe.request.steps.length)throw new Error('Incomplete oracle response.');
    report.stats.scenarios++;report.stats.operations+=expected.length;
    const category=report.categories[probe.category]??={scenarios:0,operations:0,failures:0};category.scenarios++;category.operations+=expected.length;
    for(let i=0;i<expected.length;i++)if(probe.request.steps[i].kind==='lin-save'&&expected[i].ok&&isDeepStrictEqual(expected[i],actual[i]))report.stats.textComparisons++;
    const differences=expected.flatMap((value,i)=>isDeepStrictEqual(value,actual[i])?[]:[{step:i,operation:probe.request.steps[i],expected:value,actual:actual[i]}]);
    if(differences.length){report.stats.failures+=differences.length;category.failures+=differences.length;report.failures.push({name:probe.name,category:probe.category,differences});
      if(report.failures.length<=12)console.error('Entity mismatch',JSON.stringify(report.failures.at(-1)));}
  }
  report.completed=true;
}catch(error){report.fatal=error.stack;}
finally {
  try{await oracle.close();}catch(error){report.fatal??=error.stack;report.completed=false;}
  if(runtimeFingerprint()!==proof.runtimeFingerprint||verificationFingerprint()!==proof.verificationFingerprint){report.completed=false;report.fatal='Executable source changed during verification.';}
  fs.writeFileSync(path.join(output,'results.json'),JSON.stringify(report,null,2)+'\n');
}
console.log(JSON.stringify({stats:report.stats,categories:report.categories,fatal:report.fatal}));
if(!report.completed||report.fatal||report.stats.failures)process.exitCode=1;
