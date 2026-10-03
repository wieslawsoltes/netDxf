// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { DxfIdBuffer } from '../Objects/DxfIdBuffer.js';
import { DxfSortentsTable } from '../Objects/DxfSortentsTable.js';
import { DxfSpatialFilter } from '../Objects/DxfSpatialFilter.js';
import { NullReferenceException,InvalidOperationException } from '../../runtime/Errors.js';
const required=value=>{if(value==null)throw new NullReferenceException();return value;};
const nullable=value=>{if(value===null)throw new InvalidOperationException('Nullable object must have a value.');return value;};
export function WriteContainerVector(chunk,code,vector){chunk.Write(code,vector.X);chunk.Write(code+10,vector.Y);chunk.Write(code+20,vector.Z);}
export function WriteContainerMatrix(chunk,matrix){for(let row=1;row<=3;row++)for(let col=1;col<=4;col++)chunk.Write(40,matrix['M'+row+col]);}
export function WriteContainerPayload(chunk,item){
  if(item instanceof DxfIdBuffer){required(chunk).Write(100,'AcDbIdBuffer');for(const target of item.References)chunk.Write(330,target?.Handle??'0');}
  else if(item instanceof DxfSortentsTable){required(chunk).Write(100,'AcDbSortentsTable');chunk.Write(330,required(item.BlockRecord).Handle);
    for(const entry of item.Entries){chunk.Write(331,entry.Entity.Handle);chunk.Write(5,entry.SortHandle);}}
  else if(item instanceof DxfSpatialFilter){
    required(chunk).Write(100,'AcDbFilter');chunk.Write(100,'AcDbSpatialFilter');chunk.Write(70,(item.Boundary.Count<<16)>>16);
    for(const point of item.Boundary){chunk.Write(10,point.X);chunk.Write(20,point.Y);}
    WriteContainerVector(chunk,210,item.Normal);WriteContainerVector(chunk,11,item.Origin);
    chunk.Write(71,item.IsClippingEnabled?1:0);chunk.Write(72,item.FrontClippingDistance!==null?1:0);
    if(item.FrontClippingDistance!==null)chunk.Write(40,nullable(item.FrontClippingDistance));
    chunk.Write(73,item.BackClippingDistance!==null?1:0);if(item.BackClippingDistance!==null)chunk.Write(41,nullable(item.BackClippingDistance));
    WriteContainerMatrix(chunk,item.InverseInsertTransform);WriteContainerMatrix(chunk,item.ClipBoundaryTransform);
  }else return false;return true;
}
