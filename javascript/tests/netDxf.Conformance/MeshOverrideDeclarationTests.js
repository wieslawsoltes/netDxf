// Pinned MeshOverrideDeclarationTests.cs. The 42 reject identities still lack
// native allocation assertions; only their functional subset is supplemental.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { gunzipSync } from 'node:zlib';
import { createHash } from 'node:crypto';
import { DxfDocument, DxfRawDocument, DxfTag, DxfVersion, Vector3, MemoryStream } from '../../index.js';
import { InvalidDataException } from '../../runtime/Errors.js';
import { GetTypedIOConfiguration } from '../../runtime/TypedDocumentIO.js';
import { Run, Check, Equal, SameDoubleBits, SupportedVersions, VersionName, BooleanName } from './TestHarness.js';
import { RawFixtureBytes, SameRawTags } from './RawDocumentTests.js';
import { MeshReadTags } from './MeshReadValidationTests.js';
import { MeshSingle, MeshSame, MeshRecords, MeshWriteArtifact } from './MeshVersionTests.js';
export function RegisterMeshOverrideDeclarationTests(){
  for(const v of SupportedVersions.filter(v=>v>=DxfVersion.AutoCad2010))for(const b of [false,true]){
    const id=`${VersionName(v)}/${BooleanName(b)}`;
    for(const s of ['zero','absent','private-before','private-after','private-nested','later-subclass','xdata-trailer','comments'])Run(`mesh/override-declaration/accept/${id}/${s}`,()=>MeshOverrideAccept(v,b,s));
    for(const s of ['zero','absent','nonzero','negative'])Run(`mesh/override-declaration/optional-edges/${id}/${s}`,()=>MeshOverrideOptionalEdges(v,b,s));
  }
  for(const b of [false,true])Run(`mesh/override-declaration/native-zero/${BooleanName(b)}`,()=>MeshOverrideNative(b));
}
export function MeshOverridePrivate(nested=false){const tags=[new DxfTag(102,'{PRIVATE_MESH')];if(nested)tags.push(new DxfTag(102,'{INNER'));tags.push(...[[90,1],[91,7],[92,0],[100,'PrivateMeshSubclass']].map(([c,x])=>new DxfTag(c,x)));if(nested)tags.push(new DxfTag(102,'}'));tags.push(new DxfTag(102,'}'));return tags;}
export function MeshOverrideRejectFunctional(v,b,s){
  const tags=MeshReadTags(v),suffix=tags.findIndex(t=>t.Code===1001)-1;tags[suffix]=new DxfTag(90,s==='negative'?-1:s==='large'?2147483647:1);
  if(s==='marker'||s==='property-count')tags.splice(suffix+1,0,new DxfTag(91,7));if(s==='property-count')tags.splice(suffix+2,0,new DxfTag(92,0));
  if(s==='private-then-public')tags.splice(suffix,0,...MeshOverridePrivate(true));if(s==='subclass-then-public')tags.splice(suffix,0,...[[100,'FutureMeshSubclass'],[90,0],[100,'AcDbSubDMesh']].map(([c,x])=>new DxfTag(c,x)));
  const input=new MemoryStream(RawFixtureBytes(tags,b));try{
    if(GetTypedIOConfiguration()==='Debug'){let caught=false;try{DxfDocument.Load(input);}catch(e){Check(e instanceof InvalidDataException,'Expected InvalidDataException');Check(e.message.includes('MESH')&&e.message.includes('90'),'Override diagnostic lost entity/count context.');Check(e.message.includes(s==='negative'?'negative':'not supported'),'Override diagnostic lost the unsupported/malformed distinction.');caught=true;}Check(caught,'Unsupported MESH override declaration was accepted.');}
    else Check(DxfDocument.Load(input)===null,'Unsupported MESH override declaration was accepted.');Check(input.CanRead,'Override rejection closed the caller stream.');
  }finally{input.Dispose();}
}
export function MeshOverrideAccept(v,b,s){
  const tags=MeshReadTags(v),suffix=tags.findIndex(t=>t.Code===1001)-1;
  switch(s){case 'absent':tags.splice(suffix,1);break;case 'private-before':tags.splice(suffix,0,...MeshOverridePrivate());break;case 'private-after':tags.splice(suffix+1,0,...MeshOverridePrivate());break;case 'private-nested':tags.splice(suffix,0,...MeshOverridePrivate(true));break;
    case 'later-subclass':tags.splice(suffix+1,0,...[[100,'FutureMeshSubclass'],[90,1],[91,7],[92,0]].map(([c,x])=>new DxfTag(c,x)));break;
    case 'xdata-trailer':tags.splice(tags.findIndex(t=>t.Code===0&&t.Value==='LINE'),0,...[[90,1],[91,7],[92,0],[100,'AcDbSubDMesh'],[90,1]].map(([c,x])=>new DxfTag(c,x)));break;
    case 'comments':if(!b){tags.splice(suffix,0,new DxfTag(999,'90 1 91 7 92 0'));tags.splice(suffix+2,0,new DxfTag(999,'after override count'));}break;}
  const input=new MemoryStream(RawFixtureBytes(tags,b)),output=new MemoryStream();try{const doc=DxfDocument.Load(input);Check(doc!==null,'Zero/absent public MESH override declaration was rejected.');MeshOverrideCheckGeometry(doc);Check(doc.Save(output,b),'Zero-override MESH save failed.');MeshWriteArtifact(`mesh-override-${VersionName(v)}-${BooleanName(b)}-${s}.dxf`,output.ToArray());output.Position=0;const loaded=DxfDocument.Load(output);Check(loaded!==null,'Zero-override MESH reload failed.');MeshOverrideCheckGeometry(loaded);}finally{input.Dispose();output.Dispose();}
}
export function MeshOverrideCheckGeometry(doc,hasEdge=true){
  const mesh=MeshSingle(doc.Entities.Meshes);Equal(3,mesh.SubdivisionLevel,'Private/suffix marker overwrote subdivision');Equal(3,mesh.Vertexes.Count,'Private/suffix property count overwrote vertices');SameDoubleBits(1e-20,mesh.Vertexes.get_Item(0).X,'Vertex bits changed');Check(new Vector3(1,0,0).Equals(mesh.Vertexes.get_Item(1)),'Second vertex changed');Check(new Vector3(0,1,0).Equals(mesh.Vertexes.get_Item(2)),'Third vertex changed');Check(MeshSame(MeshSingle(mesh.Faces),[0,1,2]),'Face topology changed');
  if(hasEdge){const edge=MeshSingle(mesh.Edges);Equal(0,edge.StartVertexIndex,'Edge start changed');Equal(2,edge.EndVertexIndex,'Edge end changed');Equal(1.25,edge.Crease,'Crease changed');}else Equal(0,mesh.Edges.Count,'Optional edge list acquired edges');
  Check(mesh.BlendCrease,'Blend flag changed');Equal('following XData',MeshSingle(mesh.XData.get_Item('MESH_READ').XDataRecord).Value,'XData boundary changed');Check(new Vector3(7,8,9).Equals(MeshSingle(doc.Entities.Lines).StartPoint),'Following entity changed');
}
export function MeshOverrideOptionalEdges(v,b,s){
  const seed=new MemoryStream(RawFixtureBytes(MeshReadTags(v),b)),saved=new MemoryStream(),input=new MemoryStream(),output=new MemoryStream();
  try{const authored=DxfDocument.Load(seed);Check(authored!==null,'Optional-edge seed load failed.');MeshSingle(authored.Entities.Meshes).Edges.Clear();Check(authored.Save(saved,b),'Empty-edge authored save failed.');saved.Position=0;let loaded=DxfDocument.Load(saved);Check(loaded!==null,'Empty-edge authored reload failed.');MeshOverrideCheckGeometry(loaded,false);saved.Position=0;let raw=DxfRawDocument.Load(saved);const record=MeshSingle(MeshRecords(raw).filter(r=>r.Name==='MESH')),tags=Array.from(record.Tags);
    Equal(0,MeshSingle(tags.filter(t=>t.Code===94)).Value,'Authored empty edge count');Equal(0,MeshSingle(tags.filter(t=>t.Code===95)).Value,'Authored empty crease count');
    const trimmed=tags.filter(t=>t.Code!==94&&t.Code!==95),suffix=trimmed.findIndex(t=>t.Code===1001)-1;Equal(90,trimmed[suffix].Code,'Authored override declaration boundary');if(s==='absent')trimmed.splice(suffix,1);else trimmed[suffix]=new DxfTag(90,s==='nonzero'?1:s==='negative'?-1:0);
    raw=raw.WithRecord(record,trimmed);raw.Save(input,b);input.Position=0;
    if(s==='nonzero'||s==='negative'){
      if(GetTypedIOConfiguration()==='Debug'){let caught=false;try{DxfDocument.Load(input);}catch(e){Check(e instanceof InvalidDataException,'Expected InvalidDataException');Check(e.message.includes('90')&&e.message.includes(s==='negative'?'negative':'not supported'),'Optional-edge diagnostic changed.');caught=true;}Check(caught,'Optional-edge override declaration accepted.');}else Check(DxfDocument.Load(input)===null,'Optional-edge override declaration accepted.');Check(input.CanRead,'Optional-edge rejection closed caller stream.');return;
    }
    loaded=DxfDocument.Load(input);Check(loaded!==null,'Optional-edge zero/absent declaration rejected.');MeshOverrideCheckGeometry(loaded,false);Check(loaded.Save(output,b),'Optional-edge save failed.');MeshWriteArtifact(`mesh-override-optional-${VersionName(v)}-${BooleanName(b)}-${s}.dxf`,output.ToArray());output.Position=0;loaded=DxfDocument.Load(output);Check(loaded!==null,'Optional-edge reload failed.');MeshOverrideCheckGeometry(loaded,false);
  }finally{seed.Dispose();saved.Dispose();input.Dispose();output.Dispose();}
}
export function MeshOverrideNative(b){
  const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../../..'),bytes=gunzipSync(fs.readFileSync(path.join(root,'tests/fixtures/table-oracle/sample_AC1024_ascii.dxf.gz')));
  Equal('c97e857047ad4cecd84754ea1a8638e47508e339fd489f1e895ee7332127b372',createHash('sha256').update(bytes).digest('hex'),'Native MESH source hash');
  const original=new MemoryStream(bytes);try{const packets=MeshRecords(DxfRawDocument.Load(original)).filter(r=>r.Name==='MESH');Equal(2,packets.length,'Native zero-override MESH inventory');
    for(const packet of packets){const pt=Array.from(packet.Tags),handle=MeshSingle(pt.filter(t=>t.Code===5)).Value,body=pt.slice(pt.findIndex(t=>t.Code===100&&t.Value==='AcDbSubDMesh'));Equal(90,body.at(-1).Code,'Native override declaration code');Equal(0,body.at(-1).Value,'Native override declaration value');
      const tags=MeshReadTags(DxfVersion.AutoCad2010),start=tags.findIndex(t=>t.Code===100&&t.Value==='AcDbSubDMesh'),end=tags.findIndex((t,i)=>i>=start&&t.Code===1001);tags.splice(start,end-start,...body);
      const input=new MemoryStream(RawFixtureBytes(tags,b)),output=new MemoryStream();try{const doc=DxfDocument.Load(input);Check(doc!==null,'Native zero-override subclass failed to load.');Check(doc.Save(output,b),'Native zero-override subclass failed to save.');MeshWriteArtifact(`mesh-override-native-${handle}-${BooleanName(b)}.dxf`,output.ToArray());output.Position=0;const written=Array.from(MeshSingle(MeshRecords(DxfRawDocument.Load(output)).filter(r=>r.Name==='MESH')).Tags),rest=written.slice(written.findIndex(t=>t.Code===100&&t.Value==='AcDbSubDMesh')),at=rest.findIndex(t=>t.Code===1001);SameRawTags(body,at<0?rest:rest.slice(0,at));}finally{input.Dispose();output.Dispose();}
    }
  }finally{original.Dispose();}
}
