// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import * as api from '../../index.js';
import { DxfTag } from './DxfTag.js';
import { ReadXDataRecord } from '../../runtime/DxfXDataIO.js';
import { WrappedFormat } from '../../runtime/DatabaseIOContext.js';
import { ReferenceList } from '../../runtime/ReferenceList.js';
import { ReadStoredRecord, ResolveStoredRecords, StoredPolylineGroupEnd, At } from '../../runtime/RetainedRecordIO.js';
import { FormatException, ArgumentException } from '../../runtime/Errors.js';
export const ReadStoredPolyfaceMeshRecord=(context,end)=>ReadStoredRecord(context,end,'polyface');
export const ResolveStoredPolyfaceMeshRecords=context=>ResolveStoredRecords(context,'polyface',api.PolyfaceMesh);
export function ReadStoredPolyfaceMesh(context) {
  const chunk=context.Chunk,doc=context.Document,header=[new DxfTag(100,api.SubclassMarker.PolyfaceMesh)],xdata=[];let xdataSeen=false;
  chunk.Next();while(chunk.Code!==0) {
    if(chunk.Code===1001){xdataSeen=true;xdata.push(ReadXDataRecord(chunk,doc));continue;}
    if(xdataSeen)throw new FormatException('POLYFACE common XData must follow its complete subclass packet.');
    if(header.length>=4096)throw new FormatException('Retained POLYFACE header exceeds its tag admission budget.');
    if(chunk.Code!==999)header.push(new DxfTag(chunk.Code,chunk.Value));chunk.Next();
  }
  let flags=64,flagsSeen=false,privateClass=false,hasPrivate=false,verticesDeclared=null,facesDeclared=null,publicEnd=header.length;
  const normal=api.Vector3.UnitZ,normalIndices=new Map();
  for(let i=1;i<header.length;i++) {
    const tag=header[i],code=tag.Code,v=tag.Value;
    if(code===102){hasPrivate=true;i=StoredPolylineGroupEnd(header,i);continue;}
    if(code===100){if(v===api.SubclassMarker.PolyfaceMesh)throw new FormatException('Duplicate POLYFACE subclass.');if(!privateClass)publicEnd=i;privateClass=true;hasPrivate=true;continue;}
    if(privateClass)continue;
    switch(code) {
      case 70: if(flagsSeen||(v&~128)!==64)throw new FormatException('Unsupported ordinary POLYFACE flags.');flags=v;flagsSeen=true;break;
      case 71: if(verticesDeclared!==null)throw new FormatException('Duplicate POLYFACE advisory vertex count.');verticesDeclared=v;break;
      case 72: if(facesDeclared!==null)throw new FormatException('Duplicate POLYFACE advisory face count.');facesDeclared=v;break;
      case 210: case 220: case 230:
        if(normalIndices.has(code))throw new FormatException('Duplicate POLYFACE normal coordinate.');normalIndices.set(code,i);normal[code===210?'X':code===220?'Y':'Z']=v;break;
      case 75: if(v!==0)throw new FormatException('Fitted POLYFACE records require an unsupported schema.');break;
      case 10: case 20: case 30: case 66: break;
      default:hasPrivate=true;break;
    }
  }
  if(!flagsSeen||normalIndices.size!==0&&normalIndices.size!==3)throw new FormatException('Incomplete POLYFACE subclass packet.');
  const sequence=[],points=[],faces=[];
  while(chunk.Code===0&&chunk.ReadString()==='VERTEX') {
    if(sequence.length>=65536)throw new FormatException('Retained POLYFACE child sequence exceeds its record admission budget.');
    const record=ReadStoredPolyfaceMeshRecord(context,false);sequence.push(record);
    if(record.IsFaceRecord) {
      const indices=[];for(let code=71;code<=74;code++){if(!record.FaceSlots.has(code)||At(record.Tags,record.FaceSlots.get(code)).Value===0)break;indices.push(At(record.Tags,record.FaceSlots.get(code)).Value);}
      record.OriginalIndexes=indices.slice();
      try{const face=new api.PolyfaceMeshFace(indices);face.Layer=record.Layer;face.Color=record.OriginalColor===null?null:record.OriginalColor.Clone();record.StoredFace=face;}
      catch(error){if(!(error instanceof ArgumentException))throw error;throw WrappedFormat('Invalid POLYFACE face indices.',error);}
      record.FaceIndex=faces.length;faces.push(record.StoredFace);
    }else{record.CoordinateIndex=points.length;points.push(record.Position);}
  }
  if(chunk.Code!==0||chunk.ReadString()!=='SEQEND')throw new FormatException('A POLYFACE child sequence requires SEQEND.');sequence.push(ReadStoredPolyfaceMeshRecord(context,true));
  let result;try{result=new api.PolyfaceMesh(points,faces);result.Flags=flags;result.Normal=normal;}
  catch(error){if(!(error instanceof ArgumentException))throw error;throw WrappedFormat('POLYFACE signed indices must resolve against the complete coordinate sequence.',error);}
  result.SetStoredRecords(doc,sequence);result.XData.AddRange(xdata);result.StoredHeaderTags=new ReferenceList(header);result.StoredNormal=result.Normal;result.HasPrivateHeader=hasPrivate;result.StoredHeaderPublicEnd=publicEnd;
  result.DeclaredVertexCount=verticesDeclared;result.DeclaredFaceCount=facesDeclared;for(const [code,index] of normalIndices)result.StoredNormalIndices.set(code,index);return result;
}
