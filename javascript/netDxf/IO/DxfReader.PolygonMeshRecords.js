// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { PolygonMesh } from '../Entities/PolygonMesh.js';
import { ReadStoredRecord, ResolveStoredRecords } from '../../runtime/RetainedRecordIO.js';
import { FormatException, InvalidDataException } from '../../runtime/Errors.js';
export const ReadStoredPolygonMeshRecord=(context,end)=>ReadStoredRecord(context,end,'polygon');
export const ResolveStoredPolygonMeshRecords=context=>ResolveStoredRecords(context,'polygon',PolygonMesh);
export function ReadStoredPolygonMeshSequence(context,flags,normal,xdata,m,n) {
  if(m<2||m>256||n<2||n>256)throw new InvalidDataException('POLYGONMESH groups 71/72 require M and N between 2 and 256.');
  const chunk=context.Chunk,expected=m*n,records=[];
  while(chunk.Code===0&&chunk.ReadString()==='VERTEX') {
    if(records.length>=expected)throw new InvalidDataException('POLYGONMESH has more VERTEX records than M x N.');records.push(ReadStoredPolygonMeshRecord(context,false));
  }
  if(records.length!==expected)throw new InvalidDataException('POLYGONMESH requires exactly M x N VERTEX records.');
  if(chunk.Code!==0||chunk.ReadString()!=='SEQEND')throw new FormatException('A POLYGONMESH vertex sequence requires SEQEND.');
  const end=ReadStoredPolygonMeshRecord(context,true),points=new Array(expected),slots=new Array(expected);
  for(let ordinal=0;ordinal<expected;ordinal++){const slot=Math.trunc(ordinal/n)+(ordinal%n)*m;points[slot]=records[ordinal].Position;slots[slot]=records[ordinal];}
  const result=new PolygonMesh(m,n,points);result.Flags=flags;result.Normal=normal;result.XData.AddRange(xdata);result.SetStoredRecords(context.Document,slots,end);return result;
}
