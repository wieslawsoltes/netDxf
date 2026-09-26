// Port of the pinned C# conformance module; original case identities and assertions retained.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { Run, Check, Equal, SupportedVersions, VersionName, BooleanName } from './TestHarness.js';
const single=items=>{const a=Array.from(items);Equal(1,a.length,'Expected one item');return a[0];};
const artifacts=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../../artifacts/conformance/fixtures');
const saveFixture=(name,bytes)=>{fs.mkdirSync(artifacts,{recursive:true});fs.writeFileSync(path.join(artifacts,name),bytes);};
import { DxfDocument, DxfRawDocument, DxfTag, Hatch, HatchPattern, HatchBoundaryPath, HatchType, Block, Insert, Vector2, Vector3, MemoryStream } from '../../index.js';
import { HatchDoubleTags } from './HatchDoublePatternTests.js';
import { RawFixtureBytes } from './RawDocumentTests.js';
export function RegisterHatchPolylineClosureTests(){
  for(const closed of [false,true]){
    Run(`hatch/polyline-closure/direct-clone/${BooleanName(closed)}`,()=>HatchPolylineClosureClone(closed));
    for(const version of SupportedVersions)for(const binary of [false,true]){
      const suffix=`${VersionName(version)}/${BooleanName(binary)}/${BooleanName(closed)}`;
      Run(`hatch/polyline-closure/wire/${suffix}`,()=>HatchPolylineClosureWire(version,binary,closed));
      Run(`hatch/polyline-closure/nested/${suffix}`,()=>HatchPolylineClosureNested(version,binary,closed));
    }
  }
}
export function ClosurePolyline(closed){const edge=new HatchBoundaryPath.Polyline();edge.IsClosed=closed;edge.Vertexes=[new Vector3(0,0,0),new Vector3(10,0,0),new Vector3(10,10,0),new Vector3(0,10,0)];return edge;}
export function ClosureEdge(hatch){return single(single(hatch.BoundaryPaths).Edges);}
export function CheckClosure(edge,closed){
  Equal(closed,edge.IsClosed,'HATCH polyline closure flag');Equal(closed,edge.ConvertTo().IsClosed,'Closure changed in entity conversion');
  const segments=Array.from(edge.Explode());Equal(closed?4:3,segments.length,'Closing segment was added or lost');
  if(closed){const last=segments.at(-1),vertex=edge.Vertexes.at(-1),first=edge.Vertexes[0];Equal(new Vector2(vertex.X,vertex.Y),last.Start,'Closing segment start');Equal(new Vector2(first.X,first.Y),last.End,'Closing segment end');}
}
export function HatchPolylineClosureClone(closed){
  const original=ClosurePolyline(closed),copy=original.Clone();CheckClosure(copy,closed);
  Equal(original.Vertexes,copy.Vertexes,'Cloning changed vertices.');Check(original.Vertexes!==copy.Vertexes,'Cloning aliases vertex storage.');
  copy.IsClosed=!closed;copy.Vertexes[0]=new Vector3(99,100,0);CheckClosure(original,closed);Equal(Vector3.Zero,original.Vertexes[0],'Editing clone changed source geometry');
}
export function HatchPolylineClosureWire(version,binary,closed){
  const tags=HatchDoubleTags(version,HatchType.UserDefined,0);tags[tags.findIndex(t=>t.Code===73)]=new DxfTag(73,closed?1:0);
  const input=new MemoryStream(RawFixtureBytes(tags,binary));try{
    let document=DxfDocument.Load(input);Check(document!==null,'Closure fixture load failed.');const original=single(document.Entities.Hatches);CheckClosure(ClosureEdge(original),closed);document.Entities.Add(original.Clone());
    for(let cycle=0;cycle<3;cycle++){const output=new MemoryStream(),transport=cycle===1?binary:!binary;try{
      Check(document.Save(output,transport),'Closure round-trip save failed.');output.Position=0;const raw=DxfRawDocument.Load(output);
      for(const r of single(Array.from(raw.Sections).filter(s=>s.Name==='ENTITIES')).Records)if(r.Name==='HATCH')Equal(closed?1:0,Array.from(r.Tags).find(t=>t.Code===73).Value,'Emitted group 73 closure');
      output.Position=0;document=DxfDocument.Load(output);Check(document!==null,'Closure round-trip load failed.');Equal(2,Array.from(document.Entities.Hatches).length,'HATCH count after clone');
      for(const hatch of document.Entities.Hatches){CheckClosure(ClosureEdge(hatch),closed);Equal(2.5,hatch.Elevation,'Closure changed elevation');Equal('after pattern',single(hatch.XData.get_Item('DOUBLE_TEST').XDataRecord).Value,'Closure disrupted XData');}
      if(cycle===0&&closed)saveFixture(`hatch-closure-${VersionName(version)}-${BooleanName(transport)}.dxf`,output.ToArray());
      Check(output.CanRead,'Closure round trip closed caller output.');
    }finally{output.Dispose();}}
    Check(input.CanRead,'Closure load closed caller input.');
  }finally{input.Dispose();}
}
export function HatchPolylineClosureNested(version,binary,closed){
  const original=new Hatch(HatchPattern.Solid,[new HatchBoundaryPath([ClosurePolyline(closed)])],false),block=new Block('ClosureBlock');block.Entities.Add(original);
  const insert=new Insert(block),clone=insert.Clone();CheckClosure(ClosureEdge(single(Array.from(clone.Block.Entities).filter(e=>e instanceof Hatch))),closed);
  const exploded=single(Array.from(insert.Explode()).filter(e=>e instanceof Hatch));CheckClosure(ClosureEdge(exploded),closed);
  if(!closed)Equal(new Vector3(0,10,0),ClosureEdge(exploded).Vertexes.at(-1),'Open path endpoint');
  const doc=new DxfDocument(version);doc.Entities.Add(original.Clone());const output=new MemoryStream();try{
    Check(doc.Save(output,binary),'Authored closure save failed.');output.Position=0;const loaded=DxfDocument.Load(output);Check(loaded!==null,'Authored closure load failed.');CheckClosure(ClosureEdge(single(loaded.Entities.Hatches)),closed);CheckClosure(ClosureEdge(original),closed);
  }finally{output.Dispose();}
}
