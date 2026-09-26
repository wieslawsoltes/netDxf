// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import * as api from '../../index.js';
import { At, Count, WriteStoredRecord, WriteRecordExtension, WriteRecordReactors, CheckRecordText } from '../../runtime/RetainedRecordIO.js';
import { CheckDatabaseText, WriteDatabaseTag } from './DxfWriter.Objects.js';
import { WriteXData } from '../../runtime/DxfXDataIO.js';
import { NotSupportedException } from '../../runtime/Errors.js';
export const WritePolyline2DExtension=(chunk,record)=>WriteRecordExtension(chunk,record);
export const WritePolyline2DReactors=(chunk,record)=>WriteRecordReactors(chunk,record);
export const WriteStoredPolyline2DRecord=(chunk,document,record,point=api.Vector3.Zero)=>WriteStoredRecord(chunk,document,record,point,'legacy');
export function ValidateStoredPolyline2DRecords(document,binary=false) {
  for(const polyline of document.AddedObjects.Values)if(polyline instanceof api.Polyline2D) {
    polyline.ValidateStoredRecords(document,true);
    if(polyline.HasStoredRecords&&Count(polyline.StoredHeaderTags)+polyline.LegacyHeaderValues().size-polyline.StoredHeaderIndices.size>4096)
      throw new NotSupportedException('Retained legacy POLYLINE header exceeds its tag admission budget.');
    if(binary)continue;
    if(polyline.StoredHeaderTags!==null)for(const tag of polyline.StoredHeaderTags)if(typeof tag.Value==='string')CheckDatabaseText(tag.Value);
    CheckRecordText(polyline.StoredRecords);
  }
}
export function WriteStoredPolyline2DRecords(chunk,document,polyline) {
  WriteStoredPolyline2DHeader(chunk,document,polyline);for(const record of polyline.StoredRecords)WriteStoredPolyline2DRecord(chunk,document,record);
}
export function WriteStoredPolyline2DHeader(chunk,document,polyline) {
  const values=polyline.LegacyHeaderValues(),raw=tag=>WriteDatabaseTag(chunk,document.DrawingVariables.AcadVer,tag,false);
  const append=()=>{for(const [code,tag] of values)if(!polyline.StoredHeaderIndices.has(code))raw(tag);};
  for(let i=0;i<Count(polyline.StoredHeaderTags);i++) {
    if(i===polyline.StoredHeaderPublicEnd)append();const tag=At(polyline.StoredHeaderTags,i);
    if(polyline.StoredHeaderIndices.has(tag.Code)&&polyline.StoredHeaderIndices.get(tag.Code)===i){if(values.has(tag.Code))raw(values.get(tag.Code));}
    else raw(tag);
  }
  if(polyline.StoredHeaderPublicEnd===Count(polyline.StoredHeaderTags))append();WriteXData(chunk,()=>document.DrawingVariables.AcadVer,polyline.XData);
}
