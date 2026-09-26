import { NullReferenceException } from '../../runtime/Errors.js';
// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { Ole2Frame, Ole2FrameMetadataFields as F } from '../Entities/Ole2Frame.js';
import { Vector3 } from '../Vector3.js';
import { MemoryStream } from '../../runtime/MemoryStream.js';
import { ArgumentException, InvalidDataException } from '../../runtime/Errors.js';
import { DecodeDxfText, EncodeDxfDatabaseText } from '../../runtime/DxfStringEncoding.js';
import { ReadXDataRecord, WriteXData, WrappedInvalidData } from '../../runtime/DxfXDataIO.js';
export function ReadOle2Frame(chunk, document) {
  if(chunk.Code!==100 || chunk.ReadString()!=='AcDbOle2Frame')throw new InvalidDataException('OLE2FRAME requires AcDbOle2Frame.');
  const seen=new Set(),xdata=[],upper=Vector3.Zero,lower=Vector3.Zero,payload=new MemoryStream();
  let version=2,type=2,tile=0,description='',length=-1,terminated=false;
  try {
    chunk.Next();
    while(chunk.Code!==0) {
      const code=chunk.Code;
      if(code===1001 && terminated){xdata.push(ReadXDataRecord(chunk,document));continue;}
      if(terminated)throw new InvalidDataException('OLE2FRAME has unexpected data after its OLE terminator.');
      if(code!==310){if(seen.has(code))throw new InvalidDataException('Duplicate OLE2FRAME group '+code+'.');seen.add(code);}
      switch(code) {
        case 70:version=chunk.ReadShort();break;
        case 3:description=DecodeDxfText(chunk.ReadString());break;
        case 10:upper.X=chunk.ReadDouble();break;case 20:upper.Y=chunk.ReadDouble();break;case 30:upper.Z=chunk.ReadDouble();break;
        case 11:lower.X=chunk.ReadDouble();break;case 21:lower.Y=chunk.ReadDouble();break;case 31:lower.Z=chunk.ReadDouble();break;
        case 71:type=chunk.ReadShort();break;case 72:tile=chunk.ReadShort();break;
        case 90:length=chunk.ReadInt();if(length<0 || payload.Length>length)throw new InvalidDataException('Invalid OLE2FRAME binary length.');break;
        case 310: {
          const part=chunk.ReadBytes();
          if(part.length>127 || payload.Length+part.length>2147483647 || length>=0 && payload.Length+part.length>length)
            throw new InvalidDataException('OLE2FRAME binary chunks exceed the allowed length.');
          payload.Write(part,0,part.length);break;
        }
        case 1:if(chunk.ReadString()!=='OLE')throw new InvalidDataException('Invalid OLE2FRAME terminator.');terminated=true;break;
        default:throw new InvalidDataException('Unsupported OLE2FRAME group '+code+'; use DxfRawDocument for private extensions.');
      }
      chunk.Next();
    }
    if(!terminated || length<0 || payload.Length!==length)throw new InvalidDataException('Incomplete OLE2FRAME binary packet or length mismatch.');
    for(const start of [10,11]) {const count=Number(seen.has(start))+Number(seen.has(start+10))+Number(seen.has(start+20));if(count!==0&&count!==3)throw new InvalidDataException('Incomplete OLE2FRAME corner.');}
    try {
      let fields=F.None;
      for(const [code,flag] of [[70,F.OleVersion],[3,F.Description],[10,F.UpperLeftCorner],[11,F.LowerRightCorner],[71,F.ObjectType],[72,F.TileMode]])if(seen.has(code))fields|=flag;
      const frame=new Ole2Frame(payload.ToArray(),upper,lower,description,version,type,tile,false,fields);
      for(const data of xdata)frame.XData.Add(data);
      return frame;
    } catch(error){if(error instanceof ArgumentException)throw WrappedInvalidData('Invalid OLE2FRAME metadata.',error);throw error;}
  } finally {payload.Dispose();}
}
export function WriteOle2Frame(chunk, version, frame) {
  chunk.Write(100,'AcDbOle2Frame');if(frame==null)throw new NullReferenceException();const fields=frame.MetadataFields;
  if(fields&F.OleVersion)chunk.Write(70,frame.OleVersion);
  if(fields&F.Description)chunk.Write(3,EncodeDxfDatabaseText(frame.Description,version));
  if(fields&F.UpperLeftCorner){chunk.Write(10,frame.UpperLeftCorner.X);chunk.Write(20,frame.UpperLeftCorner.Y);chunk.Write(30,frame.UpperLeftCorner.Z);}
  if(fields&F.LowerRightCorner){chunk.Write(11,frame.LowerRightCorner.X);chunk.Write(21,frame.LowerRightCorner.Y);chunk.Write(31,frame.LowerRightCorner.Z);}
  if(fields&F.ObjectType)chunk.Write(71,frame.ObjectType);if(fields&F.TileMode)chunk.Write(72,frame.TileMode);
  chunk.Write(90,frame.BinaryDataLength);
  const data=frame.BinaryData;
  for(let offset=0;offset<data.length;){const count=Math.min(127,data.length-offset),part=new Uint8Array(count);part.set(data.subarray(offset,offset+count));chunk.Write(310,part);offset+=count;}
  chunk.Write(1,'OLE');WriteXData(chunk,version,frame.XData);
}
