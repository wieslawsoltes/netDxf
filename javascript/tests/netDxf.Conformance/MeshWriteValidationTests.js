// Port of all original cases in pinned MeshWriteValidationTests.cs.
import { DxfDocument, DxfRawDocument, DxfVersion, Mesh, Vector3, MemoryStream } from '../../index.js';
import { InvalidOperationException } from '../../runtime/Errors.js';
import { GetTypedIOConfiguration } from '../../runtime/TypedDocumentIO.js';
import { Run, Check, Equal, SupportedVersions, VersionName, BooleanName } from './TestHarness.js';
import { ProfileMeshDocument, OnlyProfileMesh, SameProfileMesh, MeshSame, MeshSingle, MeshRecords, MeshWriteArtifact } from './MeshVersionTests.js';
export function RegisterMeshWriteValidationTests(){
  for(const v of SupportedVersions.filter(v=>v>=DxfVersion.AutoCad2010))for(const b of [false,true])for(let p=0;p<4;p++){
    for(let d=0;d<13;d++)Run(`mesh/write-validation/${VersionName(v)}/${BooleanName(b)}/${p}/${d}`,()=>MeshWriteInvalid(v,b,p,d));
    Run(`mesh/write-validation/valid/${VersionName(v)}/${BooleanName(b)}/${p}`,()=>MeshWriteValid(v,b,p));
  }
  Run('mesh/write-validation/empty',MeshWriteEmpty);Run('mesh/write-validation/count-overflow',MeshWriteCountOverflow);
}
export function MeshWriteInvalid(v,b,p,d){
  const doc=ProfileMeshDocument(v,p,2),mesh=OnlyProfileMesh(doc);
  switch(d){
    case 0:mesh.Faces.set_Item(0,null);break;case 1:mesh.Faces.set_Item(0,[]);break;case 2:mesh.Faces.set_Item(0,[0,1]);break;
    case 3:mesh.Faces.get_Item(0)[1]=-1;break;case 4:mesh.Faces.get_Item(0)[1]=mesh.Vertexes.Count;break;case 5:mesh.Vertexes.RemoveAt(2);break;
    case 6:mesh.Edges.set_Item(1,null);break;case 7:mesh.Edges.get_Item(1).StartVertexIndex=mesh.Vertexes.Count;break;case 8:mesh.Edges.get_Item(1).EndVertexIndex=2147483647;break;
    case 9:mesh.Edges.get_Item(1).Crease=NaN;break;case 10:mesh.Edges.get_Item(1).Crease=Infinity;break;case 11:mesh.Vertexes.set_Item(0,new Vector3(NaN,0,0));break;case 12:mesh.Vertexes.set_Item(0,new Vector3(0,-Infinity,Infinity));break;
  }
  MeshExpectPreflight(doc,b);
}
export function MeshExpectPreflight(doc,b){
  const output=new MemoryStream(),initial=Uint8Array.of(10,20,30,40,50);output.Write(initial);output.Position=2;
  const seed=doc.DrawingVariables.HandleSeed,active=doc.Entities.ActiveLayout,registrations=Array.from(doc.ApplicationRegistries),layouts=Array.from(doc.Layouts),entities=Array.from(doc.Blocks).flatMap(b=>Array.from(b.Entities)),handles=entities.map(e=>e.Handle);
  try{
    if(GetTypedIOConfiguration()==='Debug'){let caught=false;try{doc.Save(output,b);}catch(e){Check(e instanceof InvalidOperationException,'Expected InvalidOperationException');Check(e.message.includes('MESH')&&e.message.includes('group'),'Missing contextual MESH preflight error: '+e.message);caught=true;}Check(caught,'Invalid mutable MESH was exported.');}
    else Check(!doc.Save(output,b),'Invalid mutable MESH was exported.');
    Check(MeshSame(initial,output.ToArray()),'Invalid MESH changed destination bytes.');Equal(2,output.Position,'Invalid MESH moved destination position');Equal(seed,doc.DrawingVariables.HandleSeed,'Invalid MESH allocated handles');Equal(active,doc.Entities.ActiveLayout,'Invalid MESH changed active layout');
    Check(MeshSame(registrations,doc.ApplicationRegistries),'Invalid MESH registered APPIDs.');Check(MeshSame(layouts,doc.Layouts),'Invalid MESH created layouts.');Check(MeshSame(entities,Array.from(doc.Blocks).flatMap(b=>Array.from(b.Entities)))&&MeshSame(handles,entities.map(e=>e.Handle)),'Invalid MESH mutated entity identities.');Check(output.CanWrite,'Preflight closed caller-owned stream.');
  }finally{output.Dispose();}
}
export function MeshWriteValid(v,b,p){
  const doc=ProfileMeshDocument(v,p,255),mesh=OnlyProfileMesh(doc),output=new MemoryStream();mesh.BlendCrease=true;mesh.Vertexes.Add(new Vector3(5,7,9));mesh.Faces.Add([0,1,3,2]);mesh.Vertexes.Add(new Vector3(10,11,12));mesh.Faces.Add([0,1,1]);
  try{Check(doc.Save(output,b),'Valid mutable MESH rejected.');output.Position=0;const loaded=DxfDocument.Load(output);Check(loaded!==null,'Validated output cannot reload.');SameProfileMesh(mesh,OnlyProfileMesh(loaded));Equal(true,OnlyProfileMesh(loaded).BlendCrease,'Validation changed blend crease');output.Position=0;
    const record=MeshSingle(MeshRecords(DxfRawDocument.Load(output)).filter(r=>r.Name==='MESH'));Equal(13,MeshSingle(Array.from(record.Tags).filter(t=>t.Code===93)).Value,'Serialized total face-list size');
    if(p===0){mesh.Faces.RemoveAt(2);output.SetLength(0);output.Position=0;Check(doc.Save(output,b),'Fixture save failed.');MeshWriteArtifact(`mesh-write-validation-${VersionName(v)}-${BooleanName(b)}.dxf`,output.ToArray());}
  }finally{output.Dispose();}
}
export function MeshWriteEmpty(){const doc=new DxfDocument(DxfVersion.AutoCad2018),output=new MemoryStream();doc.Entities.Add(new Mesh([],[]));try{Check(doc.Save(output,true),'Empty mesh policy changed.');output.Position=0;const loaded=DxfDocument.Load(output);Check(loaded!==null,'Empty mesh reload failed.');Equal(0,MeshSingle(loaded.Entities.Meshes).Vertexes.Count,'Empty mesh changed');}finally{output.Dispose();}}
export function MeshWriteCountOverflow(){const shared=Array(100000).fill(0),mesh=new Mesh([Vector3.Zero],Array(22000).fill(shared)),doc=new DxfDocument(DxfVersion.AutoCad2018);doc.Entities.Add(mesh);MeshExpectPreflight(doc,false);MeshExpectPreflight(doc,true);}
