// Explicit synchronous Node filesystem adapters. Portable document modules do not import Node.
import fs from 'node:fs';
import path from 'node:path';
import { FileStream, FullPath, FileError } from './NodeFileStream.js';
import { DecodePatternText } from './NodePatternFileSystem.js';
import { Encoding } from './Encoding.js';
import { ArgumentNullException, ArgumentException } from './Errors.js';
const windows=process.platform==='win32',separator=value=>value==='/'||value==='\\';
function resolve(value,base){
  if(value==null)throw new ArgumentNullException('path');
  if(value.includes('\0'))throw new ArgumentException('A path cannot contain NUL.','path');
  if(value.length===0)return base;
  if(!windows)return path.resolve(base,value);
  const rooted=separator(value[0]),drive=/^[a-z]:/i.test(value),device=/^[\\/]{2}[?.][\\/]/.test(base);
  if(rooted&&value.length>=2&&separator(value[1])||drive&&value.length>=3&&separator(value[2]))return path.resolve(value);
  const root=path.parse(base).root;let combined;
  if(rooted)combined=path.join(root,value.slice(1));
  else if(drive){const offset=device?4:0,same=root.length>offset+1&&root[offset+1]===':'&&root[offset].toUpperCase()===value[0].toUpperCase();
    combined=same?path.join(base,value.slice(2)):(device?base.slice(0,4):'')+value.slice(0,2)+'\\'+value.slice(2);
  }else combined=path.join(base,value);
  return path.normalize(combined);
}
export const NodeSupportFolders=Object.freeze({
  CurrentDirectory:()=>process.cwd(),FullPath,Resolve:resolve,
  Exists(file){try{return fs.statSync(file).isFile();}catch{return false;}},
  FileName(file){return (windows?/[\\/]$/:/\/$/).test(file)?'':path.basename(file);},
  Combine:(folder,name)=>path.join(folder,name)
});
export const NodeLayerStateFiles=Object.freeze({
  OpenRead(file){const stream=new FileStream(file);return{ReadAll(){const bytes=new Uint8Array(stream.Length);let at=0;while(at<bytes.length){const count=stream.Read(bytes,at,bytes.length-at);if(count===0)break;at+=count;}return DecodePatternText(bytes.subarray(0,at));},Close:()=>stream.Close()};},
  Create(file){const stream=new FileStream(file,'Create','Write');return{NewLine:windows?'\r\n':'\n',Write:text=>stream.Write(Encoding.UTF8.GetBytes(text)),Close:()=>stream.Close()};}
});
export function DeleteLinetypeFile(file){const name=FullPath(file);try{fs.unlinkSync(name);}catch(error){if(error.code!=='ENOENT')throw FileError(error,name);}}
