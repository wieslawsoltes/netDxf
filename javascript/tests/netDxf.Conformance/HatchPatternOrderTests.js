// Port of the pinned C# conformance module; original case identities and assertions retained.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { Run, Check, Equal, Near, SupportedVersions, VersionName, BooleanName } from './TestHarness.js';
const single=items=>{const a=Array.from(items);Equal(1,a.length,'Expected one item');return a[0];};
const artifacts=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../../artifacts/conformance/fixtures');
const saveFixture=(name,bytes)=>{fs.mkdirSync(artifacts,{recursive:true});fs.writeFileSync(path.join(artifacts,name),bytes);};
import { DxfDocument, DxfRawDocument, DxfTag, HatchFillType, HatchType, HatchStyle, Vector2, Vector3, MemoryStream } from '../../index.js';
import { HatchPatternValidationTags } from './HatchPatternValidationTests.js';
import { EqualHatchSeeds } from './HatchSeedPointTests.js';
import { RawFixtureBytes } from './RawDocumentTests.js';
export const HatchPatternOrders=['canonical','late-angle','late-scale','late-both','early-metadata','late-style','omitted-style','pattern-before-boundary','seeds-before-pattern','late-name-fill','late-geometry','comments'];
export function RegisterHatchPatternOrderTests(){for(const v of SupportedVersions)for(const b of [false,true])for(const [angle,scale]of [[37,.25],[90,2],[-45,4],[450,.5]])for(const order of HatchPatternOrders){if(b&&order==='comments')continue;Run(`hatch/pattern-order/${VersionName(v)}/${BooleanName(b)}/(${angle}, ${scale})/${order}`,()=>HatchPatternOrder(v,b,angle,scale,order));}}
export function HatchPatternOrderTags(version,angle,scale,order){
  const tags=HatchPatternValidationTags(version);let begin=tags.findIndex(t=>t.Code===78),end=tags.findIndex((t,i)=>i>=begin&&t.Code===98),line=0;
  for(let i=begin+1;i<end;i++){if(tags[i].Code!==53)continue;const localAngle=line++===0?11.5:173.25,a=(angle+localAngle)*Math.PI/180,g=angle*Math.PI/180,x=tags[i+1].Value,y=tags[i+2].Value,dx=tags[i+3].Value,dy=tags[i+4].Value;
    tags[i]=new DxfTag(53,angle+localAngle);tags[i+1]=new DxfTag(43,scale*(x*Math.cos(g)-y*Math.sin(g)));tags[i+2]=new DxfTag(44,scale*(x*Math.sin(g)+y*Math.cos(g)));tags[i+3]=new DxfTag(45,scale*(dx*Math.cos(a)-dy*Math.sin(a)));tags[i+4]=new DxfTag(46,scale*(dx*Math.sin(a)+dy*Math.cos(a)));
  }
  for(let i=0;i<tags.length;i++){if(tags[i].Code===52)tags[i]=new DxfTag(52,angle);else if(tags[i].Code===41)tags[i]=new DxfTag(41,scale);else if(tags[i].Code===49)tags[i]=new DxfTag(49,tags[i].Value*scale);}
  tags.splice(tags.findIndex(t=>t.Code===98),0,new DxfTag(47,.125));
  const move=(code,before)=>{const at=tags.findIndex(t=>t.Code===code),tag=tags.splice(at,1)[0];tags.splice(tags.findIndex(t=>t.Code===before),0,tag);};
  switch(order){
    case 'late-angle':move(52,98);break;case 'late-scale':move(41,98);break;case 'late-both':move(52,98);move(41,98);break;
    case 'early-metadata':for(const code of [76,52,41,77])move(code,75);break;case 'late-style':move(75,1001);break;case 'omitted-style':tags.splice(tags.findIndex(t=>t.Code===75),1);break;
    case 'pattern-before-boundary':{begin=tags.findIndex(t=>t.Code===75);end=tags.findIndex(t=>t.Code===98);const packet=tags.splice(begin,end-begin);tags.splice(tags.findIndex(t=>t.Code===91),0,...packet);break;}
    case 'seeds-before-pattern':{begin=tags.findIndex(t=>t.Code===98);const seeds=tags.splice(begin,3);tags.splice(tags.findIndex(t=>t.Code===75),0,...seeds);break;}
    case 'late-name-fill':{const name=tags.findIndex(t=>t.Code===2&&t.Value==='U'),tag=tags.splice(name,1)[0];tags.splice(tags.findIndex(t=>t.Code===98),0,tag);move(70,98);break;}
    case 'late-geometry':for(const code of [30,210,220,230])move(code,98);break;
    case 'comments':move(52,98);move(41,98);begin=tags.findIndex(t=>t.Code===75);end=tags.findIndex(t=>t.Code===1001);for(let i=end;i>begin;i--)tags.splice(i,0,new DxfTag(999,'52 41 75 are comment text'));break;
  }return tags;
}
export function HatchPatternOrder(version,binary,angle,scale,order){const tags=HatchPatternOrderTags(version,angle,scale,order),input=new MemoryStream(RawFixtureBytes(tags,binary));try{
  let doc=DxfDocument.Load(input);Check(doc!==null,'Reordered pattern rejected.');Check(input.CanRead,'Pattern-order read closed input.');CheckHatchPatternOrder(doc,angle,scale,1);const original=single(doc.Entities.Hatches),copy=original.Clone();Check(copy.Pattern.LineDefinitions.get_Item(0)!==original.Pattern.LineDefinitions.get_Item(0),'Line clone aliases source.');doc.Entities.Add(copy);
  for(let cycle=0;cycle<3;cycle++){const output=new MemoryStream(),transport=cycle%2===0?!binary:binary;try{Check(doc.Save(output,transport),'Reordered pattern save failed.');output.Position=0;const raw=DxfRawDocument.Load(output);
    for(const hatch of Array.from(raw.Sections).flatMap(s=>Array.from(s.Records)).filter(r=>r.Name==='HATCH'))for(const code of [43,44,45,46,49]){const actual=Array.from(hatch.Tags).filter(t=>t.Code===code).map(t=>t.Value),expected=tags.filter(t=>t.Code===code).map(t=>t.Value);Equal(expected.length,actual.length,'Wire component count');for(let i=0;i<expected.length;i++)Near(expected[i],actual[i],'Wire geometry/dash '+code);}
    if(order==='late-both'&&angle===37&&cycle===1)saveFixture(`hatch-pattern-order-${VersionName(version)}-${BooleanName(binary)}.dxf`,output.ToArray());output.Position=0;doc=DxfDocument.Load(output);Check(doc!==null,'Pattern-order round trip failed.');CheckHatchPatternOrder(doc,angle,scale,2);Check(output.CanRead,'Pattern-order round trip closed stream.');
  }finally{output.Dispose();}}
}finally{input.Dispose();}}
export function CheckHatchPatternOrder(doc,angle,scale,count){Equal(count,Array.from(doc.Entities.Hatches).length,'Ordered hatch count');for(const hatch of doc.Entities.Hatches){const p=hatch.Pattern;Equal('U',p.Name,'Pattern name');Equal(HatchFillType.PatternFill,p.Fill,'Pattern fill');Equal(HatchType.Custom,p.Type,'Pattern type');Equal(HatchStyle.Normal,p.Style,'Pattern style');Check(p.IsDouble,'Late metadata lost double flag.');Near(((angle%360)+360)%360,p.Angle,'Global angle');Near(scale,p.Scale,'Global scale');Near(2.5,hatch.Elevation,'Late elevation');Equal(Vector3.UnitZ,hatch.Normal,'Late normal');Equal(.125,hatch.PixelSize,'Pixel size');Equal(1,hatch.BoundaryPaths.Count,'Boundary packet preserved');Check(single(hatch.BoundaryPaths.get_Item(0).Edges).IsClosed,'Boundary closure lost.');EqualHatchSeeds([new Vector2(2,3)],hatch.SeedPoints);Equal('after pattern',single(hatch.XData.get_Item('DOUBLE_TEST').XDataRecord).Value,'Pattern following XData');Equal(2,p.LineDefinitions.Count,'Ordered pattern line count');
    const expected=[[11.5,1.5,-2.25,.5,2,1.25,-.75,0],[173.25,-3,4.5,-.25,.125,2]];for(let i=0;i<expected.length;i++){const line=p.LineDefinitions.get_Item(i),actual=[line.Angle,line.Origin.X,line.Origin.Y,line.Delta.X,line.Delta.Y,...line.DashPattern];Equal(expected[i].length,actual.length,'PAT-local component count');for(let j=0;j<actual.length;j++)Near(expected[i][j],actual[j],`PAT-local line ${i} component ${j}`);}
  }Equal(new Vector3(20,30,40),single(doc.Entities.Lines).StartPoint,'Following entity');}
