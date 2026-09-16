import fs from 'node:fs';
import path from 'node:path';
import { OracleClient } from './OracleClient.mjs';
import { javascriptRoot, configuration, baseline } from './dotnet.mjs';
import { runtimeFingerprint, verificationFingerprint } from './evidence.mjs';
import { validateOrdinalProfile } from './globalization.mjs';
import { OrdinalIgnoreCaseEquals, OrdinalIgnoreCaseKey } from '../runtime/Collections.js';
const oracle = new OracleClient(), pairs = [];
for (let i=0;i<=0x10ffff;i++) {
  if(i>=0xd800 && i<=0xdfff) continue;
  const c=String.fromCodePoint(i);
  for(const candidate of new Set([c.toUpperCase(),c.toLowerCase(),OrdinalIgnoreCaseKey(c)])) if(candidate!==c) pairs.push([c,candidate]);
}
pairs.push(['ß','SS'],['straße','STRASSE'],['i','İ'],['i','ı'],['s','ſ'],['k','K']);
let completed=false, fatal=null, failures=0, globalization=null;
const proof={runtimeFingerprint:runtimeFingerprint(),verificationFingerprint:verificationFingerprint()};
try {
  const mapping=await oracle.request({op:'ordinal-map'});
  if(!mapping.ok) throw new Error('Cannot inspect .NET ordinal profile: '+mapping.error);
  globalization=validateOrdinalProfile(mapping.value);
  for(let at=0;at<pairs.length;at+=1000) {
    const batch=pairs.slice(at,at+1000),expected=await oracle.request({op:'ordinal',pairs:batch});
    if(!expected.ok) throw new Error(expected.error);
    for(let i=0;i<batch.length;i++) if(OrdinalIgnoreCaseEquals(...batch[i])!==expected.value[i]) {
      failures++;console.error('Ordinal mismatch',batch[i],expected.value[i]);
    }
  }
  completed=true;
} catch(error) {fatal=error.stack;throw error;}
finally {
  try{await oracle.close();}catch(error){fatal??=error.stack;completed=false;}
  if(proof.runtimeFingerprint!==runtimeFingerprint() || proof.verificationFingerprint!==verificationFingerprint()){fatal='Code changed during verification.';completed=false;}
  const out=path.join(javascriptRoot,'artifacts','casing-differential',configuration);fs.mkdirSync(out,{recursive:true});
  fs.writeFileSync(path.join(out,'results.json'),JSON.stringify({...proof,sourceRef:baseline.ref,configuration,completed,fatal,globalization,stats:{comparisons:pairs.length,failures}},null,2)+'\n');
}
if(!completed||fatal||failures)throw new Error('Ordinal casing differential failed.');
console.log(`Ordinal casing: ${pairs.length} comparisons; no mismatches.`);
