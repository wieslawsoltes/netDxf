import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { javascriptRoot } from './dotnet.mjs';
import { runtimeFingerprint, verificationFingerprint } from './evidence.mjs';
const proof={runtimeFingerprint:runtimeFingerprint(),verificationFingerprint:verificationFingerprint()};
const temp=fs.mkdtempSync(path.join(os.tmpdir(),'netdxf-package-'));
const npm=process.platform==='win32'?'npm.cmd':'npm';
function run(command,args,cwd){const r=spawnSync(command,args,{cwd,encoding:'utf8',shell:process.platform==='win32' && command===npm});if(r.status!==0)throw r.error||new Error(r.stdout+'\n'+r.stderr);return r.stdout;}
try {
  const [info]=JSON.parse(run(npm,['pack','--ignore-scripts','--json','--pack-destination',temp],javascriptRoot));
  if(!info.files.some(f=>f.path==='node-entry.js')) throw new Error('Packed Node entry is missing.');
  if(!info.files.some(f=>f.path==='Enums.generated.js')) throw new Error('Packed enum barrel is missing.');
  if(info.files.some(f=>/^artifacts\/|^tools\/|^tests\//.test(f.path) && !['tools/ReferenceMath/generate.py','tools/ReferenceMath/source-manifest.json','tools/ReferenceMath/generate-exp-log.py','tools/ReferenceMath/exp-log-manifest.json','tools/ReferenceMath/exp-log.template.js'].includes(f.path))) throw new Error('Development artifacts leaked into the runtime package.');
  for(const file of ['netDxf/GTE/LICENSE.BSL-1.0','THIRD_PARTY_NOTICES.md','runtime/reference-math/LICENSE.LGPL-2.1','runtime/reference-math/LICENSE.GPL-2','third_party/glibc-math/COPYING.LIB','third_party/glibc-math/sysdeps/ieee754/dbl-64/dla.h','tools/ReferenceMath/generate.py','tools/ReferenceMath/source-manifest.json','tools/ReferenceMath/generate-exp-log.py','tools/ReferenceMath/exp-log-manifest.json','tools/ReferenceMath/exp-log.template.js','runtime/reference-math/exp-log.js','runtime/reference-math/exp-log-data.js','runtime/reference-math/exp-log-source.json'])
    if(!info.files.some(f=>f.path===file)) throw new Error('Required mathematical source/notice is missing: '+file);
  const install=path.join(temp,'install');fs.mkdirSync(install);
  run(npm,['install','--offline','--ignore-scripts','--no-audit','--no-fund','--prefix',install,path.join(temp,info.filename)],install);
  const script=fs.readFileSync(new URL('./packed-core-smoke.mjs',import.meta.url),'utf8')+'\n'+fs.readFileSync(new URL('./packed-header-smoke.mjs',import.meta.url),'utf8');
  run(process.env.PYTHON||'python',['tools/ReferenceMath/generate-exp-log.py','--check'],path.join(install,'node_modules','@netdxf','javascript'));
  run(process.execPath,['--input-type=module','-e',script],install);
  run(process.execPath,['--input-type=module','-e',`import fs from 'node:fs';
    import {DxfRawDocument,DxfTag,FileStream,UnitHelper,XDataRecord,XDataCode} from '@netdxf/javascript/node';
    const tags=[[0,'SECTION'],[2,'HEADER'],[9,'$ACADVER'],[1,'AC1032'],[0,'ENDSEC'],[0,'EOF']].map(([c,v])=>new DxfTag(c,v));
    const raw=DxfRawDocument.Create(tags);raw.SaveAtomic('packed.dxf');
    const stream=new FileStream('packed.dxf');try{raw.SaveAtomic('packed.dxf');if(DxfRawDocument.Load(stream).Version!==18)throw new Error('Atomic packed read failed');}finally{stream.Dispose();fs.unlinkSync('packed.dxf');}
    if(typeof UnitHelper.ConversionFactor!=='function'||new XDataRecord(XDataCode.Int16,12).Value!==12)throw new Error('Recovered exports missing');`],install);
  const dir=path.join(javascriptRoot,'artifacts/package');fs.mkdirSync(dir,{recursive:true});
  fs.writeFileSync(path.join(dir,'results.json'),JSON.stringify({...proof,completed:true,private:true,files:info.files.length,packedBytes:info.size,unpackedBytes:info.unpackedSize},null,2)+'\n');
  console.log(`Packed-package offline import and binary object round trip passed (${info.files.length} files).`);
} finally {fs.rmSync(temp,{recursive:true,force:true});}
