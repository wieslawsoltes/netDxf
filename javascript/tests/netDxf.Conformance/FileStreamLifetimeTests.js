// Complete port of pinned FileStreamLifetimeTests.cs. Linux checks real open descriptors.
// Node has no NoGCRegion API; no explicit GC/finalizer is used before any assertion.
import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import { DxfDocument, DxfClass, DxfVersion, Line, Vector3, MemoryStream, FileStream, DxfVersionNotSupportedException } from '../../node-entry.js';
import { GetTypedIOConfiguration } from '../../runtime/TypedDocumentIO.js';
import { NewCodeWriter } from '../support/CodecFactory.js';
import * as E from '../../runtime/Errors.js';
import { Run, Check, Equal, Throws, SupportedVersions, VersionName, BooleanName } from './TestHarness.js';
const Single=items=>{const rows=Array.from(items);Equal(1,rows.length,'Expected exactly one item');return rows[0];};
export function RegisterFileStreamLifetimeTests() {
  for(const v of SupportedVersions)for(const b of [false,true]) {
    const suffix=VersionName(v)+'/'+BooleanName(b);
    Run('file-lifetime/success/'+suffix,()=>FileLifetimeSuccess(v,b));
    Run('file-lifetime/setup-null/'+suffix,()=>FileLifetimeSetup(v,b,false));
    Run('file-lifetime/setup-enumeration/'+suffix,()=>FileLifetimeSetup(v,b,true));
    Run('file-lifetime/malformed-load/'+suffix,()=>FileLifetimeMalformed(v,b));
    Run('file-lifetime/failed-save/'+suffix,()=>FileLifetimeFailedSave(v,b));
  }
  for(const b of [false,true]) {
    Run('file-lifetime/unsupported-version/'+BooleanName(b),()=>FileLifetimeUnsupported(b));
    Run('file-lifetime/caller-streams/'+BooleanName(b),()=>FileLifetimeCallerStreams(b));
  }
  Run('file-lifetime/open-failures',FileLifetimeOpenFailures);
}
export function FileLifetimeFixture(version,binary) {
  const doc=new DxfDocument(version);doc.Entities.Add(new Line(new Vector3(1,2,3),new Vector3(4,5,6)));
  const output=new MemoryStream();Check(doc.Save(output,binary),'Lifetime fixture save failed.');const data=output.ToArray();output.Dispose();return data;
}
export function WithLifetimeFile(action) {
  const directory=fs.mkdtempSync(path.join(os.tmpdir(),'netDxf-lifetime-')),file=path.join(directory,'drawing.dxf');
  try {action(file);CheckLifetimeFileReleased(file);}finally{fs.rmSync(directory,{recursive:true,force:true});}
}
export function CheckLifetimeFileReleased(file) {
  if(!fs.existsSync(file))return;
  if(process.platform==='linux')for(const fd of fs.readdirSync('/proc/self/fd')) {
    let target;try{target=fs.readlinkSync('/proc/self/fd/'+fd);}catch(error){if(error.code==='ENOENT')continue;throw error;}
    Check(target!==file,'The file overload left an open descriptor: '+file);
  }
  const stream=new FileStream(file,'Open','ReadWrite','None');stream.Dispose();
}
export function FileLifetimeSuccess(version,binary) {WithLifetimeFile(file=>{
  fs.writeFileSync(file,FileLifetimeFixture(version,binary));const directory=path.dirname(file),doc=DxfDocument.Load(file,[directory]);Check(doc!==null,'File load failed.');
  Equal('drawing',doc.Name,'Loaded file name');Equal(directory,doc.SupportFolders.WorkingFolder,'Loaded working folder');Check(new Vector3(1,2,3).Equals(Single(doc.Entities.Lines).StartPoint),'Loaded geometry');CheckLifetimeFileReleased(file);
  const detected={};Equal(version,DxfDocument.CheckDxfFileVersion(file,detected),'File probe version');Equal(binary,detected.value,'File probe transport');CheckLifetimeFileReleased(file);
  Check(doc.Save(file,!binary),'File save failed.');Equal('drawing',doc.Name,'Saved file name');Equal(directory,doc.SupportFolders.WorkingFolder,'Saved working folder');CheckLifetimeFileReleased(file);
  Equal(version,DxfDocument.CheckDxfFileVersion(file),'Version-only file probe');const loaded=DxfDocument.Load(file);Check(loaded!==null,'Saved file reload failed.');Check(new Vector3(4,5,6).Equals(Single(loaded.Entities.Lines).EndPoint),'Reloaded geometry');
});}
export function* ThrowingSupportFolders(){yield 'a valid first folder';throw new E.InvalidOperationException('Injected support-folder enumeration failure.');}
export function FileLifetimeSetup(version,binary,enumeration){WithLifetimeFile(file=>{
  fs.writeFileSync(file,FileLifetimeFixture(version,binary));Throws(enumeration?E.InvalidOperationException:E.ArgumentNullException,()=>DxfDocument.Load(file,enumeration?ThrowingSupportFolders():null));
});}
export function FileLifetimeMalformed(version,binary){WithLifetimeFile(file=>{
  const bytes=FileLifetimeFixture(version,binary);
  if(binary){const eof=Uint8Array.of(0,0,69,79,70,0);Equal(eof,bytes.slice(-eof.length),'Binary fixture EOF framing changed.');fs.writeFileSync(file,bytes.slice(0,-eof.length));}
  else {const lines=Buffer.from(bytes).toString('utf8').replace(/^\uFEFF/,'').replace(/\r\n/g,'\n').replace(/\n+$/,'').split('\n');Equal('0',lines.at(-2).trim(),'Text fixture EOF group code');Equal('EOF',lines.at(-1),'Text fixture EOF value');fs.writeFileSync(file,lines.slice(0,-2).join('\n')+'\n');}
  if(GetTypedIOConfiguration()==='Debug')Throws(E.EndOfStreamException,()=>DxfDocument.Load(file));else Check(DxfDocument.Load(file)===null,'Malformed file was accepted.');
});}
export function FileLifetimeConflictingDocument(version){const doc=new DxfDocument(version);doc.Classes.Add(new DxfClass('RASTERVARIABLES','WrongCpp','Lifetime test'));return doc;}
export function FileLifetimeFailedSave(version,binary){WithLifetimeFile(file=>{
  fs.writeFileSync(file,'previous file contents');const doc=FileLifetimeConflictingDocument(version);
  if(GetTypedIOConfiguration()==='Debug')Throws(E.InvalidDataException,()=>doc.Save(file,binary));else Check(!doc.Save(file,binary),'Conflicting file export unexpectedly succeeded.');
  Equal(0,fs.statSync(file).size,'File.Create truncation policy changed');
});}
export function FileLifetimeUnsupported(binary){WithLifetimeFile(file=>{
  const fixture=new MemoryStream(),writer=NewCodeWriter(fixture,binary);
  for(const[code,value]of[[0,'SECTION'],[2,'HEADER'],[9,'$ACADVER'],[1,'AC1009'],[0,'ENDSEC'],[0,'EOF']])writer.Write(code,value);
  writer.Flush();fs.writeFileSync(file,fixture.ToArray());Throws(DxfVersionNotSupportedException,()=>DxfDocument.Load(file));fixture.Dispose();
});}
export function FileLifetimeCallerStreams(binary){
  const input=new MemoryStream(FileLifetimeFixture(DxfVersion.AutoCad2018,binary)),doc=DxfDocument.Load(input);Check(doc!==null,'Caller input failed.');Check(input.CanRead,'File lifetime change closed caller input.');
  const output=new MemoryStream();Check(doc.Save(output,binary),'Caller output failed.');Check(output.CanWrite,'File lifetime change closed caller output.');const failed=new MemoryStream();
  if(GetTypedIOConfiguration()==='Debug')Throws(E.InvalidDataException,()=>FileLifetimeConflictingDocument(DxfVersion.AutoCad2018).Save(failed,binary));else Check(!FileLifetimeConflictingDocument(DxfVersion.AutoCad2018).Save(failed,binary),'Invalid caller save succeeded.');
  Check(failed.CanWrite,'Failed save closed caller output.');input.Dispose();output.Dispose();failed.Dispose();
}
export function FileLifetimeOpenFailures(){WithLifetimeFile(file=>{
  Throws(E.FileNotFoundException,()=>DxfDocument.Load(file));Throws(E.FileNotFoundException,()=>DxfDocument.CheckDxfFileVersion(file));Throws(E.DirectoryNotFoundException,()=>new DxfDocument().Save(path.join(file,'missing','drawing.dxf')));
});}
