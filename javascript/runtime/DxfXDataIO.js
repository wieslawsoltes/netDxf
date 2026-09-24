// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
// Source-derived ReadXDataRecord/WriteXData adapters shared by entity body codecs.
import { XData } from '../netDxf/XData.js';
import { XDataRecord } from '../netDxf/XDataRecord.js';
import { ApplicationRegistry } from '../netDxf/Tables/ApplicationRegistry.js';
import { DecodeDxfText, EncodeDxfText } from './DxfStringEncoding.js';
const realCodes = new Set([1010,1020,1030,1011,1021,1031,1012,1022,1032,1013,1023,1033,1040,1041,1042]);
export function ReadXDataRecord(chunk, document) {
  const name=DecodeDxfText(chunk.ReadString()), found={};
  const app=document.ApplicationRegistries.TryGetValue(name,found)?found.value:document.ApplicationRegistries.Add(new ApplicationRegistry(name));
  const result=new XData(app); chunk.Next();
  while(chunk.Code>=1000 && chunk.Code<=1071 && chunk.Code!==1001) {
    const code=chunk.Code; let value=null;
    if(code===1000 || code===1003)value=DecodeDxfText(chunk.ReadString());
    else if(code===1002 || code===1005)value=chunk.ReadString();
    else if(code===1004)value=chunk.ReadBytes();
    else if(realCodes.has(code))value=chunk.ReadDouble();
    else if(code===1070)value=chunk.ReadShort();
    else if(code===1071)value=chunk.ReadInt();
    result.XDataRecord.Add(new XDataRecord(code,value)); chunk.Next();
  }
  return result;
}
export function WriteXData(chunk, version, xdata) {
  for(const app of xdata.AppIds) {
    chunk.Write(1001,EncodeDxfText(app,version));
    for(const record of xdata.get_Item(app).XDataRecord) {
      const code=record.Code,value=record.Value;
      if(code===1000 || code===1003)chunk.Write(code,EncodeDxfText(value,version));
      else if(code===1004) {
        let index=0;
        while(value.length-index>127) { const part=new Uint8Array(127); part.set(value.subarray(index,index+127));chunk.Write(code,part);index+=127; }
        const last=new Uint8Array(value.length-index); last.set(value.subarray(index));chunk.Write(code,last);
      } else chunk.Write(code,value);
    }
  }
}

import { InvalidDataException } from './Errors.js';
export function WrappedInvalidData(message, cause) {const error=new InvalidDataException(message,{cause});error.InnerException=cause;return error;}
