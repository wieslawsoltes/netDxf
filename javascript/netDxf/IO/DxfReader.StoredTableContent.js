// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { DxfStoredTableContent } from '../Objects/DxfStoredTableContent.js';
import { DxfOpaqueObject } from '../Objects/DxfOpaqueObject.js';
import { ReadStoredObjectHeader } from './DxfReader.LayerFilterPointer.js';
import { PayloadEnd } from '../../runtime/DatabaseIOContext.js';
import { DecodeDxfText } from '../../runtime/DxfStringEncoding.js';
import { FormatException } from '../../runtime/Errors.js';
const names=['AcDbLinkedData','AcDbLinkedTableData','AcDbFormattedTableData','AcDbTableContent'];
export function ReadStoredTableContentRecord(context,tags) {
  if(tags.length>DxfStoredTableContent.MaximumPayloadTags)throw new FormatException('TABLECONTENT exceeds the supported storage packet limit.');
  const {record,opaque,payload:start,handle}=ReadStoredObjectHeader(tags),end=PayloadEnd(tags,start),body=tags.slice(start,end);
  const subclasses=body.filter(t=>t.Code===100).map(t=>t.Value);
  if(context.Document.DrawingVariables.AcadVer>=14&&subclasses.length&&subclasses.every(s=>names.includes(s))&&body.every(t=>t.Code!==102)&&(subclasses.length!==names.length||subclasses.some((s,i)=>s!==names[i])))throw new FormatException('TABLECONTENT requires the complete ordered public subclass hierarchy.');

  const known=context.Document.DrawingVariables.AcadVer>=14&&!opaque.length&&body.some(t=>t.Code===100&&names.includes(t.Value))&&body.every(t=>t.Code!==102&&(t.Code!==100||names.includes(t.Value)));
  if(known) {
    if(body.some(t=>t.Code>=1000&&t.Code<=1071))throw new FormatException('TABLECONTENT extended data must begin with an application registry.');
    const item=new DxfStoredTableContent(context.Document,body,DecodeDxfText);item.Handle=handle;record.Object=item;
    if(end<tags.length)context.ReadDatabaseXData(item,tags,end);context.storedTableContents.push(item);
  }else{for(const tag of tags.slice(start))opaque.push(tag);record.Object=new DxfOpaqueObject('TABLECONTENT',opaque);record.Object.Handle=handle;}
  return record;
}
export function ResolveStoredTableContentReferences(context){for(const item of context.storedTableContents)item.Resolve(h=>context.GetObjectBySourceHandle(h,true));}
