// Port of the complete pinned tests/netDxf.Conformance/OleFrameTests.cs.
import { DxfDocument, DxfRawDocument, DxfTag, DxfVersion, OleFrame, Ole2Frame, Vector3, Matrix3, Block, Insert, Layout, MemoryStream } from '../../index.js';
import { ArgumentNullException, ArgumentOutOfRangeException, InvalidDataException, NotSupportedException } from '../../runtime/Errors.js';
import { GetTypedIOConfiguration } from '../../runtime/TypedDocumentIO.js';
import { Run, Check, Equal, Throws, SupportedVersions, VersionName, HeaderVersion, BooleanName } from './TestHarness.js';
import { RawFixtureBytes } from './RawDocumentTests.js';
import { OlePayload, OleSingle, OleRecords, OleWriteArtifact } from './Ole2FrameTests.js';
export function RegisterOleFrameTests(){
  for(const v of SupportedVersions)for(const b of [false,true]){const id=`${VersionName(v)}/${BooleanName(b)}`;
    for(const n of [0,1,126,127,128,1025])Run(`oleframe/wire/${id}/${n}`,()=>LegacyOleRoundTrip(v,b,n));
    for(let f=0;f<14;f++)Run(`oleframe/invalid/${id}/${f}`,()=>LegacyOleInvalid(v,b,f));
    for(let p=0;p<4;p++)Run(`oleframe/placement/${id}/${p}`,()=>LegacyOlePlacement(v,b,p));}
  Run('oleframe/api/isolation-transform',LegacyOleApi);
}
export function LegacyOleTags(v,size,lateHeader){
  const tags=[[0,'SECTION'],[2,'HEADER'],[9,'$ACADVER'],[1,HeaderVersion(v)],[9,'$DWGCODEPAGE'],[3,'ANSI_1252'],[0,'ENDSEC'],[0,'SECTION'],[2,'ENTITIES'],[0,'OLEFRAME'],[5,'210'],[100,'AcDbEntity'],[8,'0'],[62,5],[100,'AcDbOleFrame']];
  if(!lateHeader)tags.push([70,1],[90,size]);const bytes=OlePayload(size);for(let at=0;at<bytes.length;at+=29)tags.push([310,bytes.slice(at,at+29)]);if(lateHeader)tags.push([90,size],[70,7]);
  tags.push([1,'OLE'],[1001,'LEGACY_OLE'],[1000,'after legacy bytes'],[0,'LINE'],[5,'211'],[100,'AcDbEntity'],[8,'0'],[100,'AcDbLine'],[10,10],[20,20],[30,30],[11,40],[21,50],[31,60],[0,'ENDSEC'],[0,'EOF']);return tags.map(([c,x])=>new DxfTag(c,x));
}
export function LegacyOleRoundTrip(v,b,size){
  for(const late of [false,true]){const tags=LegacyOleTags(v,size,late);if(!b)tags.splice(tags.findIndex(t=>t.Code===90),0,new DxfTag(999,'OLEFRAME 90 310 OLE'));const input=new MemoryStream(RawFixtureBytes(tags,b));
    try{let doc=DxfDocument.Load(input);Check(doc!==null,'Legacy OLE input rejected.');const original=OleSingle(doc.Entities.OleFrames);Equal('210',original.Handle,'Legacy OLE input identity');const clone=original.Clone();Check(clone.Handle===null&&clone.Owner===null,'Legacy OLE clone retained identity.');doc.Entities.Add(clone);
      for(let cycle=0;cycle<3;cycle++){const output=new MemoryStream();try{
        Check(doc.Save(output,cycle%2===0?!b:b),'Legacy OLE save failed.');output.Position=0;
        for(const record of OleRecords(DxfRawDocument.Load(output)).filter(r=>r.Name==='OLEFRAME')){const t=Array.from(record.Tags);Equal(size,OleSingle(t.filter(t=>t.Code===90)).Value,'Legacy OLE wire byte count');const chunks=t.filter(t=>t.Code===310).map(t=>t.Value);Check(chunks.every(c=>c.length<=127),'Oversized legacy OLE chunk.');Equal(OlePayload(size),Uint8Array.from(chunks.flatMap(c=>Array.from(c))),'Legacy OLE wire bytes changed.');Equal('OLE',OleSingle(t.filter(t=>t.Code===1)).Value,'Legacy OLE terminator');}
        if(cycle===1&&size===1025&&late)OleWriteArtifact(`oleframe-${VersionName(v)}-${BooleanName(b)}.dxf`,output.ToArray());output.Position=0;doc=DxfDocument.Load(output);Check(doc!==null,'Legacy OLE reload failed.');Equal(2,Array.from(doc.Entities.OleFrames).length,'Legacy OLE count');
        for(const frame of doc.Entities.OleFrames){Equal(size,frame.BinaryDataLength,'Legacy OLE length');Equal(OlePayload(size),frame.GetBinaryData(),'Legacy OLE bytes changed.');Equal(late?7:1,frame.OleVersion,'Legacy OLE version');Equal(5,frame.Color.Index,'Legacy OLE color');Equal('after legacy bytes',OleSingle(frame.XData.get_Item('LEGACY_OLE').XDataRecord).Value,'Legacy OLE XData');}
        Check(new Vector3(10,20,30).Equals(OleSingle(doc.Entities.Lines).StartPoint),'Legacy OLE following LINE');
      }finally{output.Dispose();}}Check(input.CanRead,'Legacy OLE closed input stream.');
    }finally{input.Dispose();}
  }
}
export function LegacyOleInvalid(v,b,f){
  const tags=LegacyOleTags(v,128,false),size=tags.findIndex(t=>t.Code===90),end=tags.findIndex(t=>t.Code===1&&t.Value==='OLE');
  switch(f){case 0:tags[size]=new DxfTag(90,-1);break;case 1:tags[size]=new DxfTag(90,127);break;case 2:tags[size]=new DxfTag(90,129);break;case 3:tags[size]=new DxfTag(90,2147483647);break;
    case 4:tags.splice(size,1);break;case 5:tags.splice(size,0,new DxfTag(90,128));break;case 6:tags.splice(end,1);break;case 7:tags[end]=new DxfTag(1,'NOT_OLE');break;case 8:tags.splice(end+1,0,new DxfTag(310,Uint8Array.of(1)));break;case 9:tags.splice(size,0,new DxfTag(70,1));break;
    case 10:tags[tags.findIndex(t=>t.Code===70)]=new DxfTag(70,-1);break;case 11:tags.splice(end,0,new DxfTag(100,'PrivateFrame'));break;case 12:tags.splice(end,0,new DxfTag(1001,'EARLY_XDATA'));break;case 13:tags[tags.findIndex(t=>t.Code===100&&t.Value==='AcDbOleFrame')]=new DxfTag(100,'AcDbOle2Frame');break;}
  const input=new MemoryStream(RawFixtureBytes(tags,b));try{if(GetTypedIOConfiguration()==='Debug')Throws(InvalidDataException,()=>DxfDocument.Load(input));else Check(DxfDocument.Load(input)===null,'Invalid legacy OLE accepted.');Check(input.CanRead,'Invalid legacy OLE closed caller stream.');}finally{input.Dispose();}
}
export function LegacyOlePlacement(v,b,p){
  const frame=new OleFrame(OlePayload(128)),doc=new DxfDocument(v);
  if(p===0)doc.Entities.Add(frame);else if(p===1){doc.Layouts.Add(new Layout('LegacyPaper'));doc.Entities.ActiveLayout='LegacyPaper';doc.Entities.Add(frame);doc.Entities.ActiveLayout='Model';}
  else if(p===2){const inner=new Block('LegacyInner'),outer=new Block('LegacyOuter');inner.Entities.Add(frame);outer.Entities.Add(new Insert(inner));doc.Entities.Add(new Insert(outer));}
  else{const unused=new Block('LegacyUnused');unused.Entities.Add(frame);doc.Blocks.Add(unused);}doc.Entities.Add(new Ole2Frame(OlePayload(1),Vector3.Zero,Vector3.UnitX));
  const stream=new MemoryStream();try{Check(doc.Save(stream,b),'Legacy OLE placement save failed.');stream.Position=0;const loaded=DxfDocument.Load(stream);Check(loaded!==null,'Legacy OLE placement reload failed.');const found=OleSingle(Array.from(loaded.Blocks).flatMap(block=>Array.from(block.Entities)).filter(e=>e instanceof OleFrame));Equal(OlePayload(128),found.GetBinaryData(),'Legacy placement bytes changed.');Equal(1,Array.from(loaded.Entities.Ole2Frames).length,'Legacy OLE confused with OLE2FRAME.');if(p===0)Check(loaded.Entities.Remove(found),'Legacy OLE removal failed.');}finally{stream.Dispose();}
}
export function LegacyOleApi(){
  const bytes=OlePayload(128),frame=new OleFrame(bytes);bytes[0]=23;Equal(0,frame.GetBinaryData()[0],'Legacy constructor aliases bytes');const copy=frame.GetBinaryData();copy[0]=42;Equal(0,frame.GetBinaryData()[0],'Legacy getter aliases bytes');Throws(ArgumentNullException,()=>new OleFrame(null));Throws(ArgumentOutOfRangeException,()=>new OleFrame(bytes,-1));Equal(32767,new OleFrame(new Uint8Array(),32767).OleVersion,'Version metadata normalized');
  frame.TransformBy(Matrix3.Identity,Vector3.Zero);Throws(NotSupportedException,()=>frame.TransformBy(Matrix3.Identity,new Vector3(1e-15,0,0)));Throws(NotSupportedException,()=>frame.TransformBy(Matrix3.Scale(2),Vector3.Zero));
  const block=new Block('LegacyInsert');block.Entities.Add(frame);const insert=new Insert(block),clone=insert.Clone();Check(frame!==OleSingle(Array.from(clone.Block.Entities).filter(e=>e instanceof OleFrame)),'Legacy nested clone alias.');Equal(1,Array.from(insert.Explode()).filter(e=>e instanceof OleFrame).length,'Legacy identity explosion');insert.Position=Vector3.UnitX;Throws(NotSupportedException,()=>insert.Explode());Equal(0,frame.GetBinaryData()[0],'Failed legacy transform mutated bytes');
  const tags=LegacyOleTags(DxfVersion.AutoCad2018,0,false).filter(t=>t.Code!==70),stream=new MemoryStream(RawFixtureBytes(tags,false));try{Equal(1,OleSingle(DxfDocument.Load(stream).Entities.OleFrames).OleVersion,'Legacy default version');}finally{stream.Dispose();}
}
