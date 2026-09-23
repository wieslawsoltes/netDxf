// Complete port of SunStudyProducerRawTests.cs. Raw transport is not typed SUNSTUDY IO.
import fs from 'node:fs';
import path from 'node:path';
import { gunzipSync } from 'node:zlib';
import { createHash } from 'node:crypto';
import { DxfRawDocument, DxfVersion, MemoryStream } from '../../index.js';
import { InvalidDataException, FormatException } from '../../runtime/Errors.js';
import { sourceRoot, javascriptRoot } from '../../tools/dotnet.mjs';
import { Run, Check, Equal, BooleanName } from './TestHarness.js';
import { LoadRaw, SaveRaw, SameRawTags } from './RawDocumentTests.js';
const hash=bytes=>createHash('sha256').update(bytes).digest('hex');
export function RunSunStudyProducerRawTests() {
  for(const year of [2013,2018])for(const sourceBinary of [false,true])for(const hours of [false,true])
    for(const mode of sourceBinary&&hours?['source-rejection']:['source-rejection','exact','ascii','binary'])
      Run(`sunstudy/producer-raw/${year}/${BooleanName(sourceBinary)}/${BooleanName(hours)}/${mode}`,()=>SunStudyProducerRaw(year,sourceBinary,hours,mode));
}
// The JavaScript harness registers each source partial using this naming adapter.
export const RegisterSunStudyProducerRawTests=RunSunStudyProducerRawTests;
export function SunStudyProducerRaw(year,sourceBinary,hours,mode) {
  const name=`ixmilia-sunstudy-R${year}-${sourceBinary?'binary':'ascii'}-no-dates-${hours?'hours':'no-hours'}.dxf`;
  const folder=path.join(sourceRoot,'tests/fixtures/sunstudy-producer');
  const manifest=JSON.parse(fs.readFileSync(path.join(folder,'source-manifest.json'),'utf8'));
  const matches=manifest.files.filter(item=>item.name===name);Equal(1,matches.length,'Exactly one producer source');const entry=matches[0];
  Equal(sourceBinary&&hours?'failed-reload':'original',entry.kind,'Producer output classification');
  const compressed=fs.readFileSync(path.join(folder,entry.storedFile));Equal(entry.gzipSha256,hash(compressed),'Pinned compressed original');
  const source=new Uint8Array(gunzipSync(compressed));Equal(entry.bytes,source.length,'Pinned original byte count');Equal(entry.sha256,hash(source),'Pinned exact original');
  if(mode==='source-rejection') {
    const input=new MemoryStream(source);let rejected=false;
    try { DxfRawDocument.Load(input); }
    catch(error) {
      if((sourceBinary?error instanceof InvalidDataException:error instanceof FormatException)&&error.message.includes('Invalid hexadecimal handle')&&error.message.includes('group code 340'))rejected=true;
      else throw error;
    }
    Check(rejected,'Unchanged producer scaffolding did not reject its empty DIMSTYLE pointer.');Check(input.CanRead,'Malformed producer input closed the caller stream.');return;
  }
  const carrier=entry.carrier,carrierBytes=new Uint8Array(fs.readFileSync(path.join(folder,carrier.file)));
  Equal(carrier.sha256,hash(carrierBytes),'Pinned disclosed carrier');Equal(10,carrier.transformations.length,'Disclosed unrelated DIMSTYLE null pointers');
  const raw=LoadRaw(carrierBytes);Equal(year===2013?DxfVersion.AutoCad2013:DxfVersion.AutoCad2018,raw.Version,'Producer profile');Equal(sourceBinary,raw.IsBinary,'Producer source transport');
  const objects=raw.Sections.filter(section=>section.Name==='OBJECTS');Equal(1,objects.length,'Exactly one OBJECTS section');
  Equal(1,Array.from(objects[0].Content).filter(tag=>tag.Code===0&&tag.Value==='SUNSTUDY').length,'Actual SUNSTUDY object count');
  let output;
  if(mode==='exact'){output=SaveRaw(raw);Equal(carrierBytes,output,'Exact carrier save changed bytes.');}
  else {output=SaveRaw(raw.WithTags(raw.Tags),mode==='binary');const copy=LoadRaw(output);Equal(mode==='binary',copy.IsBinary,'Requested normalized transport');SameRawTags(raw.Tags,copy.Tags);}
  const directory=process.env.DXF_JS_TEST_ARTIFACTS||path.join(javascriptRoot,'artifacts/conformance');fs.mkdirSync(directory,{recursive:true});
  fs.writeFileSync(path.join(directory,`sunstudy-producer-${name.slice(0,-4)}-${mode}.dxf`),output);
}
