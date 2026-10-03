// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { DxfLayerFilter } from '../Objects/DxfLayerFilter.js';
import { DxfObjectPointer } from '../Objects/DxfObjectPointer.js';
import { DxfOpaqueObject } from '../Objects/DxfOpaqueObject.js';
import { DatabaseRecord,PayloadEnd,WrappedFormat } from '../../runtime/DatabaseIOContext.js';
import { DecodeDxfText } from '../../runtime/DxfStringEncoding.js';
import { OrdinalIgnoreCaseEquals } from '../../runtime/Collections.js';
import { FormatException,ArgumentException } from '../../runtime/Errors.js';
// Explicit out-parameter adapter: record, opaque, payload and handle are returned together.
export function ReadStoredObjectHeader(tags){
  const record=new DatabaseRecord(),opaque=[];let handle=null,payload=0,reactorsSeen=false,extensionSeen=false;
  for(;payload<tags.length;payload++){
    const tag=tags[payload];if(tag.Code===100||tag.Code===1001)break;
    if(tag.Code===5){if(handle!==null)throw new FormatException('Duplicate object identity.');handle=tag.Value;}
    else if(tag.Code===330){
      if(record.Metadata.Owner===null)record.Metadata.Owner=tag.Value;
      else if(OrdinalIgnoreCaseEquals(record.Metadata.Owner,tag.Value))throw new FormatException('Duplicate object owner.');else opaque.push(tag);
    }else if(tag.Code===102){
      const start=payload,group=tag.Value;if(group.length===0||group[0]!=='{')throw new FormatException('Invalid object control-group opening.');
      while(++payload<tags.length&&!(tags[payload].Code===102&&tags[payload].Value==='}'))
        if([102,100,1001].includes(tags[payload].Code))throw new FormatException('Invalid object control-group framing.');
      if(payload===tags.length)throw new FormatException('Unterminated object control group.');
      const content=tags.slice(start+1,payload);
      if(group==='{ACAD_REACTORS'){
        if(reactorsSeen||content.some(v=>v.Code!==330))throw new FormatException('Invalid persistent-reactor group.');reactorsSeen=true;for(const value of content)record.Metadata.Reactors.push(value.Value);
      }else if(group==='{ACAD_XDICTIONARY'){
        if(content.length!==1||content[0].Code!==360||extensionSeen)throw new FormatException('Invalid extension-dictionary group.');extensionSeen=true;record.Metadata.Extension=content[0].Value;
      }else for(const value of tags.slice(start,payload+1))opaque.push(value);
    }else opaque.push(tag);
  }
  if(handle===null)throw new FormatException('A database object requires an identity.');return {record,opaque,payload,handle};
}
export function ReadLayerFilterPointerRecord(context,type,tags){
  const {record,opaque,payload,handle}=ReadStoredObjectHeader(tags),xdata=PayloadEnd(tags,payload);
  let known=opaque.length===0;
  if(type==='OBJECT_PTR')known=known&&payload===xdata;
  else known=known&&xdata-payload>=2&&tags[payload].Code===100&&tags[payload].Value==='AcDbFilter'&&tags[payload+1].Code===100&&tags[payload+1].Value==='AcDbLayerFilter'&&tags.slice(payload+2,xdata).every(v=>v.Code===8);
  if(known){
    if(type==='OBJECT_PTR')record.Object=new DxfObjectPointer();
    else try{record.Object=new DxfLayerFilter(tags.slice(payload+2,xdata).map(v=>DecodeDxfText(v.Value)));}
    catch(error){if(error instanceof ArgumentException)throw WrappedFormat('Invalid stored layer-filter name.',error);throw error;}
    if(xdata<tags.length)context.ReadDatabaseXData(record.Object,tags,xdata);
  }else{for(const value of tags.slice(payload))opaque.push(value);record.Object=new DxfOpaqueObject(type,opaque);}
  record.Object.Handle=handle;return record;
}
