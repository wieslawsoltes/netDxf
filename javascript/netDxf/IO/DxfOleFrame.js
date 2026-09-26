import { NullReferenceException } from '../../runtime/Errors.js';
// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { OleFrame } from '../Entities/OleFrame.js';
import { MemoryStream } from '../../runtime/MemoryStream.js';
import { InvalidDataException } from '../../runtime/Errors.js';
import { ReadXDataRecord, WriteXData } from '../../runtime/DxfXDataIO.js';
/** Read the original AcDbOleFrame body; leave the following group-0 record current. */
export function ReadOleFrame(chunk, document) {
  if(chunk.Code!==100 || chunk.ReadString()!=='AcDbOleFrame')throw new InvalidDataException('OLEFRAME requires AcDbOleFrame.');
  let version=1,length=-1,versionSeen=false,lengthSeen=false,terminated=false;
  const xdata=[],payload=new MemoryStream();
  try {
    chunk.Next();
    while(chunk.Code!==0) {
      const code=chunk.Code;
      if(code===1001 && terminated) { xdata.push(ReadXDataRecord(chunk,document));continue; }
      if(terminated)throw new InvalidDataException('Unexpected data after OLEFRAME terminator.');
      switch(code) {
        case 70:
          if(versionSeen)throw new InvalidDataException('Duplicate OLEFRAME version.');
          versionSeen=true;version=chunk.ReadShort();
          if(version<0)throw new InvalidDataException('Negative OLEFRAME version.');break;
        case 90:
          if(lengthSeen)throw new InvalidDataException('Duplicate OLEFRAME byte count.');
          lengthSeen=true;length=chunk.ReadInt();
          if(length<0 || payload.Length>length)throw new InvalidDataException('Invalid OLEFRAME byte count.');break;
        case 310: {
          const part=chunk.ReadBytes();
          if(part.length>127 || payload.Length+part.length>2147483647 || lengthSeen && payload.Length+part.length>length)
            throw new InvalidDataException('OLEFRAME chunks exceed the allowed byte count.');
          payload.Write(part,0,part.length);break;
        }
        case 1:if(chunk.ReadString()!=='OLE')throw new InvalidDataException('Invalid OLEFRAME terminator.');terminated=true;break;
        default:throw new InvalidDataException('Unsupported OLEFRAME group '+code+'; use DxfRawDocument for private extensions.');
      }
      chunk.Next();
    }
    if(!lengthSeen || !terminated || payload.Length!==length)throw new InvalidDataException('Incomplete OLEFRAME packet or binary length mismatch.');
    const result=new OleFrame(payload.ToArray(),version,false,versionSeen);
    for(const data of xdata)result.XData.Add(data);
    return result;
  } finally {payload.Dispose();}
}
export function WriteOleFrame(chunk, version, frame) {
  chunk.Write(100,'AcDbOleFrame');if(frame==null)throw new NullReferenceException();
  if(frame.HasOleVersion)chunk.Write(70,frame.OleVersion);
  chunk.Write(90,frame.BinaryDataLength);
  const data=frame.BinaryData;
  for(let offset=0;offset<data.length;) { const count=Math.min(127,data.length-offset),part=new Uint8Array(count);part.set(data.subarray(offset,offset+count));chunk.Write(310,part);offset+=count; }
  chunk.Write(1,'OLE');WriteXData(chunk,version,frame.XData);
}
