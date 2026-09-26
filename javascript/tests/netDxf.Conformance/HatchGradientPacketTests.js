// Pinned original valid/solid packet cases. Invalid cases remain supplemental until
// the original per-thread allocation assertion has an independently qualified port.
import fs from 'node:fs';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {DxfDocument,DxfVersion,DxfTag,HatchGradientPattern,HatchGradientPatternType,MemoryStream,Vector2,Vector3} from '../../index.js';
import {InvalidDataException} from '../../runtime/Errors.js';
import {GetTypedIOConfiguration} from '../../runtime/TypedDocumentIO.js';
import {HatchGradientColorStateTags,AssertGradientColorState} from './HatchGradientColorStateTests.js';
import {RawFixtureBytes} from './RawDocumentTests.js';
import {Run,Check,Equal,SupportedVersions,VersionName,BooleanName} from './TestHarness.js';
export const GradientScalarCodes=[450,451,452,453,460,461,462,470];
const isGradient=t=>GradientScalarCodes.includes(t.Code)||[463,63,421].includes(t.Code);
const single=items=>{const a=Array.from(items);Equal(1,a.length,'Expected one item');return a[0];};
const artifacts=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../../artifacts/conformance/fixtures');
export function RegisterHatchGradientPacketTests(){
  for(const version of SupportedVersions)for(const binary of [false,true]){
    for(const [name,type]of Object.entries(HatchGradientPatternType))for(let variant=0;variant<8;variant++)
      Run(`hatch/gradient-packet/valid/${VersionName(version)}/${BooleanName(binary)}/${name}/${variant}`,()=>HatchGradientPacketValid(version,binary,type,variant));
    Run(`hatch/gradient-packet/solid/${VersionName(version)}/${BooleanName(binary)}`,()=>HatchGradientPacketSolid(version,binary));
  }
}
export function GradientPacketTags(version,type,variant){
  let tags=HatchGradientColorStateTags(version,type,true,.35),fields=tags.filter(t=>GradientScalarCodes.includes(t.Code));tags=tags.filter(t=>!isGradient(t));
  let stops=[[463,0],[63,1],[421,0x123456],[463,1],[63,5],[421,0xABCDEF]].map(([code,value])=>new DxfTag(code,value));
  switch(variant){
    case 1:stops=stops.filter(t=>t.Code!==63);break;case 2:stops.splice(4,1);break;case 3:stops.splice(1,1);break;
    case 4:[stops[1],stops[2]]=[stops[2],stops[1]];[stops[4],stops[5]]=[stops[5],stops[4]];break;
    case 5:fields.reverse();break;case 6:stops.push(...fields.toReversed());fields=[];break;
    case 7:stops.splice(1,0,...fields);fields=[];break;
  }
  if(variant<5){const name=single(fields.filter(t=>t.Code===470));fields=fields.filter(t=>t!==name);fields.push(...stops,name);}else fields.push(...stops);
  if(variant!==0){fields=fields.flatMap(t=>[t,new DxfTag(999,'gradient comment')]);fields.splice(3,0,new DxfTag(52,213));}
  tags.splice(tags.findIndex(t=>t.Code===1001),0,...fields);return tags;
}
export function HatchGradientPacketValid(version,binary,type,variant){
  const input=new MemoryStream(RawFixtureBytes(GradientPacketTags(version,type,variant).filter(t=>!binary||t.Code!==999),binary));
  try{let doc=DxfDocument.Load(input);Check(doc!==null,'Valid gradient packet rejected.');AssertGradientColorState(single(doc.Entities.Hatches).Pattern,type,true,.35);doc.Entities.Add(single(doc.Entities.Hatches).Clone());
    for(let cycle=0;cycle<2;cycle++){const output=new MemoryStream();try{
      Check(doc.Save(output,cycle===0?!binary:binary),'Gradient packet save failed.');
      if(version>=DxfVersion.AutoCad2004&&cycle===1&&type===HatchGradientPatternType.Linear&&variant===1){fs.mkdirSync(artifacts,{recursive:true});fs.writeFileSync(path.join(artifacts,`hatch-gradient-packet-${VersionName(version)}-${BooleanName(binary)}.dxf`),output.ToArray());}
      output.Position=0;doc=DxfDocument.Load(output);Check(doc!==null,'Gradient packet reload failed.');
      for(const hatch of doc.Entities.Hatches){if(version>=DxfVersion.AutoCad2004)AssertGradientColorState(hatch.Pattern,type,true,.35);else Check(!(hatch.Pattern instanceof HatchGradientPattern),'Existing AC1015 solid downgrade changed.');
        Equal(2.5,hatch.Elevation,'Gradient packet elevation');Check(new Vector2(2,3).Equals(single(hatch.SeedPoints)),'Gradient packet seeds');Equal('after pattern',single(hatch.XData.get_Item('DOUBLE_TEST').XDataRecord).Value,'Gradient packet XData');}
      Check(new Vector3(20,30,40).Equals(single(doc.Entities.Lines).StartPoint),'Gradient packet consumed following LINE');Check(input.CanRead&&output.CanRead,'Gradient packet closed caller streams.');
    }finally{output.Dispose();}}
  }finally{input.Dispose();}
}
export function GradientPacketInvalidTags(version,fault){
  let tags=GradientPacketTags(version,HatchGradientPatternType.Linear,0);const at=tags.findIndex(t=>t.Code===450);
  const set=(code,value)=>{const i=tags.findIndex((t,i)=>i>=at&&t.Code===code);Check(i>=0,'Fault target not found');tags[i]=new DxfTag(code,value);};
  if(fault<8)tags=tags.filter(t=>t.Code!==GradientScalarCodes[fault]);
  else if(fault<16)tags.splice(at,0,single(tags.filter(t=>t.Code===GradientScalarCodes[fault-8])));
  else switch(fault){
    case 16:set(450,2);break;case 17:set(451,1);break;case 18:set(452,-1);break;case 19:set(452,2);break;
    case 20:set(453,-1);break;case 21:set(453,1);break;case 22:set(453,2147483647);break;case 23:set(463,.25);break;
    case 24:tags[tags.findLastIndex(t=>t.Code===463)]=new DxfTag(463,0);break;
    case 25:tags.splice(tags.findIndex(t=>t.Code===421),1);break;case 26:tags.splice(tags.findLastIndex(t=>t.Code===421),1);break;
    case 27:tags.splice(tags.findIndex(t=>t.Code===421),0,new DxfTag(421,0x654321));break;
    case 28:tags.splice(tags.findIndex(t=>t.Code===63),0,new DxfTag(63,2));break;
    case 29:tags.splice(at,0,new DxfTag(421,0x654321));break;case 30:tags.splice(at,0,new DxfTag(63,2));break;
    case 31:tags.splice(tags.findIndex(t=>t.Code===1001),0,new DxfTag(463,1));break;
    case 32:set(470,'NOT_A_KNOWN_GRADIENT');break;case 33:tags=tags.filter(t=>![463,63,421].includes(t.Code));break;
    default:throw new Error('Unknown packet fault');
  }return tags;
}
// Functional assertions only; deliberately not registered under original invalid-case identities.
export function ProbeMalformedGradient(version,binary,fault){
  const input=new MemoryStream(RawFixtureBytes(GradientPacketInvalidTags(version,fault).filter(t=>!binary||t.Code!==999),binary));
  try{if(GetTypedIOConfiguration()==='Debug'){
      let rejected=false;try{DxfDocument.Load(input);}catch(error){Check(error instanceof InvalidDataException,'Expected InvalidDataException');Check(['HATCH gradient','group code','position'].every(s=>error.message.includes(s)),'Gradient packet diagnostic lacks context.');rejected=true;}
      Check(rejected,'Malformed gradient packet accepted.');
    }else Check(DxfDocument.Load(input)===null,'Malformed gradient packet accepted.');
    Check(input.CanRead,'Malformed gradient closed caller stream.');
  }finally{input.Dispose();}
}
export function HatchGradientPacketSolid(version,binary){
  const tags=GradientPacketTags(version,HatchGradientPatternType.Linear,5).filter(t=>![463,63,421].includes(t.Code));
  for(const [code,value]of [[450,0],[453,0],[461,-5],[462,7],[470,'IGNORED_SOLID_NAME']])tags[tags.findIndex(t=>t.Code===code)]=new DxfTag(code,value);
  const input=new MemoryStream(RawFixtureBytes(tags.filter(t=>!binary||t.Code!==999),binary));
  try{const doc=DxfDocument.Load(input);Check(doc!==null,'Valid solid marker packet rejected.');Check(!(single(doc.Entities.Hatches).Pattern instanceof HatchGradientPattern),'Solid packet created gradient model.');Equal(1,Array.from(doc.Entities.Lines).length,'Solid packet consumed following entity.');}
  finally{input.Dispose();}
}
