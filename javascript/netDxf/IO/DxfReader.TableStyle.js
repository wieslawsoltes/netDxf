// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { DxfTableStyle } from '../Objects/DxfTableStyle.js';
import { ReadStoredObjectHeader } from './DxfReader.LayerFilterPointer.js';
import { PayloadEnd } from '../../runtime/DatabaseIOContext.js';
import { DecodeDxfText } from '../../runtime/DxfStringEncoding.js';
import { FormatException } from '../../runtime/Errors.js';
export function ReadTableStyleRecord(context,tags) {
  const {record,opaque:retained,payload:start,handle}=ReadStoredObjectHeader(tags),end=PayloadEnd(tags,start);
  if(start>=end||tags[start].Code!==100)throw new FormatException('TABLESTYLE requires a subclass payload.');
  if(tags.slice(start,end).some(t=>t.Code>=1000&&t.Code<=1071))throw new FormatException('TABLESTYLE extended data must begin with an application registry.');
  for(const tag of tags.slice(start,end))retained.push(tag);
  const style=new DxfTableStyle(context.Document,retained,DecodeDxfText);style.Handle=handle;record.Object=style;
  if(end<tags.length)context.ReadDatabaseXData(style,tags,end);context.tableStyles.push(style);return record;
}
export function ResolveTableStyleReferences(context){for(const item of context.tableStyles)item.Resolve(h=>context.GetObjectBySourceHandle(h,true),DecodeDxfText);}
