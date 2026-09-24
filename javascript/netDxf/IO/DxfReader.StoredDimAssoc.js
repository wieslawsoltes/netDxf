// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { DxfStoredDimAssoc,DxfStoredDimAssocPoint } from '../Objects/DxfStoredDimAssoc.js';
import { DxfOpaqueObject } from '../Objects/DxfOpaqueObject.js';
import { Vector3 } from '../Vector3.js';
import { ReadStoredObjectHeader } from './DxfReader.LayerFilterPointer.js';
import { PayloadEnd } from '../../runtime/DatabaseIOContext.js';
import { SourceHandle } from './DxfReader.SourceIdentity.js';
import { FormatException } from '../../runtime/Errors.js';
const codes=new Set([330,90,70,71,72,73,91,40,10,20,30,75]);
export function ReadStoredDimAssocRecord(context,tags) {
  const {record,opaque,payload:start,handle}=ReadStoredObjectHeader(tags),end=PayloadEnd(tags,start),body=tags.slice(start,end);
  if(!body.length||body[0].Code!==100)throw new FormatException('DIMASSOC requires its subclass marker.');
  let unknown=opaque.length!==0||body[0].Value!=='AcDbDimAssoc',mainHandles=0;
  for(const tag of body.slice(1)) {
    if(tag.Code===100&&tag.Value==='AcDbDimAssoc')throw new FormatException('DIMASSOC repeats its public subclass marker.');
    if(tag.Code===1){mainHandles=0;if(tag.Value!=='AcDbOsnapPointRef')unknown=true;}
    else if(tag.Code===331){if(++mainHandles>1)unknown=true;}
    else if(tag.Code===72&&tag.Value>=0&&tag.Value<=13&&![1,3,13].includes(tag.Value))unknown=true;
    else if(tag.Code===75&&tag.Value===1)unknown=true;
    else if(!codes.has(tag.Code))unknown=true;
  }
  if(unknown){for(const tag of tags.slice(start))opaque.push(tag);record.Object=new DxfOpaqueObject('DIMASSOC',opaque);record.Object.Handle=handle;return record;}
  if(new Set(record.Metadata.Reactors.map(SourceHandle)).size!==record.Metadata.Reactors.length)throw new FormatException('DIMASSOC repeats a persistent-reactor identity.');
  let index=1;const take=code=>{if(index>=body.length||body[index].Code!==code)throw new FormatException('DIMASSOC has a missing, duplicate or misplaced group '+code+'.');return body[index++].Value;};
  const dimension=take(330),mask=take(90),trans=take(70),rotated=take(71);
  if(mask<0||mask>15||trans<0||trans>1||rotated<0||rotated>1)throw new FormatException('DIMASSOC contains an invalid mask, trans-space flag or rotated type.');
  const points=[];
  for(let slot=0;slot<4;slot++)if(mask&(1<<slot)) {
    take(1);const osnap=take(72);
    if(![1,3,13].includes(osnap))throw new FormatException('DIMASSOC osnap type is outside the qualified public range.');
    const geometry=take(331),subentity=take(73),marker=take(91),parameter=take(40),point=new Vector3(take(10),take(20),take(30));
    if(take(75)!==0)throw new FormatException('DIMASSOC last-point flag must be zero or one.');
    points.push(new DxfStoredDimAssocPoint(slot,osnap,geometry,subentity,marker,parameter,point));
  }
  if(index!==body.length)throw new FormatException('DIMASSOC point-reference count does not match its mask.');
  const association=new DxfStoredDimAssoc(context.Document,body,dimension,mask,trans,rotated,points);association.Handle=handle;record.Object=association;
  if(end<tags.length)context.ReadDatabaseXData(association,tags,end);context.storedDimAssocs.push(association);return record;
}
export function ResolveStoredDimAssocReferences(context){for(const item of context.storedDimAssocs)item.Resolve(h=>context.GetObjectBySourceHandle(h));}
