// Compare actual native/JavaScript observations without tolerances or output rewriting.
import fs from 'node:fs';
import path from 'node:path';
import { isDeepStrictEqual } from 'node:util';
import { GteOracleSession } from './GteOracleSession.mjs';
import { validateGteObservation } from './gte-observation.mjs';
import { javascriptRoot, configuration, baseline } from './dotnet.mjs';
import { runtimeFingerprint, verificationFingerprint } from './evidence.mjs';
import { gteCorpus } from './gte-corpus.mjs';
import { gteCall } from './gte-wire.mjs';
const proof={runtimeFingerprint:runtimeFingerprint(),verificationFingerprint:verificationFingerprint()};
const report={...proof,sourceRef:baseline.ref,configuration,completed:false,fatal:null,stats:{scenarios:0,observedScenarios:0,requestedOperations:0,operations:0,failures:0,oracleFailures:0},categories:{},failures:[]};
const oracle=new GteOracleSession();
try{
 const manifest=JSON.parse(fs.readFileSync(path.join(javascriptRoot,'gte-port-manifest.json')));
 report.environment=await oracle.environment();
 const corpus=gteCorpus();
 if(corpus.length!==401||new Set(corpus.map(p=>p.name)).size!==401||corpus.reduce((n,p)=>n+p.request.steps.length,0)!==5435)
  throw new Error('Incomplete or duplicate GTE corpus.');
 for(const probe of corpus){
  report.stats.scenarios++;report.stats.requestedOperations+=probe.request.steps.length;
  const category=report.categories[probe.category]??={scenarios:0,operations:0,failures:0,oracleFailures:0};category.scenarios++;
  const observed=await oracle.observe(probe.request),actual=validateGteObservation(probe.request,gteCall(probe.request,manifest));
  if(!observed.ok){report.stats.oracleFailures++;report.stats.failures++;category.oracleFailures++;category.failures++;report.failures.push({name:probe.name,oracleFailure:observed.failure,actual});continue;}
  report.stats.observedScenarios++;report.stats.operations+=observed.value.length;category.operations+=observed.value.length;
  const differences=observed.value.flatMap((value,j)=>isDeepStrictEqual(value,actual[j])?[]:[{step:j,input:probe.request.steps[j],expected:value,actual:actual[j]}]);
  if(differences.length){report.stats.failures+=differences.length;category.failures+=differences.length;report.failures.push({name:probe.name,differences});console.error(probe.name+': '+differences.length+' differences');}
 }
 report.completed=report.stats.oracleFailures===0;
}catch(error){report.fatal=error.stack??String(error);}
finally{
 try{await oracle.close();}catch(error){report.completed=false;report.fatal??=error.stack??String(error);}
 if(runtimeFingerprint()!==proof.runtimeFingerprint||verificationFingerprint()!==proof.verificationFingerprint){report.completed=false;report.fatal='Executable source changed during qualification.';}
 const folder=path.join(javascriptRoot,'artifacts/gte',configuration);fs.mkdirSync(folder,{recursive:true});fs.writeFileSync(path.join(folder,'results.json'),JSON.stringify(report,null,2)+'\n');
}
console.log(JSON.stringify({stats:report.stats,categories:report.categories,fatal:report.fatal}));
if(!report.completed||report.fatal||report.stats.failures)process.exitCode=1;
