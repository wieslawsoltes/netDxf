// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import * as api from '../../index.js';
import { Polyline } from '../Entities/Polyline.js';
import { DxfTag } from './DxfTag.js';
import { ReadXDataRecord } from '../../runtime/DxfXDataIO.js';
import { ReferenceList } from '../../runtime/ReferenceList.js';
import { ReadStoredRecord, ResolveStoredRecords, StoredPolylineGroupEnd } from '../../runtime/RetainedRecordIO.js';
import { ReadVertex, ReadPolyline2D } from '../../runtime/FittedPolylineIO.js';
import { FormatException } from '../../runtime/Errors.js';
export const ReadStoredPolyline2DRecord=(context,end)=>ReadStoredRecord(context,end,'legacy');
export const ResolveStoredPolyline2DRecords=context=>ResolveStoredRecords(context,'legacy',api.Polyline2D);
export function ReadStoredPolyline2D(context) {
  const chunk=context.Chunk,doc=context.Document,header=[new DxfTag(100,api.SubclassMarker.Polyline2D)],xdata=[];let xdataSeen=false;
  chunk.Next();while(chunk.Code!==0) {
    if(chunk.Code===1001){xdataSeen=true;xdata.push(ReadXDataRecord(chunk,doc));continue;}
    if(xdataSeen)throw new FormatException('Legacy POLYLINE common XData must follow its complete subclass packet.');
    if(header.length>=4096)throw new FormatException('Retained legacy POLYLINE header exceeds its tag admission budget.');
    if(chunk.Code!==999)header.push(new DxfTag(chunk.Code,chunk.Value));chunk.Next();
  }
  const indices=new Map(),seen=new Set(),normal=api.Vector3.UnitZ;
  let flags=0,smooth=0,elevation=0,thickness=0,startWidth=null,endWidth=null,privateClass=false,hasPrivate=false,publicEnd=header.length;
  for(let i=1;i<header.length;i++) {
    const tag=header[i],code=tag.Code,v=tag.Value;
    if(code===102){hasPrivate=true;i=StoredPolylineGroupEnd(header,i);continue;}
    if(code===100){if(v===api.SubclassMarker.Polyline2D)throw new FormatException('Duplicate legacy POLYLINE subclass.');if(!privateClass)publicEnd=i;privateClass=true;hasPrivate=true;continue;}
    if(privateClass)continue;
    if([10,20,30,39,40,41,66,70,75,210,220,230].includes(code)){if(seen.has(code))throw new FormatException('Duplicate qualified legacy POLYLINE field.');seen.add(code);}
    switch(code) {
      case 10:case 20:if(v!==0)throw new FormatException('Legacy POLYLINE requires zero dummy X/Y coordinates.');indices.set(code,i);break;
      case 30:elevation=v;indices.set(code,i);break;
      case 39:thickness=v;indices.set(code,i);break;
      case 40:startWidth=v;api.Polyline2D.ValidateWidth(v,'LegacyDefaultStartWidth');indices.set(code,i);break;
      case 41:endWidth=v;api.Polyline2D.ValidateWidth(v,'LegacyDefaultEndWidth');indices.set(code,i);break;
      case 70:flags=v;indices.set(code,i);break;
      case 75:smooth=v;break;
      case 210:case 220:case 230:normal[code===210?'X':code===220?'Y':'Z']=v;indices.set(code,i);break;
      case 66:if(v!==1)throw new FormatException('Legacy POLYLINE group 66 must indicate its child sequence.');break;
      default:hasPrivate=true;break;
    }
  }
  if((flags&~135)!==0)throw new FormatException('The AcDb2dPolyline subclass cannot contain 3D or mesh roles.');
  if((flags&6)!==0||smooth!==0)return ReadFittedLegacyPolyline2D(context,flags,normal,elevation,thickness,smooth,xdata);
  const pointCount=[10,20,30].filter(c=>indices.has(c)).length,normalCount=[210,220,230].filter(c=>indices.has(c)).length;
  if(pointCount!==0&&pointCount!==3)throw new FormatException('A retained legacy POLYLINE requires a complete dummy point or its complete omission.');
  if(normalCount!==0&&normalCount!==3||api.Vector3.IsZero(normal))throw new FormatException('Legacy POLYLINE requires a complete finite nonzero extrusion normal.');
  const records=[];while(chunk.Code===0&&chunk.ReadString()==='VERTEX') {
    if(records.length>=65536)throw new FormatException('Retained legacy POLYLINE exceeds its record admission budget.');records.push(ReadStoredPolyline2DRecord(context,false));
  }
  if(chunk.Code!==0||chunk.ReadString()!=='SEQEND')throw new FormatException('A legacy POLYLINE child sequence requires SEQEND.');
  const end=ReadStoredPolyline2DRecord(context,true),result=new api.Polyline2D(records.map(record=>record.Vertex));result.Flags=flags;result.Normal=normal;result.Elevation=elevation;result.Thickness=thickness;
  result.XData.AddRange(xdata);result.SetStoredRecords(doc,records,end);result.StoredHeaderTags=new ReferenceList(header);result.StoredHeaderPublicEnd=publicEnd;result.HasPrivateHeader=hasPrivate;result.StoredNormal=result.Normal;
  result.LegacyDefaultStartWidth=startWidth;result.LegacyDefaultEndWidth=endWidth;for(const [code,index] of indices)result.StoredHeaderIndices.set(code,index);return result;
}
export function ReadFittedLegacyPolyline2D(context,flags,normal,elevation,thickness,smooth,xdata) {
  const chunk=context.Chunk,vertices=[];while(chunk.Code===0&&chunk.ReadString()==='VERTEX')vertices.push(ReadVertex(context,false));
  if(chunk.Code!==0||chunk.ReadString()!=='SEQEND')throw new FormatException('A fitted legacy POLYLINE requires SEQEND.');chunk.Next();while(chunk.Code!==0)chunk.Next();
  const result=new Polyline();result.Vertexes=new ReferenceList(vertices);result.Flags=flags;result.Normal=normal;result.Elevation=elevation;result.Thickness=thickness;result.SmoothType=smooth===8?0:smooth;result.XData.AddRange(xdata);return ReadPolyline2D(result);
}
