// Port of the complete pinned tests/netDxf.Conformance/OleMetadataTests.cs.
import { DxfDocument, DxfRawDocument, Ole2Frame, OleObjectType, Vector3, Block, Insert, MemoryStream } from '../../index.js';
import { Run, Check, Equal, SupportedVersions, VersionName, BooleanName } from './TestHarness.js';
import { RawFixtureBytes } from './RawDocumentTests.js';
import { OleTags, OlePayload, OleSingle, OleRecords, OleWriteArtifact } from './Ole2FrameTests.js';
export const OleMetadataGroupCodes=[[70],[3],[10,20,30],[11,21,31],[71],[72]];
export function RegisterOleMetadataTests(){for(const v of SupportedVersions)for(const b of [false,true])for(let mask=0;mask<64;mask++)Run(`olemetadata/wire/${VersionName(v)}/${BooleanName(b)}/${mask}`,()=>OleMetadataWire(v,b,mask));}
export function OleMetadataTags(v,mask){
  const tags=OleTags(v,33,3,true),start=tags.findIndex(t=>t.Code===100&&t.Value==='AcDbOle2Frame'),remove=new Set();for(let bit=0;bit<6;bit++)if((mask&(1<<bit))===0)for(const code of OleMetadataGroupCodes[bit])remove.add(code);
  for(let at=tags.findIndex(t=>t.Code===1001)-1;at>start;at--)if(remove.has(tags[at].Code))tags.splice(at,1);return tags;
}
export function CheckOleMetadataPresence(record,mask){
  const all=Array.from(record.Tags),start=all.findIndex(t=>t.Code===100&&t.Value==='AcDbOle2Frame'),tags=all.slice(start+1);
  for(let bit=0;bit<6;bit++)for(const code of OleMetadataGroupCodes[bit])Equal((mask&(1<<bit))!==0,tags.some(t=>t.Code===code),`Optional metadata presence ${code}`);
  Equal(33,OleSingle(tags.filter(t=>t.Code===90)).Value,'Metadata payload length');Equal(OlePayload(33),Uint8Array.from(tags.filter(t=>t.Code===310).flatMap(t=>Array.from(t.Value))),'Metadata editing changed payload.');Equal('OLE',OleSingle(tags.filter(t=>t.Code===1)).Value,'Required terminator changed');
}
export function OleMetadataWire(v,b,mask){
  const input=new MemoryStream(RawFixtureBytes(OleMetadataTags(v,mask),b));try{
    let doc=DxfDocument.Load(input);Check(doc!==null,'Optional OLE metadata rejected.');const frame=OleSingle(doc.Entities.Ole2Frames);
    Equal(mask&2?'Picture Żółć':'',frame.Description,'Optional description value');Equal(2,frame.OleVersion,'Existing default version');Equal(mask&16?OleObjectType.Static:OleObjectType.Embedded,frame.ObjectType,'Existing default object kind');Check((mask&4?new Vector3(1.0000000000000002,6,-2):Vector3.Zero).Equals(frame.UpperLeftCorner),'Optional upper value');Check((mask&8?new Vector3(8,-4,-2):Vector3.Zero).Equals(frame.LowerRightCorner),'Optional lower value');
    doc.Entities.Add(frame.Clone());const block=new Block('OptionalOle');block.Entities.Add(frame.Clone());const insert=new Insert(block).Clone(),separate=new MemoryStream();
    try{const nested=new DxfDocument(v);nested.Entities.Add(insert);Check(nested.Save(separate,b),'Nested optional metadata save failed.');separate.Position=0;CheckOleMetadataPresence(OleSingle(OleRecords(DxfRawDocument.Load(separate)).filter(r=>r.Name==='OLE2FRAME')),mask);}finally{separate.Dispose();}
    for(let cycle=0;cycle<3;cycle++){const output=new MemoryStream();try{
      Check(doc.Save(output,cycle%2===0?!b:b),'Optional metadata save failed.');output.Position=0;for(const record of OleRecords(DxfRawDocument.Load(output)).filter(r=>r.Name==='OLE2FRAME'))CheckOleMetadataPresence(record,mask);
      if(cycle===1&&[0,1,2,4,8,16,32,63].includes(mask))OleWriteArtifact(`olemetadata-${VersionName(v)}-${BooleanName(b)}-${mask}.dxf`,output.ToArray());
      output.Position=0;doc=DxfDocument.Load(output);Check(doc!==null,'Optional metadata reload failed.');Equal(2,Array.from(doc.Entities.Ole2Frames).length,'Metadata original/clone count');for(const current of doc.Entities.Ole2Frames)Equal('after binary data',OleSingle(current.XData.get_Item('OLE_TEST').XDataRecord).Value,'Adjacent metadata XData');Check(new Vector3(10,20,30).Equals(OleSingle(doc.Entities.Lines).StartPoint),'Entity after metadata changed');
    }finally{output.Dispose();}}Check(input.CanRead,'Metadata reader closed the caller stream.');
  }finally{input.Dispose();}
}
