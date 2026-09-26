// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { DxfSpatialIndex } from '../Objects/DxfSpatialIndex.js';
import { DxfVbaProject } from '../Objects/DxfVbaProject.js';
import { PayloadEnd } from '../../runtime/DatabaseIOContext.js';
import { FormatException } from '../../runtime/Errors.js';
export function ReadStoredEnvelopePayload(context,record,type,tags,start) {
  if(type!=='SPATIAL_INDEX'&&type!=='VBA_PROJECT')return false;
  const end=PayloadEnd(tags,start),first=type==='SPATIAL_INDEX'?'AcDbIndex':'AcDbVbaProject';
  if(start>=end||tags[start].Code!==100)throw new FormatException(type+' requires its public subclass marker.');
  if(tags[start].Value!==first){if(type==='SPATIAL_INDEX'&&tags[start].Value==='AcDbSpatialIndex')throw new FormatException('SPATIAL_INDEX is missing its AcDbIndex base subclass.');return false;}
  let unknown=false,publicScope=true,spatialMarker=false,hasValue=false,timestamp=0,count=0,length=0;const chunks=[];
  for(let i=start+1;i<end;i++) {
    const tag=tags[i];
    if(tag.Code===100) {
      const marker=tag.Value;if(marker===first)throw new FormatException(type+' repeats its public subclass marker.');
      if(type==='SPATIAL_INDEX'&&marker==='AcDbSpatialIndex') {
        if(spatialMarker||!hasValue)throw new FormatException('SPATIAL_INDEX requires one timestamp before its unique spatial subclass.');spatialMarker=true;publicScope=true;
      }else{unknown=true;publicScope=false;}continue;
    }
    if(!publicScope)continue;
    if(type==='SPATIAL_INDEX'&&tag.Code===40) {
      if(hasValue||spatialMarker)throw new FormatException('SPATIAL_INDEX repeats or misplaces its timestamp.');
      timestamp=tag.Value;if(!Number.isFinite(timestamp))throw new FormatException('SPATIAL_INDEX timestamp must be finite.');hasValue=true;
    }else if(type==='VBA_PROJECT'&&tag.Code===90) {
      if(hasValue)throw new FormatException('VBA_PROJECT repeats its byte count.');count=tag.Value;
      if(count<0||count>DxfVbaProject.MaximumDataLength)throw new FormatException('VBA_PROJECT byte count exceeds the admitted range.');hasValue=true;
    }else if(type==='VBA_PROJECT'&&tag.Code===310) {
      const chunk=tag.Value;if(!hasValue||chunk.length>DxfVbaProject.MaximumChunkLength||chunks.length===DxfVbaProject.MaximumChunkCount||chunk.length>count-length)throw new FormatException('VBA_PROJECT chunks violate their count, order, or admission limits.');
      chunks.push(chunk);length+=chunk.length;
    }else unknown=true;
  }
  if(!hasValue||(type==='SPATIAL_INDEX'&&!spatialMarker)||(type==='VBA_PROJECT'&&length!==count))throw new FormatException(type+' has an incomplete public envelope.');
  if(unknown)return false;
  if(type==='SPATIAL_INDEX'){record.Object=new DxfSpatialIndex();record.Object.Timestamp=timestamp;}
  else{const project=new DxfVbaProject();project.SetChunks(chunks);record.Object=project;}
  if(end<tags.length)context.ReadDatabaseXData(record.Object,tags,end);return true;
}
