// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { DxfLightList,DxfLightListEntry } from '../Objects/DxfLightList.js';
import { Light } from '../Entities/Light.js';
import { PayloadEnd,WrappedFormat } from '../../runtime/DatabaseIOContext.js';
import { DecodeDxfText } from '../../runtime/DxfStringEncoding.js';
import { FormatException,ArgumentException } from '../../runtime/Errors.js';
export function ReadLightListPayload(context,record,type,tags,start=0){
  if(type!=='LIGHTLIST'||context.Document.DrawingVariables.AcadVer<15)return false;
  const end=PayloadEnd(tags,start);
  if(start===end||tags[start].Code!==100||tags[start].Value!=='AcDbLightList')return false;
  for(let i=start+1;i<end;i++){const tag=tags[i];if(tag.Code===100&&tag.Value!=='AcDbLightList'||![100,90,5,1].includes(tag.Code))return false;}
  if(end-start<3||tags[start+1].Code!==90||tags[start+2].Code!==90)throw new FormatException('LIGHTLIST requires version and count group-90 fields.');
  const count=tags[start+2].Value,available=end-start-3;
  if(count<0||available%2!==0||count!==available/2)throw new FormatException('LIGHTLIST count does not match complete LIGHT/name pairs.');
  const value=new DxfLightList(tags[start+1].Value),references=[];
  for(let i=start+3;i<end;i+=2){
    if(tags[i].Code!==5||tags[i+1].Code!==1)throw new FormatException('LIGHTLIST requires ordered group-5 LIGHT/group-1 name pairs.');references.push([tags[i].Value,DecodeDxfText(tags[i+1].Value)]);
  }
  if(end<tags.length)context.ReadDatabaseXData(value,tags,end);record.Object=value;context.lightListReferences.push([value,references]);return true;
}
export function ResolveLightListReferences(context){
  for(const [list,entries]of context.lightListReferences)for(const [handle,name]of entries){
    const light=context.Document.GetObjectByHandle(handle);if(!(light instanceof Light))throw new FormatException('LIGHTLIST reference does not identify a LIGHT: '+handle);
    try{list.Entries.Add(new DxfLightListEntry(light,name));}catch(error){if(error instanceof ArgumentException)throw WrappedFormat('Invalid LIGHTLIST stored name.',error);throw error;}
  }
}
