// Original pinned HatchGradientAngleTests.cs: every registered case and assertion.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { DxfDocument, DxfRawDocument, DxfTag, DxfVersion, MemoryStream, HatchGradientPattern,
  HatchGradientPatternType, Block, Insert, Hatch, Vector2, Vector3 } from '../../index.js';
import { HatchPatternValidationTags } from './HatchPatternValidationTests.js';
import { RawFixtureBytes } from './RawDocumentTests.js';
import { Run, Check, Equal, Near, SupportedVersions, VersionName, BooleanName } from './TestHarness.js';
const single=items=>{const list=Array.from(items);Equal(1,list.length,'Expected one item');return list[0];};
const artifacts=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../../artifacts/conformance/fixtures');
export const GradientWireNames=['LINEAR','CYLINDER','INVCYLINDER','SPHERICAL','INVSPHERICAL','HEMISPHERICAL','INVHEMISPHERICAL','CURVED','INVCURVED'];
export function RegisterHatchGradientAngleTests(){
  for(const version of SupportedVersions.filter(v=>v>=DxfVersion.AutoCad2004))for(const binary of [false,true])
    for(const [name,type] of Object.entries(HatchGradientPatternType))for(const angle of [0,37,-45,450])
      Run(`hatch/gradient-angle/${VersionName(version)}/${BooleanName(binary)}/${name}/${angle}`,()=>HatchGradientAngle(version,binary,type,angle));
  for(const binary of [false,true])Run(`hatch/gradient-angle/legacy-loss/${BooleanName(binary)}`,()=>HatchGradientAngleLegacy(binary));
}
export function HatchGradientAngleTags(version,type,angle){
  let tags=HatchPatternValidationTags(version);const start=tags.findIndex(t=>t.Code===78),end=tags.findIndex(t=>t.Code===98);
  tags.splice(start,end-start);tags=tags.filter(t=>![52,41,77].includes(t.Code));
  tags[tags.findIndex(t=>t.Code===2&&t.Value==='U')]=new DxfTag(2,'SOLID');
  tags[tags.findIndex(t=>t.Code===70)]=new DxfTag(70,1);
  tags.splice(tags.findIndex(t=>t.Code===1001),0,...[[450,1],[451,0],[460,angle*Math.PI/180],[461,0],
    [452,angle===37||angle===450?1:0],[462,.35],[453,2],[463,0],[63,1],[421,0x123456],
    [463,1],[63,5],[421,0xABCDEF],[470,GradientWireNames[type]]].map(([code,value])=>new DxfTag(code,value)));
  if(angle===37)tags.splice(tags.findIndex(t=>t.Code===450),0,new DxfTag(52,123));
  if(angle===-45)tags.splice(tags.findIndex(t=>t.Code===1001),0,new DxfTag(52,213));
  return tags;
}
export function HatchGradientAngle(version,binary,type,angle){
  const expected=(angle%360+360)%360,input=new MemoryStream(RawFixtureBytes(HatchGradientAngleTags(version,type,angle),binary));
  try{
    let doc=DxfDocument.Load(input);Check(doc!==null,'Gradient angle fixture rejected.');
    const original=single(doc.Entities.Hatches),initial=original.Pattern;Check(initial instanceof HatchGradientPattern,'Gradient subtype lost.');
    Near(expected,initial.Angle,'Loaded gradient angle');Equal(angle===37||angle===450,initial.SingleColor,'Initial single-color mode');
    const direct=initial.Clone();Near(expected,direct.Angle,'Pattern clone angle');direct.Angle=19;Near(expected,initial.Angle,'Clone edit aliased source angle');
    const block=new Block('GradientAngle');block.Entities.Add(original.Clone());const insert=new Insert(block,new Vector3(10,20,0)),copy=insert.Clone();
    Near(expected,single(Array.from(copy.Block.Entities).filter(e=>e instanceof Hatch)).Pattern.Angle,'Nested clone angle');doc.Entities.Add(original.Clone());
    for(let cycle=0;cycle<3;cycle++){
      const output=new MemoryStream();try{
        Check(doc.Save(output,cycle%2===0?!binary:binary),'Gradient save failed.');output.Position=0;const raw=DxfRawDocument.Load(output);
        for(const record of Array.from(raw.Sections).flatMap(s=>Array.from(s.Records)).filter(r=>r.Name==='HATCH')){
          const tags=Array.from(record.Tags);Equal(1,single(tags.filter(t=>t.Code===450)).Value,'Gradient marker');
          Near(expected*Math.PI/180,single(tags.filter(t=>t.Code===460)).Value,'Gradient wire radians');Check(tags.every(t=>t.Code!==52),'Gradient writer emitted pattern-only angle.');
        }
        if(cycle===1&&type===HatchGradientPatternType.Linear&&angle===37){fs.mkdirSync(artifacts,{recursive:true});fs.writeFileSync(path.join(artifacts,`hatch-gradient-angle-${VersionName(version)}-${BooleanName(binary)}.dxf`),output.ToArray());}
        output.Position=0;doc=DxfDocument.Load(output);Check(doc!==null,'Gradient reload failed.');Equal(2,Array.from(doc.Entities.Hatches).length,'Original/clone count');
        for(const hatch of doc.Entities.Hatches){const gradient=hatch.Pattern;Check(gradient instanceof HatchGradientPattern,'Reload lost gradient subtype.');
          Near(expected,gradient.Angle,'Repeated gradient angle');Equal(type,gradient.GradientType,'Gradient kind');Equal(2.5,hatch.Elevation,'Gradient elevation');
          Check(new Vector2(2,3).Equals(single(hatch.SeedPoints)),'Gradient seed point');Equal('after pattern',single(hatch.XData.get_Item('DOUBLE_TEST').XDataRecord).Value,'Following gradient XData');}
        Check(new Vector3(20,30,40).Equals(single(doc.Entities.Lines).StartPoint),'Following LINE');Check(output.CanRead&&input.CanRead,'Gradient parser closed caller stream.');
      }finally{output.Dispose();}
    }
  }finally{input.Dispose();}
}
export function HatchGradientAngleLegacy(binary){
  const input=new MemoryStream(RawFixtureBytes(HatchGradientAngleTags(DxfVersion.AutoCad2000,HatchGradientPatternType.Linear,37),binary)),output=new MemoryStream();
  try{const doc=DxfDocument.Load(input);Check(doc!==null,'Existing permissive legacy reader changed.');Near(37,single(doc.Entities.Hatches).Pattern.Angle,'Retained out-of-profile gradient angle');
    Check(doc.Save(output,!binary),'Existing legacy save policy changed.');output.Position=0;const raw=DxfRawDocument.Load(output);
    Check(Array.from(raw.Sections).flatMap(s=>Array.from(s.Records)).filter(r=>r.Name==='HATCH').flatMap(r=>Array.from(r.Tags)).every(t=>t.Code!==450&&t.Code!==460),'Legacy save unexpectedly admitted gradient payload.');
  }finally{input.Dispose();output.Dispose();}
}
