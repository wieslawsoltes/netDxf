import '../node-entry.js';
import fs from 'node:fs';
import path from 'node:path';
import {isDeepStrictEqual} from 'node:util';
import {OracleClient} from './OracleClient.mjs';
import {oracleRoot,javascriptRoot,configuration,baseline} from './dotnet.mjs';
import {runtimeFingerprint,verificationFingerprint} from './evidence.mjs';
import {attributeCollectionCorpus} from './attribute-collection-corpus.mjs';
import {attributeCorpus} from './attribute-corpus.mjs';
import {jsGeometry} from './foundations-wire.mjs';
const proof={runtimeFingerprint:runtimeFingerprint(),verificationFingerprint:verificationFingerprint()},out={...proof,configuration,sourceRef:baseline.ref,completed:false,stats:{scenarios:0,operations:0,failures:0},failures:[]};
const oracle=new OracleClient({args:[path.join(oracleRoot,'GeometryOracle.dll')]});
try{for(const p of attributeCorpus().concat(attributeCollectionCorpus())){
 const expected=await oracle.request(p.request),actual=jsGeometry(p.request);if(!Array.isArray(expected)||expected.length!==p.request.steps.length)throw new Error('Incomplete oracle output');
 out.stats.scenarios++;out.stats.operations+=expected.length;
 const differences=expected.flatMap((e,i)=>isDeepStrictEqual(e,actual[i])?[]:[{step:i,operation:p.request.steps[i],expected:e,actual:actual[i]}]);
 if(differences.length){out.stats.failures+=differences.length;out.failures.push({name:p.name,differences});if(out.failures.length<=3)console.error(JSON.stringify(out.failures.at(-1)));}
 }out.completed=true;}catch(e){out.fatal=e.stack;}finally{await oracle.close();if(runtimeFingerprint()!==proof.runtimeFingerprint||verificationFingerprint()!==proof.verificationFingerprint){out.completed=false;out.fatal='Source changed during test';}
 const dir=path.join(javascriptRoot,'artifacts/attribute-differential',configuration);fs.mkdirSync(dir,{recursive:true});fs.writeFileSync(path.join(dir,'results.json'),JSON.stringify(out,null,2)+'\n');}
console.log(JSON.stringify({stats:out.stats,fatal:out.fatal}));if(!out.completed||out.stats.failures)process.exitCode=1;
