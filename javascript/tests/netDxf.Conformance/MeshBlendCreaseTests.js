// Port of every original case in pinned MeshBlendCreaseTests.cs, retaining the default identity.
import { DxfDocument, DxfRawDocument, DxfTag, DxfVersion, Mesh, Block, Insert, MemoryStream } from '../../index.js';
import { InvalidDataException } from '../../runtime/Errors.js';
import { GetTypedIOConfiguration } from '../../runtime/TypedDocumentIO.js';
import { Run, Check, Equal, SupportedVersions, VersionName, HeaderVersion, BooleanName } from './TestHarness.js';
import { RawFixtureBytes } from './RawDocumentTests.js';
import { ProfileMesh, OnlyProfileMesh, MeshSingle, MeshRecords, MeshWriteArtifact } from './MeshVersionTests.js';
export function RegisterMeshBlendCreaseTests(){
  Run('mesh/blend/default',()=>Check(!ProfileMesh(0).BlendCrease,'Default mesh blend flag is not false.'));
  for(const v of SupportedVersions.filter(v=>v>=DxfVersion.AutoCad2010))for(const b of [false,true]){
    const id=`${VersionName(v)}/${BooleanName(b)}`;
    for(const e of [false,true])Run(`mesh/blend/api-clone-explode/${id}/${BooleanName(e)}`,()=>MeshBlendApi(v,b,e));
    for(const f of [null,0,1])for(const late of [false,true])Run(`mesh/blend/read-write/${id}/${f??''}/${BooleanName(late)}`,()=>MeshBlendWire(v,b,f,late));
    for(const f of [-1,2])Run(`mesh/blend/invalid/${id}/${f}`,()=>MeshBlendInvalid(v,b,f));
  }
}
export function MeshBlendFixture(v,b,flag,late){
  const tags=[[0,'SECTION'],[2,'HEADER'],[9,'$ACADVER'],[1,HeaderVersion(v)],[0,'ENDSEC'],[0,'SECTION'],[2,'ENTITIES'],[0,'MESH'],[5,'200'],[100,'AcDbEntity'],[8,'0'],[100,'AcDbSubDMesh'],[71,2]].map(([c,x])=>new DxfTag(c,x));
  const addFlag=()=>{if(flag!==null)tags.push(new DxfTag(72,flag));if(!b)tags.push(new DxfTag(999,'interleaved comment'));};if(!late)addFlag();
  tags.push(...[[91,2],[92,3],[10,0],[20,0],[30,0],[10,1],[20,0],[30,0],[10,0],[20,1],[30,0],[93,4],[90,3],[90,0],[90,1],[90,2],[94,1],[90,0],[90,1],[95,1],[140,1.5],[90,0]].map(([c,x])=>new DxfTag(c,x)));if(late)addFlag();
  tags.push(...[[1001,'MESH_BLEND'],[1000,'after mesh'],[0,'ENDSEC'],[0,'EOF']].map(([c,x])=>new DxfTag(c,x)));return new MemoryStream(RawFixtureBytes(tags,b));
}
export function MeshBlendWire(v,b,flag,late){const input=MeshBlendFixture(v,b,flag,late),output=new MemoryStream();try{const doc=DxfDocument.Load(input);Check(doc!==null,'MESH blend fixture failed to load.');const original=MeshSingle(doc.Entities.Meshes);Equal(2,original.SubdivisionLevel,'Blend flag affected subdivision level');Equal(3,original.Vertexes.Count,'Blend flag affected vertices');Equal(1.5,MeshSingle(original.Edges).Crease,'Blend flag affected crease value');Equal('after mesh',MeshSingle(original.XData.get_Item('MESH_BLEND').XDataRecord).Value,'Blend flag disrupted XData');doc.Entities.Add(original.Clone());Check(doc.Save(output,!b),'MESH blend export failed.');output.Position=0;const meshes=MeshRecords(DxfRawDocument.Load(output)).filter(r=>r.Name==='MESH');Equal(2,meshes.length,'Cloned MESH count');for(const mesh of meshes)Equal(flag??0,MeshSingle(Array.from(mesh.Tags).filter(t=>t.Code===72)).Value,'MESH blend flag was reset');Check(input.CanRead&&output.CanWrite,'MESH blend IO closed caller streams.');if(flag===1&&!late)MeshWriteArtifact(`mesh-blend-${VersionName(v)}-${BooleanName(!b)}.dxf`,output.ToArray());}finally{input.Dispose();output.Dispose();}}
export function MeshBlendApi(v,b,enabled){const mesh=ProfileMesh(2);mesh.BlendCrease=enabled;const block=new Block('BlendMesh');block.Entities.Add(mesh);const insert=new Insert(block),copy=insert.Clone(),child=MeshSingle(Array.from(copy.Block.Entities).filter(e=>e instanceof Mesh));Equal(enabled,child.BlendCrease,'Block clone lost blend flag');child.BlendCrease=!enabled;Equal(enabled,mesh.BlendCrease,'Clone edit changed the source blend flag');Equal(enabled,MeshSingle(Array.from(insert.Explode()).filter(e=>e instanceof Mesh)).BlendCrease,'Explosion lost blend flag');const doc=new DxfDocument(v),output=new MemoryStream();doc.Entities.Add(insert);try{Check(doc.Save(output,b),'Authored blend flag failed to save.');output.Position=0;const loaded=DxfDocument.Load(output);Check(loaded!==null,'Authored blend flag failed to load.');Equal(enabled,OnlyProfileMesh(loaded).BlendCrease,'Authored blend flag changed on reload');}finally{output.Dispose();}}
export function MeshBlendInvalid(v,b,flag){const input=MeshBlendFixture(v,b,flag,false);try{if(GetTypedIOConfiguration()==='Debug'){let caught=false;try{DxfDocument.Load(input);}catch(e){Check(e instanceof InvalidDataException,'Expected InvalidDataException');Check(e.message.includes('72'),'Blend flag diagnostic lost the group code.');caught=true;}Check(caught,'Invalid blend flag was accepted.');}else Check(DxfDocument.Load(input)===null,'Invalid blend flag was accepted.');Check(input.CanRead,'Invalid MESH input closed caller stream.');}finally{input.Dispose();}}
