import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { isDeepStrictEqual } from 'node:util';
import { OracleClient } from './OracleClient.mjs';
import { javascriptRoot, configuration, baseline } from './dotnet.mjs';
import { runtimeFingerprint, verificationFingerprint } from './evidence.mjs';
import { lifecycleCall } from './lifecycle-wire.mjs';
export function lifecycleCorpus() {
  const corpus=[];
  function base(){return [{method:'Carrier',id:'a',name:'CARRIER'}, {method:'Carrier',id:'b',name:'CARRIER'}, {method:'Dictionary',id:'c'},
    ...['OLD','PEER','THIRD'].flatMap((name,i)=>[{method:'Registry',id:'r'+i,name},{method:'Data',id:'d'+i,registry:'r'+i},{method:'Record',target:'d'+i,code:1004,value:[i+1,2]}])];}
  let seed=78139;const next=()=>{seed=(Math.imul(seed,1664525)+1013904223)>>>0;return seed;};
  for(let i=0;i<300;i++) {
    const operations=base();
    for(let j=0;j<30;j++) {
      const n=next()%3,target=['a','b','c'][next()%3],data='d'+n,key=['OLD','PEER','THIRD','NEW'][next()%4];
      const method=['Add','Set','Remove','Clear','ContainsPair','RemovePair','AddKey','Try','Has','Clone','Rename'][next()%11];
      operations.push({method,target:method==='Rename'?'r'+n:method==='Clone'?'d'+n:target,data,key,name:['NEW','MORE','OLD','PEER','THIRD'][next()%5],id:'copy'+j,
        mode:['','','','throw-add','throw-remove','throw-name','change-event'][next()%7]});
    }
    corpus.push({name:'random/'+i,operations});
  }
  for(const mode of ['','throw-name','change-event','collision','detach'])for(const target of ['a','b','c']) {
    corpus.push({name:'rename/'+mode+'/'+target,operations:[...base(),{method:'Add',target,data:'d0'}, {method:'Add',target:'b',data:'d0'},
      {method:'Rename',target:'r0',name:'NEW',mode,collisionTarget:target},{method:'Rename',target:'r0',name:'LATER'}, {method:'Clear',target},
      {method:'Rename',target:'r0',name:'FINAL'}]});
  }
  for(const edit of ['Remove','Clear','Set','Add','Rename'])corpus.push({name:'enumerate/'+edit,operations:[...base(),{method:'Add',target:'a',data:'d0'},
    {method:'Add',target:'a',data:'d1'},{method:'Enumerate',target:'a'},{method:'Next'},
    {method:edit,target:edit==='Rename'?'r0':'a',name:'NEW',key:'OLD',data:'d2'},{method:'Next'},{method:'Reset'},{method:'Next'}]});
  for(const action of ['Canonicalize','ReplaceBinding'])corpus.push({name:'binding/'+action,operations:[...base(),
    {method:'Registry',id:'canonical',name:'OLD'},{method:'Data',id:'replacement',registry:'canonical'},
    {method:'Add',target:'a',data:'d0'},{method:action,target:'a',key:'OLD',registry:'canonical',data:'replacement'},
    {method:'Rename',target:'r0',name:'CALLER'},{method:'Rename',target:'canonical',name:'BOUND'},
    {method:'Remove',target:'a',key:'BOUND'},{method:'Rename',target:'canonical',name:'RELEASED'}]});
  for(const name of ['RENAMED','PEER',null,'','ACAD'])corpus.push({name:'cycle/'+name,operations:[...base(),
    {method:'Add',target:'r0',data:'d0'},{method:'Add',target:'r0',data:'d1'},{method:'Add',target:'r1',data:'d0'},
    {method:'Clone',target:'r0',id:'clone',name}]});
  for(const name of [null,'',' ',' OLD ','ACAD',' ACAD ','acad','A/B','\u0085OK\u0085','\ufeffOK\ufeff','ß','SS','Σ','ς','a\0b'])corpus.push({name:'names/'+name,operations:[
    {method:'Registry',id:'r0',name},{method:'Registry',id:'other',name:'OTHER'}]});
  for(const value of ['0','-1','9223372036854775807','-9223372036854775808','123456'])corpus.push({name:'handle/'+value,operations:[...base(),
    {method:'AssignHandle',target:'r0',value},{method:'Clone',target:'r0',id:'clone'},{method:'References',target:'clone'}]});
  for(const a of ['A','old','SS','ß','ς','Σ','abc','ABcd'])for(const b of ['a','OLD','SS','ß','σ','abcd'])corpus.push({name:'equality/'+a+'/'+b,operations:[{method:'Registry',id:'a',name:a},{method:'Registry',id:'b',name:b},{method:'Compare',target:'a',other:'b'},{method:'Equal',target:'a',other:'b'}]});
  for(let i=0;i<128;i++)corpus.push({name:'clone-emit/'+i,operations:[...base(),{method:'Record',target:'d0',code:1000,value:'Zażółć 東京 '+i},
    {method:'Record',target:'d0',code:1040,value:(i-64)*1.2345678901234567},{method:'Clone',target:'d0',id:'copy'},
    {method:'Mutate',target:'d0',index:0,at:0,value:99},{method:'Emit',target:'copy'}]});
  return corpus;
}
export async function lifecycleDifferential(){
  const scenarios=lifecycleCorpus(),oracle=new OracleClient(),failures=[],proof={runtimeFingerprint:runtimeFingerprint(),verificationFingerprint:verificationFingerprint()};
  let completed=false,fatal=null,operations=0,byteComparisons=0;
  try{for(let at=0;at<scenarios.length;at+=10){const batch=scenarios.slice(at,at+10),expected=await oracle.request({op:'lifecycle',scenarios:batch});if(!expected.ok)throw new Error(expected.error);
    const actual=lifecycleCall({scenarios:batch});
    for(let i=0;i<batch.length;i++){operations+=batch[i].operations.length;
      if(!isDeepStrictEqual(expected.value[i],actual[i])){const mismatch=expected.value[i].findIndex((row,j)=>!isDeepStrictEqual(row,actual[i][j]));failures.push({name:batch[i].name,scenario:batch[i],firstMismatch:mismatch,expected:expected.value[i][mismatch],actual:actual[i][mismatch]});}
      for(let j=0;j<batch[i].operations.length;j++)if(batch[i].operations[j].method==='Emit'&&expected.value[i][j].error===null&&isDeepStrictEqual(expected.value[i][j].result,actual[i][j].result))byteComparisons+=2;
    }
  }completed=true;}catch(e){fatal=e.stack;throw e;}finally{
    try{await oracle.close();}catch(e){fatal??=e.stack;completed=false;}
    if(proof.runtimeFingerprint!==runtimeFingerprint()||proof.verificationFingerprint!==verificationFingerprint()){completed=false;fatal='Code changed during verification.';}
    const dir=path.join(javascriptRoot,'artifacts/lifecycle-differential',configuration);fs.mkdirSync(dir,{recursive:true});
    fs.writeFileSync(path.join(dir,'results.json'),JSON.stringify({...proof,sourceRef:baseline.ref,configuration,completed,fatal,stats:{comparisons:scenarios.length,operations,byteComparisons,failures:failures.length},failures},null,2)+'\n');
  }
  console.log(`Typed lifecycle: ${scenarios.length} scenarios; ${operations} operations; ${byteComparisons} byte comparisons; ${failures.length} mismatches.`);
  if(!completed||fatal||failures.length)throw new Error('Typed lifecycle differential failed.');
}
if(process.argv[1]&&path.resolve(process.argv[1])===fileURLToPath(import.meta.url))await lifecycleDifferential();
