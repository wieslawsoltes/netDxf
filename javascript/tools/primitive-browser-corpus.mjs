// Focused real-browser corpus. Does not replace the required complete browser suite.
import fs from 'node:fs';
import path from 'node:path';
import { createHash } from 'node:crypto';
import { primitiveIOCorpus } from './primitive-io-corpus.mjs';
import { ModelOracleSession } from './ModelOracleSession.mjs';
import { ValidateEntityBodyObservation } from './entity-body-io-observation.mjs';
import { runtimeFingerprint, verificationFingerprint } from './evidence.mjs';
import { baseline, configuration, javascriptRoot } from './dotnet.mjs';
import { canonical } from './browser-runner.mjs';
const proof={runtimeFingerprint:runtimeFingerprint(),verificationFingerprint:verificationFingerprint()};
const oracle=new ModelOracleSession(),cases=[],probes=primitiveIOCorpus();
if(probes.length!==6416||probes.reduce((n,p)=>n+p.request.steps.length,0)!==25569)throw new Error('Incomplete primitive corpus.');
try {
  for(const probe of probes) {
    const observed=await oracle.observe(probe.request);
    if(!observed.ok)throw new Error('Unavailable native primitive observation: '+probe.name+' '+JSON.stringify(observed.failure));
    const expected=ValidateEntityBodyObservation(observed.value,probe.request.steps.length);
    cases.push({name:probe.name,input:probe.request,expected:{entityBodyIO:createHash('sha256').update(canonical(expected)).digest('hex')}});
  }
} finally {await oracle.close();}
if(runtimeFingerprint()!==proof.runtimeFingerprint||verificationFingerprint()!==proof.verificationFingerprint)throw new Error('Source changed while generating native digests.');
const output=path.join(javascriptRoot,'artifacts/primitive-browser',configuration);fs.mkdirSync(output,{recursive:true});
fs.writeFileSync(path.join(output,'corpus.json'),JSON.stringify({...proof,sourceRef:baseline.ref,sourceFingerprint:baseline.sourceFingerprint,configuration,fixtures:0,scope:'primitive-io-only',requestedOperations:25569,cases}));
console.log('Prepared '+cases.length+' independently observed primitive browser inputs.');
