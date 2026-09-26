// Complete original cases except valid-position: those also require typed Load.
// The missing load-after-probe assertions are not removed or counted as passes.
import { DxfDocument, DxfVersion, DxfTag, MemoryStream } from '../../index.js';
import { DxfReader } from '../../netDxf/IO/DxfReader.HeaderProbe.js';
import { BinarySentinel } from '../../runtime/BinaryCursor.js';
import { IOException, NotSupportedException, EndOfStreamException } from '../../runtime/Errors.js';
import { RawFixtureBytes } from './RawDocumentTests.js';
import { Run, Check, Equal, Throws, SupportedVersions, VersionName, HeaderVersion, BooleanName } from './TestHarness.js';
const ascii=text=>new TextEncoder().encode(text);
export function MinimalProbeBytes(version,binary,entity=false){
  const tags=[[0,'SECTION'],[2,'HEADER'],[9,'$ACADVER'],[1,HeaderVersion(version)],[9,'$DWGCODEPAGE'],[3,'ANSI_1252'],[9,'$HANDSEED'],[5,'1000'],[0,'ENDSEC']];
  if(entity)tags.push([0,'SECTION'],[2,'ENTITIES'],[0,'LINE'],[5,'200'],[100,'AcDbEntity'],[8,'ImplicitLayer'],[100,'AcDbLine'],[10,1],[20,2],[30,3],[11,4],[21,5],[31,6],[1001,'MINIMAL_DXF'],[1000,'authored payload'],[0,'ENDSEC']);
  tags.push([0,'EOF']);return RawFixtureBytes(tags.map(([code,value])=>new DxfTag(code,value)),binary);
}
export class FragmentedProbeStream extends MemoryStream { Read(buffer,offset,count){return super.Read(buffer,offset,Math.min(count,1));} }
export class ProbeFailureStream extends MemoryStream { reads=0; Read(buffer,offset,count){if(++this.reads>1)throw new IOException('Injected read failure.');return super.Read(buffer,offset,count);} }
export class ProbeNonSeekableStream extends MemoryStream { ReadCount=0; get CanSeek(){return false;}get Position(){throw new NotSupportedException();}set Position(value){throw new NotSupportedException();}Read(buffer,offset,count){this.ReadCount++;return super.Read(buffer,offset,count);} }
export function CheckProbeBytes(bytes,expected,binary){
  for(const offset of [0,7,31])for(const fragmented of [false,true]){
    const input=new Uint8Array(offset+bytes.length);input.fill(0xcc,0,offset);input.set(bytes,offset);
    const stream=fragmented?new FragmentedProbeStream(input,false):new MemoryStream(input,false);stream.Position=offset;
    try{for(let repeat=0;repeat<2;repeat++){const found={};Equal(expected,DxfDocument.CheckDxfFileVersion(stream,found),'probe version');Equal(binary,found.value,'probe transport');Equal(offset,stream.Position,'probe position');Check(stream.CanRead,'Caller stream closed.');}}finally{stream.Dispose();}
  }
}
export const ProbeHeaderString=(stream,variable)=>DxfReader.CheckHeaderVariable(stream,variable,{});
export function RegisterHeaderProbeTests(){
  for(let count=0;count<BinarySentinel.length;count++)Run('probe/truncated-signature/'+count,()=>CheckProbeBytes(BinarySentinel.slice(0,count),DxfVersion.Unknown,false));
  for(let offset=0;offset<BinarySentinel.length;offset++)Run('probe/corrupt-signature/'+offset,()=>{const bytes=MinimalProbeBytes(DxfVersion.AutoCad2018,true);bytes[offset]^=1;CheckProbeBytes(bytes,DxfVersion.Unknown,false);});
  for(const version of SupportedVersions)for(const binary of [false,true])Run(`probe/IO-failure/${VersionName(version)}/${BooleanName(binary)}`,()=>{
    const file=MinimalProbeBytes(version,binary,true),input=new Uint8Array(7+file.length);input.set(file,7);const stream=new ProbeFailureStream(input,false);stream.Position=7;
    try{Equal(DxfVersion.Unknown,DxfDocument.CheckDxfFileVersion(stream,{}),'IO failure version');Equal(7,stream.Position,'IO failure position');Check(stream.CanRead,'Caller disposed after failure.');}finally{stream.Dispose();}
  });
  Run('probe/null-public-contract',()=>{const flag={};Equal(DxfVersion.Unknown,DxfDocument.CheckDxfFileVersion(null,flag));Equal(false,flag.value);});
  Run('probe/nonseekable',()=>{const stream=new ProbeNonSeekableStream(ascii('0\nEOF\n'));try{Equal(DxfVersion.Unknown,DxfDocument.CheckDxfFileVersion(stream,{}));Equal(0,stream.ReadCount);Check(stream.CanRead);}finally{stream.Dispose();}});
  Run('probe/headerless-short-file',()=>{for(const text of ['0\nEOF','0\nEOF\n','999\nHEADER\n0\nEOF\n']){CheckProbeBytes(ascii(text),0,false);const stream=new MemoryStream(ascii(text));try{Equal('',ProbeHeaderString(stream,'$ACADVER'));Equal(0,stream.Position);}finally{stream.Dispose();}}});
  Run('probe/malformed-text',()=>{for(const text of ['0\nSECTION\n2\nHEADER\n9\n$ACADVER\n1\n','0\nSECTION\n2\nHEADER\n9\n$ACADVER\n70\n1\n0\nENDSEC\n0\nEOF\n','0\nSECTION\n2\nHEADER\n9\n$ACADVER\n3\nAC1032\n0\nENDSEC\n0\nEOF\n','0\nSECTION\n1\nHEADER\n9\n$ACADVER\n1\nAC1032\n0\nENDSEC\n0\nEOF\n','0\nSECTION\n2\nHEADER\n9\n$ACADVER\n9\nAC1032\n0\nENDSEC\n0\nEOF\n','0\nSECTION\n2\nCLASSES\n0\nEOF\n'])CheckProbeBytes(ascii(text),0,false);});
  Run('probe/ignore-comment-control-words',()=>{for(const word of ['EOF','HEADER','ENDSEC','SECTION','$ACADVER'])CheckProbeBytes(ascii(`999\n${word}\n0\nSECTION\n999\n${word}\n2\nHEADER\n999\n${word}\n9\n$ACADVER\n999\n${word}\n1\nAC1032\n0\nENDSEC\n0\nEOF\n`),18,false);});
  Run('probe/ignore-nonheader-payload',()=>CheckProbeBytes(ascii('0\nSECTION\n2\nENTITIES\n0\nTEXT\n1\nHEADER\n9\n$ACADVER\n1\nAC1032\n0\nENDSEC\n0\nEOF\n'),0,false));
  Run('probe/skip-multitype-section',()=>CheckProbeBytes(ascii('0\nSECTION\n2\nCLASSES\n0\nCLASS\n1\nHEADER\n90\n0\n280\n0\n281\n0\n0\nENDSEC\n0\nSECTION\n2\nHEADER\n9\n$ACADVER\n1\nAC1032\n0\nENDSEC\n0\nEOF\n'),18,false));
  Run('probe/skip-multivalue-header',()=>CheckProbeBytes(ascii('0\nSECTION\n2\nHEADER\n9\n$INSBASE\n10\n1\n20\n2\n30\n3\n9\n$ACADVER\n1\nAC1032\n0\nENDSEC\n0\nEOF\n'),18,false));
  Run('probe/codepage-string',()=>{const stream=new MemoryStream(MinimalProbeBytes(13,false));try{Equal('ANSI_1252',ProbeHeaderString(stream,'$DWGCODEPAGE'));Equal(0,stream.Position);Check(stream.CanRead);}finally{stream.Dispose();}});
  Run('probe/not-full-file-validation',()=>CheckProbeBytes(ascii('0\nSECTION\n2\nHEADER\n9\n$ACADVER\n1\nAC1032\ngarbage-after-selected-variable\n'),18,false));
  Run('probe/unknown-format-string',()=>CheckProbeBytes(ascii('0\nSECTION\n2\nHEADER\n9\n$ACADVER\n1\nAC9999\n0\nENDSEC\n0\nEOF\n'),0,false));
  Run('probe/internal-error-position',()=>{const file=ascii('0\nSECTION\n2\nHEADER\n9\n$ACADVER\n1\n'),bytes=new Uint8Array(7+file.length);bytes.set(file,7);const stream=new MemoryStream(bytes);stream.Position=7;try{Throws(EndOfStreamException,()=>ProbeHeaderString(stream,'$ACADVER'));Equal(7,stream.Position);Check(stream.CanRead);}finally{stream.Dispose();}});
}
