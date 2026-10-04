// Independent observation of the unchanged C# assembly and production JavaScript.
import '../node-entry.js';
import fs from 'node:fs';
import path from 'node:path';
import { isDeepStrictEqual } from 'node:util';
import { ModelOracleSession } from './ModelOracleSession.mjs';
import { javascriptRoot, configuration, baseline } from './dotnet.mjs';
import { runtimeFingerprint, verificationFingerprint } from './evidence.mjs';
import { leaderCorpus } from './leader-corpus.mjs';
import { jsGeometry } from './foundations-wire.mjs';

const proof={runtimeFingerprint:runtimeFingerprint(),verificationFingerprint:verificationFingerprint()};
const report={...proof,configuration,sourceRef:baseline.ref,completed:false,fatal:null,
  stats:{scenarios:0,observedScenarios:0,requestedOperations:0,operations:0,failures:0,oracleFailures:0},categories:{},failures:[]};
const oracle=new ModelOracleSession();
try{
  const corpus=leaderCorpus();
  if(corpus.length!==639||new Set(corpus.map(p=>p.name)).size!==639||corpus.reduce((n,p)=>n+p.request.steps.length,0)!==7221)
    throw new Error('Incomplete or duplicate leader corpus.');
  const nativeManifest=JSON.parse(fs.readFileSync(path.join(javascriptRoot,'native-port-manifest.json')));
  report.environment=await oracle.environment();
  for(const probe of corpus){
    const observed=await oracle.observe(probe.request),actual=jsGeometry({...probe.request,nativeManifest});
    report.stats.scenarios++;report.stats.requestedOperations+=probe.request.steps.length;
    const category=report.categories[probe.category]??={scenarios:0,operations:0,failures:0,oracleFailures:0};category.scenarios++;
    if(!observed.ok){
      report.stats.failures++;report.stats.oracleFailures++;category.failures++;category.oracleFailures++;
      report.failures.push({name:probe.name,category:probe.category,oracleFailure:observed.failure,actual});
      console.error('Native observation unavailable: '+probe.name);continue;
    }
    const expected=observed.value;
    if(!Array.isArray(actual)||actual.length!==expected.length)throw new Error('Incomplete JavaScript result: '+probe.name);
    report.stats.observedScenarios++;report.stats.operations+=expected.length;category.operations+=expected.length;
    const differences=expected.flatMap((value,i)=>isDeepStrictEqual(value,actual[i])?[]:[{step:i,operation:probe.request.steps[i],expected:value,actual:actual[i]}]);
    if(differences.length){report.stats.failures+=differences.length;category.failures+=differences.length;report.failures.push({name:probe.name,category:probe.category,differences});}
  }
  report.completed=report.stats.oracleFailures===0;
}catch(error){report.fatal=error.stack??String(error);}
finally{
  try{await oracle.close();}catch(error){report.completed=false;report.fatal??=error.stack??String(error);}
  if(runtimeFingerprint()!==proof.runtimeFingerprint||verificationFingerprint()!==proof.verificationFingerprint){report.completed=false;report.fatal='Executable source changed during leader qualification.';}
  const directory=path.join(javascriptRoot,'artifacts/leader-differential',configuration);fs.mkdirSync(directory,{recursive:true});
  fs.writeFileSync(path.join(directory,'results.json'),JSON.stringify(report,null,2)+'\n');
}
console.log(JSON.stringify({stats:report.stats,categories:report.categories,fatal:report.fatal}));
if(!report.completed||report.fatal||report.stats.failures)process.exitCode=1;
