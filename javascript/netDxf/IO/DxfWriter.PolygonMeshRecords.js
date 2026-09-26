// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import * as api from '../../index.js';
import { At, Count, WriteStoredRecord, WriteRecordExtension, WriteRecordReactors, CheckRecordText } from '../../runtime/RetainedRecordIO.js';
import { CheckDatabaseText, WriteDatabaseTag } from './DxfWriter.Objects.js';
import { WriteXData } from '../../runtime/DxfXDataIO.js';
import { NotSupportedException } from '../../runtime/Errors.js';
export const WritePolygonMeshExtension=(chunk,record)=>WriteRecordExtension(chunk,record);
export const WritePolygonMeshReactors=(chunk,record)=>WriteRecordReactors(chunk,record);
export const WriteStoredPolygonMeshRecord=(chunk,document,record,point=api.Vector3.Zero)=>WriteStoredRecord(chunk,document,record,point,'polygon');
export function ValidateStoredPolygonMeshRecords(document,binary=false) {
  let total=0;
  for(const item of document.AddedObjects.Values) {
    const count=item instanceof api.PolygonMeshRecord||item instanceof api.Polyline3DRecord||item instanceof api.PolyfaceMeshRecord||item instanceof api.Polyline2DRecord?item.TopologyTagCount():0;
    if(count>4096)throw new NotSupportedException('A retained VERTEX/SEQEND exceeds the 4096-tag packet admission budget.');total+=count;
    if(total>1048576)throw new NotSupportedException('Retained VERTEX/SEQEND records exceed the shared document tag admission budget.');
  }
  for(const polyline of document.AddedObjects.Values)if(polyline instanceof api.PolygonMesh) {
    polyline.ValidateStoredRecords(document,true);
    if(binary)continue;
    CheckRecordText(polyline.StoredRecords);
  }
}
export function WriteStoredPolygonMeshRecords(chunk,document,polyline) {
  for(let i=0;i<polyline.U;i++)for(let j=0;j<polyline.V;j++){const slot=i+j*polyline.U;WriteStoredPolygonMeshRecord(chunk,document,At(polyline.VertexRecords,slot),At(polyline.Vertexes,slot));}
  WriteStoredPolygonMeshRecord(chunk,document,polyline.EndSequenceRecord,api.Vector3.Zero);
}
