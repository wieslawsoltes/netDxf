// Pinned MeshReadValidationTests.cs. Six allocation-sensitive large-declaration
// identities remain unregistered; their functional checks are supplemental only.
import { DxfDocument, DxfTag, DxfVersion, Vector3, MemoryStream } from '../../index.js';
import { InvalidDataException } from '../../runtime/Errors.js';
import { GetTypedIOConfiguration } from '../../runtime/TypedDocumentIO.js';
import { Run, Check, Equal, SameDoubleBits, SupportedVersions, VersionName, HeaderVersion, BooleanName } from './TestHarness.js';
import { RawFixtureBytes } from './RawDocumentTests.js';
import { MeshSingle, MeshSame, MeshWriteArtifact } from './MeshVersionTests.js';
export function MeshReadTags(v){return [[0,'SECTION'],[2,'HEADER'],[9,'$ACADVER'],[1,HeaderVersion(v)],[0,'ENDSEC'],[0,'SECTION'],[2,'ENTITIES'],[0,'MESH'],[5,'200'],[100,'AcDbEntity'],[8,'0'],[100,'AcDbSubDMesh'],[71,2],[72,1],[91,3],[92,3],[10,1e-20],[20,0],[30,0],[10,1],[20,0],[30,0],[10,0],[20,1],[30,0],[93,4],[90,3],[90,0],[90,1],[90,2],[94,1],[90,0],[90,2],[95,1],[140,1.25],[90,0],[1001,'MESH_READ'],[1000,'following XData'],[0,'LINE'],[5,'201'],[100,'AcDbEntity'],[8,'0'],[100,'AcDbLine'],[10,7],[20,8],[30,9],[11,10],[21,11],[31,12],[0,'ENDSEC'],[0,'EOF']].map(([c,x])=>new DxfTag(c,x));}
export function RegisterMeshReadValidationTests(){
  for(const v of SupportedVersions.filter(v=>v>=DxfVersion.AutoCad2010))for(const b of [false,true]){
    const id=`${VersionName(v)}/${BooleanName(b)}`;
    for(let s=0;s<13;s++)Run(`mesh/read-validation/invalid/${id}/${s}`,()=>MeshReadInvalid(v,b,s));
    Run(`mesh/read-validation/empty-lists/${id}`,()=>MeshReadEmpty(v,b));Run(`mesh/read-validation/valid/${id}`,()=>MeshReadValid(v,b,false));
    if(!b)Run(`mesh/read-validation/comments/${VersionName(v)}`,()=>MeshReadValid(v,false,true));
  }
}
export function MeshReadInvalid(v,b,s){
  const tags=MeshReadTags(v),at=c=>tags.findIndex(t=>t.Code===c);let group;
  switch(s){
    case 0:group=92;tags[at(92)]=new DxfTag(92,-1);break;
    case 1:group=93;tags[at(93)]=new DxfTag(93,3);break;
    case 2:group=93;tags[at(93)+1]=new DxfTag(90,2);tags.splice(at(93)+4,1);tags[at(93)]=new DxfTag(93,3);break;
    case 3:group=90;tags[at(93)+2]=new DxfTag(90,-1);break;
    case 4:group=90;tags[at(93)+4]=new DxfTag(90,3);break;
    case 5:group=94;tags[at(94)]=new DxfTag(94,-1);break;
    case 6:group=90;tags[at(94)+2]=new DxfTag(90,3);break;
    case 7:group=10;tags[at(10)]=new DxfTag(40,1e-20);break;
    case 8:group=20;tags[at(20)]=new DxfTag(21,0);break;
    case 9:group=90;tags[at(94)+1]=new DxfTag(91,0);break;
    case 10:group=140;tags[at(140)]=new DxfTag(40,1.25);break;
    case 11:group=95;tags[at(95)]=new DxfTag(95,0);break;
    default:group=91;tags[at(91)]=new DxfTag(91,256);break;
  }
  const input=new MemoryStream(RawFixtureBytes(tags,b));try{
    if(GetTypedIOConfiguration()==='Debug'){let caught=false;try{DxfDocument.Load(input);}catch(e){Check(e instanceof InvalidDataException,'Expected InvalidDataException');Check(e.message.includes('MESH'),'Diagnostic lost the entity type.');Check(e.message.includes(String(group)),'Diagnostic lost the responsible group code.');caught=true;}Check(caught,'Malformed MESH was accepted.');}
    else Check(DxfDocument.Load(input)===null,'Malformed MESH group '+group+' was accepted.');
    Check(input.CanRead,"Invalid MESH closed the caller's stream.");
  }finally{input.Dispose();}
}
// Functional subset only: no claim about GC.GetAllocatedBytesForCurrentThread.
export function MeshReadLargeDeclarationsFunctional(v,b){
  for(const group of [92,93,94,95])for(const count of [-1,2147483647]){
    const tags=MeshReadTags(v),at=tags.findIndex(t=>t.Code===group);tags[at]=new DxfTag(group,count);if(group===93&&count===2147483647)tags[at+1]=new DxfTag(90,2147483646);
    const input=new MemoryStream(RawFixtureBytes(tags,b));try{if(GetTypedIOConfiguration()==='Debug'){let caught=false;try{DxfDocument.Load(input);}catch(e){Check(e instanceof InvalidDataException,'Expected InvalidDataException');caught=true;}Check(caught,'Forged MESH list declaration was accepted.');}else Check(DxfDocument.Load(input)===null,'Forged MESH list declaration was accepted.');Check(input.CanRead,'Forged MESH closed the caller stream.');}finally{input.Dispose();}
  }
}
export function MeshReadEmpty(v,b){const tags=MeshReadTags(v),start=tags.findIndex(t=>t.Code===92),end=tags.findIndex(t=>t.Code===1001);tags.splice(start,end-start,...[92,93,94,95,90].map(c=>new DxfTag(c,0)));
  const input=new MemoryStream(RawFixtureBytes(tags,b)),output=new MemoryStream();try{const doc=DxfDocument.Load(input);Check(doc!==null,'Empty counted lists failed to load.');const mesh=MeshSingle(doc.Entities.Meshes);Equal(0,mesh.Vertexes.Count,'Empty vertices');Equal(0,mesh.Faces.Count,'Empty faces');Equal(0,mesh.Edges.Count,'Empty edges');Check(doc.Save(output,!b),'Empty counted mesh failed to save.');output.Position=0;Check(DxfDocument.Load(output)!==null,'Empty counted mesh failed to reload.');}finally{input.Dispose();output.Dispose();}}
export function MeshReadValid(v,b,comments){const tags=MeshReadTags(v);if(comments){const begin=tags.findIndex(t=>t.Code===92),end=tags.findIndex(t=>t.Code===1001);for(let i=end-1;i>=begin;i--)tags.splice(i+1,0,new DxfTag(999,'ENDSEC 90 140'));}
  const input=new MemoryStream(RawFixtureBytes(tags,b)),output=new MemoryStream();try{const doc=DxfDocument.Load(input);Check(doc!==null,'Valid counted MESH failed.');const mesh=MeshSingle(doc.Entities.Meshes);
    SameDoubleBits(1e-20,mesh.Vertexes.get_Item(0).X,'Counted mesh coordinate');Check(MeshSame([0,1,2],MeshSingle(mesh.Faces)),'Counted mesh face');Equal(2,MeshSingle(mesh.Edges).EndVertexIndex,'Counted mesh edge');Equal(1.25,MeshSingle(mesh.Edges).Crease,'Counted mesh crease');Equal(3,mesh.SubdivisionLevel,'Counted mesh subdivision');Check(mesh.BlendCrease,'Counted mesh blend flag');Equal('following XData',MeshSingle(mesh.XData.get_Item('MESH_READ').XDataRecord).Value,'Counted mesh XData boundary');Check(new Vector3(7,8,9).Equals(MeshSingle(doc.Entities.Lines).StartPoint),'Following entity boundary');Check(doc.Save(output,!b),'Counted mesh save failed.');output.Position=0;Check(DxfDocument.Load(output)!==null,'Counted mesh reload failed.');if(!comments)MeshWriteArtifact(`mesh-validated-${VersionName(v)}-${BooleanName(!b)}.dxf`,output.ToArray());
  }finally{input.Dispose();output.Dispose();}}
