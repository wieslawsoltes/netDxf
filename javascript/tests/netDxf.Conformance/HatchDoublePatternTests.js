import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { GetTypedIOConfiguration } from '../../runtime/TypedDocumentIO.js';
import { InvalidDataException, InvalidOperationException } from '../../runtime/Errors.js';
import { RawFixtureBytes } from './RawDocumentTests.js';
const artifactDirectory=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../../artifacts/conformance/fixtures');
const single=items=>{const rows=Array.from(items);Equal(1,rows.length,'Expected one item');return rows[0];};
// Complete original HatchDoublePatternTests.cs, including typed wire/edit cases.
import { HatchPattern, HatchGradientPattern, HatchType, Hatch, Block, Insert, Vector3, Matrix3, DxfDocument, DxfRawDocument, DxfTag, MemoryStream } from '../../index.js';
import { Run, Check, Equal, SupportedVersions, VersionName, HeaderVersion, BooleanName } from './TestHarness.js';
export function RegisterHatchDoublePatternTests() {
  Run('hatch/double/model-defaults-and-clones', HatchDoubleDefaults);
  for(const v of SupportedVersions)for(const b of [false,true]){
    const suffix=VersionName(v)+'/'+BooleanName(b);
    Run('hatch/double/edit-and-insert/'+suffix,()=>HatchDoubleEdit(v,b));
    Run('hatch/double/inapplicable-fills/'+suffix,()=>HatchDoubleSolid(v,b));
    Run('hatch/double/late-flag/'+suffix,()=>HatchDoubleLate(v,b));
  }
  for(const v of SupportedVersions)for(const b of [false,true])for(const [typeName,type]of Object.entries(HatchType))for(const flag of [null,0,1,-1,2])
    Run(`hatch/double/wire/${VersionName(v)}/${BooleanName(b)}/${typeName}/${flag??''}`,()=>HatchDoubleWire(v,b,type,flag));
}
export function HatchDoubleDefaults() {
  for (const pattern of [new HatchPattern('U'), HatchPattern.Line, HatchPattern.Net, HatchPattern.Solid, new HatchGradientPattern()]) {
    Check(!pattern.IsDouble, 'A new pattern invented a double flag.'); pattern.IsDouble = true;
    const copy = pattern.Clone(); Check(copy.IsDouble, 'Pattern clone lost the double flag.');
    Equal(pattern.constructor, copy.constructor, 'Pattern clone changed subtype'); copy.IsDouble = false;
    Check(pattern.IsDouble, 'Clone edit changed source double flag.');
  }
}
export function HatchDoubleTags(version,type,flag){const tags=[
  [0,'SECTION'],[2,'HEADER'],[9,'$ACADVER'],[1,HeaderVersion(version)],[9,'$DWGCODEPAGE'],[3,'ANSI_1252'],[0,'ENDSEC'],[0,'SECTION'],[2,'ENTITIES'],[0,'HATCH'],[5,'200'],[100,'AcDbEntity'],[8,'0'],[100,'AcDbHatch'],
  [10,0],[20,0],[30,2.5],[210,0],[220,0],[230,1],[2,'U'],[70,0],[71,0],[91,1],[92,2],[72,0],[73,1],[93,4],
  [10,0],[20,0],[10,10],[20,0],[10,10],[20,10],[10,0],[20,10],[97,0],[75,0],[76,type],[52,0],[41,1]
];if(flag!==null)tags.push([77,flag]);tags.push([78,1],[53,0],[43,0],[44,0],[45,0],[46,.125],[79,0],[98,1],[10,2],[20,3],[1001,'DOUBLE_TEST'],[1000,'after pattern'],[0,'ENDSEC'],[0,'EOF']);return tags.map(([c,v])=>new DxfTag(c,v));}
export function HatchDoubleWire(version,binary,type,flag){const tags=HatchDoubleTags(version,type,flag);if(!binary)tags.splice(tags.findIndex(t=>t.Code===78),0,new DxfTag(999,'77 ENDSEC'));
  const input=new MemoryStream(RawFixtureBytes(tags,binary));try{
    if(flag!==null&&(flag<0||flag>1)){if(GetTypedIOConfiguration()==='Debug'){try{DxfDocument.Load(input);throw new InvalidOperationException('Invalid double-pattern flag accepted.');}catch(error){Check(error instanceof InvalidDataException,'Expected invalid double flag');Check(error.message.includes('77'),'Flag diagnostic lost group 77.');}}else Check(DxfDocument.Load(input)===null,'Invalid double-pattern flag accepted.');Check(input.CanRead,'Rejected HATCH closed input.');return;}
    const doc=DxfDocument.Load(input);if(!doc)throw new InvalidOperationException('Valid patterned HATCH failed to load.');const original=single(doc.Entities.Hatches);Equal(type,original.Pattern.Type,'Double flag changed pattern type');Equal(1,original.Pattern.LineDefinitions.Count,'Double flag changed authored line count');Equal(.125,original.Pattern.LineDefinitions.get_Item(0).Delta.Y,'Double flag changed spacing');Equal(2.5,original.Elevation,'Double flag changed elevation');Equal('after pattern',single(original.XData.get_Item('DOUBLE_TEST').XDataRecord).Value,'Double flag disrupted XData');doc.Entities.Add(original.Clone());
    const output=new MemoryStream();try{Check(doc.Save(output,!binary),'Double pattern save failed.');output.Position=0;const raw=DxfRawDocument.Load(output),hatches=Array.from(raw.Sections).flatMap(s=>Array.from(s.Records)).filter(r=>r.Name==='HATCH');Equal(2,hatches.length,'Double hatch clone count');for(const hatch of hatches)Equal(flag??0,single(Array.from(hatch.Tags).filter(t=>t.Code===77)).Value,'Double flag lost through load/clone/save');Check(input.CanRead&&output.CanWrite,'HATCH closed caller streams.');if(type===HatchType.UserDefined&&flag===1){fs.mkdirSync(artifactDirectory,{recursive:true});fs.writeFileSync(path.join(artifactDirectory,`hatch-double-${VersionName(version)}-${BooleanName(!binary)}.dxf`),output.ToArray());}}finally{output.Dispose();}
  }finally{input.Dispose();}
}
export function HatchDoubleEdit(version,binary){const input=new MemoryStream(RawFixtureBytes(HatchDoubleTags(version,HatchType.UserDefined,0),binary));try{
  const source=DxfDocument.Load(input);if(!source)throw new InvalidOperationException('Editable hatch fixture failed.');const original=single(source.Entities.Hatches);original.Pattern.IsDouble=true;const block=new Block('DoubleHatch');block.Entities.Add(original.Clone());const insert=new Insert(block,new Vector3(10,20,0)),clonedInsert=insert.Clone(),child=single(Array.from(clonedInsert.Block.Entities).filter(e=>e instanceof Hatch));Check(child.Pattern.IsDouble&&child.Pattern!==original.Pattern,'INSERT clone lost or aliased pattern state.');child.Pattern.IsDouble=false;Check(original.Pattern.IsDouble&&single(Array.from(block.Entities).filter(e=>e instanceof Hatch)).Pattern.IsDouble,'Nested clone edit changed source.');
  const exploded=single(Array.from(insert.Explode()).filter(e=>e instanceof Hatch));Check(exploded.Pattern.IsDouble,'INSERT explosion lost double flag.');exploded.TransformBy(Matrix3.RotationZ(Math.PI/2),new Vector3(30,40,0));Check(exploded.Pattern.IsDouble,'Transform lost double flag.');Equal(1,exploded.Pattern.LineDefinitions.Count,'Double flag duplicated the actual line-definition list');Equal(1,original.Pattern.LineDefinitions.Count,'Double flag mutated source line definitions');let doc=new DxfDocument(version);doc.Entities.Add(exploded);
  for(let cycle=0;cycle<3;cycle++){const expected=cycle!==1;single(doc.Entities.Hatches).Pattern.IsDouble=expected;const output=new MemoryStream();try{Check(doc.Save(output,cycle===1?!binary:binary),'Double flag edit failed to save.');output.Position=0;doc=DxfDocument.Load(output);if(!doc)throw new InvalidOperationException('Double flag edit failed to reload.');Equal(expected,single(doc.Entities.Hatches).Pattern.IsDouble,'Edited flag changed');Equal(1,single(doc.Entities.Hatches).Pattern.LineDefinitions.Count,'Repeated output changed line count');}finally{output.Dispose();}}
}finally{input.Dispose();}}
export function HatchDoubleSolid(version,binary){for(const pattern of [HatchPattern.Solid,new HatchGradientPattern()]){const input=new MemoryStream(RawFixtureBytes(HatchDoubleTags(version,HatchType.UserDefined,0),binary)),output=new MemoryStream();try{const doc=DxfDocument.Load(input);if(!doc)throw new InvalidOperationException('Solid control fixture failed.');pattern.IsDouble=true;single(doc.Entities.Hatches).Pattern=pattern;Check(doc.Save(output,binary),'Inapplicable-fill control failed to save.');output.Position=0;const raw=DxfRawDocument.Load(output),hatch=single(Array.from(raw.Sections).flatMap(s=>Array.from(s.Records)).filter(r=>r.Name==='HATCH'));Check(!Array.from(hatch.Tags).some(t=>t.Code===77),'Solid/gradient fill emitted inapplicable double flag.');Check(pattern.IsDouble,'Writer mutated dormant model flag.');}finally{input.Dispose();output.Dispose();}}}
export function HatchDoubleLate(version,binary){const tags=HatchDoubleTags(version,HatchType.UserDefined,null);tags.splice(tags.findIndex(t=>t.Code===98),0,new DxfTag(77,1));const input=new MemoryStream(RawFixtureBytes(tags,binary));try{const doc=DxfDocument.Load(input);if(!doc)throw new InvalidOperationException('Late double-flag fixture failed.');Check(single(doc.Entities.Hatches).Pattern.IsDouble,'Flag after pattern definitions was ignored.');Equal(1,single(doc.Entities.Hatches).Pattern.LineDefinitions.Count,'Late flag affected pattern definitions');}finally{input.Dispose();}}
