// Port of all original cases in pinned MeshVersionTests.cs.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { DxfDocument, DxfRawDocument, DxfTag, DxfVersion, Mesh, MeshEdge, PolygonMesh, PolyfaceMesh, Vector3, Block, Insert, Layout, XData, XDataRecord, XDataCode, ApplicationRegistry, MemoryStream } from '../../index.js';
import { NotSupportedException } from '../../runtime/Errors.js';
import { GetTypedIOConfiguration } from '../../runtime/TypedDocumentIO.js';
import { Run, Check, Equal, SameDoubleBits, SupportedVersions, VersionName, HeaderVersion, BooleanName } from './TestHarness.js';
export const MeshSingle = items => { const values = Array.from(items); Equal(1, values.length, 'Expected exactly one item'); return values[0]; };
export const MeshSame = (a,b) => { a=Array.from(a); b=Array.from(b); return a.length===b.length && a.every((v,i)=>v?.Equals?v.Equals(b[i]):v===b[i]); };
export const MeshRecords = raw => Array.from(raw.Sections).flatMap(s=>Array.from(s.Records));
export function MeshWriteArtifact(name, bytes) {
  const directory=path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../artifacts/conformance/fixtures');
  fs.mkdirSync(directory,{recursive:true}); fs.writeFileSync(path.join(directory,name),bytes);
}
export function ProfileMesh(level) {
  const mesh=new Mesh([new Vector3(1e-20,0,0),new Vector3(2,0,0),new Vector3(0,3,1)],[[0,1,2]],
    [new MeshEdge(0,1,0),new MeshEdge(1,2,1.5),new MeshEdge(2,0,-1)]);
  mesh.SubdivisionLevel=level; return mesh;
}
export function ProfileMeshDocument(version,placement,level) {
  const doc=new DxfDocument(version); doc.Comments.Clear(); const mesh=ProfileMesh(level),data=new XData(new ApplicationRegistry('MESH_PROFILE_TEST'));
  data.XDataRecord.Add(new XDataRecord(XDataCode.String,'mesh metadata')); mesh.XData.Add(data);
  if(placement===0) doc.Entities.Add(mesh);
  else if(placement===1){doc.Layouts.Add(new Layout('Paper'));doc.Entities.ActiveLayout='Paper';doc.Entities.Add(mesh);doc.Entities.ActiveLayout='Model';}
  else if(placement===2){const inner=new Block('MeshInner'),outer=new Block('MeshOuter');inner.Entities.Add(mesh);outer.Entities.Add(new Insert(inner));doc.Entities.Add(new Insert(outer));}
  else {const unused=new Block('MeshUnused');unused.Entities.Add(mesh);doc.Blocks.Add(unused);}
  return doc;
}
export const OnlyProfileMesh = doc => MeshSingle(Array.from(doc.Blocks).flatMap(b=>Array.from(b.Entities)).filter(e=>e instanceof Mesh));
export function SameProfileMesh(expected,actual) {
  Equal(expected.SubdivisionLevel,actual.SubdivisionLevel,'Mesh subdivision level'); Check(MeshSame(expected.Vertexes,actual.Vertexes),'Mesh vertices changed.');
  SameDoubleBits(expected.Vertexes.get_Item(0).X,actual.Vertexes.get_Item(0).X,'Mesh coordinate precision'); Equal(expected.Faces.Count,actual.Faces.Count,'Mesh face count');
  for(let i=0;i<expected.Faces.Count;i++)Check(MeshSame(expected.Faces.get_Item(i),actual.Faces.get_Item(i)),'Mesh face topology changed.');
  Equal(expected.Edges.Count,actual.Edges.Count,'Mesh edge count');
  for(let i=0;i<expected.Edges.Count;i++){const a=expected.Edges.get_Item(i),b=actual.Edges.get_Item(i);Equal(a.StartVertexIndex,b.StartVertexIndex,'Mesh edge start');Equal(a.EndVertexIndex,b.EndVertexIndex,'Mesh edge end');SameDoubleBits(a.Crease,b.Crease,'Mesh crease');}
  Equal('mesh metadata',MeshSingle(actual.XData.get_Item('MESH_PROFILE_TEST').XDataRecord).Value,'Mesh XData');
}
export function RegisterMeshVersionTests() {
  for(const v of SupportedVersions)for(const b of [false,true]){
    const id=`${VersionName(v)}/${BooleanName(b)}`;
    for(let p=0;p<4;p++)for(const l of [0,2])Run(`mesh/export-profile/${id}/${p}/${l}`,()=>MeshExportProfile(v,b,p,l));
    Run(`mesh/legacy-polyline-controls/${id}`,()=>MeshLegacyControls(v,b));Run(`mesh/empty-document/${id}`,()=>MeshEmptyDocument(v,b));
    if(v<DxfVersion.AutoCad2010)Run(`mesh/explicit-upgrade/${id}`,()=>MeshExplicitUpgrade(v,b));
  }
}
export function MeshExportProfile(v,b,p,l) {
  const doc=ProfileMeshDocument(v,p,l),original=OnlyProfileMesh(doc),output=new MemoryStream(),prefix=Uint8Array.of(12,34,56,78,90);
  try{
    output.Write(prefix);output.Position=2;
    const handles=doc.DrawingVariables.HandleSeed,active=doc.Entities.ActiveLayout,layouts=doc.Layouts.Count,apps=doc.ApplicationRegistries.Count,
      entities=Array.from(doc.Blocks).flatMap(block=>Array.from(block.Entities)),identities=entities.map(e=>e.Handle);
    if(v<DxfVersion.AutoCad2010){
      if(GetTypedIOConfiguration()==='Debug'){
        let caught=false;try{doc.Save(output,b);}catch(e){Check(e instanceof NotSupportedException,'Expected NotSupportedException');Check(e.message.includes('MESH')&&e.message.includes('2010'),'Missing feature/version diagnostic.');caught=true;}Check(caught,'Old target accepted a modern MESH record.');
      }else Check(!doc.Save(output,b),'Old target accepted a modern MESH record.');
      Check(MeshSame(prefix,output.ToArray()),'Rejected mesh export wrote destination bytes.');Equal(2,output.Position,'Rejected mesh export advanced destination');
      Equal(handles,doc.DrawingVariables.HandleSeed,'Preflight allocated handles');Equal(layouts,doc.Layouts.Count,'Preflight added layouts');Equal(apps,doc.ApplicationRegistries.Count,'Preflight added application registrations');Equal(active,doc.Entities.ActiveLayout,'Preflight changed active layout');
      Check(MeshSame(entities,Array.from(doc.Blocks).flatMap(block=>Array.from(block.Entities))),'Preflight replaced entities.');Check(MeshSame(identities,entities.map(e=>e.Handle)),'Preflight changed entity identities.');
    }else{
      output.SetLength(0);output.Position=0;Check(doc.Save(output,b),'Supported MESH export was rejected.');const bytes=output.ToArray();output.Position=0;
      const loaded=DxfDocument.Load(output);Check(loaded!==null,'Supported MESH reload failed.');SameProfileMesh(original,OnlyProfileMesh(loaded));output.Position=0;const raw=DxfRawDocument.Load(output);
      Equal(v,raw.Version,'Mesh output version');Equal(1,MeshRecords(raw).filter(r=>r.Name==='MESH').length,'Mesh wire record count');
      const second=new MemoryStream();try{Check(loaded.Save(second,!b),'Mesh cross-transport export failed.');second.Position=0;SameProfileMesh(original,OnlyProfileMesh(DxfDocument.Load(second)));}finally{second.Dispose();}
      MeshWriteArtifact(`mesh-profile-${VersionName(v)}-${BooleanName(b)}-${p}-${l}.dxf`,bytes);
    }
    Check(output.CanWrite,'Mesh export closed caller-owned stream.');
  }finally{output.Dispose();}
}
export function MeshLegacyControls(v,b){
  const vertices=[Vector3.Zero,Vector3.UnitX,Vector3.UnitY,new Vector3(1,1,0)],doc=new DxfDocument(v),output=new MemoryStream();
  doc.Entities.Add(new PolygonMesh(2,2,vertices));doc.Entities.Add(new PolyfaceMesh(vertices,[[1,2,4,3]]));
  try{Check(doc.Save(output,b),'Legacy POLYLINE mesh was blocked.');output.Position=0;const loaded=DxfDocument.Load(output);Check(loaded!==null,'Legacy mesh reload failed.');
    Check(MeshSame(vertices,MeshSingle(loaded.Entities.PolygonMeshes).Vertexes),'Polygon mesh vertices changed.');Check(MeshSame(vertices,MeshSingle(loaded.Entities.PolyfaceMeshes).Vertexes),'Polyface mesh vertices changed.');
    Equal(0,Array.from(loaded.Entities.Meshes).length,'Legacy mesh converted to modern MESH.');output.Position=0;Equal(0,MeshRecords(DxfRawDocument.Load(output)).filter(r=>r.Name==='MESH').length,'Legacy output contains modern MESH.');
  }finally{output.Dispose();}
}
export function MeshEmptyDocument(v,b){const stream=new MemoryStream();try{Check(new DxfDocument(v).Save(stream,b),'Mesh gate rejected a mesh-free document.');}finally{stream.Dispose();}}
export function MeshExplicitUpgrade(v,b){
  const doc=ProfileMeshDocument(v,2,2);doc.DrawingVariables.AcadVer=DxfVersion.AutoCad2010;const stream=new MemoryStream(),retained=new MemoryStream(),promoted=new MemoryStream();
  try{Check(doc.Save(stream,b),'Explicit upgrade was rejected.');stream.Position=0;const raw=DxfRawDocument.Load(stream),declared=Array.from(raw.Tags),at=declared.findIndex(t=>t.Code===9&&t.Value==='$ACADVER')+1;declared[at]=new DxfTag(1,HeaderVersion(v));
    const old=DxfRawDocument.Create(declared,b);old.Save(retained);retained.Position=0;Equal(v,DxfRawDocument.Load(retained).Version,'Raw retention unexpectedly changed the profile.');retained.Position=0;
    const loaded=DxfDocument.Load(retained);Check(loaded!==null,'Read-side retention changed.');SameProfileMesh(OnlyProfileMesh(doc),OnlyProfileMesh(loaded));loaded.DrawingVariables.AcadVer=DxfVersion.AutoCad2010;Check(loaded.Save(promoted,!b),'Explicit promotion failed.');
  }finally{stream.Dispose();retained.Dispose();promoted.Dispose();}
}
