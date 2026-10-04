// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { Polyline3D } from '../Entities/Polyline3D.js';
import { ReadStoredRecord, ResolveStoredRecords } from '../../runtime/RetainedRecordIO.js';
import { FormatException } from '../../runtime/Errors.js';
export { StoredPolylineGroupEnd, PolylineRecordHandle } from '../../runtime/RetainedRecordIO.js';
export const ReadStoredPolylineRecord=(context,end)=>ReadStoredRecord(context,end,'polyline');
export const ResolveStoredPolylineRecords=context=>ResolveStoredRecords(context,'polyline',Polyline3D);
export function ReadStoredPolylineSequence(context,flags,normal,xdata) {
  const chunk=context.Chunk,records=[];
  while(chunk.Code===0&&chunk.ReadString()==='VERTEX') {
    if(records.length>=65536)throw new FormatException('A retained polyline exceeds the vertex admission budget.');records.push(ReadStoredPolylineRecord(context,false));
  }
  if(chunk.Code!==0||chunk.ReadString()!=='SEQEND')throw new FormatException('A POLYLINE vertex sequence requires SEQEND.');
  const end=ReadStoredPolylineRecord(context,true),result=new Polyline3D(records.map(record=>record.Position),!!(flags&1));result.Flags=flags;result.Normal=normal;
  result.XData.AddRange(xdata);result.SetStoredRecords(context.Document,records,end);return result;
}
