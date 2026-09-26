import { ValidateEntityBodyObservation } from './entity-body-io-observation.mjs';
import fs from 'node:fs';
import path from 'node:path';
import { isDeepStrictEqual } from 'node:util';
import { ModelOracleSession } from './ModelOracleSession.mjs';
import { javascriptRoot, configuration, baseline } from './dotnet.mjs';
import { runtimeFingerprint, verificationFingerprint } from './evidence.mjs';
import { entityBodyIOCorpus } from './entity-body-io-corpus.mjs';
import { entityBodyIOCall } from './entity-body-io-wire.mjs';
const proof={runtimeFingerprint:runtimeFingerprint(),verificationFingerprint:verificationFingerprint()};
const report={...proof,sourceRef:baseline.ref,configuration,completed:false,fatal:null,stats:{scenarios:0,observedScenarios:0,requestedOperations:0,operations:0,failures:0,oracleFailures:0},failures:[]};
const oracle=new ModelOracleSession();
try {
  const corpus=entityBodyIOCorpus();
  if(corpus.length!==1107||new Set(corpus.map(p=>p.name)).size!==1107||corpus.reduce((n,p)=>n+p.request.steps.length,0)!==3714)throw new Error('Incomplete or duplicate entity body corpus.');
  report.environment=await oracle.environment();
  for(const probe of corpus) {
    report.stats.scenarios++;report.stats.requestedOperations+=probe.request.steps.length;
    const observed=await oracle.observe(probe.request),actual=ValidateEntityBodyObservation(entityBodyIOCall(probe.request),probe.request.steps.length);
    if(!observed.ok){report.stats.oracleFailures++;report.stats.failures++;report.failures.push({name:probe.name,oracleFailure:observed.failure});continue;}
    const expected=ValidateEntityBodyObservation(observed.value,probe.request.steps.length);report.stats.observedScenarios++;report.stats.operations+=expected.length;
    if(!Array.isArray(actual)||actual.length!==expected.length)throw new Error('Incomplete JavaScript observation.');
    const differences=expected.flatMap((value,index)=>isDeepStrictEqual(value,actual[index])?[]:[{index,step:probe.request.steps[index],expected:value,actual:actual[index]}]);
    if(differences.length){report.stats.failures+=differences.length;report.failures.push({name:probe.name,differences});if(report.failures.length<=15)console.error(probe.name+': '+differences.length);}
  }
  report.completed=report.stats.oracleFailures===0;
}catch(e){report.fatal=e.stack??String(e);}
finally {
  try{await oracle.close();}catch(e){report.completed=false;report.fatal??=e.stack??String(e);}
  if(runtimeFingerprint()!==proof.runtimeFingerprint||verificationFingerprint()!==proof.verificationFingerprint){report.completed=false;report.fatal='Source changed during qualification.';}
  const out=path.join(javascriptRoot,'artifacts/entity-body-io',configuration);fs.mkdirSync(out,{recursive:true});fs.writeFileSync(path.join(out,'results.json'),JSON.stringify(report,null,2)+'\n');
}
console.log(JSON.stringify({stats:report.stats,fatal:report.fatal}));if(!report.completed||report.fatal||report.stats.failures)process.exitCode=1;
