// Port of RawLegacyProfileTests.cs. RawLegacyTypedBoundary awaits the typed JavaScript engine.
import fs from 'node:fs';
import path from 'node:path';
import { createHash } from 'node:crypto';
import { sourceRoot, javascriptRoot } from '../../tools/dotnet.mjs';
import { DxfTag, DxfRawDocument, DxfRawOptions, DxfVersion, DxfVersionNotSupportedException, MemoryStream } from '../../index.js';
import { Encoding } from '../../runtime/Encoding.js';
import * as E from '../../runtime/Errors.js';
import { Run, Check, Equal, Throws, VersionName, HeaderVersion, BooleanName } from './TestHarness.js';
import { RawFixtureBytes, LoadRaw, SaveRaw, SameRawTags } from './RawDocumentTests.js';
import { CheckRawRecordPartition, AssertOutsideRecordUnchanged } from './RawRecordTests.js';
const T=(code,value)=>new DxfTag(code,value);
export const LegacyFixtures=Object.freeze([
  ['small_r13.dxf',DxfVersion.AutoCad13,932,1502,'a1007452f8ada2e71fdef60f79923924045cb371f37eeab896a61f5628573d0b'],
  ['small_r14.dxf',DxfVersion.AutoCad14,1252,789,'92e792f8f9169e4226c9b7dbe848ba9afb96d3e388e6297c55481e1941c65fb0'],
  ['bin_dxf_r13.dxf',DxfVersion.AutoCad13,1252,2075,'c65782683815ff0f1680d26207da1637a486f96b30bc215685e26cf7adee8464'],
  ['bin_dxf_r14.dxf',DxfVersion.AutoCad14,1252,2083,'177a97e5dc86ca106d0dd6365d139122fc54ff78df87670847eb7174a8bbd5eb'],
].map(Object.freeze));
const WriteArtifact=(name,bytes)=>{const dir=process.env.DXF_JS_TEST_ARTIFACTS||path.join(javascriptRoot,'artifacts/conformance');fs.mkdirSync(dir,{recursive:true});fs.writeFileSync(path.join(dir,name),bytes);};
export function RegisterRawLegacyProfileTests(){
  for(const f of LegacyFixtures)Run(`raw-legacy/external/${f[0]}`,()=>RawLegacyExternal(f));
  for(const v of [DxfVersion.AutoCad13,DxfVersion.AutoCad14])for(const b of [false,true]){
    Run(`raw-legacy/authored/${VersionName(v)}/${BooleanName(b)}`,()=>RawLegacyAuthored(v,b));
    Run(`raw-legacy/invalid-and-bounded/${VersionName(v)}/${BooleanName(b)}`,()=>RawLegacyBoundaries(v,b));
  }
  for(const v of [DxfVersion.AutoCad13,DxfVersion.AutoCad14,DxfVersion.AutoCad2000,DxfVersion.AutoCad2004])
    for(const b of [false,true])for(const p of [['dos437',437,'é'],['DoS850',850,'é'],['dos932',932,'東京']])
      Run(`raw-legacy/dos-codepage/${VersionName(v)}/${BooleanName(b)}/${p[0]}`,()=>RawLegacyEncoding(v,b,...p));
  Run('raw-legacy/invalid-codepage-aliases',RawLegacyBadAliases);
}
export function RawLegacyTags(version,codePage='ANSI_1252',text='legacy text'){
  return [T(0,'SECTION'),T(2,'HEADER'),T(9,'$ACADVER'),T(1,HeaderVersion(version)),T(9,'$DWGCODEPAGE'),T(3,codePage),T(0,'ENDSEC'),
    T(0,'SECTION'),T(2,'ENTITIES'),T(0,'LINE'),T(5,'A'),T(100,'AcDbEntity'),T(8,'0'),T(100,'AcDbLine'),
    T(10,1e-20),T(20,-0),T(30,3),T(11,4),T(21,5),T(31,6),T(1001,'RAW_TEST'),T(1000,text),T(1004,Uint8Array.of(0,128,255)),
    T(1005,'B'),T(1070,-1),T(1071,-2147483648),T(0,'ENDSEC'),T(0,'EOF')];
}
export function RawLegacyExternal([file,version,codePage,count,hash]){
  const bytes=new Uint8Array(fs.readFileSync(path.join(sourceRoot,'tests/fixtures/legacy',file)));
  Equal(hash,createHash('sha256').update(bytes).digest('hex'));
  const raw=LoadRaw(bytes);Equal(version,raw.Version);Equal(codePage,raw.EncodingCodePage);Equal(count,raw.Tags.Count);
  Equal(file.startsWith('bin_'),raw.IsBinary);Equal(bytes,SaveRaw(raw));
  SameRawTags(raw.Tags,LoadRaw(SaveRaw(raw.WithTags(raw.Tags))).Tags);SameRawTags(raw.Tags,LoadRaw(SaveRaw(raw,!raw.IsBinary)).Tags);
  CheckRawRecordPartition(raw);
  const layer=raw.Sections.find(s=>s.Name==='TABLES').Records.find(r=>r.Name==='LAYER');Check([...layer.Content].some(t=>t.Code===62));
  const replacement=[...layer.Tags].map(t=>t.Code===62?T(62,3):t),edited=raw.WithRecord(layer,replacement);
  AssertOutsideRecordUnchanged(raw,layer,edited,replacement.length);SameRawTags(edited.Tags,LoadRaw(SaveRaw(edited,!raw.IsBinary)).Tags);
  WriteArtifact('legacy-normalized-'+file,SaveRaw(raw.WithTags(raw.Tags),false));WriteArtifact('legacy-edited-'+file,SaveRaw(edited,false));
}
export function RawLegacyAuthored(version,binary){
  const tags=RawLegacyTags(version),raw=LoadRaw(RawFixtureBytes(tags,binary));Equal(version,raw.Version);SameRawTags(tags,raw.Tags);
  SameRawTags(tags,LoadRaw(SaveRaw(DxfRawDocument.Create(tags,binary),!binary)).Tags);
  SameRawTags(tags,LoadRaw(SaveRaw(raw.WithTags(raw.Tags))).Tags);CheckRawRecordPartition(raw);
}
export function RawLegacyBoundaries(version,binary){
  const tags=RawLegacyTags(version),bytes=RawFixtureBytes(tags,binary);
  Throws(E.InvalidDataException,()=>LoadRaw(bytes,new DxfRawOptions(bytes.length-1)));
  Throws(E.InvalidDataException,()=>LoadRaw(bytes,new DxfRawOptions(undefined,tags.length-1)));
  Throws(E.InvalidDataException,()=>LoadRaw(bytes,new DxfRawOptions(undefined,undefined,9)));
  const raw=LoadRaw(bytes);Throws(E.NotSupportedException,()=>raw.WithTags(tags.map(t=>t.Code===1?T(1,'AC1015'):t)));
  const cancelled=new MemoryStream(bytes);Throws(E.OperationCanceledException,()=>DxfRawDocument.Load(cancelled,null,{aborted:true}));Check(cancelled.CanRead);
  for(const version of ['AC1006','AC1034','VENDOR_UNKNOWN'])
    Throws(DxfVersionNotSupportedException,()=>LoadRaw(RawFixtureBytes(tags.map(t=>t.Code===1?T(1,version):t),binary)));
  Throws(E.EndOfStreamException,()=>LoadRaw(RawFixtureBytes(tags.slice(0,-1),binary)));
}
export function RawLegacyEncoding(version,binary,name,codePage,text){
  const tags=RawLegacyTags(version,name,text),raw=LoadRaw(RawFixtureBytes(tags,binary,Encoding.GetEncoding(codePage)));
  Equal(codePage,raw.EncodingCodePage);SameRawTags(tags,raw.Tags);SameRawTags(tags,LoadRaw(SaveRaw(raw.WithTags(raw.Tags),!binary)).Tags);
  Equal(name,raw.Tags.find(t=>t.Code===3).Value);
}
export function RawLegacyBadAliases(){
  for(const name of ['dos','dos_932','dos+932','dos 932','dos932 ','dos99999999999','UTF-16','ANSI_',
    'dos1200','dos1201','dos12000','dos12001'])
    Throws(E.NotSupportedException,()=>DxfRawDocument.Create(RawLegacyTags(DxfVersion.AutoCad13,name)));
}
