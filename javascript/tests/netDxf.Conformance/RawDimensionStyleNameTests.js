// Port of tests/netDxf.Conformance/RawDimensionStyleNameTests.cs.
import fs from 'node:fs';
import path from 'node:path';
import { javascriptRoot } from '../../tools/dotnet.mjs';
import { DxfTag,DxfVersion,DxfTagValueType,DxfHandleKind,DxfGroupCode,MemoryStream } from '../../index.js';
import { Encoding } from '../../runtime/Encoding.js';
import * as E from '../../runtime/Errors.js';
import { Run,Check,Equal,Throws,SupportedVersions,VersionName,HeaderVersion,BooleanName } from './TestHarness.js';
import { LoadRaw,SaveRaw,SameRawTags } from './RawDocumentTests.js';
import { FindRawRecord,CheckRawRecordPartition,AssertOutsideRecordUnchanged } from './RawRecordTests.js';
export function RegisterRawDimensionStyleNameTests(){
  for(const v of SupportedVersions)for(const b of [false,true])for(const n of ['','OPEN_ARROW','00aB','  a0  ','flèche'])
    Run(`raw-dimblk/name/${VersionName(v)}/${BooleanName(b)}/${n}`,()=>RawDimensionStyleName(v,b,n));
  for(const b of [false,true])for(const [name,action] of [['context-boundaries',RawDimStyleContexts],['strict-handles',RawDimStyleStrictHandles],['scoped-edits',RawDimStyleEdits]])
    Run(`raw-dimblk/${name}/${BooleanName(b)}`,()=>action(b));
  Run('raw-dimblk/factory-and-placement',RawDimStyleFactory);Run('raw-dimblk/binary-multiline-preflight',RawDimStyleMultiline);
}
export function RawDimStylePairs(version,arrowName){
  return [[0,'SECTION'],[2,'HEADER'],[9,'$ACADVER'],[1,HeaderVersion(version)],[9,'$DWGCODEPAGE'],[3,'ANSI_1252'],[0,'ENDSEC'],
    [0,'SECTION'],[2,'TABLES'],[0,'TABLE'],[2,'DIMSTYLE'],[5,'10'],[330,'0'],[100,'AcDbSymbolTable'],[70,1],
    [0,'DIMSTYLE'],[105,'11'],[330,'10'],[100,'AcDbSymbolTableRecord'],[100,'AcDbDimStyleTableRecord'],[2,'Style'],[70,0],
    [5,arrowName],[6,'OPEN_ARROW_1'],[7,'OPEN_ARROW_2'],[0,'ENDTAB'],[0,'ENDSEC'],[0,'SECTION'],[2,'ENTITIES'],
    [0,'LINE'],[5,'000aF'],[100,'AcDbEntity'],[8,'0'],[100,'AcDbLine'],[10,1],[20,2],[30,3],[11,4],[21,5],[31,6],[0,'ENDSEC'],[0,'EOF']];
}
export function RawDimStyleBytes(pairs,version,binary){
  // This encoder bypasses DxfTag validation and both production codecs to author malformed cases.
  const encoding=version>=DxfVersion.AutoCad2007?Encoding.UTF8:Encoding.GetEncoding(1252);
  if(!binary)return encoding.GetBytes([...pairs].map(([code,value])=>code+'\n'+value+'\n').join(''));
  const chunks=[Buffer.from('AutoCAD Binary DXF\r\n\x1a\0','ascii')];
  for(const [code,value] of pairs){const group=Buffer.alloc(2);group.writeInt16LE(code);chunks.push(group);
    if(typeof value==='string')chunks.push(encoding.GetBytes(value),Uint8Array.of(0));
    else{const data=Buffer.alloc(code===70?2:8);if(code===70)data.writeInt16LE(value);else data.writeDoubleLE(value);chunks.push(data);}}
  return new Uint8Array(Buffer.concat(chunks));
}
export function RawDimensionStyleName(version,binary,name){
  const bytes=RawDimStyleBytes(RawDimStylePairs(version,name),version,binary),raw=LoadRaw(bytes),style=FindRawRecord(raw,'TABLES','DIMSTYLE');
  const arrow=[...style.Content].find(t=>t.Code===5);Equal(name,arrow.Value);Equal(DxfTagValueType.String,arrow.ValueType);Equal(DxfHandleKind.None,arrow.HandleKind);
  Equal(DxfHandleKind.ObjectIdentity,[...style.Content].find(t=>t.Code===105).HandleKind);
  Equal('AF',[...FindRawRecord(raw,'ENTITIES','LINE').Content].find(t=>t.Code===5).Value);Equal(bytes,SaveRaw(raw));
  SameRawTags(raw.Tags,LoadRaw(SaveRaw(raw,!binary)).Tags);const rewritten=raw.WithTags(raw.Tags);SameRawTags(raw.Tags,LoadRaw(SaveRaw(rewritten,binary)).Tags);CheckRawRecordPartition(raw);
  if(name==='00aB'){const dir=process.env.DXF_JS_TEST_ARTIFACTS||path.join(javascriptRoot,'artifacts/conformance');fs.mkdirSync(dir,{recursive:true});fs.writeFileSync(path.join(dir,`raw-dimblk-${VersionName(version)}-${BooleanName(binary)}.dxf`),SaveRaw(rewritten,binary));}
}
export function RawDimStyleContexts(binary){
  let pairs=RawDimStylePairs(DxfVersion.AutoCad2018,'00aB'),arrow=pairs.findIndex(([c,v])=>c===5&&v==='00aB');
  pairs.splice(arrow,0,[102,'{VENDOR'],[102,'{NESTED'],[5,'00ab'],[102,'}'],[5,'00cd'],[102,'}'],[5,''],[1001,'APP'],[1005,'00ef']);
  const raw=LoadRaw(RawDimStyleBytes(pairs,DxfVersion.AutoCad2018,binary)),values=[...FindRawRecord(raw,'TABLES','DIMSTYLE').Content].filter(t=>t.Code===5);
  Equal('AB',values[0].Value);Equal('CD',values[1].Value);Equal('',values[2].Value);Equal(DxfTagValueType.String,values[2].ValueType);Equal('AB',values[3].Value);
  SameRawTags(raw.Tags,LoadRaw(SaveRaw(raw,!binary)).Tags);
  pairs=RawDimStylePairs(DxfVersion.AutoCad2018,'OPEN_ARROW').map(([c,v])=>[c,[0,2].includes(c)?v.toLowerCase():v]);
  const lower=LoadRaw(RawDimStyleBytes(pairs,DxfVersion.AutoCad2018,binary));Equal('OPEN_ARROW',lower.Tags.find(t=>t.Code===5&&t.ValueType===DxfTagValueType.String).Value);
  if(!binary){pairs.splice(2,0,[999,'DIMSTYLE']);pairs.splice(pairs.findIndex(([c,v])=>v==='OPEN_ARROW'),0,[999,'ENDTAB']);LoadRaw(RawDimStyleBytes(pairs,DxfVersion.AutoCad2018,false));}
}
export function RawDimStyleStrictHandles(binary){
  const Reject=tags=>Throws(binary?E.InvalidDataException:E.FormatException,()=>LoadRaw(RawDimStyleBytes(tags,DxfVersion.AutoCad2018,binary)));
  for(const value of ['','NOT_A_HANDLE']){
    let tags=RawDimStylePairs(DxfVersion.AutoCad2018,'OPEN_ARROW');tags[tags.findIndex(([c])=>c===5)]=[5,value];Reject(tags);
    tags=RawDimStylePairs(DxfVersion.AutoCad2018,'OPEN_ARROW');tags[tags.findLastIndex(([c])=>c===5)]=[5,value];Reject(tags);
    tags=RawDimStylePairs(DxfVersion.AutoCad2018,'OPEN_ARROW');tags[tags.findIndex(([c])=>c===105)]=[105,value];Reject(tags);
    for(const prefix of [[[102,'{VENDOR']],[[1001,'APP']]]){tags=RawDimStylePairs(DxfVersion.AutoCad2018,value);const arrow=tags.findLastIndex(([c,v])=>c===5&&v!=='000aF');tags.splice(arrow,0,...prefix);Reject(tags);}
  }
  for(const context of ['wrong-table','wrong-record','no-table','after-endtab','wrong-section']){
    const tags=RawDimStylePairs(DxfVersion.AutoCad2018,'NOT_A_HANDLE');
    if(context==='wrong-table')tags[10]=[2,'LAYER'];if(context==='wrong-record')tags[15]=[0,'LAYER'];
    if(context==='no-table')tags.splice(9,6);if(context==='after-endtab')tags.splice(15,0,[0,'ENDTAB']);if(context==='wrong-section')tags[8]=[2,'ENTITIES'];Reject(tags);
  }
}
export function RawDimStyleEdits(binary){
  const raw=LoadRaw(RawDimStyleBytes(RawDimStylePairs(DxfVersion.AutoCad2018,''),DxfVersion.AutoCad2018,binary)),record=FindRawRecord(raw,'TABLES','DIMSTYLE');
  const replacement=[...record.Tags].map(t=>t.Code===5?DxfTag.CreateDimensionStyleArrowName('00aB'):t),edited=raw.WithRecord(record,replacement);
  AssertOutsideRecordUnchanged(raw,record,edited,replacement.length);SameRawTags(edited.Tags,LoadRaw(SaveRaw(edited,!binary)).Tags);
  Throws(E.ArgumentException,()=>raw.WithRecord(record,[...record.Tags].map(t=>t.Code===5?new DxfTag(5,'AB'):t)));
  const line=FindRawRecord(raw,'ENTITIES','LINE');Throws(E.ArgumentException,()=>raw.WithRecord(line,[...line.Tags].map(t=>t.Code===5?DxfTag.CreateDimensionStyleArrowName(''):t)));
  const tags=[...record.Tags];tags.splice(tags.findIndex(t=>t.Code===5),0,new DxfTag(102,'{VENDOR'));Throws(E.ArgumentException,()=>raw.WithRecord(record,tags));Check(raw.HasOriginalBytes);
}
export function RawDimStyleFactory(){
  Throws(E.ArgumentNullException,()=>DxfTag.CreateDimensionStyleArrowName(null));Throws(E.ArgumentException,()=>DxfTag.CreateDimensionStyleArrowName('x\0y'));
  Throws(E.ArgumentException,()=>new DxfTag(5,''));Throws(E.ArgumentException,()=>new DxfTag(5,'OPEN_ARROW'));Equal(DxfTagValueType.Handle,DxfGroupCode.GetValueType(5));
  const name=DxfTag.CreateDimensionStyleArrowName('000aB');Equal(5,name.Code);Equal('000aB',name.Value);Equal(DxfTagValueType.String,name.ValueType);Equal(DxfHandleKind.None,name.HandleKind);
}
export function RawDimStyleMultiline(){
  const raw=LoadRaw(RawDimStyleBytes(RawDimStylePairs(DxfVersion.AutoCad2018,'line1\nline2'),DxfVersion.AutoCad2018,true));
  SameRawTags(raw.Tags,LoadRaw(SaveRaw(raw.WithTags(raw.Tags),true)).Tags);
  const destination=new MemoryStream(Uint8Array.of(1,2,3),true);Throws(E.NotSupportedException,()=>raw.Save(destination,false));Equal(Uint8Array.of(1,2,3),destination.ToArray());
}
