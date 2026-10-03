// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import * as api from '../../index.js';
import { At, Count, WriteStoredRecord, WriteRecordExtension, WriteRecordReactors, CheckRecordText } from '../../runtime/RetainedRecordIO.js';
import { CheckDatabaseText, WriteDatabaseTag } from './DxfWriter.Objects.js';
import { WriteXData } from '../../runtime/DxfXDataIO.js';
import { NotSupportedException } from '../../runtime/Errors.js';
export const WritePolyfaceMeshExtension=(chunk,record)=>WriteRecordExtension(chunk,record);
export const WritePolyfaceMeshReactors=(chunk,record)=>WriteRecordReactors(chunk,record);
export const WriteStoredPolyfaceMeshRecord=(chunk,document,record,point=api.Vector3.Zero)=>WriteStoredRecord(chunk,document,record,point,'polyface');
export function ValidateStoredPolyfaceMeshRecords(document,binary=false) {
  for(const polyline of document.AddedObjects.Values)if(polyline instanceof api.PolyfaceMesh) {
    polyline.ValidateStoredRecords(document,true);
    if(polyline.HasStoredRecords&&Count(polyline.StoredHeaderTags)+(polyline.StoredNormalIndices.size===0&&!api.Vector3.Equals(polyline.Normal,polyline.StoredNormal)?3:0)>4096)
      throw new NotSupportedException('Retained POLYFACE header exceeds its physical tag admission budget.');
    if(binary)continue;
    if(polyline.StoredHeaderTags!==null)for(const tag of polyline.StoredHeaderTags)if(typeof tag.Value==='string')CheckDatabaseText(tag.Value);
    CheckRecordText(polyline.StoredRecords);
  }
}
export function WriteStoredPolyfaceMeshRecords(chunk,document,polyline) {
  for(const record of polyline.RecordSequence)WriteStoredPolyfaceMeshRecord(chunk,document,record,record.CoordinateIndex<0?api.Vector3.Zero:At(polyline.Vertexes,record.CoordinateIndex));
}
export function WriteStoredPolyfaceMeshHeader(chunk,document,mesh) {
  const changed=!api.Vector3.Equals(mesh.Normal,mesh.StoredNormal),append=changed&&mesh.StoredNormalIndices.size===0;
  const normal=()=>{chunk.Write(210,mesh.Normal.X);chunk.Write(220,mesh.Normal.Y);chunk.Write(230,mesh.Normal.Z);};
  for(let i=0;i<Count(mesh.StoredHeaderTags);i++) {
    if(append&&i===mesh.StoredHeaderPublicEnd)normal();const tag=At(mesh.StoredHeaderTags,i);
    if(changed&&mesh.StoredNormalIndices.has(tag.Code)&&mesh.StoredNormalIndices.get(tag.Code)===i)chunk.Write(tag.Code,mesh.Normal[tag.Code===210?'X':tag.Code===220?'Y':'Z']);
    else WriteDatabaseTag(chunk,document.DrawingVariables.AcadVer,tag,false);
  }
  if(append&&mesh.StoredHeaderPublicEnd===Count(mesh.StoredHeaderTags))normal();WriteXData(chunk,()=>document.DrawingVariables.AcadVer,mesh.XData);
}
