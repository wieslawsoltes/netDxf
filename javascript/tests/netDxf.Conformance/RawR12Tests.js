// Port of RawR12Tests.cs. RawR12TypedBoundary awaits the typed JavaScript engine.
import fs from 'node:fs';
import path from 'node:path';
import { createHash } from 'node:crypto';
import { sourceRoot, javascriptRoot } from '../../tools/dotnet.mjs';
import { DxfTag, DxfRawDocument, DxfRawOptions, DxfVersion, DxfGroupCode, MemoryStream } from '../../index.js';
import { Encoding } from '../../runtime/Encoding.js';
import * as E from '../../runtime/Errors.js';
import { Run, Check, Equal, Throws, BooleanName } from './TestHarness.js';
import { RawFixtureBytes, RawNonseekableStream, LoadRaw, SaveRaw, SameRawTags } from './RawDocumentTests.js';
import { CheckRawRecordPartition, AssertOutsideRecordUnchanged } from './RawRecordTests.js';
import { RawTagSample } from './RawTagTests.js';
const T=(code,value)=>new DxfTag(code,value);
const WriteArtifact=(name,bytes)=>{const dir=process.env.DXF_JS_TEST_ARTIFACTS||path.join(javascriptRoot,'artifacts/conformance');fs.mkdirSync(dir,{recursive:true});fs.writeFileSync(path.join(dir,name),bytes);};
export function RegisterRawR12Tests(){
  for(const f of [['ASCII_R12.dxf','b476d3e53fe24c1db3c701d20b2bebd774f7bd7966b12d81891505b9b29e4d21'],
    ['bin_dxf_r12.dxf','1e7a904d67bc7036bd3f78a6273cdf28124ba7e8b731604c0593288182f2aa0e']])Run('raw-r12/external/'+f[0],()=>RawR12External(...f));
  for(const b of [false,true]){
    for(const [name,action] of [['authored',RawR12Authored],['budgets-cancellation-offsets',RawR12Limits],['dimstyle-names',RawR12DimensionStyle],['header-not-first',RawR12HeaderNotFirst]])
      Run(`raw-r12/${name}/${BooleanName(b)}`,()=>action(b));
    for(const c of [['ANSI_1250',1250,'Zażółć'],['dos932',932,'東京']])Run(`raw-r12/codepage/${BooleanName(b)}/${c[0]}`,()=>RawR12Encoding(b,...c));
  }
  for(const c of [1000,1001,1002,1003,1004,1005,1010,1020,1030,1040,1041,1042,1070,1071])Run(`raw-r12/escaped-code/${c}`,()=>RawR12EscapedCode(c));
  for(const n of [0,1,2,126,127,128,255])Run(`raw-r12/chunk-framing/${n}`,()=>RawR12Chunks(n));
  for(const n of [0,1,2])Run(`raw-r12/truncated-escape/${n}`,()=>RawR12TruncatedEscape(n));
  for(const c of [-1,0,1,254])Run(`raw-r12/noncanonical-escape/${c}`,()=>RawR12BadEscape(c));
  Run('raw-r12/version-framing-consistency',RawR12FramingMismatch);Run('raw-r12/comments-explicit-removal',RawR12Comments);Run('raw-r12/trailing-data',RawR12Trailing);
}
export function RawR12Tags(){
  return [T(0,'SECTION'),T(2,'HEADER'),T(9,'$ACADVER'),T(1,'AC1009'),T(9,'$DWGCODEPAGE'),T(3,'ANSI_1252'),T(0,'ENDSEC'),
    T(0,'SECTION'),T(2,'ENTITIES'),T(0,'LINE'),T(5,'A'),T(8,'0'),T(10,1e-20),T(20,-0),T(30,0),T(11,1),T(21,2),T(31,3),
    T(1001,'R12_TEST'),T(1000,'AC1032 $ACADVER SECTION EOF'),T(1002,'{'),T(1004,Uint8Array.of(0,255,1,255,0)),T(1005,'FFFFFFFFFFFFFFFF'),
    T(1040,Number.MIN_VALUE),T(1070,-32768),T(1071,-2147483648),T(1002,'}'),T(0,'ENDSEC'),T(0,'EOF')];
}
export function RawR12Bytes(tags,binary=true,encoding=Encoding.UTF8){
  // Reuse the independent fixture encoder, not the production codecs.
  return RawFixtureBytes([...tags],binary,encoding,'\n',true);
}
export function RawR12External(name,hash){
  const bytes=new Uint8Array(fs.readFileSync(path.join(sourceRoot,'tests/fixtures/legacy',name)));Equal(hash,createHash('sha256').update(bytes).digest('hex'));
  const raw=LoadRaw(bytes);Equal(DxfVersion.AutoCad12,raw.Version);Equal(name.startsWith('bin_'),raw.IsBinary);Equal(bytes,SaveRaw(raw));
  for(const binary of [false,true]){const normalized=SaveRaw(raw.WithTags(raw.Tags),binary);SameRawTags(raw.Tags,LoadRaw(normalized).Tags);WriteArtifact(`r12-normalized-${BooleanName(binary)}-${name}`,normalized);}
  CheckRawRecordPartition(raw);
  const line=raw.Sections.find(s=>s.Name==='ENTITIES').Records.find(r=>r.Name==='LINE');
  const replacement=[...line.Tags].map(t=>t.Code===10?T(10,12.345678901234567):t),edited=raw.WithRecord(line,replacement);
  AssertOutsideRecordUnchanged(raw,line,edited,replacement.length);SameRawTags(edited.Tags,LoadRaw(SaveRaw(edited,!raw.IsBinary)).Tags);
  WriteArtifact('r12-edited-'+name,SaveRaw(edited,true));
}
export function RawR12Authored(binary){
  const tags=RawR12Tags(),bytes=RawR12Bytes(tags,binary),raw=LoadRaw(bytes);Equal(DxfVersion.AutoCad12,raw.Version);SameRawTags(tags,raw.Tags);Equal(bytes,SaveRaw(raw));
  for(const format of [false,true]){SameRawTags(tags,LoadRaw(SaveRaw(raw,format)).Tags);SameRawTags(tags,LoadRaw(SaveRaw(DxfRawDocument.Create(tags),format)).Tags);}
  Equal(RawR12Bytes(tags),SaveRaw(raw.WithTags(raw.Tags),true));WriteArtifact(`r12-xdata-${BooleanName(binary)}.dxf`,SaveRaw(raw.WithTags(tags),binary));
}
export function RawR12EscapedCode(code){
  const tags=RawR12Tags();let sample=RawTagSample(DxfGroupCode.GetValueType(code));if(typeof sample==='string'&&code!==1005)sample='ASCII';
  tags.splice(tags.length-2,0,T(code,sample));const expected=RawR12Bytes(tags),raw=LoadRaw(expected);SameRawTags(tags,raw.Tags);Equal(expected,SaveRaw(raw.WithTags(tags),true));
}
export function RawR12Chunks(length){
  const tags=RawR12Tags();tags.splice(tags.length-2,0,T(1004,Uint8Array.from({length},(_,i)=>i)));
  const expected=RawR12Bytes(tags),raw=LoadRaw(expected);SameRawTags(tags,raw.Tags);Equal(expected,SaveRaw(raw.WithTags(tags),true));
}
export function RawR12DimensionStyle(binary){
  for(const name of ['','_CLOSED','00aB']){
    const tags=RawR12Tags();tags.splice(7,0,T(0,'SECTION'),T(2,'TABLES'),T(0,'TABLE'),T(2,'DIMSTYLE'),T(70,1),T(0,'DIMSTYLE'),T(2,'STANDARD'),T(70,0),DxfTag.CreateDimensionStyleArrowName(name),T(0,'ENDTAB'),T(0,'ENDSEC'));
    const raw=LoadRaw(RawR12Bytes(tags,binary));SameRawTags(tags,raw.Tags);SameRawTags(tags,LoadRaw(SaveRaw(raw.WithTags(tags),!binary)).Tags);
  }
}
export function RawR12HeaderNotFirst(binary){
  const tags=RawR12Tags();tags.splice(0,0,T(0,'SECTION'),T(2,'VENDOR'),T(0,'RECORD'),T(1,'AC1032'),T(0,'ENDSEC'));
  const raw=LoadRaw(RawR12Bytes(tags,binary));SameRawTags(tags,raw.Tags);Equal(DxfVersion.AutoCad12,raw.Version);
}
export function RawR12Encoding(binary,alias,codePage,value){
  const tags=RawR12Tags();tags[5]=T(3,alias);tags.splice(tags.length-2,0,T(1000,value));
  const raw=LoadRaw(RawR12Bytes(tags,binary,Encoding.GetEncoding(codePage)));Equal(codePage,raw.EncodingCodePage);SameRawTags(tags,raw.Tags);SameRawTags(tags,LoadRaw(SaveRaw(raw.WithTags(tags),!binary)).Tags);
}
export function RawR12Limits(binary){
  const bytes=RawR12Bytes(RawR12Tags(),binary),raw=LoadRaw(bytes);
  Throws(E.InvalidDataException,()=>LoadRaw(bytes,new DxfRawOptions(bytes.length-1)));Throws(E.InvalidDataException,()=>LoadRaw(bytes,new DxfRawOptions(undefined,raw.Tags.Count-1)));
  Throws(E.InvalidDataException,()=>LoadRaw(bytes,new DxfRawOptions(undefined,undefined,5)));
  const fragmented=new RawNonseekableStream(bytes,false,1);SameRawTags(raw.Tags,DxfRawDocument.Load(fragmented).Tags);Check(!fragmented.WasDisposed);
  const cancelled=new MemoryStream(bytes);Throws(E.OperationCanceledException,()=>DxfRawDocument.Load(cancelled,null,{aborted:true}));Equal(0,cancelled.Position);
  const offset=new MemoryStream(Uint8Array.from([1,2,3,...bytes]));offset.Position=3;SameRawTags(raw.Tags,DxfRawDocument.Load(offset).Tags);
  const output=new MemoryStream();output.WriteByte(42);Throws(E.OperationCanceledException,()=>raw.Save(output,!binary,{aborted:true}));Equal(1,output.Length);
}
export function RawR12Partial(tail){return Uint8Array.from([...RawR12Bytes(RawR12Tags().slice(0,10)),...tail]);}
export function RawR12TruncatedEscape(length){Throws(E.EndOfStreamException,()=>LoadRaw(RawR12Partial([255,0xEF,0x03].slice(0,length))));}
export function RawR12BadEscape(code){Throws(E.InvalidDataException,()=>LoadRaw(RawR12Partial([255,code&255,(code>>8)&255])));}
export function RawR12FramingMismatch(){
  Throws(E.FormatException,()=>LoadRaw(RawFixtureBytes(RawR12Tags(),true)));
  for(const profile of ['AC1012','AC1014','AC1015','AC1032']){const tags=RawR12Tags();tags[3]=T(1,profile);Throws(E.FormatException,()=>LoadRaw(RawR12Bytes(tags)));}
}
export function RawR12Comments(){
  const tags=RawR12Tags();tags.splice(3,0,T(999,'$ACADVER AC1032'));const raw=LoadRaw(RawR12Bytes(tags,false));SameRawTags(tags,raw.Tags);
  const output=new MemoryStream();output.WriteByte(42);Throws(E.NotSupportedException,()=>raw.Save(output,true));Equal(1,output.Length);
  const stripped=raw.WithTags(tags.filter(t=>t.Code!==999));SameRawTags(stripped.Tags,LoadRaw(SaveRaw(stripped,true)).Tags);
}
export function RawR12Trailing(){
  Throws(E.FormatException,()=>LoadRaw(Uint8Array.from([...RawR12Bytes(RawR12Tags()),0])));
  Throws(E.EndOfStreamException,()=>LoadRaw(RawR12Bytes(RawR12Tags().slice(0,-1))));
}
