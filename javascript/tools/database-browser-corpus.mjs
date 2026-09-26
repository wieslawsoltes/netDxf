// Focused real-browser inputs; not a replacement for the full browser qualification.
import fs from 'node:fs';
import path from 'node:path';
import { createHash } from 'node:crypto';
import { databasePayloadCorpus } from './database-payload-corpus.mjs';
import { sourceMetadataCorpus } from './source-metadata-corpus.mjs';
import { ValidateDatabaseObservation } from './database-io-observation.mjs';
import { ModelOracleSession } from './ModelOracleSession.mjs';
import { runtimeFingerprint,verificationFingerprint } from './evidence.mjs';
import { baseline,configuration,javascriptRoot } from './dotnet.mjs';
import { canonical } from './browser-runner.mjs';
const proof={runtimeFingerprint:runtimeFingerprint(),verificationFingerprint:verificationFingerprint()};
const groups=[['payload','databasePayload',databasePayloadCorpus(),1070,4701],['metadata','sourceMetadata',sourceMetadataCorpus(),420,12322]];
const oracle=new ModelOracleSession(),cases=[];
try {
  for(const [kind,op,probes,count,operations] of groups){
    if(probes.length!==count||new Set(probes.map(p=>p.name)).size!==count||probes.reduce((n,p)=>n+p.request.steps.length,0)!==operations)throw new Error('Incomplete database corpus.');
    for(const probe of probes){
      const observed=await oracle.observe(probe.request);
      if(!observed.ok)throw new Error('Unavailable database native observation: '+probe.name+' '+JSON.stringify(observed.failure));
      const expected=ValidateDatabaseObservation(observed.value,probe.request.steps.length,kind);
      cases.push({name:probe.name,input:probe.request,expected:{[op]:createHash('sha256').update(canonical(expected)).digest('hex')}});
    }
  }
}finally{await oracle.close();}
if(runtimeFingerprint()!==proof.runtimeFingerprint||verificationFingerprint()!==proof.verificationFingerprint)throw new Error('Source changed during native browser preparation.');
const out=path.join(javascriptRoot,'artifacts/database-browser',configuration);fs.mkdirSync(out,{recursive:true});
fs.writeFileSync(path.join(out,'corpus.json'),JSON.stringify({...proof,sourceRef:baseline.ref,sourceFingerprint:baseline.sourceFingerprint,configuration,fixtures:0,scope:'database-io-only',requestedOperations:17023,cases}));
console.log('Prepared '+cases.length+' independent database browser inputs.');
