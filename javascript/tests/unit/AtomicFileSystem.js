import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { FileStream } from '../../node-entry.js';
import { DxfAtomicFile } from '../../netDxf/IO/DxfAtomicFile.js';
import { NodeFileSystem } from '../../runtime/NodeFileSystem.js';
import { SetFileSystemAdapter } from '../../runtime/FileSystem.js';
import * as E from '../../runtime/Errors.js';
import { WithAtomicDirectory, AtomicOriginal, AtomicPrepare, AtomicUnchanged, AtomicRawSource } from '../netDxf.Conformance/AtomicSaveTests.js';

function injected(method, replacement, action) {
  const previous=fs[method];fs[method]=replacement(previous);
  try{return action();}finally{fs[method]=previous;}
}
test('filesystem/partial writes are drained without exposing a prefix',()=>WithAtomicDirectory(file=>{
  AtomicPrepare(file,true);const data=new Uint8Array(10003).map((_,i)=>i);
  injected('writeSync',original=>(fd,b,o,c,p)=>original(fd,b,o,Math.min(7,c),p),()=>DxfAtomicFile.Write(file,s=>s.Write(data)));
  assert.deepEqual(new Uint8Array(fs.readFileSync(file)),data);
}));
test('filesystem/zero-progress write fails and preserves old bytes',()=>WithAtomicDirectory(file=>{
  AtomicPrepare(file,true);
  injected('writeSync',()=>()=>0,()=>assert.throws(()=>DxfAtomicFile.Write(file,s=>s.Write(Uint8Array.of(1))),E.IOException));
  AtomicUnchanged(file,true);
}));
test('filesystem/fsync failure cannot publish staged bytes',()=>WithAtomicDirectory(file=>{
  AtomicPrepare(file,true);
  injected('fsyncSync',()=>()=>{throw Object.assign(new Error('disk error'),{code:'EIO'});},
    ()=>assert.throws(()=>DxfAtomicFile.Write(file,s=>s.Write(Uint8Array.of(2))),E.IOException));
  AtomicUnchanged(file,true);
}));
test('filesystem/publication failure has no delete/copy fallback',()=>WithAtomicDirectory(file=>{
  AtomicPrepare(file,true);
  // Exercise the host contract on Windows too; patching fs.renameSync misses ReplaceFileW.
  SetFileSystemAdapter({...NodeFileSystem,Publish(){throw new E.IOException('Injected publication failure.');}});
  try{assert.throws(()=>DxfAtomicFile.Write(file,s=>s.Write(Uint8Array.of(2))),E.IOException);}
  finally{SetFileSystemAdapter(NodeFileSystem);}
  AtomicUnchanged(file,true);
}));
test('filesystem/new destination appearing at publication is not overwritten',()=>WithAtomicDirectory(file=>{
  injected('linkSync',original=>(temporary,destination)=>{fs.writeFileSync(destination,AtomicOriginal);return original(temporary,destination);},
    ()=>assert.throws(()=>DxfAtomicFile.Write(file,s=>s.Write(Uint8Array.of(2))),E.IOException));
  AtomicUnchanged(file,true);
}));
test('filesystem/unavailable hard-link publication rejects without fallback',()=>WithAtomicDirectory(file=>{
  injected('linkSync',()=>()=>{throw Object.assign(new Error('not supported'),{code:'ENOTSUP'});},
    ()=>assert.throws(()=>DxfAtomicFile.Write(file,s=>s.Write(Uint8Array.of(2))),E.IOException));
  AtomicUnchanged(file,false);
}));
test('filesystem/failure cleanup cannot conceal original exception',()=>WithAtomicDirectory(file=>{
  AtomicPrepare(file,true);const injectedError=new E.FormatException('serializer');
  injected('unlinkSync',()=>()=>{throw Object.assign(new Error('cleanup denied'),{code:'EACCES'});},
    ()=>assert.throws(()=>DxfAtomicFile.Write(file,()=>{throw injectedError;}),e=>e===injectedError));
  assert.deepEqual(new Uint8Array(fs.readFileSync(file)),AtomicOriginal);
  assert.equal(fs.readdirSync(path.dirname(file)).filter(n=>n.endsWith('.tmp')).length,1);
}));
test('filesystem/reentrant host registration does not change active commit',()=>WithAtomicDirectory(file=>{
  try {
    DxfAtomicFile.Write(file,s=>{
      SetFileSystemAdapter({...NodeFileSystem,Publish(){throw new Error('wrong host');}});
      s.Write(Uint8Array.of(5,6));
    });
    assert.deepEqual([...fs.readFileSync(file)],[5,6]);
  }finally{SetFileSystemAdapter(NodeFileSystem);}
}));
test('filesystem/asynchronous serializer cannot report premature success',()=>WithAtomicDirectory(file=>{
  AtomicPrepare(file,true);
  assert.throws(()=>DxfAtomicFile.Write(file,async s=>{s.Write(Uint8Array.of(1));}),E.ArgumentException);
  AtomicUnchanged(file,true);
}));
test('filesystem/adapter validation is explicit',()=>{
  assert.throws(()=>SetFileSystemAdapter({}),E.ArgumentException);
  assert.throws(()=>DxfAtomicFile.Write(null,()=>{}),E.ArgumentNullException);
  assert.throws(()=>DxfAtomicFile.Write('ignored',null),E.ArgumentNullException);
});
test('filesystem/portable import has no implicit Node filesystem access',()=>{
  const script=`import {DxfRawDocument,DxfTag} from './index.js';
  const raw=DxfRawDocument.Create([[0,'SECTION'],[2,'HEADER'],[9,'$ACADVER'],[1,'AC1032'],[0,'ENDSEC'],[0,'EOF']].map(([c,v])=>new DxfTag(c,v)));
  try{raw.SaveAtomic('/not-created');process.exit(3);}catch(e){if(e.name!=='NotSupportedException')throw e;}`;
  const result=spawnSync(process.execPath,['--input-type=module','-e',script],{cwd:new URL('../../',import.meta.url),encoding:'utf8'});
  assert.equal(result.status,0,result.stderr);
});
test('filesystem/stream positioning, gaps, truncation, read-only and disposal',()=>WithAtomicDirectory(file=>{
  const stream=new FileStream(file,'CreateNew','ReadWrite');
  stream.Write(Uint8Array.of(1,2,3));stream.Position=5;stream.WriteByte(9);stream.Flush(true);
  assert.equal(stream.Length,6);stream.Position=0;const bytes=new Uint8Array(6);assert.equal(stream.Read(bytes),6);
  assert.deepEqual([...bytes],[1,2,3,0,0,9]);assert.equal(stream.ReadByte(),-1);
  stream.SetLength(2);assert.equal(stream.Position,2);stream.Dispose();stream.Dispose();
  assert.equal(stream.CanRead,false);assert.equal(stream.CanWrite,false);assert.equal(stream.CanSeek,false);
  assert.throws(()=>stream.Flush(),E.ObjectDisposedException);
  const read=new FileStream(file);try{assert.equal(read.ReadByte(),1);assert.throws(()=>read.WriteByte(1),E.NotSupportedException);}finally{read.Dispose();}
}));
test('filesystem/readonly and symbolic-link targets are rejected before serialization',()=>WithAtomicDirectory(file=>{
  AtomicPrepare(file,true);const raw=AtomicRawSource(18,false);
  fs.chmodSync(file,0o444);try{assert.throws(()=>raw.SaveAtomic(file),E.UnauthorizedAccessException);}finally{fs.chmodSync(file,0o666);}
  for(const target of [file,file+'.absent']){
    const link=file+'.link';fs.symlinkSync(target,link);
    try{assert.throws(()=>raw.SaveAtomic(link),E.NotSupportedException);assert.equal(fs.readlinkSync(link),target);}finally{fs.unlinkSync(link);}
  }
  AtomicUnchanged(file,true);
}));
