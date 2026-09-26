import { ValidateDatabaseObservation } from './database-io-observation.mjs';
import fs from 'node:fs';
import path from 'node:path';
import { isDeepStrictEqual } from 'node:util';
import { ModelOracleSession } from './ModelOracleSession.mjs';
import { javascriptRoot, configuration, baseline } from './dotnet.mjs';
import { runtimeFingerprint, verificationFingerprint } from './evidence.mjs';
import { databasePayloadCorpus } from './database-payload-corpus.mjs';
import { databasePayloadCall } from './database-payload-wire.mjs';
const proof={runtimeFingerprint:runtimeFingerprint(),verificationFingerprint:verificationFingerprint()};
const report={...proof,sourceRef:baseline.ref,configuration,completed:false,fatal:null,stats:{scenarios:0,observedScenarios:0,requestedOperations:0,operations:0,failures:0,oracleFailures:0},failures:[]};
const oracle=new ModelOracleSession();
try {
  const corpus=databasePayloadCorpus();
  if(corpus.length!==1070||new Set(corpus.map(p=>p.name)).size!==1070||corpus.reduce((sum,p)=>sum+p.request.steps.length,0)!==4701)throw new Error('Incomplete or duplicate database payload corpus.');
  report.environment=await oracle.environment();
  for(const probe of corpus) {
    report.stats.scenarios++;report.stats.requestedOperations+=probe.request.steps.length;
    const observed=await oracle.observe(probe.request),actual=ValidateDatabaseObservation(databasePayloadCall(probe.request),probe.request.steps.length,'payload');
    if(!observed.ok){report.stats.oracleFailures++;report.stats.failures++;report.failures.push({name:probe.name,oracleFailure:observed.failure});continue;}
    const expected=ValidateDatabaseObservation(observed.value,probe.request.steps.length,'payload');report.stats.observedScenarios++;report.stats.operations+=expected.length;
    if(!Array.isArray(actual)||actual.length!==expected.length)throw new Error('Incomplete JavaScript observation.');
    const differences=expected.flatMap((value,index)=>isDeepStrictEqual(value,actual[index])?[]:[{index,step:probe.request.steps[index],expected:value,actual:actual[index]}]);
    if(differences.length){report.stats.failures+=differences.length;report.failures.push({name:probe.name,differences});if(report.failures.length<=15)console.error(probe.name+': '+differences.length);}
  }
  report.completed=report.stats.oracleFailures===0;
}catch(e){report.fatal=e.stack??String(e);}
finally {
  try{await oracle.close();}catch(e){report.completed=false;report.fatal??=e.stack??String(e);}
  if(runtimeFingerprint()!==proof.runtimeFingerprint||verificationFingerprint()!==proof.verificationFingerprint){report.completed=false;report.fatal='Source changed during qualification.';}
  const out=path.join(javascriptRoot,'artifacts/database-payload',configuration);fs.mkdirSync(out,{recursive:true});fs.writeFileSync(path.join(out,'results.json'),JSON.stringify(report,null,2)+'\n');
}
console.log(JSON.stringify({stats:report.stats,fatal:report.fatal}));if(!report.completed||report.fatal||report.stats.failures)process.exitCode=1;
