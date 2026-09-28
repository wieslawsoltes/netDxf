// Port of pinned EntitySourceIdentityTests.cs; original identity and sequence assertions.
import { DxfDocument, DxfRawDocument, DxfTag, DxfVersion, MemoryStream, Line, Vector3, Polyline3D, Block, AttributeDefinition, Insert } from '../../index.js';
import { FormatException } from '../../runtime/Errors.js';
import { Run, Check, Equal, SupportedVersions, VersionName, BooleanName } from './TestHarness.js';
import fs from 'node:fs';
import { gunzipSync } from 'node:zlib';
const single = values => { const a=Array.from(values); Equal(1,a.length,'Single'); return a[0]; };
export function RegisterEntitySourceIdentityTests() {
  for(const binary of [false,true])for(const kind of ['LINE','POLYLINE','SECTIONOBJECT'])for(const fault of ['missing','duplicate-same','zero'])
    Run(`source-entity/identity/${kind}/${fault}/${BooleanName(binary)}`,()=>EntitySourceIdentityMalformed(kind,fault,binary));
  for(const version of SupportedVersions)for(const binary of [false,true])
    Run(`source-entity/valid-sequences/${VersionName(version)}/${BooleanName(binary)}`,()=>EntitySourceIdentitySequences(version,binary));
}
export function EntitySourceIdentityRaw(kind) {
  const bytes=new MemoryStream();
  try {
    if(kind==='SECTIONOBJECT')bytes.Write(gunzipSync(fs.readFileSync('tests/fixtures/section/LiveSection1.dxf.gz')));
    else {const doc=new DxfDocument(DxfVersion.AutoCad2018);doc.Entities.Add(kind==='LINE'?new Line(Vector3.Zero,Vector3.UnitX):new Polyline3D([Vector3.Zero,Vector3.UnitX,Vector3.UnitY]));Check(doc.Save(bytes),'Identity test seed save');}
    bytes.Position=0;return DxfRawDocument.Load(bytes);
  }finally{bytes.Dispose();}
}
export function EntitySourceIdentityMalformed(kind,fault,binary) {
  let raw=EntitySourceIdentityRaw(kind);const record=single(raw.Sections.filter(s=>s.Name==='ENTITIES')).Records.find(r=>r.Name===kind);Check(record,'entity record');
  const tags=Array.from(record.Tags),index=tags.findIndex(t=>t.Code===5);Check(index>=0,'common handle');
  if(fault==='missing')tags.splice(index,1);else if(fault==='duplicate-same')tags.splice(index,0,tags[index]);else tags[index]=new DxfTag(5,'0');
  raw=raw.WithRecord(record,tags);const bytes=new MemoryStream();try {
    raw.WithTags(raw.Tags.filter(t=>t.Code!==999)).Save(bytes,binary);bytes.Position=0;let rejected=false;
    try{rejected=DxfDocument.Load(bytes)===null;}catch(e){if(!(e instanceof FormatException))throw e;rejected=true;}
    Check(rejected,'Malformed retained entity identity must reject through the public load error path');
  }finally{bytes.Dispose();}
}
export function EntitySourceIdentitySequences(version,binary) {
  const doc=new DxfDocument(version);doc.Entities.Add(new Polyline3D([Vector3.Zero,Vector3.UnitX,Vector3.UnitY]));
  const block=new Block('IDENTITY_SEQUENCE');block.AttributeDefinitions.Add(new AttributeDefinition('VALUE'));doc.Entities.Add(new Insert(block));
  const bytes=new MemoryStream();try{Check(doc.Save(bytes,binary),'Valid sequence save');bytes.Position=0;const loaded=DxfDocument.Load(bytes);Check(loaded!==null,'Valid entity sequence rejected');Equal(3,single(loaded.Entities.Polylines3D).Vertexes.Count,'Valid POLYLINE/VERTEX sequence changed');Equal(1,single(loaded.Entities.Inserts).Attributes.Count,'Valid INSERT/ATTRIB/SEQEND sequence changed');}finally{bytes.Dispose();}
}
