// Complete original TypedCommentTests.cs. Comment text must never become semantic tags.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { DxfDocument, DxfRawDocument, DxfTag, Vector3, MemoryStream } from '../../index.js';
import { InvalidDataException, InvalidOperationException } from '../../runtime/Errors.js';
import { GetTypedIOConfiguration } from '../../runtime/TypedDocumentIO.js';
import { HatchPatternValidationTags, HatchPatternCheck } from './HatchPatternValidationTests.js';
import { RawFixtureBytes } from './RawDocumentTests.js';
import { Run, Check, Equal, SupportedVersions, VersionName, BooleanName } from './TestHarness.js';
const artifactDirectory=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../../artifacts/conformance/fixtures');
const single=items=>{const rows=Array.from(items);Equal(1,rows.length,'Expected one item');return rows[0];};
export function RegisterTypedCommentTests(){for(const v of SupportedVersions){for(const mode of ['every-tag','runs','section','header','table','subclass','geometry','xdata','eof','leading'])Run(`typed/comments/${VersionName(v)}/${mode}`,()=>TypedCommentRoundTrip(v,mode));Run(`typed/comments/${VersionName(v)}/binary-control`,()=>TypedCommentBinaryControl(v));for(const c of [false,true])Run(`typed/comments/${VersionName(v)}/malformed/${BooleanName(c)}`,()=>TypedCommentMalformed(v,c));}}
export function TypedCommentTags(version,mode){const original=HatchPatternValidationTags(version),result=[new DxfTag(999,'leading source comment')];let section='';
  for(let i=0;i<original.length;i++){const tag=original[i];if(tag.Code===2&&i>0&&original[i-1].Code===0&&original[i-1].Value==='SECTION')section=tag.Value;
    const inject=mode==='every-tag'||mode==='runs'||mode==='section'&&(tag.Code===0||tag.Code===2)||mode==='header'&&section==='HEADER'||mode==='table'&&section==='TABLES'||mode==='subclass'&&tag.Code===100||mode==='geometry'&&section==='ENTITIES'&&tag.Code>=10&&tag.Code<100||mode==='xdata'&&tag.Code>=1000||mode==='eof'&&tag.Code===0&&tag.Value==='EOF';
    if(inject)for(let n=0;n<(mode==='runs'?33:1);n++)result.push(new DxfTag(999,'0 SECTION 2 HEADER 0 EOF are comment data '+n));result.push(tag);
  }return result;
}
export function TypedCommentRoundTrip(version,mode){const tags=TypedCommentTags(version,mode),leading=[];for(const tag of tags){if(tag.Code!==999)break;leading.push(tag.Value);}const bytes=RawFixtureBytes(tags,false),input=new MemoryStream(bytes);try{
  const binary={};Equal(version,DxfDocument.CheckDxfFileVersion(input,binary),'Commented version probe');Check(!binary.value,'Text detected as binary.');input.Position=0;const doc=DxfDocument.Load(input);if(!doc)throw new InvalidOperationException('Valid commented drawing rejected.');Equal(leading,Array.from(doc.Comments),'Leading comments changed or interstitial comments leaked into header.');HatchPatternCheck(doc,'canonical',1);Check(new Vector3(20,30,40).Equals(single(doc.Entities.Lines).StartPoint),'Comment shifted point fields');
  for(const format of [false,true]){const output=new MemoryStream();try{Check(doc.Save(output,format),'Commented drawing export failed.');output.Position=0;const raw=DxfRawDocument.Load(output),emitted=Array.from(raw.Tags).filter(t=>t.Code===999).map(t=>t.Value);Equal(format?[]:leading,emitted,'Typed comment preservation policy changed.');output.Position=0;const loaded=DxfDocument.Load(output);if(!loaded)throw new InvalidOperationException('Commented drawing reload failed.');HatchPatternCheck(loaded,'canonical',1);Check(new Vector3(20,30,40).Equals(single(loaded.Entities.Lines).StartPoint),'Following entity changed');}finally{output.Dispose();}}
  input.Position=0;const untouched=DxfRawDocument.Load(input),rawOutput=new MemoryStream();try{untouched.Save(rawOutput);Equal(bytes,rawOutput.ToArray(),'Typed comment fix changed exact raw preservation.');}finally{rawOutput.Dispose();}
  if(mode==='every-tag'){fs.mkdirSync(artifactDirectory,{recursive:true});fs.writeFileSync(path.join(artifactDirectory,`typed-comments-${VersionName(version)}.dxf`),bytes);}Check(input.CanRead,'Comment wrapper closed caller stream.');
}finally{input.Dispose();}}
export function TypedCommentBinaryControl(version){const input=new MemoryStream(RawFixtureBytes(HatchPatternValidationTags(version),true));try{const doc=DxfDocument.Load(input);if(!doc)throw new InvalidOperationException('Binary behavior changed.');HatchPatternCheck(doc,'canonical',1);Equal(0,doc.Comments.Count,'Binary invented comments');}finally{input.Dispose();}}
export function TypedCommentMalformed(version,comment){const tags=TypedCommentTags(version,comment?'every-tag':'leading');tags[tags.findIndex(t=>t.Code===78)]=new DxfTag(78,-1);const input=new MemoryStream(RawFixtureBytes(tags,false));try{
  if(GetTypedIOConfiguration()==='Debug'){try{DxfDocument.Load(input);throw new InvalidOperationException('Comments hid a malformed HATCH count.');}catch(error){Check(error instanceof InvalidDataException,'Expected InvalidDataException');Check(error.message.includes('HATCH'),'Comment filtering lost the underlying diagnostic.');}}else Check(DxfDocument.Load(input)===null,'Comments hid malformed input.');
}finally{input.Dispose();}}
