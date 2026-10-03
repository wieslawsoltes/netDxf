// Pinned MeshFieldFramingTests.cs. Only the 120 complete scoped/unique cases
// are original registrations. Rejection cases still lack native allocation assertions.
import { DxfDocument, DxfTag, DxfVersion, MemoryStream } from '../../index.js';
import { InvalidDataException } from '../../runtime/Errors.js';
import { GetTypedIOConfiguration } from '../../runtime/TypedDocumentIO.js';
import { Run, Check, SupportedVersions, VersionName, BooleanName } from './TestHarness.js';
import { RawFixtureBytes } from './RawDocumentTests.js';
import { MeshReadTags } from './MeshReadValidationTests.js';
import { MeshOverridePrivate, MeshOverrideCheckGeometry } from './MeshOverrideDeclarationTests.js';
import { MeshWriteArtifact } from './MeshVersionTests.js';
export function RegisterMeshFieldFramingTests(){
  for(const v of SupportedVersions.filter(v=>v>=DxfVersion.AutoCad2010))for(const b of [false,true]){
    const id=`${VersionName(v)}/${BooleanName(b)}`;
    for(const o of [false,true])for(const s of ['private','nested','later-subclass','private-subclass','xdata','comments'])Run(`mesh/framing/scoped/${id}/${BooleanName(o)}/${s}`,()=>MeshFramingScoped(v,b,o,s));
    for(const s of ['late-version','late-blend','late-subdivision','late-vertices','late-faces','late-edges-creases','late-creases','early-zero'])Run(`mesh/framing/unique/${id}/${s}`,()=>MeshFramingUnique(v,b,s));
  }
}
export function MeshFramingPacket(tags,code){
  const end=tags.findIndex(t=>t.Code===1001),start=code===90?end-1:tags.findIndex(t=>t.Code===code);
  const next=code===92?tags.findIndex(t=>t.Code===93):code===93?tags.findIndex(t=>t.Code===94):code===94?tags.findIndex(t=>t.Code===95):code===95?end-1:start+1;
  return {Start:start,Count:next-start};
}
export function MeshFramingDuplicateFunctional(v,b,code,changed,after,empty=false){
  const tags=MeshReadTags(v);
  if(empty){const start=tags.findIndex(t=>t.Code===92),end=tags.findIndex(t=>t.Code===1001);tags.splice(start,end-start,...[92,93,94,95,90].map(c=>new DxfTag(c,0)));}
  const span=MeshFramingPacket(tags,code),duplicate=tags.slice(span.Start,span.Start+span.Count);
  if(changed)switch(code){case 71:duplicate[0]=new DxfTag(71,3);break;case 72:duplicate[0]=new DxfTag(72,0);break;case 91:duplicate[0]=new DxfTag(91,7);break;
    case 92:duplicate[0]=new DxfTag(92,4);duplicate.push(...[[10,9],[20,8],[30,7]].map(([c,x])=>new DxfTag(c,x)));break;
    case 93:duplicate[0]=new DxfTag(93,8);duplicate.push(...[3,2,1,0].map(x=>new DxfTag(90,x)));break;
    case 94:duplicate[0]=new DxfTag(94,2);duplicate.push(new DxfTag(90,1),new DxfTag(90,2));break;case 95:duplicate[1]=new DxfTag(140,9);break;}
  tags.splice(tags.findIndex(t=>t.Code===1001)-(after?0:1),0,...duplicate);
  MeshFramingRejectFunctional(tags,b,`duplicate-${VersionName(v)}-${BooleanName(b)}-${code}-${BooleanName(changed)}-${BooleanName(after)}-${BooleanName(empty)}`,'more than once',code);
}
export function MeshFramingOrphanFunctional(v,b,code,position){const tags=MeshReadTags(v),at=position===0?tags.findIndex(t=>t.Code===92):tags.findIndex(t=>t.Code===1001)-(position===1?1:0);tags.splice(at,0,new DxfTag(code,99));MeshFramingRejectFunctional(tags,b,`orphan-${VersionName(v)}-${BooleanName(b)}-${code}-${position}`,'counted list',code);}
export function MeshFramingReentryFunctional(v,b,scope){const tags=MeshReadTags(v),privateTags=scope==='private'?MeshOverridePrivate(true):[[100,'FutureMeshSubclass'],[91,8],[100,'AcDbSubDMesh']].map(([c,x])=>new DxfTag(c,x));tags.splice(tags.findIndex(t=>t.Code===1001),0,...privateTags,new DxfTag(91,7));MeshFramingRejectFunctional(tags,b,`reentry-${VersionName(v)}-${BooleanName(b)}-${scope}`,'more than once',91);}
export function MeshFramingEarlyInvalidOverrideFunctional(v,b,before,count){const tags=MeshReadTags(v);tags.splice(tags.findIndex(t=>t.Code===1001)-1,1);tags.splice(tags.findIndex(t=>t.Code===before),0,new DxfTag(90,count));MeshFramingRejectFunctional(tags,b,`early-override-${VersionName(v)}-${BooleanName(b)}-${before}-${count}`,count<0?'negative':'not supported',90);}
export function MeshFramingLargeDuplicateFunctional(v,b,code,after){const tags=MeshReadTags(v);tags.splice(tags.findIndex(t=>t.Code===1001)-(after?0:1),0,new DxfTag(code,2147483647));MeshFramingRejectFunctional(tags,b,`large-duplicate-${VersionName(v)}-${BooleanName(b)}-${code}-${BooleanName(after)}`,'more than once',code);}
export function MeshFramingRejectFunctional(tags,b,name,diagnostic,group){
  const bytes=RawFixtureBytes(tags,b);MeshWriteArtifact(`mesh-framing-input-${name}.dxf`,bytes);const input=new MemoryStream(bytes);
  try{if(GetTypedIOConfiguration()==='Debug'){let caught=false;try{DxfDocument.Load(input);}catch(e){Check(e instanceof InvalidDataException,'Expected InvalidDataException');Check(e.message.includes('MESH group '+group+' at position ')&&e.message.includes(diagnostic),'Framing diagnostic lost entity/group/position context: '+e.message);caught=true;}Check(caught,'Invalid public MESH field framing was admitted.');}else Check(DxfDocument.Load(input)===null,'Invalid public MESH field framing was admitted.');Check(input.CanRead,'Framing rejection closed the caller stream.');}finally{input.Dispose();}
}
export function MeshFramingScoped(v,b,optional,s){
  const tags=MeshReadTags(v);if(optional){const start=tags.findIndex(t=>t.Code===94),end=tags.findIndex(t=>t.Code===1001)-1;tags.splice(start,end-start);}
  const after=tags.findIndex(t=>t.Code===1001),lookalikes=[[71,3],[72,0],[91,7],[92,0],[93,0],[94,0],[95,0],[90,1],[10,99],[20,98],[30,97],[140,9]].map(([c,x])=>new DxfTag(c,x));
  if(['private','nested','private-subclass'].includes(s)){const payload=[new DxfTag(102,'{PRIVATE_MESH_FRAMING')];if(s==='nested')payload.push(new DxfTag(102,'{INNER'));if(s==='private-subclass')payload.push(new DxfTag(100,'PrivateMeshSubclass'));payload.push(...lookalikes);if(s==='nested')payload.push(new DxfTag(102,'}'));payload.push(new DxfTag(102,'}'));tags.splice(after,0,...payload);}
  else if(s==='later-subclass')tags.splice(after,0,new DxfTag(100,'FutureMeshSubclass'),...lookalikes);
  else if(s==='xdata')tags.splice(tags.findIndex(t=>t.Code===0&&t.Value==='LINE'),0,new DxfTag(100,'AcDbSubDMesh'),...lookalikes);
  else if(s==='comments'&&!b)tags.splice(after,0,new DxfTag(999,'91 7 outside a real field'));
  MeshFramingSave(tags,b,`scoped-${VersionName(v)}-${BooleanName(b)}-${BooleanName(optional)}-${s}`,!optional);
}
export function MeshFramingUnique(v,b,s){
  const tags=MeshReadTags(v),code={'late-version':71,'late-blend':72,'late-subdivision':91,'late-vertices':92,'late-faces':93,'late-edges-creases':94,'late-creases':95}[s]??90,span=MeshFramingPacket(tags,code);
  if(s==='late-edges-creases')span.Count=tags.findIndex(t=>t.Code===1001)-1-span.Start;
  const packet=tags.splice(span.Start,span.Count),dest=s==='early-zero'?tags.findIndex(t=>t.Code===71):tags.findIndex(t=>t.Code===1001);tags.splice(dest,0,...packet);MeshFramingSave(tags,b,`unique-${VersionName(v)}-${BooleanName(b)}-${s}`,true);
}
export function MeshFramingSave(tags,b,name,hasEdges){const input=new MemoryStream(RawFixtureBytes(tags,b)),output=new MemoryStream();try{const doc=DxfDocument.Load(input);Check(doc!==null,'A unique/scoped MESH field packet was rejected.');MeshOverrideCheckGeometry(doc,hasEdges);Check(doc.Save(output,b),'MESH framing control did not save.');MeshWriteArtifact(`mesh-framing-${name}.dxf`,output.ToArray());output.Position=0;const loaded=DxfDocument.Load(output);Check(loaded!==null,'MESH framing control did not reload.');MeshOverrideCheckGeometry(loaded,hasEdges);}finally{input.Dispose();output.Dispose();}}
