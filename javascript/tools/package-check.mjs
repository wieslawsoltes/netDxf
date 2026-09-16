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
  if(!info.files.some(f=>f.path==='node.js')) throw new Error('Packed Node entry is missing.');
  if(!info.files.some(f=>f.path==='Enums.generated.js')) throw new Error('Packed enum barrel is missing.');
  if(info.files.some(f=>/^artifacts\/|^tools\/|^tests\//.test(f.path))) throw new Error('Development artifacts leaked into the runtime package.');
  const install=path.join(temp,'install');fs.mkdirSync(install);
  run(npm,['install','--offline','--ignore-scripts','--no-audit','--no-fund','--prefix',install,path.join(temp,info.filename)],install);
  const script=`import {DxfRawDocument,DxfRawObjectStore,DxfTag,Vector3,Matrix3,AciColor,ObservableCollection,DxfClass,DxfClassCollection} from '@netdxf/javascript';
    const tags=[[0,'SECTION'],[2,'HEADER'],[9,'$ACADVER'],[1,'AC1032'],[0,'ENDSEC'],[0,'EOF']].map(([c,v])=>new DxfTag(c,v));
    const tx=DxfRawObjectStore.Open(DxfRawDocument.Create(tags)).BeginEdit();
    const root=tx.EnsureRootDictionary();tx.CreateVariable(root,'Test','Zażółć 東京');
    const result=DxfRawObjectStore.Open(DxfRawDocument.Load(tx.Commit().ToBytes(true)));
    if(result.Get(result.RootDictionary.Find('test').Handle).Value!=='Zażółć 東京')throw new Error('Packed round trip failed');
    if(Vector3.Normalize(new Vector3(3,4,0)).X!==0.6000000000000001)throw new Error('Packed geometry failed');
    const classes=new DxfClassCollection();classes.Add(new DxfClass('Name','Cpp','App'));
    if(classes.get_Item('Name').CppClassName!=='Cpp')throw new Error('Packed classes failed');
    const collection=new ObservableCollection();collection.Add(3);collection.Insert(0,2);
    if(collection.get_Item(0)!==2)throw new Error('Packed collection failed');`;
  run(process.execPath,['--input-type=module','-e',script],install);
  run(process.execPath,['--input-type=module','-e',`import fs from 'node:fs';
    import {DxfRawDocument,DxfTag,FileStream,UnitHelper,XDataRecord,XDataCode} from '@netdxf/javascript/node';
    const tags=[[0,'SECTION'],[2,'HEADER'],[9,'$ACADVER'],[1,'AC1032'],[0,'ENDSEC'],[0,'EOF']].map(([c,v])=>new DxfTag(c,v));
    const raw=DxfRawDocument.Create(tags);raw.SaveAtomic('packed.dxf');
    const stream=new FileStream('packed.dxf');try{if(DxfRawDocument.Load(stream).Version!==18)throw new Error('Atomic packed read failed');}finally{stream.Dispose();fs.unlinkSync('packed.dxf');}
    if(typeof UnitHelper.ConversionFactor!=='function'||new XDataRecord(XDataCode.Int16,12).Value!==12)throw new Error('Recovered exports missing');`],install);
  const dir=path.join(javascriptRoot,'artifacts/package');fs.mkdirSync(dir,{recursive:true});
  fs.writeFileSync(path.join(dir,'results.json'),JSON.stringify({...proof,completed:true,private:true,files:info.files.length,packedBytes:info.size,unpackedBytes:info.unpackedSize},null,2)+'\n');
  console.log(`Packed-package offline import and binary object round trip passed (${info.files.length} files).`);
} finally {fs.rmSync(temp,{recursive:true,force:true});}
