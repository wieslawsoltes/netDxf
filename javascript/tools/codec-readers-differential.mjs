import '../node-entry.js';
import fs from 'node:fs';
import path from 'node:path';
import { isDeepStrictEqual } from 'node:util';
import { OracleClient } from './OracleClient.mjs';
import { javascriptRoot,oracleRoot,configuration,baseline } from './dotnet.mjs';
import { runtimeFingerprint,verificationFingerprint } from './evidence.mjs';
import { codecReadersCorpus } from './codec-readers-corpus.mjs';
import { codecReadersCall } from './codec-readers-wire.mjs';
const proof={runtimeFingerprint:runtimeFingerprint(),verificationFingerprint:verificationFingerprint()};
const report={...proof,configuration,sourceRef:baseline.ref,completed:false,fatal:null,stats:{scenarios:0,requestedCommands:0,commands:0,constructorRejections:0,failures:0},failures:[]};
const oracle=new OracleClient({args:[path.join(oracleRoot,'GeometryOracle.dll')]});
try{
  const corpus=codecReadersCorpus();
  if(new Set(corpus.map(p=>p.name)).size!==corpus.length)throw new Error('Duplicate codec scenario names.');
  report.environment=await oracle.request({op:'environment'});
  for(const probe of corpus){
    const expected=await oracle.request(probe.request),actual=codecReadersCall(probe.request);
    if(!expected||!Array.isArray(expected.results)||!Object.hasOwn(expected,'constructor'))throw new Error('Invalid native observation: '+JSON.stringify(expected));
    report.stats.scenarios++;report.stats.requestedCommands+=probe.request.steps.length;report.stats.commands+=expected.results.length;if(expected.constructor!==null)report.stats.constructorRejections++;
    if(!isDeepStrictEqual(expected,actual)){report.stats.failures++;report.failures.push({name:probe.name,request:probe.request,expected,actual});if(report.stats.failures<=12)console.error(probe.name);}
  }
  report.completed=true;
}catch(error){report.fatal=error.stack??String(error);}
finally{
  try{await oracle.close();}catch(error){report.completed=false;report.fatal??=error.stack??String(error);}
  if(runtimeFingerprint()!==proof.runtimeFingerprint||verificationFingerprint()!==proof.verificationFingerprint){report.completed=false;report.fatal='Executable files changed during comparison.';}
  const output=path.join(javascriptRoot,'artifacts/codec-readers',configuration);fs.mkdirSync(output,{recursive:true});fs.writeFileSync(path.join(output,'results.json'),JSON.stringify(report,null,2)+'\n');
}
console.log(JSON.stringify({stats:report.stats,fatal:report.fatal}));if(!report.completed||report.fatal||report.stats.failures)process.exitCode=1;
