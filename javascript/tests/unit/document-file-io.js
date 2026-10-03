import { DxfReader } from '../../netDxf/IO/DxfReader.js';
import { DxfWriter } from '../../netDxf/IO/DxfWriter.js';
import { BinaryCodeValueWriter } from '../../netDxf/IO/BinaryCodeValueWriter.js';
import { TextCodeValueWriter } from '../../netDxf/IO/TextCodeValueWriter.js';
import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import * as api from '../../node-entry.js';
import { OperationCanceledException } from '../../runtime/Errors.js';
const all=doc=>Array.from(doc.Entities.All);
function directory(action){const dir=fs.mkdtempSync(path.join(os.tmpdir(),'netdxf-typed-'));try{return action(dir);}finally{fs.rmSync(dir,{recursive:true,force:true});}}
function invalidDocument(){const doc=new api.DxfDocument(18);doc.Entities.Add(new api.Line());// Inject a serialization exception through the actual comment enumerator, after writer preprocessing.
doc.Comments[Symbol.iterator]=function*(){yield 'first comment';throw new Error('injected comment enumeration failure');};return doc;}
for(const profile of ['Debug','Release'])for(const binary of [false,true])test(`typed file Load/Save/SaveAtomic ${profile} ${binary}`,()=>directory(dir=>{
 const old=api.SetTypedIOConfiguration(profile);try{const doc=new api.DxfDocument(18);doc.Entities.Add(new api.Line());const file=path.join(dir,'drawing.dxf');assert.equal(doc.Save(file,binary),true);assert.equal(doc.Name,'drawing');assert.equal(doc.SupportFolders.WorkingFolder,dir);assert.equal(all(api.DxfDocument.Load(file)).length,1);doc.Entities.Add(new api.Circle());doc.SaveAtomic(file,binary);assert.equal(all(api.DxfDocument.Load(file)).length,2);assert.deepEqual(fs.readdirSync(dir),['drawing.dxf']);}finally{api.SetTypedIOConfiguration(old);}
}));
for(const profile of ['Debug','Release'])for(const existed of [false,true])test(`typed atomic serialization failure restores path and preserves destination ${profile} ${existed}`,()=>directory(dir=>{
 const old=api.SetTypedIOConfiguration(profile);try{const file=path.join(dir,'drawing.dxf'),doc=invalidDocument();doc.Name='Before';doc.SupportFolders.WorkingFolder=dir;if(existed)fs.writeFileSync(file,'ORIGINAL');assert.throws(()=>doc.SaveAtomic(file));assert.equal(doc.Name,'Before');assert.equal(doc.SupportFolders.WorkingFolder,dir);assert.equal(fs.existsSync(file),existed);if(existed)assert.equal(fs.readFileSync(file,'utf8'),'ORIGINAL');assert.deepEqual(fs.readdirSync(dir),existed?['drawing.dxf']:[]);assert.equal(doc.Layouts.Count,2,'Serialization side effects are not rolled back by SaveAtomic.');}finally{api.SetTypedIOConfiguration(old);}
}));
for(const binary of [false,true])for(const existed of [false,true])for(const at of [1,2,3,4])test(`typed atomic cancellation checkpoint ${at} ${binary} ${existed}`,()=>directory(dir=>{
 const file=path.join(dir,'drawing.dxf'),doc=new api.DxfDocument(18);doc.Entities.Add(new api.Line());doc.Name='Before';if(existed)fs.writeFileSync(file,'ORIGINAL');let calls=0;const token={ThrowIfCancellationRequested(){if(++calls===at)throw new OperationCanceledException();}};assert.throws(()=>doc.SaveAtomic(file,binary,token),{name:'OperationCanceledException'});assert.equal(calls,at);assert.equal(doc.Name,'Before');assert.equal(fs.existsSync(file),existed);if(existed)assert.equal(fs.readFileSync(file,'utf8'),'ORIGINAL');assert.deepEqual(fs.readdirSync(dir),existed?['drawing.dxf']:[]);
}));
for(const profile of ['Debug','Release'])test(`typed borrowed streams and ordinary Save failures ${profile}`,()=>{
 const old=api.SetTypedIOConfiguration(profile);try{const output=new api.MemoryStream(),doc=invalidDocument();if(profile==='Debug')assert.throws(()=>doc.SaveStream(output));else assert.equal(doc.SaveStream(output),false);assert.equal(output.CanWrite,true);const input=new api.MemoryStream(new TextEncoder().encode('not DXF'));assert.throws(()=>api.DxfDocument.LoadStream(input),{name:'DxfVersionNotSupportedException',Version:api.DxfVersion.Unknown});assert.equal(input.CanRead,true);assert.throws(()=>api.DxfDocument.LoadFile('/missing-netdxf-parent/file.dxf'));}finally{api.SetTypedIOConfiguration(old);}
});
test('typed file creation errors propagate even in Release',()=>directory(dir=>{const old=api.SetTypedIOConfiguration('Release');try{assert.throws(()=>new api.DxfDocument().SaveFile(path.join(dir,'absent','file.dxf')));}finally{api.SetTypedIOConfiguration(old);}}));
test('typed build-profile adapter rejects invalid values without replacing the previous setting',()=>{const old=api.GetTypedIOConfiguration();assert.throws(()=>api.SetTypedIOConfiguration('Other'),{name:'ArgumentException'});assert.equal(api.GetTypedIOConfiguration(),old);});

// The native reader checks the declared header version before choosing a body decoder.
for(const profile of ['Debug','Release'])for(const binary of [false,true])for(const code of ['AC1009','AC1012','AC1014','UNKNOWN'])
test(`typed version preflight propagates ${code} in ${profile}/${binary}`,()=>{
  const previous=api.SetTypedIOConfiguration(profile);
  try {
    const output=new api.MemoryStream(),writer=binary?new BinaryCodeValueWriter(output):new TextCodeValueWriter(output);
    for(const [group,value]of[[0,'SECTION'],[2,'HEADER'],[9,'$ACADVER'],[1,code]])writer.Write(group,value);
    writer.Flush();output.Position=0;
    assert.throws(()=>api.DxfDocument.LoadStream(output),{name:'DxfVersionNotSupportedException'});
    assert.equal(output.Position,0,'Version probe restores the entry position before refusal.');assert.equal(output.CanRead,true);
  } finally {api.SetTypedIOConfiguration(previous);}
});

for(const binary of [false,true])test(`typed reader byte-buffer convenience input preserves version preflight ${binary}`,()=>{
  const document=new api.DxfDocument(api.DxfVersion.AutoCad2018),output=new api.MemoryStream();
  document.Entities.Add(new api.Line(new api.Vector3(1,2,3),new api.Vector3(4,5,6)));
  new DxfWriter().Write(output,document,binary);
  const bytes=output.ToArray(),before=bytes.slice(),loaded=new DxfReader().Read(bytes);
  assert.equal(all(loaded).length,1);assert.equal(all(loaded)[0].StartPoint.X,1);
  assert.deepEqual(bytes,before,'Reading a caller buffer does not mutate it.');
  assert.equal(output.CanWrite,true,'Unrelated output remains caller-owned.');
});
