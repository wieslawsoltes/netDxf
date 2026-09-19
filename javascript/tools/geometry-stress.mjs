// Strict randomized qualification. Known libm/V8 mismatches stay failing; no tolerance or allowlist.
import fs from 'node:fs';
import path from 'node:path';
import { isDeepStrictEqual } from 'node:util';
import { OracleClient } from './OracleClient.mjs';
import { javascriptRoot, configuration, baseline } from './dotnet.mjs';
import { runtimeFingerprint, verificationFingerprint } from './evidence.mjs';
import { createGeometryCaller } from './geometry-wire.mjs';
import { doubleBits } from './wire.mjs';
const D=d=>({d:doubleBits(d)}),V=a=>({type:'netDxf.Vector3',signature:'double,double,double',args:a.map(D)});
const manifest=JSON.parse(fs.readFileSync(path.join(javascriptRoot,'native-port-manifest.json')));
const call=createGeometryCaller(manifest),oracle=new OracleClient(),proof={runtimeFingerprint:runtimeFingerprint(),verificationFingerprint:verificationFingerprint()};
let seed=1297;const random=()=>{seed=(Math.imul(seed,1664525)+1013904223)>>>0;return(seed/4294967296-0.5)*10;};
const corpus=[];
for(let i=0;i<1000;i++){
  const a=V([random(),random(),random()]),b=V([random(),random(),random()]);
  corpus.push({id:`random/Vector3.AngleBetween/${i}`,type:'netDxf.Vector3',action:'call',static:true,member:'AngleBetween',signature:'netDxf.Vector3,netDxf.Vector3',args:[a,b]});
  corpus.push({id:`random/Matrix3.RotationZ/${i}`,type:'netDxf.Matrix3',action:'call',static:true,member:'RotationZ',signature:'double',args:[D(random())]});
}
const failures=[],results=[];let completed=false,fatal=null;
try{
  for(let at=0;at<corpus.length;at+=200){const requests=corpus.slice(at,at+200),expected=await oracle.request({op:'geometry',requests});if(!expected.ok)throw new Error(expected.error);
    const actual=call({requests});for(let i=0;i<requests.length;i++){
      const passed=isDeepStrictEqual(expected.value[i],actual[i]);results.push({id:requests[i].id,passed});
      if(!passed)failures.push({request:requests[i],expected:expected.value[i],actual:actual[i]});
    }
  }completed=true;
}catch(e){fatal=e.stack;throw e;}finally{
  try{await oracle.close();}catch(e){fatal??=e.stack;completed=false;}
  if(proof.runtimeFingerprint!==runtimeFingerprint()||proof.verificationFingerprint!==verificationFingerprint()){completed=false;fatal='Source changed during exact qualification.';}
  const out=path.join(javascriptRoot,'artifacts/geometry-exact',configuration);fs.mkdirSync(out,{recursive:true});
  fs.writeFileSync(path.join(out,'results.json'),JSON.stringify({...proof,sourceRef:baseline.ref,configuration,completed,fatal,seed:1297,
    comparison:'Exact IEEE-754 bits of every public result and mutation; NO tolerance, normalization, filtering, or expected-failure allowance.',
    stats:{comparisons:results.length,failures:failures.length},results,failures},null,2)+'\n');
}
console.log(`Geometry exact qualification: ${results.length} comparisons; ${failures.length} mismatches.`);
if(!completed||fatal||failures.length)throw new Error('Full geometry bit parity is NOT qualified. See geometry-exact/results.json.');
