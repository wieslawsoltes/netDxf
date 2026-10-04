import fs from 'node:fs';
import path from 'node:path';
import {OracleClient} from './OracleClient.mjs';
import {oracleRoot,javascriptRoot,configuration,baseline} from './dotnet.mjs';
import {runtimeFingerprint,verificationFingerprint} from './evidence.mjs';
import {referenceMathCorpus} from './reference-math-corpus.mjs';
import {referenceMathCall} from './reference-math-wire.mjs';
const calls=referenceMathCorpus(),proof={runtimeFingerprint:runtimeFingerprint(),verificationFingerprint:verificationFingerprint()};
const report={...proof,sourceRef:baseline.ref,configuration,completed:false,fatal:null,stats:{comparisons:0,failures:0},categories:{},failures:[]};
const oracle=new OracleClient({args:[path.join(oracleRoot,'GeometryOracle.dll')]});
try {
  report.environment=await oracle.request({op:'environment'});
  for(let offset=0;offset<calls.length;offset+=256){
    const batch=calls.slice(offset,offset+256),expected=await oracle.request({op:'reference-math',calls:batch}),actual=referenceMathCall(batch);
    if(!Array.isArray(expected)||expected.length!==batch.length)throw new Error('Incomplete System.Math oracle response.');
    for(let i=0;i<batch.length;i++){
      const kind=report.categories[batch[i].name]??={comparisons:0,failures:0};kind.comparisons++;report.stats.comparisons++;
      if(actual[i]!==expected[i]){kind.failures++;report.stats.failures++;report.failures.push({call:batch[i],expected:expected[i],actual:actual[i]});}
    }
  }
  report.completed=true;
}catch(e){report.fatal=e.stack;}finally{
  try{await oracle.close();}catch(e){report.completed=false;report.fatal??=e.stack;}
  if(proof.runtimeFingerprint!==runtimeFingerprint()||proof.verificationFingerprint!==verificationFingerprint()){report.completed=false;report.fatal='Executable source changed during verification.';}
  const dir=path.join(javascriptRoot,'artifacts/reference-math',configuration);fs.mkdirSync(dir,{recursive:true});fs.writeFileSync(path.join(dir,'results.json'),JSON.stringify(report,null,2)+'\n');
}
console.log(JSON.stringify({stats:report.stats,categories:report.categories,fatal:report.fatal}));
if(!report.completed||report.fatal||report.stats.failures)process.exitCode=1;
