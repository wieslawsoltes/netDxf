import '../node-entry.js';
import fs from 'node:fs';
import path from 'node:path';
import { isDeepStrictEqual } from 'node:util';
import { OracleClient } from './OracleClient.mjs';
import { oracleRoot, javascriptRoot, baseline, configuration } from './dotnet.mjs';
import { runtimeFingerprint, verificationFingerprint } from './evidence.mjs';
import { nurbsCorpus } from './nurbs-corpus.mjs';
import { jsNurbs } from './nurbs-wire.mjs';
const corpus=nurbsCorpus();
const proof={runtimeFingerprint:runtimeFingerprint(),verificationFingerprint:verificationFingerprint()};
const report={...proof,sourceRef:baseline.ref,configuration,completed:false,fatal:null,
  stats:{scenarios:0,operations:0,failures:0},categories:{},failures:[]};
const output=path.join(javascriptRoot,'artifacts/nurbs-differential',configuration);fs.mkdirSync(output,{recursive:true});
const oracle=new OracleClient({args:[path.join(oracleRoot,'GeometryOracle.dll')]});
try {
  if(!corpus.length||new Set(corpus.map(p=>p.name)).size!==corpus.length)throw new Error('NURBS scenarios require unique nonempty identities.');
  report.environment=await oracle.request({op:'environment'});
  for(const probe of corpus) {
    const expected=await oracle.request(probe.request),actual=jsNurbs(probe.request);
    if(!Array.isArray(expected)||expected.length!==probe.request.steps.length)throw new Error('Incomplete oracle response.');
    report.stats.scenarios++;report.stats.operations+=expected.length;
    const category=report.categories[probe.category]??={scenarios:0,operations:0,failures:0};category.scenarios++;category.operations+=expected.length;
    const differences=expected.flatMap((value,i)=>isDeepStrictEqual(value,actual[i])?[]:[{step:i,operation:probe.request.steps[i],expected:value,actual:actual[i]}]);
    if(differences.length){report.stats.failures+=differences.length;category.failures+=differences.length;report.failures.push({name:probe.name,category:probe.category,differences});
      if(report.failures.length<=12)console.error('NURBS mismatch',JSON.stringify(report.failures.at(-1)));}
  }
  report.completed=true;
}catch(error){report.fatal=error.stack;}
finally {
  try{await oracle.close();}catch(error){report.fatal??=error.stack;report.completed=false;}
  if(runtimeFingerprint()!==proof.runtimeFingerprint||verificationFingerprint()!==proof.verificationFingerprint){report.completed=false;report.fatal='Executable source changed during verification.';}
  fs.writeFileSync(path.join(output,'results.json'),JSON.stringify(report,null,2)+'\n');
}
console.log(JSON.stringify({stats:report.stats,categories:report.categories,fatal:report.fatal}));
if(!report.completed||report.fatal||report.stats.failures)process.exitCode=1;
