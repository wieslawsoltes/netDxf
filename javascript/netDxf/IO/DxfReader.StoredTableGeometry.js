// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { DxfStoredTableGeometry } from '../Objects/DxfStoredTableGeometry.js';
import { DxfOpaqueObject } from '../Objects/DxfOpaqueObject.js';
import { ReadStoredObjectHeader } from './DxfReader.LayerFilterPointer.js';
import { PayloadEnd } from '../../runtime/DatabaseIOContext.js';
import { DecodeDxfText } from '../../runtime/DxfStringEncoding.js';
import { FormatException } from '../../runtime/Errors.js';
const codes=new Set([100,90,91,92,93,40,41,330,94,10,20,30,11,21,31,43,44,45,46,95]);
export function ReadStoredTableGeometryRecord(context,tags) {
  if(tags.length>DxfStoredTableGeometry.MaximumPayloadTags)throw new FormatException('TABLEGEOMETRY exceeds the supported storage packet limit.');
  const {record,opaque,payload:start,handle}=ReadStoredObjectHeader(tags),end=PayloadEnd(tags,start),body=tags.slice(start,end);
  const known=context.Document.DrawingVariables.AcadVer>=14&&!opaque.length&&body.some(t=>t.Code===100&&t.Value==='AcDbTableGeometry')&&body.every(t=>t.Code!==100||t.Value==='AcDbTableGeometry')&&body.every(t=>codes.has(t.Code)||(t.Code>=1000&&t.Code<=1071));
  if(known) {
    if(body.some(t=>t.Code>=1000&&t.Code<=1071))throw new FormatException('TABLEGEOMETRY extended data must begin with an application registry.');
    const item=new DxfStoredTableGeometry(context.Document,body);item.Handle=handle;record.Object=item;
    if(end<tags.length)context.ReadDatabaseXData(item,tags,end);context.storedTableGeometries.push(item);
  }else{for(const tag of tags.slice(start))opaque.push(tag);record.Object=new DxfOpaqueObject('TABLEGEOMETRY',opaque);record.Object.Handle=handle;}
  return record;
}
export function ResolveStoredTableGeometryReferences(context){for(const item of context.storedTableGeometries)item.Resolve(h=>context.GetObjectBySourceHandle(h,true));}
