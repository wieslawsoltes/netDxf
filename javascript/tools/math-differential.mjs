// Direct .NET math comparisons supplement, never replace, the retained geometry gates.
import fs from 'node:fs';
import path from 'node:path';
import { OracleClient } from './OracleClient.mjs';
import { javascriptRoot, configuration, baseline } from './dotnet.mjs';
import { runtimeFingerprint, verificationFingerprint } from './evidence.mjs';
import { mathCorpus } from './math-corpus.mjs';
import { mathCall } from './math-wire.mjs';
const corpus=mathCorpus(), oracle=new OracleClient();
const proof={runtimeFingerprint:runtimeFingerprint(),verificationFingerprint:verificationFingerprint()};
const report={...proof,sourceRef:baseline.ref,configuration,completed:false,fatal:null,
  stats:{comparisons:0,failures:0},members:{},failures:[]};
try {
  const env=await oracle.request({op:'math',environment:true});if(!env.ok)throw new Error(JSON.stringify(env));report.environment=env.value;
  for(let at=0;at<corpus.length;at+=256){
    const requests=corpus.slice(at,at+256),expected=await oracle.request({op:'math',requests}),actual=mathCall(requests);
    if(!expected.ok||!Array.isArray(expected.value)||expected.value.length!==requests.length)throw new Error('Incomplete math oracle result.');
    for(let i=0;i<requests.length;i++){
      const request=requests[i],want=expected.value[i],got=actual[i];
      if(want.id!==request.id||got.id!==request.id)throw new Error('Math oracle identity mismatch.');
      const member=report.members[request.member]??={comparisons:0,failures:0};
      report.stats.comparisons++;member.comparisons++;
      if(want.result!==got.result){report.stats.failures++;member.failures++;report.failures.push({request,expected:want.result,actual:got.result});}
    }
  }
  report.completed=true;
}catch(error){report.fatal=error.stack;}
finally {
  try{await oracle.close();}catch(error){report.completed=false;report.fatal??=error.stack;}
  if(runtimeFingerprint()!==proof.runtimeFingerprint||verificationFingerprint()!==proof.verificationFingerprint){report.completed=false;report.fatal='Executable code changed during math qualification.';}
  const out=path.join(javascriptRoot,'artifacts/math-differential',configuration);fs.mkdirSync(out,{recursive:true});
  fs.writeFileSync(path.join(out,'results.json'),JSON.stringify(report,null,2)+'\n');
}
console.log(JSON.stringify({completed:report.completed,stats:report.stats,members:report.members,fatal:report.fatal}));
if(!report.completed||report.fatal||report.stats.failures)process.exitCode=1;
