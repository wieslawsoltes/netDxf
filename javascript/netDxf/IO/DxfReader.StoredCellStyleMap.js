// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { DxfStoredCellStyleMap } from '../Objects/DxfStoredCellStyleMap.js';
import { DxfOpaqueObject } from '../Objects/DxfOpaqueObject.js';
import { ReadStoredObjectHeader } from './DxfReader.LayerFilterPointer.js';
import { PayloadEnd } from '../../runtime/DatabaseIOContext.js';
import { DecodeDxfText } from '../../runtime/DxfStringEncoding.js';
import { FormatException } from '../../runtime/Errors.js';

export function ReadStoredCellStyleMapRecord(context,tags) {
  if(tags.length>DxfStoredCellStyleMap.MaximumPayloadTags)throw new FormatException('CELLSTYLEMAP exceeds the supported storage packet limit.');
  const {record,opaque,payload:start,handle}=ReadStoredObjectHeader(tags),end=PayloadEnd(tags,start),body=tags.slice(start,end);
  const known=context.Document.DrawingVariables.AcadVer>=14&&!opaque.length&&body.some(t=>t.Code===100&&t.Value==='AcDbCellStyleMap')&&body.every(t=>t.Code!==102&&(t.Code!==100||t.Value==='AcDbCellStyleMap'))&&body.every(t=>(t.Code!==1||DxfStoredCellStyleMap.FrameNames.some(f=>t.Value===f+'_BEGIN'))&&(t.Code!==309||DxfStoredCellStyleMap.FrameNames.some(f=>t.Value===f+'_END')));
  if(known) {
    if(body.some(t=>t.Code>=1000&&t.Code<=1071))throw new FormatException('CELLSTYLEMAP extended data must begin with an application registry.');
    const item=new DxfStoredCellStyleMap(context.Document,body,DecodeDxfText);item.Handle=handle;record.Object=item;
    if(end<tags.length)context.ReadDatabaseXData(item,tags,end);context.storedCellStyleMaps.push(item);
  }else{for(const tag of tags.slice(start))opaque.push(tag);record.Object=new DxfOpaqueObject('CELLSTYLEMAP',opaque);record.Object.Handle=handle;}
  return record;
}
export function ResolveStoredCellStyleMapReferences(context){for(const item of context.storedCellStyleMaps)item.Resolve(h=>context.GetObjectBySourceHandle(h,true));}
