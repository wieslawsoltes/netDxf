import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { performance } from 'node:perf_hooks';
import { DxfTag, DxfRawDocument, DxfRawObjectStore, DxfRawHandleIndex } from '../index.js';
import { javascriptRoot } from './dotnet.mjs';
import { runtimeFingerprint } from './evidence.mjs';
const entities=Number(process.env.NETDXF_BENCHMARK_OBJECTS||2000);
if(!Number.isInteger(entities)||entities<1||entities>100000)throw new RangeError('Benchmark size must be 1..100000.');
const T=(c,v)=>new DxfTag(c,v),tags=[T(0,'SECTION'),T(2,'HEADER'),T(9,'$ACADVER'),T(1,'AC1032'),T(0,'ENDSEC'),T(0,'SECTION'),T(2,'OBJECTS'),
  T(0,'DICTIONARY'),T(5,'10'),T(330,'0'),T(100,'AcDbDictionary')];
for(let i=0;i<entities;i++)tags.push(T(3,'Variable '+i),T(350,(256+i).toString(16)));
for(let i=0;i<entities;i++)tags.push(T(0,'DICTIONARYVAR'),T(5,(256+i).toString(16)),T(330,'10'),T(100,'DictionaryVariables'),T(280,0),T(1,'Value '+i));
tags.push(T(0,'ENDSEC'),T(0,'EOF'));
const doc=DxfRawDocument.Create(tags),text=doc.ToBytes(),binary=doc.ToBytes(true),store=DxfRawObjectStore.Open(doc),results=[];
function measure(name,action){
  for(let i=0;i<3;i++)action();const samples=[];
  for(let i=0;i<12;i++){const start=performance.now();action();samples.push(performance.now()-start);}
  samples.sort((a,b)=>a-b);results.push({name,medianMs:samples[6],p95Ms:samples[11],samples});
}
measure('load-text',()=>DxfRawDocument.Load(text));measure('load-binary',()=>DxfRawDocument.Load(binary));
measure('serialize-text',()=>doc.ToBytes());measure('serialize-binary',()=>doc.ToBytes(true));
measure('index-handles',()=>DxfRawHandleIndex.Create(doc));measure('open-object-store',()=>DxfRawObjectStore.Open(doc));
measure('lookup-all-dictionary-entries',()=>{for(let i=0;i<entities;i++)if(!store.RootDictionary.Find('variable '+i))throw new Error('Missing dictionary entry.');});
measure('clone-owned-tree-and-commit',()=>{const tx=store.BeginEdit();tx.CloneDictionaryTree('10','10','Copy');tx.Commit();});
const report={runtimeFingerprint:runtimeFingerprint(),completed:true,node:process.version,v8:process.versions.v8,
  platform:process.platform,architecture:process.arch,cpu:os.cpus()[0]?.model,objects:entities,tags:tags.length,textBytes:text.length,binaryBytes:binary.length,
  warmups:3,samples:12,maxRSSKiB:process.resourceUsage().maxRSS,results,
  scope:'Raw tag/object operations. Local process timings, not a .NET speed comparison or a release performance guarantee.'};
const out=path.join(javascriptRoot,'artifacts/benchmark');fs.mkdirSync(out,{recursive:true});fs.writeFileSync(path.join(out,'results.json'),JSON.stringify(report,null,2)+'\n');
console.log(results.map(r=>`${r.name}: ${r.medianMs.toFixed(3)} ms median`).join('\n'));
