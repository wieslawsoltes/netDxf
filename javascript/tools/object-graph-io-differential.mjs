import { ValidateObjectGraphObservation } from './object-graph-io-observation.mjs';
// Compare independently executed native and production JavaScript object dispatch/import.
import fs from 'node:fs';import path from 'node:path';import { isDeepStrictEqual } from 'node:util';
import { ModelOracleSession } from './ModelOracleSession.mjs';import { javascriptRoot,configuration,baseline } from './dotnet.mjs';
import { runtimeFingerprint,verificationFingerprint } from './evidence.mjs';
import { objectGraphIOCorpus } from './object-graph-io-corpus.mjs';import { objectGraphIOCall } from './object-graph-io-wire.mjs';
const proof={runtimeFingerprint:runtimeFingerprint(),verificationFingerprint:verificationFingerprint()},oracle=new ModelOracleSession();
const report={...proof,sourceRef:baseline.ref,configuration,completed:false,fatal:null,stats:{scenarios:0,observedScenarios:0,requestedOperations:0,operations:0,failures:0,oracleFailures:0},failures:[]};
try{
  const corpus=objectGraphIOCorpus();if(corpus.length!==937||new Set(corpus.map(p=>p.name)).size!==937||corpus.reduce((n,p)=>n+p.request.steps.length,0)!==8715)throw new Error('Empty or duplicate object corpus.');
  report.environment=await oracle.environment();
  for(const probe of corpus){
    const observed=await oracle.observe(probe.request),actual=ValidateObjectGraphObservation(objectGraphIOCall(probe.request),probe.request.steps.length);report.stats.scenarios++;report.stats.requestedOperations+=probe.request.steps.length;
    if(!observed.ok){report.stats.failures++;report.stats.oracleFailures++;report.failures.push({name:probe.name,oracleFailure:observed.failure});continue;}
    const expected=ValidateObjectGraphObservation(observed.value,probe.request.steps.length);if(expected.length!==probe.request.steps.length||actual.length!==expected.length)throw new Error('Incomplete object-graph observation.');
    report.stats.observedScenarios++;report.stats.operations+=expected.length;
    const differences=expected.flatMap((row,i)=>isDeepStrictEqual(row,actual[i])?[]:[{step:i,input:probe.request.steps[i],expected:row,actual:actual[i]}]);
    if(differences.length){report.stats.failures+=differences.length;report.failures.push({name:probe.name,differences});if(report.failures.length<12)console.error(probe.name,differences.length);}
  }
  report.completed=report.stats.oracleFailures===0;
}catch(e){report.fatal=e.stack??String(e);}
finally{
  try{await oracle.close();}catch(e){report.completed=false;report.fatal??=e.stack??String(e);}
  if(runtimeFingerprint()!==proof.runtimeFingerprint||verificationFingerprint()!==proof.verificationFingerprint){report.completed=false;report.fatal='Source changed during qualification.';}
  const out=path.join(javascriptRoot,'artifacts/object-graph-io',configuration);fs.mkdirSync(out,{recursive:true});fs.writeFileSync(path.join(out,'results.json'),JSON.stringify(report,null,2)+'\n');
}
console.log(JSON.stringify(report.stats),report.fatal??'');if(!report.completed||report.stats.failures||report.fatal)process.exitCode=1;
