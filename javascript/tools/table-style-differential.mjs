// Independent native/JavaScript observations. No expected output implementation.
import '../node-entry.js';
import fs from 'node:fs';
import path from 'node:path';
import { isDeepStrictEqual } from 'node:util';
import { DocumentOracleSession } from './DocumentOracleSession.mjs';
import { javascriptRoot, configuration, baseline } from './dotnet.mjs';
import { runtimeFingerprint, verificationFingerprint } from './evidence.mjs';
import { tableStyleCorpus } from './table-style-corpus.mjs';
import { documentOwnershipCall } from './document-ownership-wire.mjs';
const proof={runtimeFingerprint:runtimeFingerprint(),verificationFingerprint:verificationFingerprint()};
const report={...proof,sourceRef:baseline.ref,configuration,completed:false,fatal:null,
  stats:{scenarios:0,observedScenarios:0,requestedOperations:0,operations:0,failures:0,oracleFailures:0},failures:[]};
const oracle=new DocumentOracleSession();
try{
  const corpus=tableStyleCorpus();
  if(corpus.length!==279||new Set(corpus.map(p=>p.name)).size!==279||corpus.reduce((sum,p)=>sum+p.request.steps.length,0)!==5829)
    throw new Error('Incomplete or duplicate document corpus.');
  report.environment=await oracle.environment();
  for(const probe of corpus){
    const observed=await oracle.observe(probe.request),actual=documentOwnershipCall(probe.request);
    report.stats.scenarios++;report.stats.requestedOperations+=probe.request.steps.length;
    if(!observed.ok){report.stats.failures++;report.stats.oracleFailures++;report.failures.push({name:probe.name,oracleFailure:observed.failure,actual});continue;}
    const expected=observed.value;
    if(!Array.isArray(actual)||actual.length!==expected.length)throw new Error('Incomplete JavaScript document result.');
    report.stats.observedScenarios++;report.stats.operations+=expected.length;
    const differences=expected.flatMap((value,i)=>isDeepStrictEqual(value,actual[i])?[]:[{step:i,input:probe.request.steps[i],expected:value,actual:actual[i]}]);
    if(differences.length){report.stats.failures+=differences.length;report.failures.push({name:probe.name,differences});console.error(probe.name+': '+differences.length+' differences');}
  }
  report.completed=report.stats.oracleFailures===0;
}catch(e){report.fatal=e.stack??String(e);}
finally{
  try{await oracle.close();}catch(e){report.completed=false;report.fatal??=e.stack??String(e);}
  if(runtimeFingerprint()!==proof.runtimeFingerprint||verificationFingerprint()!==proof.verificationFingerprint){report.completed=false;report.fatal='Source changed during qualification.';}
  const folder=path.join(javascriptRoot,'artifacts/table-style',configuration);fs.mkdirSync(folder,{recursive:true});fs.writeFileSync(path.join(folder,'results.json'),JSON.stringify(report,null,2)+'\n');
}
console.log(JSON.stringify(report.stats),report.fatal??'');if(!report.completed||report.fatal||report.stats.failures)process.exitCode=1;
