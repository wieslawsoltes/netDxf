// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { DxfStoredSunStudy } from '../Objects/DxfStoredSunStudy.js';
import { DxfOpaqueObject } from '../Objects/DxfOpaqueObject.js';
import { ReadStoredObjectHeader } from './DxfReader.LayerFilterPointer.js';
import { DecodeDxfText } from '../../runtime/DxfStringEncoding.js';
import { FormatException } from '../../runtime/Errors.js';
const prefix=[100,90,1,2,70,3,290,4,291,91,292,93,94,95,73],suffix=[340,341,342,74,75,76,77,40,293,294,343];
export function IsStoredSunStudyShape(body) {
  if(body.length<prefix.length||prefix.some((c,i)=>body[i].Code!==c)||body[0].Value!=='AcDbSunStudy'||body[1].Value!==0||body[9].Value!==0)return false;
  const hours=body[14].Value;let i=prefix.length;while(i<body.length&&body[i].Code===290)i++;
  if(body.length-i!==suffix.length||suffix.some((c,j)=>body[i+j].Code!==c))return false;
  if(hours<0||hours!==i-prefix.length)throw new FormatException('SUNSTUDY hour count differs from its ordered group-290 flags.');
  if(!Number.isFinite(body[i+7].Value))throw new FormatException('SUNSTUDY spacing must be finite.');return true;
}
export function ReadStoredSunStudyRecord(context,tags) {
  const {record,opaque,payload:start,handle}=ReadStoredObjectHeader(tags);let depth=0,end=tags.length;
  for(let i=start;i<tags.length;i++) {
    const tag=tags[i];if(tag.Code===102){if(tag.Value.startsWith('{'))depth++;else if(tag.Value==='}'&&depth>0)depth--;}
    else if(depth===0&&tag.Code===1001){end=i;break;}
  }
  const body=tags.slice(start,end),profile=context.Document.DrawingVariables.AcadVer;
  if((profile===17||profile===18)&&!opaque.length&&IsStoredSunStudyShape(body)) {
    const study=new DxfStoredSunStudy(context.Document,body,DecodeDxfText);study.Handle=handle;record.Object=study;context.storedSunStudies.push(study);
    if(end<tags.length)context.ReadDatabaseXData(study,tags,end);
  }else{for(const tag of tags.slice(start))opaque.push(tag);record.Object=new DxfOpaqueObject('SUNSTUDY',opaque);record.Object.Handle=handle;}
  return record;
}
export function ResolveStoredSunStudyReferences(context){for(const item of context.storedSunStudies)item.Resolve(h=>context.GetObjectBySourceHandle(h));}
