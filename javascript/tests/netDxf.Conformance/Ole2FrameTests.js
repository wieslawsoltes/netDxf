// Port of the complete pinned tests/netDxf.Conformance/Ole2FrameTests.cs.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { DxfDocument, DxfRawDocument, DxfTag, Ole2Frame, Vector3, Matrix3, Block, Insert, Layout, MemoryStream } from '../../index.js';
import { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, InvalidDataException, FormatException, NotSupportedException } from '../../runtime/Errors.js';
import { GetTypedIOConfiguration } from '../../runtime/TypedDocumentIO.js';
import { Run, Check, Equal, SameDoubleBits, Throws, SupportedVersions, VersionName, HeaderVersion, BooleanName } from './TestHarness.js';
import { RawFixtureBytes } from './RawDocumentTests.js';
export const OlePayload = size => Uint8Array.from({length:size},(_,i)=>i*131%256);
export const OleSingle = items => {const values=Array.from(items);Equal(1,values.length,'Expected one item');return values[0];};
export const OleRecords = raw => Array.from(raw.Sections).flatMap(s=>Array.from(s.Records));
export function OleWriteArtifact(name,bytes){const dir=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../../artifacts/conformance/fixtures');fs.mkdirSync(dir,{recursive:true});fs.writeFileSync(path.join(dir,name),bytes);}
export function RegisterOle2FrameTests(){
  for(const v of SupportedVersions)for(const b of [false,true]){
    const id=`${VersionName(v)}/${BooleanName(b)}`;
    for(const n of [0,1,127,128,255,1025])Run(`ole2frame/wire/${id}/${n}`,()=>OleRoundTrip(v,b,n));
    for(let f=0;f<19;f++)Run(`ole2frame/invalid/${id}/${f}`,()=>OleInvalid(v,b,f));
    for(let p=0;p<4;p++)Run(`ole2frame/placement/${id}/${p}`,()=>OlePlacement(v,b,p));
  }
  Run('ole2frame/api/isolation-validation-transform',OleApi);
}
export function OleTags(version,size,kind,reversed){
  const tags=[[0,'SECTION'],[2,'HEADER'],[9,'$ACADVER'],[1,HeaderVersion(version)],[9,'$DWGCODEPAGE'],[3,'ANSI_1252'],[0,'ENDSEC'],[0,'SECTION'],[2,'ENTITIES'],[0,'OLE2FRAME'],[5,'210'],[100,'AcDbEntity'],[8,'0'],[62,5],[100,'AcDbOle2Frame']];
  const metadata=[[70,2],[3,String.raw`Picture \U+017B\U+00F3\U+0142\U+0107`],[10,1.0000000000000002],[20,6],[30,-2],[11,8],[21,-4],[31,-2],[71,kind],[72,0],[90,size]];
  if(reversed)metadata.reverse();tags.push(...metadata);const data=OlePayload(size);
  for(let at=0;at<data.length;at+=31)tags.push([310,data.slice(at,at+31)]);
  tags.push([1,'OLE'],[1001,'OLE_TEST'],[1000,'after binary data'],[0,'LINE'],[5,'211'],[100,'AcDbEntity'],[8,'0'],[100,'AcDbLine'],[10,10],[20,20],[30,30],[11,40],[21,50],[31,60],[0,'ENDSEC'],[0,'EOF']);
  return tags.map(([code,value])=>new DxfTag(code,value));
}
export function CheckOle(frame,size,kind){
  Equal(size,frame.BinaryDataLength,'OLE byte count');Equal(OlePayload(size),frame.GetBinaryData(),'OLE bytes changed.');
  Equal('Picture Żółć',frame.Description,'OLE user-type description');Equal(2,frame.OleVersion,'OLE version');Equal(kind,frame.ObjectType,'OLE relationship');Equal(0,frame.TileMode,'Stored OLE tile mode');
  SameDoubleBits(1.0000000000000002,frame.UpperLeftCorner.X,'OLE corner precision');Check(new Vector3(8,-4,-2).Equals(frame.LowerRightCorner),'OLE WCS lower corner');Equal(5,frame.Color.Index,'OLE common color');Equal('after binary data',OleSingle(frame.XData.get_Item('OLE_TEST').XDataRecord).Value,'Following OLE XData');
}
export function OleRoundTrip(v,b,size){
  for(const kind of [1,2,3]){
    const tags=OleTags(v,size,kind,true);
    if(!b){const end=tags.findIndex(t=>t.Code===1001),start=tags.findIndex(t=>t.Code===100&&t.Value==='AcDbOle2Frame');for(let i=end;i>start;i--)tags.splice(i,0,new DxfTag(999,'OLE2FRAME 310 1 OLE'));}
    const input=new MemoryStream(RawFixtureBytes(tags,b));
    try{
      let doc=DxfDocument.Load(input);Check(doc!==null,'OLE fixture rejected.');const original=OleSingle(doc.Entities.Ole2Frames);CheckOle(original,size,kind);Equal('210',original.Handle,'Imported OLE handle');
      const copy=original.Clone();CheckOle(copy,size,kind);Check(copy.Handle===null&&copy.Owner===null,'OLE clone kept identity.');doc.Entities.Add(copy);
      for(let cycle=0;cycle<3;cycle++){
        const output=new MemoryStream();try{
          Check(doc.Save(output,cycle%2===0?!b:b),'OLE save failed.');output.Position=0;
          for(const record of OleRecords(DxfRawDocument.Load(output)).filter(r=>r.Name==='OLE2FRAME')){
            Equal(size,OleSingle(Array.from(record.Tags).filter(t=>t.Code===90)).Value,'Serialized OLE count');const chunks=Array.from(record.Tags).filter(t=>t.Code===310).map(t=>t.Value);
            Check(chunks.every(c=>c.length<=127),'Oversized OLE chunk.');Equal(OlePayload(size),Uint8Array.from(chunks.flatMap(c=>Array.from(c))),'Serialized OLE bytes.');Equal('OLE',OleSingle(Array.from(record.Tags).filter(t=>t.Code===1)).Value,'OLE terminator');
          }
          if(cycle===1&&size===255&&kind===2)OleWriteArtifact(`ole2frame-${VersionName(v)}-${BooleanName(b)}.dxf`,output.ToArray());
          output.Position=0;doc=DxfDocument.Load(output);Check(doc!==null,'OLE reload failed.');Equal(2,Array.from(doc.Entities.Ole2Frames).length,'OLE clone count');for(const frame of doc.Entities.Ole2Frames)CheckOle(frame,size,kind);
          Check(new Vector3(10,20,30).Equals(OleSingle(doc.Entities.Lines).StartPoint),'Entity after OLE lost.');
        }finally{output.Dispose();}
      }
      Check(input.CanRead,'OLE reader closed caller stream.');
    }finally{input.Dispose();}
  }
}
export function OleInvalid(v,b,f){
  const tags=OleTags(v,255,2,false),marker=tags.findIndex(t=>t.Code===100&&t.Value==='AcDbOle2Frame'),at=tags.findIndex(t=>t.Code===90),end=tags.findIndex(t=>t.Code===1&&t.Value==='OLE');
  switch(f){
    case 0:tags[at]=new DxfTag(90,-1);break;case 1:tags[at]=new DxfTag(90,254);break;case 2:tags[at]=new DxfTag(90,256);break;case 3:tags[at]=new DxfTag(90,2147483647);break;
    case 4:tags.splice(at,1);break;case 5:tags.splice(at,0,new DxfTag(90,255));break;case 6:tags.splice(end,1);break;case 7:tags[end]=new DxfTag(1,'NOT_OLE');break;
    case 8:tags.splice(end+1,0,new DxfTag(310,Uint8Array.of(1)));break;case 9:tags.splice(tags.findIndex(t=>t.Code===20),1);break;case 10:break;
    case 11:tags[tags.findIndex(t=>t.Code===71)]=new DxfTag(71,4);break;case 12:tags[tags.findIndex(t=>t.Code===72)]=new DxfTag(72,2);break;case 13:tags[tags.findIndex(t=>t.Code===70)]=new DxfTag(70,-1);break;
    case 14:tags.splice(marker+1,0,new DxfTag(100,'PrivateOle'));break;case 15:tags.splice(marker+1,0,new DxfTag(102,'{PRIVATE'));break;case 16:tags[marker]=new DxfTag(100,'AcDbWrongFrame');break;
    case 17:tags[tags.findIndex((t,i)=>i>=marker&&t.Code===3)]=new DxfTag(3,String.raw`A\U+000AB`);break;case 18:tags.splice(end,0,new DxfTag(1001,'EARLY_XDATA'));break;
  }
  let wire=RawFixtureBytes(tags,b);
  if(f===10){
    if(b){const needle=Buffer.alloc(8);needle.writeDoubleLE(1.0000000000000002);const coordinate=Buffer.from(wire).indexOf(needle);Check(coordinate>=0,'Missing coordinate to corrupt.');const bytes=Buffer.alloc(8);bytes.writeDoubleLE(NaN);wire.set(bytes,coordinate);}
    else{const text=Buffer.from(wire).toString('utf8');Check(text.includes('10\n1.0000000000000002\n'),'Missing text coordinate.');wire=Uint8Array.from(Buffer.from(text.replaceAll('10\n1.0000000000000002\n','10\nNaN\n'),'utf8'));}
  }
  const input=new MemoryStream(wire);try{
    if(GetTypedIOConfiguration()==='Debug')Throws(f===10&&!b?FormatException:InvalidDataException,()=>DxfDocument.Load(input));
    else Check(DxfDocument.Load(input)===null,'Invalid OLE accepted.');
    Check(input.CanRead,'Invalid OLE closed caller stream.');
  }finally{input.Dispose();}
}
export function OlePlacement(v,b,p){
  const data=OlePayload(128),description=String.raw`Type \U+000A`,frame=new Ole2Frame(data,new Vector3(1,2,3),new Vector3(4,5,6),description,2,2,p===1?1:0),doc=new DxfDocument(v);
  if(p===0)doc.Entities.Add(frame);else if(p===1){doc.Layouts.Add(new Layout('OlePaper'));doc.Entities.ActiveLayout='OlePaper';doc.Entities.Add(frame);doc.Entities.ActiveLayout='Model';}
  else if(p===2){const inner=new Block('OleInner'),outer=new Block('OleOuter');inner.Entities.Add(frame);outer.Entities.Add(new Insert(inner));doc.Entities.Add(new Insert(outer));}
  else{const unused=new Block('OleUnused');unused.Entities.Add(frame);doc.Blocks.Add(unused);}
  const stream=new MemoryStream();try{
    Check(doc.Save(stream,b),'Placed OLE save failed.');stream.Position=0;const restored=DxfDocument.Load(stream);Check(restored!==null,'Placed OLE reload failed.');
    const value=OleSingle(Array.from(restored.Blocks).flatMap(block=>Array.from(block.Entities)).filter(e=>e instanceof Ole2Frame));Equal(data,value.GetBinaryData(),'Placed OLE payload lost.');Equal(description,value.Description,'Literal OLE description');Equal(frame.TileMode,value.TileMode,'Stored OLE mode');if(p===0)Check(restored.Entities.Remove(value),'OLE removal failed.');
  }finally{stream.Dispose();}
}
export function OleApi(){
  const bytes=OlePayload(256),original=new Ole2Frame(bytes,Vector3.Zero,Vector3.UnitX);bytes[0]=9;Equal(0,original.GetBinaryData()[0],'Constructor aliased payload');const returned=original.GetBinaryData();returned[1]=0;Equal(131,original.GetBinaryData()[1],'Getter aliased payload');
  const clone=original.Clone(),cloned=clone.GetBinaryData();cloned[0]=22;Equal(0,original.GetBinaryData()[0],'Clone aliased payload');original.TransformBy(Matrix3.Identity,Vector3.Zero);
  for(const matrix of [Matrix3.Scale(2),Matrix3.RotationZ(.5),new Matrix3(NaN,0,0,0,1,0,0,0,1)]){Throws(NotSupportedException,()=>original.TransformBy(matrix,Vector3.Zero));Check(Vector3.Zero.Equals(original.UpperLeftCorner),'Rejected transform mutated corner');}
  Throws(NotSupportedException,()=>original.TransformBy(Matrix3.Identity,new Vector3(1e-15,0,0)));Throws(ArgumentNullException,()=>new Ole2Frame(null,Vector3.Zero,Vector3.Zero));Throws(ArgumentNullException,()=>new Ole2Frame(bytes,Vector3.Zero,Vector3.Zero,null));
  for(const bad of ['A\rB','A\nB','A\0B'])Throws(ArgumentException,()=>new Ole2Frame(bytes,Vector3.Zero,Vector3.Zero,bad));Throws(ArgumentOutOfRangeException,()=>new Ole2Frame(bytes,new Vector3(Infinity,0,0),Vector3.Zero));
  const block=new Block('OleClone');block.Entities.Add(original);const insert=new Insert(block),copiedInsert=insert.Clone();Check(original!==OleSingle(Array.from(copiedInsert.Block.Entities).filter(e=>e instanceof Ole2Frame)),'Nested OLE clone alias.');Equal(1,Array.from(insert.Explode()).filter(e=>e instanceof Ole2Frame).length,'Identity OLE explosion');insert.Position=Vector3.UnitX;Throws(NotSupportedException,()=>insert.Explode());Check(Vector3.Zero.Equals(original.UpperLeftCorner),'Failed INSERT explosion changed original OLE');
}
