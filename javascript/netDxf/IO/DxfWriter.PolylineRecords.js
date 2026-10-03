// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import * as api from '../../index.js';
import { At, Count, WriteStoredRecord, WriteRecordExtension, WriteRecordReactors, CheckRecordText } from '../../runtime/RetainedRecordIO.js';
import { CheckDatabaseText, WriteDatabaseTag } from './DxfWriter.Objects.js';
import { WriteXData } from '../../runtime/DxfXDataIO.js';
import { NotSupportedException } from '../../runtime/Errors.js';
export const WritePolylineExtension=(chunk,record)=>WriteRecordExtension(chunk,record);
export const WritePolylineReactors=(chunk,record)=>WriteRecordReactors(chunk,record);
export const WriteStoredPolylineRecord=(chunk,document,record,point=api.Vector3.Zero)=>WriteStoredRecord(chunk,document,record,point,'polyline');
export function ValidateStoredPolylineRecords(document,binary=false) {
  for(const polyline of document.AddedObjects.Values)if(polyline instanceof api.Polyline3D) {
    polyline.ValidateStoredRecords(document,true);
    if(binary)continue;
    CheckRecordText(polyline.StoredRecords);
  }
}
export function WriteStoredPolylineRecords(chunk,document,polyline) {
  for(let i=0;i<polyline.VertexRecords.Count;i++)WriteStoredPolylineRecord(chunk,document,At(polyline.VertexRecords,i),At(polyline.Vertexes,i));
  WriteStoredPolylineRecord(chunk,document,polyline.EndSequenceRecord,api.Vector3.Zero);
}
