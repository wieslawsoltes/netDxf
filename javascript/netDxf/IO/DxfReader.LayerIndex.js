// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { DxfLayerIndex,DxfLayerIndexEntry } from '../Objects/DxfLayerIndex.js';
import { DxfIdBuffer } from '../Objects/DxfIdBuffer.js';import { DxfOpaqueObject } from '../Objects/DxfOpaqueObject.js';import { DxfDictionary } from '../Objects/DxfDatabaseObject.js';
import { ReadStoredObjectHeader } from './DxfReader.LayerFilterPointer.js';
import { PayloadEnd,WrappedFormat } from '../../runtime/DatabaseIOContext.js';
import { DecodeDxfText } from '../../runtime/DxfStringEncoding.js';import { OrdinalIgnoreCaseKey } from '../../runtime/Collections.js';
import { FormatException,ArgumentException } from '../../runtime/Errors.js';
export function ReadLayerIndexRecord(context,tags){
  const {record,opaque,payload:start,handle}=ReadStoredObjectHeader(tags),end=PayloadEnd(tags,start);
  let unknown=opaque.length!==0,timestamp=0,hasTimestamp=false,hasLayerMarker=false,publicScope=true;
  const names=[],buffers=[],counts=[];
  if(start>=end||tags[start].Code!==100)throw new FormatException('LAYER_INDEX requires its public subclass marker.');
  if(tags[start].Value!=='AcDbIndex'){
    if(tags[start].Value==='AcDbLayerIndex')throw new FormatException('LAYER_INDEX is missing AcDbIndex.');
    for(const tag of tags.slice(start))opaque.push(tag);record.Object=new DxfOpaqueObject('LAYER_INDEX',opaque);record.Object.Handle=handle;return record;
  }
  for(let i=start+1;i<end;i++){
    const tag=tags[i];
    if(tag.Code===100){
      if(tag.Value==='AcDbIndex')throw new FormatException('LAYER_INDEX repeats AcDbIndex.');
      if(tag.Value==='AcDbLayerIndex'){
        if(hasLayerMarker||!hasTimestamp)throw new FormatException('LAYER_INDEX requires one timestamp before AcDbLayerIndex.');hasLayerMarker=true;publicScope=true;
      }else{unknown=true;publicScope=false;}continue;
    }
    if(!publicScope)continue;
    if(tag.Code===40){
      if(hasTimestamp||hasLayerMarker)throw new FormatException('LAYER_INDEX repeats or misplaces its timestamp.');timestamp=tag.Value;
      if(!Number.isFinite(timestamp))throw new FormatException('LAYER_INDEX timestamp must be finite.');hasTimestamp=true;
    }else if(!hasLayerMarker&&[8,360,90].includes(tag.Code))throw new FormatException('LAYER_INDEX entry fields require AcDbLayerIndex.');
    else if(hasLayerMarker&&tag.Code===8)names.push(DecodeDxfText(tag.Value));
    else if(hasLayerMarker&&tag.Code===360){if(tag.Value==='0')throw new FormatException('LAYER_INDEX requires nonnull IDBUFFER ownership handles.');buffers.push(tag.Value);}
    else if(hasLayerMarker&&tag.Code===90){
      if(names.length===0&&buffers.length===0&&counts.length===0)unknown=true;
      if(tag.Value<0)throw new FormatException('LAYER_INDEX IDBUFFER counts cannot be negative.');counts.push(tag.Value);
    }else unknown=true;
  }
  if(!unknown&&(!hasTimestamp||!hasLayerMarker||names.length!==buffers.length||names.length!==counts.length))throw new FormatException('LAYER_INDEX has incomplete timestamp, subclass, or entry data.');
  if(unknown){for(const tag of tags.slice(start))opaque.push(tag);record.Object=new DxfOpaqueObject('LAYER_INDEX',opaque);}
  else{
    const index=new DxfLayerIndex(),entries=[],distinct=new Set();index.Timestamp=timestamp;
    for(let i=0;i<names.length;i++){
      const key=OrdinalIgnoreCaseKey(buffers[i]);if(distinct.has(key))throw new FormatException('LAYER_INDEX repeats an owned IDBUFFER.');distinct.add(key);
      try{new DxfLayerIndexEntry(names[i],new DxfIdBuffer());}catch(error){if(error instanceof ArgumentException)throw WrappedFormat('Invalid stored layer-index name.',error);throw error;}
      entries.push([names[i],buffers[i],counts[i]]);
    }
    record.Object=index;if(end<tags.length)context.ReadDatabaseXData(index,tags,end);context.pendingLayerIndexes.set(index,entries);
  }
  record.Object.Handle=handle;return record;
}
export function ResolveLayerIndexReferences(context){
  for(const [index,pending]of context.pendingLayerIndexes){
    if(!(index.Owner instanceof DxfDictionary))throw new FormatException('LAYER_INDEX requires a dictionary owner.');const entries=[];
    for(const [name,handle,count]of pending){
      const buffer=context.GetObjectBySourceHandle(handle);
      if(!(buffer instanceof DxfIdBuffer)||buffer.Owner!==index)throw new FormatException('LAYER_INDEX ownership target must be a reciprocally owned IDBUFFER: '+handle);
      if(buffer.References.Count!==count)throw new FormatException('LAYER_INDEX stored count does not match its IDBUFFER: '+handle);
      entries.push(new DxfLayerIndexEntry(name,buffer));
    }
    try{index.LoadEntries(entries);}catch(error){if(error instanceof ArgumentException)throw WrappedFormat('Invalid LAYER_INDEX ownership graph.',error);throw error;}
  }
}
