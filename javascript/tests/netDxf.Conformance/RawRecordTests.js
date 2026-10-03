// Port of RawRecordTests.cs. RawRecordKnown requires the still-unported JS typed DxfDocument API.
import { DxfTag, DxfRawDocument, DxfRawOptions, DxfVersion, DxfHandleKind } from '../../index.js';
import * as E from '../../runtime/Errors.js';
import { Run, Check, Equal, Throws, SupportedVersions, VersionName, BooleanName } from './TestHarness.js';
import { RawFixtureTags, RawFixtureBytes, LoadRaw, SaveRaw, SameRawTags } from './RawDocumentTests.js';
const T=(code,value)=>new DxfTag(code,value);
export function RegisterRawRecordTests(){
  for(const v of SupportedVersions) for(const b of [false,true]) {
    for(const [name,action] of [['index-partition',RawRecordPartition],['replace-unknown',RawRecordReplace],['remove',RawRecordRemove],
      ['duplicate-names',RawRecordDuplicates],['header-values',RawRecordHeader],['foreign-and-stale',RawRecordForeign],
      ['invalid-replacements',RawRecordInvalid],['budget-and-disposal',RawRecordBudget]])
      Run(`raw-record/${name}/${VersionName(v)}/${BooleanName(b)}`,()=>action(v,b));
  }
  for(const v of SupportedVersions) Run(`raw-record/comment-partition/${VersionName(v)}`,()=>RawRecordComments(v));
  Run('raw-record/empty-records-and-preamble',RawRecordEmpty);Run('raw-record/immutable-views',RawRecordImmutable);
  Run('raw-record/concurrent-index-publication',RawRecordConcurrent);
}
export function FindRawRecord(raw,section,name){return raw.Sections.find(s=>s.Name===section).Records.find(r=>r.Name===name);}
export function RawRecordSource(version,binary){return LoadRaw(RawFixtureBytes(RawFixtureTags(version),binary));}
export function CheckRawRecordPartition(raw){
  for(const section of raw.Sections){
    const partition=[...section.Preamble,...section.Records.flatMap(r=>[...r.Tags])];SameRawTags(section.Content,partition);
    let offset=section.ContentStartTagIndex;
    for(const tag of section.Preamble) Check(tag===raw.Tags[offset++]);
    for(const record of section.Records){
      Equal(section.Name,record.SectionName);Equal(offset,record.StartTagIndex);
      Equal(record.EndTagIndex-record.StartTagIndex,record.Tags.Count);Equal(record.Tags.Count-1,record.Content.Count);
      Equal(record.Name,record.Tags.get_Item(0).Value);Equal(record.MarkerCode,record.Tags.get_Item(0).Code);
      Equal(section.Name.toUpperCase()==='HEADER'?9:0,record.MarkerCode);
      SameRawTags([...record.Tags].slice(1),record.Content);
      for(const tag of record.Tags) Check(tag===raw.Tags[offset++]);
      Equal(offset,record.EndTagIndex);
      Throws(E.ArgumentOutOfRangeException,()=>record.Tags.get_Item(-1));
      Throws(E.ArgumentOutOfRangeException,()=>record.Content.get_Item(record.Content.Count));
    }
    Equal(section.EndTagIndex-1,offset);
  }
}
export function RawRecordPartition(v,b){
  const raw=RawRecordSource(v,b);CheckRawRecordPartition(raw);
  Equal([4,1,3,3,3,1,1,1],raw.Sections.map(s=>s.Records.Count));
  Equal('SECTION',FindRawRecord(raw,'ENTITIES','SECTION').Name);
  Equal(['TABLE','FUTURE_ENTRY','ENDTAB'],raw.Sections.find(s=>s.Name==='TABLES').Records.map(r=>r.Name));
  Check(raw.HasOriginalBytes);SameRawTags(raw.Tags,LoadRaw(SaveRaw(raw,!b)).Tags);
}
export function AssertOutsideRecordUnchanged(original,record,changed,replacementLength){
  Equal(original.Tags.Count-record.Tags.Count+replacementLength,changed.Tags.Count);
  for(let i=0;i<record.StartTagIndex;i++) Check(original.Tags[i]===changed.Tags[i]);
  const delta=replacementLength-record.Tags.Count;
  for(let i=record.EndTagIndex;i<original.Tags.Count;i++) Check(original.Tags[i]===changed.Tags[i+delta]);
}
export function RawRecordReplace(v,b){
  const raw=RawRecordSource(v,b),record=FindRawRecord(raw,'ENTITIES','FUTURE_ENTITY'),before=SaveRaw(raw);
  const replacement=[...record.Tags].map(t=>t.Code===160?T(160,9223372036854775807n):t),edited=raw.WithRecord(record,replacement);
  Check(!edited.HasOriginalBytes);AssertOutsideRecordUnchanged(raw,record,edited,replacement.length);
  SameRawTags(replacement,FindRawRecord(edited,'ENTITIES','FUTURE_ENTITY').Tags);
  Equal(-9223372036854775808n,[...record.Content].find(t=>t.Code===160).Value);Equal(before,SaveRaw(raw));
  SameRawTags(edited.Tags,LoadRaw(SaveRaw(edited,!b)).Tags);CheckRawRecordPartition(edited);
}
export function RawRecordRemove(v,b){
  const raw=RawRecordSource(v,b),record=FindRawRecord(raw,'VENDOR_SECTION','VENDOR_RECORD'),edited=raw.WithoutRecord(record);
  AssertOutsideRecordUnchanged(raw,record,edited,0);const empty=edited.Sections.find(s=>s.Name==='VENDOR_SECTION');
  Equal(0,empty.Records.Count);Equal(0,empty.Content.Count);Equal(1,raw.Sections.find(s=>s.Name==='VENDOR_SECTION').Records.Count);
  SameRawTags(edited.Tags,LoadRaw(SaveRaw(edited,!b)).Tags);
  const handles=d=>d.Tags.filter(t=>t.HandleKind!==DxfHandleKind.None).map(t=>t.Value);Equal(handles(raw),handles(edited));
}
export function RawRecordDuplicates(v,b){
  const tags=RawFixtureTags(v),at=tags.findIndex(t=>t.Code===2 && t.Value==='VENDOR_SECTION');tags.splice(at+1,0,T(0,'VENDOR_RECORD'),T(1,'first'));
  const raw=LoadRaw(RawFixtureBytes(tags,b)),records=raw.Sections.find(s=>s.Name==='VENDOR_SECTION').Records;Equal(2,records.Count);
  const edited=raw.WithRecord(records[1],[...records[1].Tags].map(t=>t.Code===1?T(1,'second changed'):t));
  const next=edited.Sections.find(s=>s.Name==='VENDOR_SECTION').Records;SameRawTags(records[0].Tags,next[0].Tags);
  Equal('second changed',[...next[1].Content].find(t=>t.Code===1).Value);SameRawTags(edited.Tags,LoadRaw(SaveRaw(edited,!b)).Tags);
}
export function RawRecordHeader(v,b){
  const raw=RawRecordSource(v,b),name=FindRawRecord(raw,'HEADER','$PROJECTNAME'),replacement=[T(9,'$PROJECTNAME'),T(1,'EOF')];
  const edited=raw.WithRecord(name,replacement);SameRawTags(replacement,FindRawRecord(edited,'HEADER','$PROJECTNAME').Tags);
  AssertOutsideRecordUnchanged(raw,name,edited,replacement.length);
  Check(!raw.WithoutRecord(name).Sections.find(s=>s.Name==='HEADER').Records.some(r=>r.Name==='$PROJECTNAME'));
  const version=FindRawRecord(raw,'HEADER','$ACADVER');
  Throws(E.NotSupportedException,()=>raw.WithRecord(version,[T(9,'$ACADVER'),T(1,v===DxfVersion.AutoCad2000?'AC1032':'AC1015')]));
  Throws(E.FormatException,()=>raw.WithoutRecord(version));
  Throws(E.NotSupportedException,()=>raw.WithoutRecord(FindRawRecord(raw,'HEADER','$DWGCODEPAGE')));
  SameRawTags(edited.Tags,LoadRaw(SaveRaw(edited,!b)).Tags);
}
export function RawRecordForeign(v,b){
  const raw=RawRecordSource(v,b),record=FindRawRecord(raw,'ENTITIES','LINE'),other=raw.WithTags(raw.Tags);let enumerated=false;
  function* Replacement(){enumerated=true;yield T(0,'LINE');}
  Throws(E.ArgumentException,()=>other.WithRecord(record,Replacement()));Check(!enumerated);
  Throws(E.ArgumentException,()=>other.WithoutRecord(record));const edited=raw.WithRecord(record,record.Tags);
  Throws(E.ArgumentException,()=>edited.WithRecord(record,Replacement()));Check(!enumerated);
  Throws(E.ArgumentNullException,()=>raw.WithRecord(null,Replacement()));Throws(E.ArgumentNullException,()=>raw.WithoutRecord(null));
  Throws(E.ArgumentNullException,()=>raw.WithRecord(record,null));
}
export function RawRecordInvalid(v,b){
  const raw=RawRecordSource(v,b),record=FindRawRecord(raw,'ENTITIES','LINE'),before=SaveRaw(raw);
  const invalid=[[],[T(0,'')],[T(1,'LINE')],[T(9,'$PROJECTNAME'),T(1,'LINE')],[T(0,'eof')],[T(0,'endsec')],
    [T(0,'LINE'),T(0,'CIRCLE')],[T(0,'LINE'),T(0,'EOF')],[null],[T(0,'LINE'),null]];
  for(const replacement of invalid) Throws(E.ArgumentException,()=>raw.WithRecord(record,replacement));
  const header=FindRawRecord(raw,'HEADER','$PROJECTNAME');
  Throws(E.ArgumentException,()=>raw.WithRecord(header,[T(9,'$PROJECTNAME'),T(0,'LINE')]));
  Throws(E.ArgumentException,()=>raw.WithRecord(header,[T(9,'$A'),T(1,'a'),T(9,'$B')]));Equal(before,SaveRaw(raw));
}
export function RawRecordBudget(v,b){
  const tags=RawFixtureTags(v),raw=DxfRawDocument.Create(tags,b,new DxfRawOptions(undefined,tags.length)),record=FindRawRecord(raw,'ENTITIES','LINE');
  Throws(E.InvalidDataException,()=>raw.WithRecord(record,[...record.Tags,T(1,'over budget')]));let disposed=false;
  function* Infinite(){try{yield T(0,'LINE');while(true)yield T(1,'bounded');}finally{disposed=true;}}
  Throws(E.InvalidDataException,()=>raw.WithRecord(record,Infinite()));Check(disposed);
  function* Failing(){try{yield T(0,'LINE');throw new E.IOException('Injected replacement failure.');}finally{disposed=true;}}
  disposed=false;Throws(E.IOException,()=>raw.WithRecord(record,Failing()));Check(disposed);
  SameRawTags(tags,raw.Tags);SameRawTags(tags,raw.WithRecord(record,record.Tags).Tags);
}
export function RawRecordComments(v){
  const tags=RawFixtureTags(v,true);
  for(const [name,text] of [['ENTITIES','preamble'],['HEADER','header preamble']]) tags.splice(tags.findIndex(t=>t.Code===2 && t.Value===name)+1,0,T(999,text));
  const raw=LoadRaw(RawFixtureBytes(tags,false));CheckRawRecordPartition(raw);
  Equal(1,raw.Sections.find(s=>s.Name==='ENTITIES').Preamble.Count);Equal(1,raw.Sections.find(s=>s.Name==='HEADER').Preamble.Count);
  const record=FindRawRecord(raw,'ENTITIES','FUTURE_ENTITY');Equal(2,[...record.Tags].filter(t=>t.Code===999).length);
  const edited=raw.WithRecord(record,[...record.Tags].filter(t=>t.Code!==999));AssertOutsideRecordUnchanged(raw,record,edited,record.Tags.Count-2);
  SameRawTags(edited.Tags,LoadRaw(SaveRaw(edited)).Tags);
}
export function RawRecordEmpty(){
  const tags=RawFixtureTags(DxfVersion.AutoCad2018);
  tags.splice(tags.length-1,0,T(0,'SECTION'),T(2,'THUMBNAILIMAGE'),T(90,2),T(310,Uint8Array.of(1,2)),T(0,'ENDSEC'),T(0,'SECTION'),T(2,'EMPTY'),T(0,'ENDSEC'));
  const raw=DxfRawDocument.Create(tags);CheckRawRecordPartition(raw);const thumbnail=raw.Sections.find(s=>s.Name==='THUMBNAILIMAGE');
  Equal(0,thumbnail.Records.Count);SameRawTags(thumbnail.Content,thumbnail.Preamble);Equal(0,raw.Sections.find(s=>s.Name==='EMPTY').Preamble.Count);
  const empty=FindRawRecord(raw,'TABLES','ENDTAB');Equal(1,empty.Tags.Count);Equal(0,empty.Content.Count);
  const lower=raw.WithTags(raw.Tags.map(t=>t.Code===2 && t.Value==='HEADER'?T(2,'header'):t));
  Equal(4,lower.Sections[0].Records.Count);Equal(9,lower.Sections[0].Records[0].MarkerCode);
}
export async function RawRecordConcurrent(){
  const raw=RawRecordSource(DxfVersion.AutoCad2018,false),section=raw.Sections.find(s=>s.Name==='ENTITIES');
  const results=await Promise.all(Array.from({length:128},async()=>{await Promise.resolve();return section.Records;}));
  for(const records of results){Check(records===results[0]);Equal(3,records.Count);Check(records[0]===results[0][0]);}
}
export function RawRecordImmutable(){
  const raw=RawRecordSource(DxfVersion.AutoCad2018,false),section=raw.Sections.find(s=>s.Name==='ENTITIES');
  Throws(TypeError,()=>section.Records.splice(0));const record=section.Records[0];
  Check(!Array.isArray(record.Tags) && !Array.isArray(record.Content));Check(record===section.Records[0]);
  const binary=[...FindRawRecord(raw,'ENTITIES','FUTURE_ENTITY').Tags].find(t=>t.Code===310);const bytes=binary.Value;bytes[0]=42;Equal(0,binary.Value[0]);
}
