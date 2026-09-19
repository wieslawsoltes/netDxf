import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import { DxfRawDocument, DxfRawOptions, FileStream } from '../node-entry.js';
import { DxfAtomicFile } from '../netDxf/IO/DxfAtomicFile.js';
import { IOException, ArgumentException } from '../runtime/Errors.js';
const originalBytes=Uint8Array.from({length:257},(_,i)=>i);
const encode=bytes=>Buffer.from(bytes).toString('base64');
const attempt=action=>{try{action();return {ok:true,error:null,param:null};}catch(e){return {ok:false,error:e.name,param:e.ParamName??null};}};
export function filesystem(input) {
  const directory=fs.mkdtempSync(path.join(os.tmpdir(),'netdxf-fs-js-')), name=path.join(directory,'Zażółć 東京 drawing.dxf');
  if(input.existing)fs.writeFileSync(name,originalBytes);
  const token={aborted:input.cancel??false},reader=input.existing?new FileStream(name):null;
  let before=null, original=null;
  try {
    const operation=attempt(()=>{
      if(input.mode==='raw') {
        let raw=DxfRawDocument.Load(Buffer.from(input.bytes,'base64'));
        if(input.maximumBytes!==undefined)raw=DxfRawDocument.Create(raw.Tags,raw.IsBinary,new DxfRawOptions(input.maximumBytes));
        if(input.binary!==undefined)raw.SaveAtomic(name,input.binary,token);else raw.SaveAtomic(name,token);
        original=raw.HasOriginalBytes;
      } else if(input.mode==='staged') {
        DxfAtomicFile.Write(name,stream=>{
          stream.Write(new Uint8Array(123456)); before=fs.existsSync(name)?encode(fs.readFileSync(name)):null;
          if(input.failure===0)throw new IOException('Injected write failure.');
          if(input.failure===1)token.aborted=true;
          else if(input.failure===2)stream.Dispose();
          else if(input.failure===3){if(input.existing)fs.unlinkSync(name);else fs.writeFileSync(name,originalBytes);}
        },token);
      } else if(input.mode==='path') {
        let target=name;
        switch(input.pathCase){
          case 'null':target=null;break;
          case 'empty':target='';break;
          case 'nul':target=name+'\0';break;
          case 'directory':target=directory;break;
          case 'missing-parent':target=path.join(directory,'missing','drawing.dxf');break;
          case 'readonly':fs.writeFileSync(name,originalBytes);fs.chmodSync(name,0o444);break;
          case 'symlink':target=name+'.link';fs.symlinkSync(name,target);break;
          case 'dangling':target=name+'.link';fs.symlinkSync(name+'.absent',target);break;
        }
        DxfAtomicFile.Write(target,s=>s.Write(Uint8Array.of(1,2,3)),token);
      } else throw new ArgumentException('Unknown filesystem operation.');
    });
    let held=null;
    if(reader){const bytes=new Uint8Array(reader.Length);let pos=0;while(pos<bytes.length){const n=reader.Read(bytes,pos,bytes.length-pos);if(!n)throw new Error('Truncated held-reader input.');pos+=n;}held=encode(bytes);}
    return {operation,exists:fs.existsSync(name),bytes:fs.existsSync(name)?encode(fs.readFileSync(name)):null,
      heldReader:held,beforePublish:before,original,temporaryCount:fs.readdirSync(directory).filter(f=>/^\.netdxf-.*\.tmp$/.test(f)).length};
  } finally {reader?.Dispose();if(fs.existsSync(name))fs.chmodSync(name,0o666);fs.rmSync(directory,{recursive:true,force:true});}
}
