// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { DxfOpaqueObject } from '../Objects/DxfOpaqueObject.js';
import { ReadStoredObjectHeader } from './DxfReader.LayerFilterPointer.js';
export function TryReadPrivateXRecord(context,tags,output){
  output.value=null;let depth=0,start=-1;
  for(let i=0;i<tags.length;i++){
    const tag=tags[i];if(tag.Code===102){if(tag.Value.startsWith('{'))depth++;else if(tag.Value==='}'&&depth>0)depth--;}
    else if(depth===0&&(tag.Code===100||tag.Code===1001)){if(tag.Code===100&&tag.Value==='AcDbXrecord')start=i;break;}
  }
  if(start<0)return false;
  let unsupported=false,end=tags.length;depth=0;
  for(let i=start+1;i<tags.length;i++){
    const tag=tags[i];if(tag.Code===102){if(tag.Value.startsWith('{'))depth++;else if(tag.Value==='}'&&depth>0)depth--;}
    if(tag.Code===1001&&depth===0){end=i;break;}if(tag.Code>=1000)unsupported=true;
  }
  if(!unsupported)return false;
  const {record,opaque,payload,handle}=ReadStoredObjectHeader(tags);output.value=record;
  for(const tag of tags.slice(payload,end))opaque.push(tag);
  record.Object=new DxfOpaqueObject('XRECORD',opaque);record.Object.Handle=handle;
  if(end<tags.length)context.ReadDatabaseXData(record.Object,tags,end);return true;
}
