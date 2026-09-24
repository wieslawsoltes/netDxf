// Focused native-browser evidence. Missing native observations remain failures.
import fs from 'node:fs';
import path from 'node:path';
import { createHash } from 'node:crypto';
import { objectGraphIOCorpus } from './object-graph-io-corpus.mjs';
import { ValidateObjectGraphObservation } from './object-graph-io-observation.mjs';
import { ModelOracleSession } from './ModelOracleSession.mjs';
import { runtimeFingerprint,verificationFingerprint } from './evidence.mjs';
import { baseline,configuration,javascriptRoot } from './dotnet.mjs';
import { canonical } from './browser-runner.mjs';
const proof={runtimeFingerprint:runtimeFingerprint(),verificationFingerprint:verificationFingerprint()};
const probes=objectGraphIOCorpus(),oracle=new ModelOracleSession(),cases=[];
if(probes.length!==937||new Set(probes.map(p=>p.name)).size!==937||probes.reduce((n,p)=>n+p.request.steps.length,0)!==8715)throw new Error('Incomplete object-graph corpus.');
try {
  for(const probe of probes) {
    const observed=await oracle.observe(probe.request);
    const expected=observed.ok?ValidateObjectGraphObservation(observed.value,probe.request.steps.length):{oracleFailure:observed.failure};
    cases.push({name:probe.name,input:probe.request,expected:{objectGraphIO:createHash('sha256').update(canonical(expected)).digest('hex')},
      ...(!observed.ok?{sourceOracleFailure:observed.failure}:{})});
  }
}finally{await oracle.close();}
if(runtimeFingerprint()!==proof.runtimeFingerprint||verificationFingerprint()!==proof.verificationFingerprint)throw new Error('Source changed during native browser preparation.');
const out=path.join(javascriptRoot,'artifacts/object-graph-browser',configuration);fs.mkdirSync(out,{recursive:true});
fs.writeFileSync(path.join(out,'corpus.json'),JSON.stringify({...proof,sourceRef:baseline.ref,sourceFingerprint:baseline.sourceFingerprint,configuration,fixtures:0,scope:'object-graph-io-only',requestedOperations:8715,cases}));
console.log('Prepared '+cases.length+' object-graph browser inputs; '+cases.filter(p=>p.sourceOracleFailure).length+' unavailable native observations remain blocking.');
