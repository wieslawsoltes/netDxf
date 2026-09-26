import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { isDeepStrictEqual } from 'node:util';
import { OracleClient } from './OracleClient.mjs';
import { javascriptRoot, configuration, baseline } from './dotnet.mjs';
import { runtimeFingerprint, verificationFingerprint } from './evidence.mjs';
import { collectionCall } from './collection-wire.mjs';
export function collectionCorpus() {
  const scenarios=[];let seed=1234;const next=()=>{seed=(Math.imul(seed,1664525)+1013904223)>>>0;return seed;};
  for(let n=0;n<256;n++) {
    const initial=Array.from({length:n%67},()=>next()%79), operations=[];
    for(let j=0;j<20;j++) {
      const method=['Add','Insert','Remove','RemoveAt','Set','Get','Clear','Reverse','Sort','SortModulo','SortRange','Contains','IndexOf','CopyTo'][next()%14];
      operations.push({method,value:next()%79,index:(next()%70)-2,count:next()%70,length:next()%90,mode:['','cancel-add','cancel-remove','throw-add','throw-remove'][next()%5]});
    }
    scenarios.push({initial,operations});
  }
  for(const initial of [[],[1],[3,2,1]]) for(const edit of ['Add','Reverse','Set','Clear','AddSelf','Remove']) {
    scenarios.push({initial,operations:[{method:'Enumerate'},{method:'MoveNext'},{method:edit,index:0,value:9},{method:'MoveNext'},{method:'Reset'}]});
  }
  for(const n of [2,3,15,16,17,31,100,1000]) {
    const initial=Array.from({length:n},(_,i)=>i);
    scenarios.push({initial,operations:[{method:'SortModulo'},{method:'Reverse'},{method:'SortModulo'},{method:'Sort'}]});
  }
  return scenarios;
}
export async function collectionDifferential() {
  const corpus=collectionCorpus(),oracle=new OracleClient(),failures=[],proof={runtimeFingerprint:runtimeFingerprint(),verificationFingerprint:verificationFingerprint()};let completed=false,fatal=null,operations=0;
  try {
    for(let at=0;at<corpus.length;at+=32){const scenarios=corpus.slice(at,at+32),expected=await oracle.request({op:'collection',scenarios});if(!expected.ok)throw new Error(expected.error);
      const actual=collectionCall({scenarios});
      for(let i=0;i<scenarios.length;i++){operations+=scenarios[i].operations.length;if(!isDeepStrictEqual(actual[i],expected.value[i]))failures.push({scenario:scenarios[i],expected:expected.value[i],actual:actual[i]});}
    }completed=true;
  }catch(e){fatal=e.stack;throw e;}finally{
    try{await oracle.close();}catch(e){fatal??=e.stack;completed=false;}
    if(proof.runtimeFingerprint!==runtimeFingerprint()||proof.verificationFingerprint!==verificationFingerprint()){completed=false;fatal='Source changed during verification.';}
    const dir=path.join(javascriptRoot,'artifacts/collection-differential',configuration);fs.mkdirSync(dir,{recursive:true});
    fs.writeFileSync(path.join(dir,'results.json'),JSON.stringify({...proof,sourceRef:baseline.ref,configuration,completed,fatal,stats:{comparisons:corpus.length,operations,failures:failures.length},failures},null,2)+'\n');
  }
  console.log(`Collections: ${operations} operations; ${failures.length} failing scenarios.`);
  if(!completed||fatal||failures.length)throw new Error('Collection differential failed.');
}
if(process.argv[1]&&path.resolve(process.argv[1])===fileURLToPath(import.meta.url))await collectionDifferential();
