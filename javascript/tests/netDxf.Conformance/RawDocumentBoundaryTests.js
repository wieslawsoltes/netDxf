// Port of tests/netDxf.Conformance/RawDocumentBoundaryTests.cs.
import fs from 'node:fs';
import path from 'node:path';
import { javascriptRoot } from '../../tools/dotnet.mjs';
import { doubleBits } from '../../tools/wire.mjs';
import { DxfTag,DxfRawDocument,DxfVersion,DxfTagValueType,MemoryStream } from '../../index.js';
import { Encoding } from '../../runtime/Encoding.js';
import * as E from '../../runtime/Errors.js';
import { Run,Check,Equal,Throws,SupportedVersions,VersionName,HeaderVersion,BooleanName } from './TestHarness.js';
import { RawFixtureTags,RawFixtureBytes,LoadRaw,SaveRaw,SameRawTags } from './RawDocumentTests.js';
const T=(code,value)=>new DxfTag(code,value);
const WriteArtifact=(name,bytes)=>{const dir=process.env.DXF_JS_TEST_ARTIFACTS||path.join(javascriptRoot,'artifacts/conformance');fs.mkdirSync(dir,{recursive:true});fs.writeFileSync(path.join(dir,name),bytes);};
export function RegisterRawDocumentBoundaryTests(){
  for(const v of SupportedVersions)for(const b of [false,true])for(const [name,action] of [
    ['duplicate-vendor-sections',RawDocumentDuplicateSections],['header-not-first',RawDocumentReorderedHeader],
    ['case-preservation',RawDocumentCase],['duplicate-case-header',RawDocumentAmbiguousHeader],
    ['destination-offset',RawDocumentDestination],['independent-raw-fixtures',RawDocumentRetainFixtures]])
    Run(`raw-document/${name}/${VersionName(v)}/${BooleanName(b)}`,()=>action(v,b));
  Run('raw-document/cancellation-during-copy',RawDocumentMidCancellation);
  Run('raw-document/unsupported-value-types',RawDocumentBadTypedInput);Run('raw-document/strict-utf8-binary',RawDocumentBinaryUtf8);
}
export function RawDocumentDuplicateSections(v,b){
  const tags=RawFixtureTags(v),start=tags.findIndex(t=>t.Code===2&&t.Value==='VENDOR_SECTION')-1;
  tags.splice(tags.length-1,0,...tags.slice(start,-1));const raw=LoadRaw(RawFixtureBytes(tags,b));
  Equal(2,raw.Sections.filter(s=>s.Name==='VENDOR_SECTION').length);SameRawTags(raw.Tags,LoadRaw(SaveRaw(raw.WithTags(raw.Tags),!b)).Tags);
}
export function RawDocumentReorderedHeader(v,b){
  const tags=RawFixtureTags(v),end=tags.findIndex(t=>t.Code===0&&t.Value==='ENDSEC');const header=tags.splice(0,end+1);tags.splice(tags.length-1,0,...header);
  const raw=LoadRaw(RawFixtureBytes(tags,b));Equal('CLASSES',raw.Sections[0].Name);Equal('HEADER',raw.Sections.at(-1).Name);
  SameRawTags(raw.Tags,LoadRaw(SaveRaw(raw.WithTags(raw.Tags),!b)).Tags);
}
export function RawDocumentCase(v,b){
  const tags=RawFixtureTags(v).map(t=>[0,2,9].includes(t.Code)||(t.Code===1&&t.Value===HeaderVersion(v))?T(t.Code,t.Value.toLowerCase()):t);
  const raw=LoadRaw(RawFixtureBytes(tags,b));Equal(v,raw.Version);Equal('header',raw.Sections[0].Name);SameRawTags(tags,LoadRaw(SaveRaw(raw.WithTags(raw.Tags),!b)).Tags);
}
export function RawDocumentAmbiguousHeader(v,b){
  let tags=RawFixtureTags(v);tags.splice(tags.length-1,0,T(0,'SECTION'),T(2,'header'),T(9,'$acadver'),T(1,HeaderVersion(v)),T(0,'ENDSEC'));
  Throws(E.FormatException,()=>LoadRaw(RawFixtureBytes(tags,b)));
  tags=RawFixtureTags(v);tags.splice(4,0,T(9,'$acadver'),T(1,HeaderVersion(v)));Throws(E.FormatException,()=>LoadRaw(RawFixtureBytes(tags,b)));
}
export function RawDocumentDestination(v,b){
  const source=RawFixtureBytes(RawFixtureTags(v),b),raw=LoadRaw(source),target=new Uint8Array(source.length+15).fill(42);
  const output=new MemoryStream(target,true);output.Position=5;raw.Save(output);
  Equal(source.length+5,output.Position);Equal(source.length+15,output.Length);
  Check(target.slice(0,5).every(x=>x===42)&&target.slice(source.length+5).every(x=>x===42));Equal(source,target.slice(5,source.length+5));
}
export function RawDocumentRetainFixtures(v,b){
  const tags=RawFixtureTags(v),raw=LoadRaw(RawFixtureBytes(tags,b)),names=new Map(Object.entries(DxfTagValueType).map(([n,k])=>[k,n]));
  const manifest=tags.map(tag=>({code:tag.Code,kind:names.get(tag.ValueType),value:
    tag.ValueType===DxfTagValueType.Double?doubleBits(tag.Value):tag.Value instanceof Uint8Array?Buffer.from(tag.Value).toString('hex').toUpperCase():
    typeof tag.Value==='boolean'?(tag.Value?'1':'0'):String(tag.Value)}));
  WriteArtifact(`raw-preservation-${VersionName(v)}.json`,JSON.stringify(manifest,null,2));
  for(const transport of [false,true]){const bytes=SaveRaw(raw.WithTags(raw.Tags),transport);SameRawTags(tags,LoadRaw(bytes).Tags);WriteArtifact(`raw-preservation-${VersionName(v)}-${BooleanName(transport)}.dxf`,bytes);}
}
export class RawCancelAfterReadStream extends MemoryStream{
  #cancellation;
  constructor(bytes,cancellation){super(bytes,false);this.#cancellation=cancellation;}
  Read(buffer,offset,count){const read=super.Read(buffer,offset,Math.min(count,3));this.#cancellation.aborted=true;return read;}
}
export function RawDocumentMidCancellation(){
  const cancellation={aborted:false},input=new RawCancelAfterReadStream(RawFixtureBytes(RawFixtureTags(DxfVersion.AutoCad2018),false),cancellation);
  Throws(E.OperationCanceledException,()=>DxfRawDocument.Load(input,null,cancellation));Equal(3,input.Position);Check(input.CanRead);
}
export function RawDocumentBadTypedInput(){
  const input=Encoding.UTF8.GetString(RawFixtureBytes(RawFixtureTags(DxfVersion.AutoCad2018),false));
  let text=input.replace('290\n1\n','290\n2\n');Throws(E.FormatException,()=>LoadRaw(Encoding.UTF8.GetBytes(text)));
  // Locate the same numeric group independently of the fixture encoder's lexical precision.
  text=input.replace(/1040\n[^\n]*\n/,'1040\nNaN\n');Check(text.includes('NaN'));Throws(E.FormatException,()=>LoadRaw(Encoding.UTF8.GetBytes(text)));
  text=input.replace('310\n00FF807F\n','310\n00G1\n');Throws(E.FormatException,()=>LoadRaw(Encoding.UTF8.GetBytes(text)));
}
export function RawDocumentBinaryUtf8(){
  const bytes=RawFixtureBytes(RawFixtureTags(DxfVersion.AutoCad2018),true),needle=Encoding.UTF8.GetBytes('Zażółć 東京');
  const index=Buffer.from(bytes).indexOf(needle);Check(index>=0);bytes[index]=255;Throws(E.DecoderFallbackException,()=>LoadRaw(bytes));
}
