// Actual System.Math results are compared bit for bit; source generation is independently checked.
import fs from 'node:fs';
import path from 'node:path';
import {spawnSync} from 'node:child_process';
import {OracleClient} from './OracleClient.mjs';
import {javascriptRoot,oracleRoot,baseline,configuration} from './dotnet.mjs';
import {runtimeFingerprint,verificationFingerprint} from './evidence.mjs';
import {expLogCorpus} from './exp-log-corpus.mjs';
import {referenceMathCall} from './reference-math-wire.mjs';
const calls=expLogCorpus(),proof={runtimeFingerprint:runtimeFingerprint(),verificationFingerprint:verificationFingerprint()};
const report={...proof,sourceRef:baseline.ref,configuration,completed:false,stats:{comparisons:0,failures:0},failures:[],fatal:null};
const oracle=new OracleClient({args:[path.join(oracleRoot,'GeometryOracle.dll')]});
try{
 const generation=spawnSync(process.env.PYTHON||'python',['tools/ReferenceMath/generate-exp-log.py','--check'],{cwd:javascriptRoot,encoding:'utf8'});
 if(generation.status!==0)throw new Error('Exp/log provenance failed: '+(generation.stderr||generation.error));
 if(!calls.length||new Set(calls.map(c=>c.id)).size!==calls.length)throw new Error('Invalid exp/log case identities.');
 report.environment=await oracle.request({op:'environment'});
 for(let at=0;at<calls.length;at+=256){const batch=calls.slice(at,at+256),expected=await oracle.request({op:'reference-math',calls:batch}),actual=referenceMathCall(batch);
  if(!Array.isArray(expected)||expected.length!==batch.length)throw new Error('Incomplete exp/log oracle result.');
  for(let i=0;i<batch.length;i++){report.stats.comparisons++;if(actual[i]!==expected[i]){report.stats.failures++;report.failures.push({call:batch[i],expected:expected[i],actual:actual[i]});}}
 }report.completed=true;
}catch(e){report.fatal=e.stack;}finally{try{await oracle.close();}catch(e){report.fatal??=e.stack;report.completed=false;}if(runtimeFingerprint()!==proof.runtimeFingerprint||verificationFingerprint()!==proof.verificationFingerprint){report.completed=false;report.fatal='Source changed during exp/log verification.';}
 const folder=path.join(javascriptRoot,'artifacts/exp-log-differential',configuration);fs.mkdirSync(folder,{recursive:true});fs.writeFileSync(path.join(folder,'results.json'),JSON.stringify(report,null,2)+'\n');}
console.log(JSON.stringify({stats:report.stats,fatal:report.fatal,firstFailures:report.failures.slice(0,10)}));
if(!report.completed||report.fatal||report.stats.failures)process.exitCode=1;
