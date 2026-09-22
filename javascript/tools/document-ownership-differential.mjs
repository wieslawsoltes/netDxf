import '../node-entry.js';
import fs from 'node:fs';
import path from 'node:path';
import { isDeepStrictEqual } from 'node:util';
import { OracleClient } from './OracleClient.mjs';
import { dotnetCommand, oracleRoot, javascriptRoot, configuration, baseline } from './dotnet.mjs';
import { runtimeFingerprint, verificationFingerprint } from './evidence.mjs';
import { documentOwnershipCorpus } from './document-ownership-corpus.mjs';
import { documentOwnershipCall } from './document-ownership-wire.mjs';
const proof={runtimeFingerprint:runtimeFingerprint(),verificationFingerprint:verificationFingerprint()};
const report={...proof,sourceRef:baseline.ref,configuration,completed:false,fatal:null,stats:{scenarios:0,operations:0,failures:0},failures:[]};
const oracle=new OracleClient({args:[path.join(oracleRoot,'GeometryOracle.dll')]});
try{
  report.environment=await oracle.request({op:'environment'});
  for(const probe of documentOwnershipCorpus()){
    const expected=await oracle.request(probe.request),actual=documentOwnershipCall(probe.request);
    if(!Array.isArray(expected)||expected.length!==probe.request.steps.length||actual.length!==expected.length)throw new Error('Incomplete ownership observation: '+JSON.stringify(expected));
    report.stats.scenarios++;report.stats.operations+=expected.length;
    const differences=expected.flatMap((value,i)=>isDeepStrictEqual(value,actual[i])?[]:[{step:i,input:probe.request.steps[i],expected:value,actual:actual[i]}]);
    if(differences.length){report.stats.failures+=differences.length;report.failures.push({name:probe.name,differences});console.error(probe.name+': '+differences.length+' differences');}
  }
  report.completed=true;
}catch(e){report.fatal=e.stack??String(e);}
finally{
  try{await oracle.close();}catch(e){report.completed=false;report.fatal??=e.stack??String(e);}
  if(runtimeFingerprint()!==proof.runtimeFingerprint||verificationFingerprint()!==proof.verificationFingerprint){report.completed=false;report.fatal='Source changed during qualification.';}
  const folder=path.join(javascriptRoot,'artifacts/document-ownership',configuration);fs.mkdirSync(folder,{recursive:true});fs.writeFileSync(path.join(folder,'results.json'),JSON.stringify(report,null,2)+'\n');
}
console.log(JSON.stringify(report.stats),report.fatal??'');if(!report.completed||report.fatal||report.stats.failures)process.exitCode=1;
