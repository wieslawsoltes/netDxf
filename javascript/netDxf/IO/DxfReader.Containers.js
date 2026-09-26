// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { DxfIdBuffer } from '../Objects/DxfIdBuffer.js';
import { DxfSortentsTable,DxfSortOrderEntry } from '../Objects/DxfSortentsTable.js';
import { DxfSpatialFilter } from '../Objects/DxfSpatialFilter.js';
import { BlockRecord } from '../Blocks/BlockRecord.js';
import { EntityObject } from '../Entities/EntityObject.js';
import { Vector2 } from '../Vector2.js';import { Vector3 } from '../Vector3.js';import { Matrix4 } from '../Matrix4.js';
import { FormatException } from '../../runtime/Errors.js';
import { PayloadEnd } from '../../runtime/DatabaseIOContext.js';
export function RequireContainerMarker(tags,index,marker){
  if(index>=tags.length||tags[index].Code!==100||tags[index].Value!==marker)throw new FormatException('Missing '+marker+' subclass.');
}
export function ReadContainerPayload(context,record,type,tags,start=0){
  if(!['IDBUFFER','SORTENTSTABLE','SPATIAL_FILTER'].includes(type))return false;
  const end=PayloadEnd(tags,start),body=tags.slice(start,end);
  if(type==='IDBUFFER'){
    RequireContainerMarker(body,0,'AcDbIdBuffer');record.Object=new DxfIdBuffer();
    for(let i=1;i<body.length;i++){if(body[i].Code!==330)throw new FormatException('IDBUFFER contains an unsupported field.');record.ContainerReferences.push(body[i].Value);}
  }else if(type==='SORTENTSTABLE'){
    RequireContainerMarker(body,0,'AcDbSortentsTable');
    if(body.length<2||body[1].Code!==330||body[1].Value==='0')throw new FormatException('SORTENTSTABLE requires its block record pointer.');
    record.Object=new DxfSortentsTable();record.ContainerReferences.push(body[1].Value);
    for(let i=2;i<body.length;i+=2){
      if(i+1>=body.length||body[i].Code!==331||body[i+1].Code!==5||body[i].Value==='0')throw new FormatException('SORTENTSTABLE requires complete nonnull entity/key pairs.');
      record.ContainerReferences.push(body[i].Value);record.SortKeys.push(body[i+1].Value);
    }
  }else record.Object=ReadSpatialFilterPayload(body);
  if(end<tags.length)context.ReadDatabaseXData(record.Object,tags,end);return true;
}
export function ContainerFlag(fields,code,fallback){
  if(!fields.has(code))return fallback;const value=fields.get(code).Value;
  if(value!==0&&value!==1)throw new FormatException('SPATIAL_FILTER flags must be zero or one.');return value===1;
}
export function ContainerVector(fields,code,fallback){
  const x=fields.has(code),y=fields.has(code+10),z=fields.has(code+20);
  if(!x&&!y&&!z)return fallback;if(!x||!y||!z)throw new FormatException('SPATIAL_FILTER vector is missing an axis.');
  return new Vector3(fields.get(code).Value,fields.get(code+10).Value,fields.get(code+20).Value);
}
export function ContainerMatrix(values,offset){return new Matrix4(...values.slice(offset,offset+12),0,0,0,1);}
export function ReadSpatialFilterPayload(tags){
  RequireContainerMarker(tags,0,'AcDbFilter');RequireContainerMarker(tags,1,'AcDbSpatialFilter');
  const fields=new Map(),boundary=[],matrix=[];let pointX=null,pointComplete=false;
  for(const tag of tags.slice(2)){
    switch(tag.Code){
      case 10:if(pointX!==null)throw new FormatException('SPATIAL_FILTER boundary point is missing group 20.');pointX=tag.Value;pointComplete=false;break;
      case 20:if(pointX===null)throw new FormatException('SPATIAL_FILTER boundary point is missing group 10.');boundary.push(new Vector2(pointX,tag.Value));pointX=null;pointComplete=true;break;
      case 30:if(!pointComplete||tag.Value!==0)throw new FormatException('SPATIAL_FILTER boundary vertices must be 2D.');pointComplete=false;break;
      case 40:matrix.push(tag.Value);break;
      case 70:case 71:case 72:case 73:case 210:case 220:case 230:case 11:case 21:case 31:case 41:
        if(fields.has(tag.Code))throw new FormatException('Duplicate SPATIAL_FILTER scalar field: '+tag.Code);fields.set(tag.Code,tag);break;
      default:throw new FormatException('Unsupported SPATIAL_FILTER field: '+tag.Code);
    }
  }
  if(pointX!==null||!fields.has(70)||fields.get(70).Value!==boundary.length)throw new FormatException('SPATIAL_FILTER boundary count or coordinates are incomplete.');
  const enabled=ContainerFlag(fields,71,true),front=ContainerFlag(fields,72,false),back=ContainerFlag(fields,73,false);
  if(matrix.length!==(front?25:24))throw new FormatException('SPATIAL_FILTER requires two complete affine matrices and its enabled front-plane distance.');
  if(fields.has(41)!==back)throw new FormatException('SPATIAL_FILTER back-plane distance does not match its enabled flag.');
  const result=new DxfSpatialFilter();result.IsClippingEnabled=enabled;result.Normal=ContainerVector(fields,210,Vector3.UnitZ);result.Origin=ContainerVector(fields,11,Vector3.Zero);
  result.FrontClippingDistance=front?matrix[0]:null;result.BackClippingDistance=back?fields.get(41).Value:null;result.SetBoundary(boundary);
  const offset=front?1:0;result.InverseInsertTransform=ContainerMatrix(matrix,offset);result.ClipBoundaryTransform=ContainerMatrix(matrix,offset+12);return result;
}
export function ResolveContainerReferences(context,record){
  const item=record.Object;
  if(item instanceof DxfIdBuffer){
    for(const handle of record.ContainerReferences){const target=handle==='0'?null:context.GetObjectBySourceHandle(handle);
      if(target===null&&handle!=='0')throw new FormatException('Unresolved IDBUFFER reference: '+handle);item.References.Add(target);}
  }else if(item instanceof DxfSortentsTable){
    const block=context.GetObjectBySourceHandle(record.ContainerReferences[0]);if(!(block instanceof BlockRecord))throw new FormatException('SORTENTSTABLE block pointer does not identify a block record.');item.BlockRecord=block;
    for(let i=1;i<record.ContainerReferences.length;i++){
      const entity=context.GetObjectBySourceHandle(record.ContainerReferences[i]);if(!(entity instanceof EntityObject))throw new FormatException('SORTENTSTABLE reference does not identify a graphical entity.');
      item.Entries.Add(new DxfSortOrderEntry(entity,record.SortKeys[i-1]));
    }
  }
}
